using AgentRouter.Core.Contracts.Configuration;
using AgentRouter.Core.Storage;

namespace AgentRouter.Core.Hosting;

public static class GatewaySettingsLoader
{
    public static string GlobalFile =>
        Path.Combine(GatewayPaths.Resolve().RootDirectory, "settings.json");

    public static GatewaySettings Load(
        string? file = null,
        string? projectRoot = null,
        IEnumerable<KeyValuePair<string, string?>>? overrides = null,
        IConfiguration? environmentOverride = null
    )
    {
        var root = FindProjectRoot(projectRoot ?? Directory.GetCurrentDirectory());
        using var environment = (ConfigurationRoot)
            new ConfigurationBuilder()
                .AddDotEnvFile(Path.Combine(root, ".env"), optional: true)
                .AddEnvironmentVariables()
                .Build();
        var variables = environmentOverride ?? environment;
        var path = Path.GetFullPath(file ?? variables["AR_SETTINGS_FILE"] ?? GlobalFile);
        using var configuration = (ConfigurationRoot)
            new ConfigurationBuilder()
                .AddJsonFile(path, optional: false, reloadOnChange: false)
                .AddInMemoryCollection(variables.GetSection("AR").AsEnumerable(true))
                .AddInMemoryCollection(
                    variables.AsEnumerable().Where(entry => IsSettingsPath(entry.Key))
                )
                .AddInMemoryCollection(overrides ?? [])
                .Build();
        if (configuration["SchemaVersion"] is null)
            throw new InvalidOperationException("Settings require an explicit SchemaVersion.");
        var settings = new GatewaySettings();
        configuration.Bind(settings, options => options.ErrorOnUnknownConfiguration = true);
        Validate(settings);
        return settings;
    }

    private static bool IsSettingsPath(string key)
    {
        var root = key.Split(':', 2)[0];
        return new[]
        {
            "SchemaVersion",
            "Server",
            "Providers",
            "Routing",
            "Execution",
            "Agents",
        }.Contains(root, StringComparer.OrdinalIgnoreCase);
    }

    public static string FindProjectRoot(string directory)
    {
        var start = Path.GetFullPath(directory);
        for (var current = new DirectoryInfo(start); current is not null; current = current.Parent)
            if (
                Directory.Exists(Path.Combine(current.FullName, ".git"))
                || File.Exists(Path.Combine(current.FullName, ".git"))
                || File.Exists(Path.Combine(current.FullName, "global.json"))
            )
                return current.FullName;
        return start;
    }

    public static void Validate(GatewaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.SchemaVersion != 1)
            throw new InvalidOperationException("Unsupported settings SchemaVersion; expected 1.");
        if (
            settings.Server.Port is < 1 or > 65535
            || settings.Server.Host is not ("127.0.0.1" or "localhost" or "::1")
        )
            throw new InvalidOperationException(
                "Settings server requires a loopback host and port 1..65535."
            );
        if (
            settings.Execution.MaxAttempts is < 1 or > 8
            || settings.Execution.DeadlineSeconds is < 1 or > 600
            || settings.Execution.CredentialCooldownSeconds is < 0 or > 3600
        )
            throw new InvalidOperationException(
                "Execution limits: attempts 1..8, deadline 1..600 seconds, cooldown 0..3600 seconds."
            );
        if (settings.Providers.Count == 0)
            throw new InvalidOperationException("Settings require at least one provider.");
        var providerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, provider) in settings.Providers)
        {
            if (!ValidId(id) || !providerIds.Add(id))
                throw new InvalidOperationException(
                    "Provider IDs must be unique simple names, not model paths."
                );
            if (
                provider.Protocol
                is not (ProviderCompatibility.OpenAi or ProviderCompatibility.Anthropic)
            )
                throw new InvalidOperationException($"Provider '{id}' protocol is unsupported.");
            if (
                !Uri.TryCreate(provider.Endpoint, UriKind.Absolute, out var endpoint)
                || endpoint.Scheme is not ("http" or "https")
                || (endpoint.Scheme == "http" && !endpoint.IsLoopback)
                || endpoint.UserInfo.Length != 0
                || endpoint.Query.Length != 0
                || endpoint.Fragment.Length != 0
            )
                throw new InvalidOperationException(
                    $"Provider '{id}' requires HTTPS or loopback HTTP without embedded credentials, query or fragment."
                );
            if (
                provider.Models.Count == 0
                || provider.Models.Any(string.IsNullOrWhiteSpace)
                || provider.Models.Distinct(StringComparer.Ordinal).Count() != provider.Models.Count
            )
                throw new InvalidOperationException(
                    $"Provider '{id}' requires unique, nonempty native model IDs."
                );
            var credentialIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var credential in provider.Credentials)
                if (
                    !ValidId(credential.Id)
                    || !credentialIds.Add(credential.Id)
                    || (
                        string.IsNullOrWhiteSpace(credential.Environment)
                        == string.IsNullOrWhiteSpace(credential.Secret)
                    )
                )
                    throw new InvalidOperationException(
                        $"Provider '{id}' credentials require unique IDs and exactly one environment or encrypted secret reference."
                    );
            if (provider.Enabled && !provider.Credentials.Any(credential => credential.Enabled))
                throw new InvalidOperationException(
                    $"Provider '{id}' needs an enabled credential reference."
                );
        }
        ValidateModel(settings, settings.Routing.DefaultModel);
        foreach (var (alias, model) in settings.Routing.Aliases)
        {
            if (string.IsNullOrWhiteSpace(alias) || alias.Contains('/'))
                throw new InvalidOperationException(
                    "Aliases must be nonempty names, not provider/model paths."
                );
            ValidateModel(settings, model);
        }
        var ruleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in settings.Routing.Rules)
        {
            if (!ValidId(rule.Id) || !ruleIds.Add(rule.Id))
                throw new InvalidOperationException("Routing rules require unique IDs.");
            ValidateModel(settings, rule.Model);
            if (
                rule.Rewrite.MaxOutputTokens is < 1 or > 131072
                || rule.Rewrite.Temperature is { } temperature
                    && (!float.IsFinite(temperature) || temperature is < 0 or > 2)
            )
                throw new InvalidOperationException(
                    $"Rule '{rule.Id}' rewrite exceeds supported numeric bounds."
                );
            foreach (var model in rule.FallbackModels)
                ValidateModel(settings, model);
        }
        foreach (var model in settings.Execution.FallbackModels)
            ValidateModel(settings, model);
        foreach (var (id, agent) in settings.Agents)
        {
            if (
                !ValidId(id)
                || agent.Kind is not ("claude" or "codex" or "opencode" or "claude-design")
            )
                throw new InvalidOperationException(
                    "Agent profiles require simple IDs and kind claude, codex, opencode, or claude-design."
                );
            if (agent.Model is not null)
                ValidateModel(settings, agent.Model);
        }
    }

    public static (string Provider, string Model) ValidateModel(
        GatewaySettings settings,
        string selector
    )
    {
        var slash = selector?.IndexOf('/') ?? -1;
        if (slash <= 0 || slash == selector!.Length - 1)
            throw new InvalidOperationException(
                "Model selections require provider/native-model-id."
            );
        var id = selector[..slash];
        var model = selector[(slash + 1)..];
        var entry = settings.Providers.FirstOrDefault(entry =>
            entry.Key.Equals(id, StringComparison.OrdinalIgnoreCase)
        );
        if (
            entry.Value is null
            || !entry.Value.Enabled
            || !entry.Value.Models.Contains(model, StringComparer.Ordinal)
        )
            throw new InvalidOperationException(
                "Model selection references an unknown or disabled provider/model."
            );
        return (entry.Key, model);
    }

    private static bool ValidId(string id) =>
        !string.IsNullOrWhiteSpace(id)
        && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
