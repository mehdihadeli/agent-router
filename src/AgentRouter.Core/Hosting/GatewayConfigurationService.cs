using AgentRouter.Core.Contracts.Configuration;
using AgentRouter.Core.Contracts.Defaults;
using AgentRouter.Core.Routes;
using AgentRouter.Core.Storage;
using AgentRouter.Core.Storage.Configuration;
using AgentRouter.Core.Storage.Secrets;
using AgentRouter.Core.Storage.Sqlite;

namespace AgentRouter.Core.Hosting;

public sealed class GatewayConfigurationService(
    GatewayDatabase database,
    IConfigStore store,
    GatewayPaths paths,
    ISecretStore secrets
)
{
    private readonly SemaphoreSlim _writes = new(1, 1);

    public async Task<GatewayConfigDocument> LoadAsync(
        CancellationToken cancellationToken = default
    )
    {
        await using var configLock = await AcquireConfigLockAsync(cancellationToken);
        await database.InitializeAsync(cancellationToken);
        return await store.LoadAsync(cancellationToken);
    }

    public async Task InitializeAsync(int? port, CancellationToken cancellationToken = default)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
        await UpdateAsync(
            document =>
            {
                if (port is not null)
                    document.Server.ProxyPort = port.Value;
            },
            cancellationToken
        );
    }

    public async Task AddProviderAsync(
        string id,
        string baseUrl,
        string apiKey,
        IEnumerable<string> models,
        CancellationToken cancellationToken = default,
        ProviderCompatibility compatibility = ProviderCompatibility.OpenAi
    )
    {
        if (compatibility is not (ProviderCompatibility.OpenAi or ProviderCompatibility.Anthropic))
            throw new ArgumentOutOfRangeException(nameof(compatibility));
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(models);
        if (apiKey.Contains(AesGcmSecretStore.MaskToken, StringComparison.Ordinal))
            throw new ArgumentException(
                "A new provider requires an unmasked API key.",
                nameof(apiKey)
            );
        if (
            id.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.')
            )
        )
            throw new ArgumentException(
                "Provider ID must contain only ASCII letters, digits, '.', '_' or '-'.",
                nameof(id)
            );
        if (
            !Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment)
        )
            throw new ArgumentException(
                "Base URL must be HTTP(S), without credentials, query or fragment.",
                nameof(baseUrl)
            );

        var modelList = models
            .Select(model =>
                model is null
                    ? throw new ArgumentException("Model IDs cannot be null.", nameof(models))
                    : model.Trim()
            )
            .Where(model => model.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (modelList.Count == 0)
            throw new ArgumentException("Configure at least one model.", nameof(models));
        id = id.ToLowerInvariant();

        await UpdateAsync(
            document =>
            {
                if (document.Providers.ContainsKey(id))
                    throw new InvalidOperationException(
                        $"Provider '{id}' already exists. Configuration was not changed."
                    );
                document.Providers.Add(
                    id,
                    new ProviderConfig
                    {
                        Enabled = true,
                        Compatibility = compatibility,
                        BaseUrl = endpoint.AbsoluteUri,
                        ApiKey = apiKey,
                        Models = modelList,
                    }
                );
                document.ActiveProvider ??= id;
            },
            cancellationToken
        );
    }

    public Task SetRouteAsync(
        string route,
        RoutingTier tier = RoutingTier.Default,
        CancellationToken cancellationToken = default
    ) =>
        UpdateAsync(
            document =>
            {
                var options = GatewayRuntimeConfiguration.Project(
                    document,
                    new ConfigurationBuilder().Build()
                );
                var model = RouteModelFactory.Resolve(options, route);
                var rule = document.Routing.AsMap()[tier];
                rule.Enabled = true;
                rule.ProviderId = model.Name;
                rule.Model = model.Model;
                if (tier == RoutingTier.Default)
                    document.ActiveProvider = model.Name;
            },
            cancellationToken
        );

    public Task UpdateProviderAsync(
        string id,
        string baseUrl,
        string? apiKey,
        IEnumerable<string> models,
        bool enabled,
        ProviderCompatibility compatibility,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(models);
        if (compatibility is not (ProviderCompatibility.OpenAi or ProviderCompatibility.Anthropic))
            throw new ArgumentOutOfRangeException(nameof(compatibility));
        if (
            !Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https")
            || endpoint.UserInfo.Length > 0
            || endpoint.Query.Length > 0
            || endpoint.Fragment.Length > 0
        )
            throw new ArgumentException(
                "Base URL must be HTTP(S), without credentials, query or fragment.",
                nameof(baseUrl)
            );
        var modelList = models
            .Select(model =>
                model is null
                    ? throw new ArgumentException("Model IDs cannot be null.", nameof(models))
                    : model.Trim()
            )
            .Where(model => model.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (modelList.Count == 0)
            throw new ArgumentException("Configure at least one model.", nameof(models));
        return UpdateAsync(
            document =>
            {
                if (!document.Providers.TryGetValue(id, out var provider))
                    throw new KeyNotFoundException("Provider was not found.");
                var referencedModels = document
                    .Routing.AsMap()
                    .Values.Where(rule => rule.IsActive && rule.ProviderId == id)
                    .Select(rule => rule.Model)
                    .Concat(
                        (document.Profiles ?? [])
                            .Where(profile =>
                                profile.Model?.StartsWith(
                                    id + "/",
                                    StringComparison.OrdinalIgnoreCase
                                ) == true
                            )
                            .Select(profile => profile.Model![(id.Length + 1)..])
                    );
                if (
                    referencedModels.Any(model =>
                        !enabled || !modelList.Contains(model!, StringComparer.OrdinalIgnoreCase)
                    )
                )
                    throw new InvalidOperationException(
                        "Provider is referenced by a route or profile."
                    );
                provider.BaseUrl = endpoint.AbsoluteUri;
                provider.Models = modelList;
                provider.Enabled = enabled;
                provider.Compatibility = compatibility;
                provider.DisabledModels.RemoveAll(model =>
                    !modelList.Contains(model, StringComparer.OrdinalIgnoreCase)
                );
                if (
                    !string.IsNullOrWhiteSpace(apiKey)
                    && !apiKey.Contains(AesGcmSecretStore.MaskToken, StringComparison.Ordinal)
                )
                    provider.ApiKey = apiKey;
            },
            cancellationToken
        );
    }

    public Task RemoveProviderAsync(
        string id,
        CancellationToken cancellationToken = default,
        string? expectedBaseUrl = null
    ) =>
        UpdateAsync(
            document =>
            {
                if (!document.Providers.TryGetValue(id, out var provider))
                    throw new KeyNotFoundException("Provider was not found.");
                if (
                    expectedBaseUrl is not null
                    && provider.BaseUrl != new Uri(expectedBaseUrl).AbsoluteUri
                )
                    throw new InvalidOperationException(
                        "Provider URL does not match expected URL. Configuration was not changed."
                    );
                if (
                    document
                        .Routing.AsMap()
                        .Values.Any(rule => rule.IsActive && rule.ProviderId == id)
                    || (document.Profiles ?? []).Any(profile =>
                        profile.Model?.StartsWith(id + "/", StringComparison.OrdinalIgnoreCase)
                        == true
                    )
                )
                    throw new InvalidOperationException(
                        "Provider is referenced by a route or profile."
                    );
                document.Providers.Remove(id);
                if (document.ActiveProvider == id)
                    document.ActiveProvider = document
                        .Providers.FirstOrDefault(entry => entry.Value.Enabled)
                        .Key;
            },
            cancellationToken,
            async () =>
            {
                foreach (
                    var key in new[]
                    {
                        SecretKeys.ProviderApiKey(id),
                        SecretKeys.ProviderOAuthAccessToken(id),
                        SecretKeys.ProviderOAuthRefreshToken(id),
                        SecretKeys.ProviderOAuthCopilotToken(id),
                    }
                )
                    await secrets.RemoveAsync(key, cancellationToken);
            }
        );

    public Task UpdateProfileAsync(
        string name,
        string? model,
        string executable,
        bool enabled,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        if (
            executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            || executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
        )
            throw new ArgumentException(
                "Use a native executable, not a shell script.",
                nameof(executable)
            );
        return UpdateAsync(
            document =>
            {
                var profile =
                    document.Profiles?.SingleOrDefault(profile =>
                        profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    )
                    ?? throw new KeyNotFoundException("Profile was not found.");
                if (!string.IsNullOrWhiteSpace(model))
                    RouteModelFactory.Resolve(
                        GatewayRuntimeConfiguration.Project(
                            document,
                            new ConfigurationBuilder().Build()
                        ),
                        model
                    );
                profile.Model = model;
                profile.Executable = executable;
                profile.Enabled = enabled;
            },
            cancellationToken
        );
    }

    public Task RemoveProfileAsync(string name, CancellationToken cancellationToken = default) =>
        UpdateAsync(
            document =>
            {
                if (
                    document.Profiles?.RemoveAll(profile =>
                        profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    )
                    is not > 0
                )
                    throw new KeyNotFoundException("Profile was not found.");
            },
            cancellationToken
        );

    public Task AddProfileAsync(
        string name,
        string? model,
        string executable = "claude",
        CancellationToken cancellationToken = default
    ) => AddProfileAsync(name, model, executable, "claude", cancellationToken);

    public Task AddProfileAsync(
        string name,
        string? model,
        string executable,
        string kind,
        CancellationToken cancellationToken = default
    ) => AddProfileAsync(name, model, executable, kind, null, cancellationToken);

    public Task AddProfileAsync(
        string name,
        string? model,
        string executable,
        string kind,
        Uri? telemetryEndpoint,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        var profileKind = kind.ToLowerInvariant();
        if (profileKind is not ("claude" or "codex" or "opencode" or "claude-design"))
            throw new ArgumentException(
                "Profile kind must be claude, codex, opencode, or claude-design.",
                nameof(kind)
            );
        if (telemetryEndpoint is not null)
            GatewayAgentLauncher.ValidateTelemetryEndpoint(telemetryEndpoint);
        var entryMode = profileKind == "claude-design" ? "app" : "cli";
        GatewayAgentLauncher.ValidateProfile(
            new AgentProfile
            {
                Name = name,
                Kind = profileKind,
                EntryMode = entryMode,
                Scope = "launch",
            }
        );
        if (
            executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            || executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
        )
            throw new ArgumentException(
                "Use a native executable, not a shell script.",
                nameof(executable)
            );
        return UpdateAsync(
            document =>
            {
                document.Profiles ??= [];
                if (
                    document.Profiles.Any(profile =>
                        string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)
                    )
                )
                    throw new InvalidOperationException($"Profile '{name}' already exists.");
                var options = GatewayRuntimeConfiguration.Project(
                    document,
                    new ConfigurationBuilder().Build()
                );
                if (!string.IsNullOrWhiteSpace(model))
                    RouteModelFactory.Resolve(options, model);
                document.Profiles.Add(
                    new AgentProfile
                    {
                        Name = name.Trim(),
                        Model = model,
                        Executable = executable,
                        Kind = profileKind,
                        EntryMode = entryMode,
                        TelemetryEndpoint = telemetryEndpoint?.AbsoluteUri,
                    }
                );
            },
            cancellationToken
        );
    }

    public Task SetProfileLaunchSettingsAsync(
        string name,
        string entryMode,
        string scope,
        CancellationToken cancellationToken = default
    ) =>
        UpdateAsync(
            document =>
            {
                var profile =
                    document.Profiles.SingleOrDefault(profile =>
                        profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    ) ?? throw new KeyNotFoundException("Profile was not found.");
                profile.EntryMode = entryMode.ToLowerInvariant();
                profile.Scope = scope.ToLowerInvariant();
                GatewayAgentLauncher.ValidateProfile(profile);
            },
            cancellationToken
        );

    public Task SetProfileKindAsync(
        string name,
        string kind,
        CancellationToken cancellationToken = default
    ) =>
        UpdateAsync(
            document =>
            {
                var profile =
                    document.Profiles.SingleOrDefault(profile =>
                        profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    ) ?? throw new KeyNotFoundException("Profile was not found.");
                var normalizedKind = kind.ToLowerInvariant();
                if (normalizedKind is not ("claude" or "codex" or "opencode" or "claude-design"))
                    throw new ArgumentException("Unsupported profile kind.", nameof(kind));
                profile.Kind = normalizedKind;
            },
            cancellationToken
        );

    public Task SetProfileTierModelsAsync(
        string name,
        IReadOnlyDictionary<string, string> tierModels,
        CancellationToken cancellationToken = default
    ) =>
        UpdateAsync(
            document =>
            {
                var profile =
                    document.Profiles.SingleOrDefault(profile =>
                        profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    ) ?? throw new KeyNotFoundException("Profile was not found.");
                var options = GatewayRuntimeConfiguration.Project(
                    document,
                    new ConfigurationBuilder().Build()
                );
                var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (tier, model) in tierModels)
                {
                    var normalizedTier = tier.ToLowerInvariant();
                    if (normalizedTier is not ("opus" or "sonnet" or "haiku"))
                        throw new ArgumentException("Tier models must use opus, sonnet, or haiku.");
                    if (string.IsNullOrWhiteSpace(model))
                        continue;
                    RouteModelFactory.Resolve(options, model);
                    normalized[normalizedTier] = model;
                }
                profile.TierModels = normalized;
            },
            cancellationToken
        );

    public Task SetProfileTelemetryEndpointAsync(
        string name,
        Uri? telemetryEndpoint,
        CancellationToken cancellationToken = default
    )
    {
        if (telemetryEndpoint is not null)
            GatewayAgentLauncher.ValidateTelemetryEndpoint(telemetryEndpoint);
        return UpdateAsync(
            document =>
            {
                var profile =
                    document.Profiles?.SingleOrDefault(profile =>
                        profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    )
                    ?? throw new KeyNotFoundException("Profile was not found.");
                profile.TelemetryEndpoint = telemetryEndpoint?.AbsoluteUri;
            },
            cancellationToken
        );
    }

    public Task SetProfileLaunchOptionsAsync(
        string name,
        IReadOnlyDictionary<string, string>? environment,
        string? workingDirectory,
        CancellationToken cancellationToken = default
    )
    {
        var candidate = new AgentProfile
        {
            Name = name,
            Environment = new Dictionary<string, string>(
                environment ?? new Dictionary<string, string>()
            ),
            WorkingDirectory = workingDirectory,
        };
        GatewayAgentLauncher.ValidateProfile(candidate);
        return UpdateAsync(
            document =>
            {
                var profile =
                    document.Profiles?.SingleOrDefault(profile =>
                        profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    )
                    ?? throw new KeyNotFoundException("Profile was not found.");
                if (environment is not null)
                    profile.Environment = new Dictionary<string, string>(environment);
                profile.WorkingDirectory = workingDirectory;
            },
            cancellationToken
        );
    }

    private async Task UpdateAsync(
        Action<GatewayConfigDocument> update,
        CancellationToken cancellationToken,
        Func<Task>? afterSave = null
    )
    {
        await _writes.WaitAsync(cancellationToken);
        try
        {
            await using var configLock = await AcquireConfigLockAsync(cancellationToken);
            await database.InitializeAsync(cancellationToken);
            var document = await store.LoadAsync(cancellationToken);
            update(document);
            await store.SaveAsync(document, cancellationToken);
            if (afterSave is not null)
                await afterSave();
        }
        finally
        {
            _writes.Release();
        }
    }

    private async Task<FileStream> AcquireConfigLockAsync(CancellationToken cancellationToken)
    {
        paths.EnsureCreated();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    Path.Combine(paths.RootDirectory, "config.lock"),
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None
                );
            }
            catch (IOException)
            {
                await Task.Delay(25, deadline.Token);
            }
        }
    }
}
