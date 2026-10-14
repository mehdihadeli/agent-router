using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json;
using AgentRouter.Core.Contracts.Configuration;
using AgentRouter.Core.Contracts.Serialization;
using AgentRouter.Core.Hosting;
using AgentRouter.Core.Providers;
using AgentRouter.Core.Routes;
using AgentRouter.Core.Storage;
using AgentRouter.Core.Storage.Configuration;
using Microsoft.Extensions.AI;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentRouter.Cli;

internal static class ProfileEnvironmentParser
{
    public static Dictionary<string, string> Parse(IEnumerable<string> entries)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var separator = entry.IndexOf('=');
            if (separator <= 0)
                throw new ArgumentException(
                    "Profile environment values must use KEY=VALUE format."
                );
            values[entry[..separator]] = entry[(separator + 1)..];
        }
        return values;
    }
}

public sealed class InitCommand(GatewayConfigurationService configuration, GatewayPaths paths)
    : AsyncCommand<InitCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-p|--port <PORT>")]
        public int? Port { get; init; }

        public override ValidationResult Validate() =>
            Port is < 1 or > 65535
                ? ValidationResult.Error("Port must be between 1 and 65535.")
                : ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        await configuration.InitializeAsync(settings.Port, cancellationToken);
        AnsiConsole.WriteLine($"Configuration ready: {paths.DatabasePath}");
        AnsiConsole.WriteLine("Next: provider add <id> --base-url <url> --models <model>");
        return 0;
    }
}

public sealed class ConfigShowCommand(GatewayConfigurationService configuration) : AsyncCommand
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        CancellationToken cancellationToken
    )
    {
        var document = await configuration.LoadAsync(cancellationToken);
        Console.WriteLine(
            JsonSerializer.Serialize(ConfigSecretSplitter.Mask(document), GatewayJson.Indented)
        );
        return 0;
    }
}

public sealed class ProviderAddCommand(
    GatewayConfigurationService configuration,
    IConfiguration settingsConfiguration
) : AsyncCommand<ProviderAddCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<ID>")]
        public string Id { get; init; } = "";

        [CommandOption("-u|--base-url <URL>")]
        public string BaseUrl { get; init; } = "";

        [CommandOption("-m|--models <MODELS>")]
        public string Models { get; init; } = "";

        [CommandOption("-t|--protocol <PROTOCOL>")]
        [DefaultValue(ProviderCompatibility.OpenAi)]
        public ProviderCompatibility Protocol { get; init; } = ProviderCompatibility.OpenAi;

        [CommandOption("-e|--api-key-env <NAME>")]
        [DefaultValue("OPENAI_API_KEY")]
        public string ApiKeyEnvironmentVariable { get; init; } = "OPENAI_API_KEY";

        public override ValidationResult Validate() =>
            string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(Models)
                ? ValidationResult.Error("--base-url and --models are required.")
                : ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        var apiKey = settingsConfiguration[settings.ApiKeyEnvironmentVariable];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (Console.IsInputRedirected)
                throw new InvalidOperationException(
                    $"Set {settings.ApiKeyEnvironmentVariable} before adding a provider."
                );
            apiKey = AnsiConsole.Prompt(new TextPrompt<string>("Upstream API key:").Secret());
        }
        await configuration.AddProviderAsync(
            settings.Id,
            settings.BaseUrl,
            apiKey,
            settings.Models.Split(','),
            cancellationToken,
            settings.Protocol
        );
        AnsiConsole.WriteLine(
            $"Provider '{settings.Id}' saved. Restart any running gateway to apply changes."
        );
        return 0;
    }
}

public sealed class ConfigTokenCommand(
    GatewayConfigurationService configuration,
    GatewayServiceController controller
) : AsyncCommand<ConfigTokenCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-M|--managed")]
        public bool Managed { get; init; }
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        if (settings.Managed)
        {
            AnsiConsole.WriteLine(await controller.GetManagementTokenAsync(cancellationToken));
            return 0;
        }
        var document = await configuration.LoadAsync(cancellationToken);
        AnsiConsole.WriteLine(
            document.Server.AuthToken
                ?? throw new InvalidOperationException("Gateway token is missing.")
        );
        return 0;
    }
}

public sealed class ProviderListCommand(GatewayConfigurationService configuration) : AsyncCommand
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        CancellationToken cancellationToken
    )
    {
        var document = await configuration.LoadAsync(cancellationToken);
        foreach (var (id, provider) in document.Providers)
            AnsiConsole.WriteLine(
                $"{id}\t{(provider.Enabled ? "enabled" : "disabled")}\t{string.Join(", ", provider.Models)}"
            );
        return 0;
    }
}

public sealed class ProviderRemoveCommand(GatewayConfigurationService configuration)
    : AsyncCommand<ProviderRemoveCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<ID>")]
        public string Id { get; init; } = "";

        [CommandOption("-u|--if-base-url <URL>")]
        public string? ExpectedBaseUrl { get; init; }
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        await configuration.RemoveProviderAsync(
            settings.Id,
            cancellationToken,
            settings.ExpectedBaseUrl
        );
        AnsiConsole.WriteLine(
            $"Provider '{settings.Id}' removed. Restart inference to apply changes."
        );
        return 0;
    }
}

public sealed class ProviderUseCommand(GatewayConfigurationService configuration)
    : AsyncCommand<ProviderUseCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<ROUTE>")]
        public string Model { get; init; } = "";

        [CommandOption("-t|--tier <TIER>")]
        [DefaultValue(RoutingTier.Default)]
        public RoutingTier Tier { get; init; } = RoutingTier.Default;
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        await configuration.SetRouteAsync(settings.Model, settings.Tier, cancellationToken);
        AnsiConsole.WriteLine(
            $"{settings.Tier} route: {settings.Model}. Restart any running gateway to apply changes."
        );
        return 0;
    }
}

public sealed class ProviderCheckCommand(GatewayConfigurationService configuration)
    : AsyncCommand<ProviderCheckCommand.Settings>
{
    private static readonly string[] SupportedProbes =
    [
        "basic",
        "streaming",
        "tools",
        "structured",
        "reasoning",
        "usage",
    ];

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<ROUTE>")]
        public string Model { get; init; } = "";

        [CommandOption("--probe <PROBE>")]
        public string[] Probes { get; init; } = [];

        public override ValidationResult Validate()
        {
            var invalid = Probes
                .Where(probe => !SupportedProbes.Contains(probe, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            return invalid.Length == 0
                ? ValidationResult.Success()
                : ValidationResult.Error(
                    $"Unknown probe '{invalid[0]}'. Choose: {string.Join(", ", SupportedProbes)}."
                );
        }
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        var document = await configuration.LoadAsync(cancellationToken);
        var options = GatewayRuntimeConfiguration.Project(
            document,
            new ConfigurationBuilder().Build()
        );
        var model = RouteModelFactory.Resolve(options, settings.Model);
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        ClientExtensions.AddAIProviderChatClient(builder, options);
        using var host = builder.Build();
        var client = host.Services.GetRequiredKeyedService<IChatClient>(
            $"{model.Name}/{model.Model}"
        );
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var probes = settings.Probes.Length == 0 ? ["basic"] : settings.Probes;
        var failed = false;
        foreach (var probe in probes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var result = await RunProbeAsync(client, model.Model, probe, timeout.Token);
            AnsiConsole.WriteLine(
                $"{(result.Success ? "PASS" : "FAIL")} {result.Name}: {result.Message}"
            );
            failed |= !result.Success;
        }
        return failed ? 1 : 0;
    }

    private static async Task<ProbeResult> RunProbeAsync(
        IChatClient client,
        string model,
        string probe,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return probe.ToLowerInvariant() switch
            {
                "basic" => await BasicAsync(client, model, cancellationToken),
                "streaming" => await StreamingAsync(client, model, cancellationToken),
                "tools" => await ToolsAsync(client, model, cancellationToken),
                "structured" => await StructuredAsync(client, model, cancellationToken),
                "reasoning" => await ReasoningAsync(client, model, cancellationToken),
                "usage" => await UsageAsync(client, model, cancellationToken),
                _ => new ProbeResult(probe, false, "unsupported probe"),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProbeResult(probe, false, "timed out");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return new ProbeResult(probe, false, error.GetType().Name);
        }
    }

    private static async Task<ProbeResult> BasicAsync(
        IChatClient client,
        string model,
        CancellationToken cancellationToken
    )
    {
        var response = await client.GetResponseAsync(
            Messages("Reply with OK."),
            Options(model, 16),
            cancellationToken
        );
        return new ProbeResult("basic", true, $"model {response.ModelId ?? model}");
    }

    private static async Task<ProbeResult> StreamingAsync(
        IChatClient client,
        string model,
        CancellationToken cancellationToken
    )
    {
        var updates = 0;
        ChatFinishReason? finishReason = null;
        await foreach (
            var update in client.GetStreamingResponseAsync(
                Messages("Reply with a short streamed response."),
                Options(model, 32),
                cancellationToken
            )
        )
        {
            updates++;
            finishReason ??= update.FinishReason;
        }
        return updates == 0
            ? new ProbeResult("streaming", false, "provider returned no updates")
            : new ProbeResult(
                "streaming",
                true,
                $"{updates} updates{(finishReason is null ? "" : $", finish {finishReason}")}"
            );
    }

    private static async Task<ProbeResult> ToolsAsync(
        IChatClient client,
        string model,
        CancellationToken cancellationToken
    )
    {
        var response = await client.GetResponseAsync(
            Messages("Call the agentrouter_probe tool exactly once."),
            new ChatOptions
            {
                ModelId = model,
                MaxOutputTokens = 32,
                Tools = [AIFunctionFactory.Create(() => "probe", "agentrouter_probe")],
                ToolMode = ChatToolMode.RequireAny,
            },
            cancellationToken
        );
        var call = response
            .Messages.SelectMany(message => message.Contents)
            .OfType<FunctionCallContent>()
            .FirstOrDefault();
        return call is null
            ? new ProbeResult("tools", false, "provider returned no tool call")
            : new ProbeResult("tools", true, $"returned {call.Name}");
    }

    private static async Task<ProbeResult> StructuredAsync(
        IChatClient client,
        string model,
        CancellationToken cancellationToken
    )
    {
        var response = await client.GetResponseAsync(
            Messages("Return only valid JSON in this exact shape: {\"ok\":true}."),
            Options(model, 32),
            cancellationToken
        );
        using var json = JsonDocument.Parse(response.Text);
        return
            json.RootElement.ValueKind == JsonValueKind.Object
            && json.RootElement.TryGetProperty("ok", out var ok)
            && ok.ValueKind == JsonValueKind.True
            ? new ProbeResult("structured", true, "valid JSON response")
            : new ProbeResult("structured", false, "JSON shape did not match");
    }

    private static async Task<ProbeResult> ReasoningAsync(
        IChatClient client,
        string model,
        CancellationToken cancellationToken
    )
    {
        var response = await client.GetResponseAsync(
            Messages("Think briefly, then answer with the single word OK."),
            Options(model, 32),
            cancellationToken
        );
        var hasContent = response
            .Messages.SelectMany(message => message.Contents)
            .Any(content => content is TextContent or TextReasoningContent);
        return hasContent
            ? new ProbeResult("reasoning", true, "text or reasoning content returned")
            : new ProbeResult("reasoning", false, "no text or reasoning content returned");
    }

    private static async Task<ProbeResult> UsageAsync(
        IChatClient client,
        string model,
        CancellationToken cancellationToken
    )
    {
        var response = await client.GetResponseAsync(
            Messages("Reply with OK."),
            Options(model, 16),
            cancellationToken
        );
        var usage = response.Usage;
        return usage?.InputTokenCount is not null || usage?.OutputTokenCount is not null
            ? new ProbeResult(
                "usage",
                true,
                $"input {usage.InputTokenCount?.ToString() ?? "unknown"}, output {usage.OutputTokenCount?.ToString() ?? "unknown"}"
            )
            : new ProbeResult("usage", false, "provider returned no token usage");
    }

    private static ChatMessage[] Messages(string text) => [new(ChatRole.User, text)];

    private static ChatOptions Options(string model, int maxOutputTokens) =>
        new() { ModelId = model, MaxOutputTokens = maxOutputTokens };

    private sealed record ProbeResult(string Name, bool Success, string Message);
}

public sealed class ProfileAddCommand(GatewayConfigurationService configuration)
    : AsyncCommand<ProfileAddCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<NAME>")]
        public string Name { get; init; } = "";

        [CommandOption("-m|--model <ROUTE>")]
        public string? Model { get; init; }

        [CommandOption("-x|--executable <PATH>")]
        public string? Executable { get; init; }

        [CommandOption("--kind <KIND>")]
        [DefaultValue("claude")]
        public string Kind { get; init; } = "claude";

        [CommandOption("--entry-mode <MODE>")]
        [DefaultValue("cli")]
        public string EntryMode { get; init; } = "cli";

        [CommandOption("--scope <SCOPE>")]
        [DefaultValue("launch")]
        public string Scope { get; init; } = "launch";

        [CommandOption("--telemetry-endpoint <URL>")]
        public Uri? TelemetryEndpoint { get; init; }

        [CommandOption("--working-directory <PATH>")]
        public string? WorkingDirectory { get; init; }

        [CommandOption("--env <KEY=VALUE>")]
        public string[] Environment { get; init; } = [];
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        GatewayAgentLauncher.ValidateProfile(
            new AgentProfile
            {
                Name = settings.Name,
                Kind = settings.Kind.ToLowerInvariant(),
                EntryMode = settings.EntryMode.ToLowerInvariant(),
                Scope = settings.Scope.ToLowerInvariant(),
            }
        );
        await configuration.AddProfileAsync(
            settings.Name,
            settings.Model,
            settings.Executable ?? settings.Kind,
            settings.Kind,
            settings.TelemetryEndpoint,
            cancellationToken
        );
        await configuration.SetProfileLaunchSettingsAsync(
            settings.Name,
            settings.EntryMode,
            settings.Scope,
            cancellationToken
        );
        await configuration.SetProfileLaunchOptionsAsync(
            settings.Name,
            ProfileEnvironmentParser.Parse(settings.Environment),
            settings.WorkingDirectory,
            cancellationToken
        );
        AnsiConsole.WriteLine(
            $"Agent profile '{settings.Name}' saved. Global agent settings unchanged."
        );
        return 0;
    }
}

public sealed class ProfileListCommand(GatewayConfigurationService configuration) : AsyncCommand
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        CancellationToken cancellationToken
    )
    {
        var document = await configuration.LoadAsync(cancellationToken);
        foreach (var profile in document.Profiles ?? [])
            AnsiConsole.WriteLine(
                $"{profile.Name}\t{profile.Kind}\t{profile.EntryMode}\t{(profile.Enabled ? "enabled" : "disabled")}\t{profile.Model ?? "default"}"
            );
        return 0;
    }
}

public sealed class ProfileEditCommand(GatewayConfigurationService configuration)
    : AsyncCommand<ProfileEditCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<NAME>")]
        public string Name { get; init; } = "";

        [CommandOption("--model <ROUTE>")]
        public string? Model { get; init; }

        [CommandOption("--kind <KIND>")]
        public string? Kind { get; init; }

        [CommandOption("--entry-mode <MODE>")]
        public string? EntryMode { get; init; }

        [CommandOption("--scope <SCOPE>")]
        public string? Scope { get; init; }

        [CommandOption("-x|--executable <PATH>")]
        public string? Executable { get; init; }

        [CommandOption("--enabled")]
        public bool Enabled { get; init; }

        [CommandOption("--disabled")]
        public bool Disabled { get; init; }

        [CommandOption("--tier-opus <ROUTE>")]
        public string? Opus { get; init; }

        [CommandOption("--tier-sonnet <ROUTE>")]
        public string? Sonnet { get; init; }

        [CommandOption("--tier-haiku <ROUTE>")]
        public string? Haiku { get; init; }

        [CommandOption("--telemetry-endpoint <URL>")]
        public Uri? TelemetryEndpoint { get; init; }

        [CommandOption("--clear-telemetry")]
        public bool ClearTelemetry { get; init; }

        [CommandOption("--working-directory <PATH>")]
        public string? WorkingDirectory { get; init; }

        [CommandOption("--env <KEY=VALUE>")]
        public string[] Environment { get; init; } = [];

        [CommandOption("--clear-env")]
        public bool ClearEnvironment { get; init; }
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        var document = await configuration.LoadAsync(cancellationToken);
        var profile =
            document.Profiles?.SingleOrDefault(profile =>
                profile.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase)
            )
            ?? throw new InvalidOperationException($"Profile '{settings.Name}' was not found.");
        var kind = settings.Kind?.ToLowerInvariant() ?? profile.Kind;
        var entryMode = settings.EntryMode?.ToLowerInvariant() ?? profile.EntryMode;
        var scope = settings.Scope?.ToLowerInvariant() ?? profile.Scope;
        if (kind == "claude-design" && settings.EntryMode is null)
            entryMode = "app";
        GatewayAgentLauncher.ValidateProfile(
            new AgentProfile
            {
                Name = profile.Name,
                Kind = kind,
                EntryMode = entryMode,
                Scope = scope,
            }
        );
        if (settings.Kind is not null)
            await configuration.SetProfileKindAsync(settings.Name, kind, cancellationToken);
        if (
            settings.Kind is not null
            || settings.EntryMode is not null
            || settings.Scope is not null
        )
            await configuration.SetProfileLaunchSettingsAsync(
                settings.Name,
                entryMode,
                scope,
                cancellationToken
            );
        if (
            settings.Model is not null
            || settings.Executable is not null
            || settings.Enabled
            || settings.Disabled
        )
            await configuration.UpdateProfileAsync(
                settings.Name,
                settings.Model ?? profile.Model,
                settings.Executable ?? profile.Executable,
                settings.Disabled ? false : settings.Enabled || profile.Enabled,
                cancellationToken
            );
        var tiers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (settings.Opus is not null)
            tiers["opus"] = settings.Opus;
        if (settings.Sonnet is not null)
            tiers["sonnet"] = settings.Sonnet;
        if (settings.Haiku is not null)
            tiers["haiku"] = settings.Haiku;
        if (tiers.Count > 0)
            await configuration.SetProfileTierModelsAsync(settings.Name, tiers, cancellationToken);
        if (settings.TelemetryEndpoint is not null || settings.ClearTelemetry)
            await configuration.SetProfileTelemetryEndpointAsync(
                settings.Name,
                settings.ClearTelemetry ? null : settings.TelemetryEndpoint,
                cancellationToken
            );
        if (
            settings.WorkingDirectory is not null
            || settings.Environment.Length > 0
            || settings.ClearEnvironment
        )
        {
            var environment = settings.ClearEnvironment
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(
                    profile.Environment,
                    StringComparer.OrdinalIgnoreCase
                );
            foreach (var (key, value) in ProfileEnvironmentParser.Parse(settings.Environment))
                environment[key] = value;
            await configuration.SetProfileLaunchOptionsAsync(
                settings.Name,
                environment,
                settings.WorkingDirectory ?? profile.WorkingDirectory,
                cancellationToken
            );
        }
        AnsiConsole.WriteLine($"Agent profile '{settings.Name}' updated.");
        return 0;
    }
}

public sealed class ProfileRemoveCommand(GatewayConfigurationService configuration)
    : AsyncCommand<ProfileRemoveCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<NAME>")]
        public string Name { get; init; } = "";
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        await configuration.RemoveProfileAsync(settings.Name, cancellationToken);
        AnsiConsole.WriteLine($"Agent profile '{settings.Name}' removed.");
        return 0;
    }
}

public sealed class LaunchCommand(GatewayConfigurationService configuration)
    : AsyncCommand<LaunchCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<PROFILE>")]
        public string Profile { get; init; } = "";

        [CommandArgument(1, "[ARGUMENTS]")]
        public string[] Arguments { get; init; } = [];

        [CommandOption("-u|--url <URL>")]
        public Uri? Url { get; init; }
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        var document = await configuration.LoadAsync(cancellationToken);
        var url = settings.Url ?? GatewayAgentLauncher.DefaultUrl(document);
        var startInfo = GatewayAgentLauncher.CreateStartInfo(
            document,
            settings.Profile,
            url,
            settings.Arguments.Concat(context.Remaining.Raw)
        );
        if (!await GatewayProbe.IsRunningAsync(url, document.Server.AuthToken, cancellationToken))
            throw new InvalidOperationException(
                $"Authenticated gateway is not available at {url}. Run 'serve' in another terminal first."
            );
        return await GatewayAgentLauncher.RunAsync(startInfo, cancellationToken);
    }
}

public sealed class StatusCommand(GatewayConfigurationService configuration)
    : AsyncCommand<StatusCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-u|--url <URL>")]
        public Uri? Url { get; init; }
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        var document = await configuration.LoadAsync(cancellationToken);
        var url = settings.Url ?? GatewayAgentLauncher.DefaultUrl(document);
        var running = await GatewayProbe.IsRunningAsync(
            url,
            document.Server.AuthToken,
            cancellationToken
        );
        AnsiConsole.WriteLine($"Gateway {(running ? "running" : "unavailable")}: {url}");
        return running ? 0 : 1;
    }
}

internal static class GatewayProbe
{
    public static async Task<bool> IsRunningAsync(
        Uri url,
        string? token,
        CancellationToken cancellationToken
    )
    {
        if (!url.IsAbsoluteUri || !url.IsLoopback || url.Scheme is not ("http" or "https"))
            throw new ArgumentException("Use an absolute loopback HTTP(S) URL.", nameof(url));
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var response = await client.GetAsync(
                new Uri(url, "/v1/models"),
                cancellationToken
            );
            return response.IsSuccessStatusCode
                && response.Headers.TryGetValues("X-Claude-Code-Gateway", out var identity)
                && identity.Contains("1", StringComparer.Ordinal);
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
