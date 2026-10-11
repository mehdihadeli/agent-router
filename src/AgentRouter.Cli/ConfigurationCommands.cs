using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json;
using AgentRouter.Contracts.Configuration;
using AgentRouter.Contracts.Serialization;
using AgentRouter.Shared.Routes;
using AgentRouter.Storage;
using AgentRouter.Storage.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentRouter.Cli;

public sealed class InitCommand(GatewayConfigurationService configuration, GatewayPaths paths) : AsyncCommand<InitCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--port <PORT>")]
        public int? Port { get; init; }
        public override ValidationResult Validate() => Port is < 1 or > 65535
            ? ValidationResult.Error("Port must be between 1 and 65535.") : ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        await configuration.InitializeAsync(settings.Port, cancellationToken);
        AnsiConsole.WriteLine($"Configuration ready: {paths.DatabasePath}");
        AnsiConsole.WriteLine("Next: provider add <id> --base-url <url> --models <model>");
        return 0;
    }
}

public sealed class ConfigShowCommand(GatewayConfigurationService configuration) : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var document = await configuration.LoadAsync(cancellationToken);
        AnsiConsole.WriteLine(JsonSerializer.Serialize(ConfigSecretSplitter.Mask(document), GatewayJson.Indented));
        return 0;
    }
}

public sealed class ProviderAddCommand(GatewayConfigurationService configuration, IConfiguration settingsConfiguration) : AsyncCommand<ProviderAddCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<ID>")]
        public string Id { get; init; } = "";
        [CommandOption("--base-url <URL>")]
        public string BaseUrl { get; init; } = "";
        [CommandOption("--models <MODELS>")]
        public string Models { get; init; } = "";
        [CommandOption("--protocol <PROTOCOL>")]
        [DefaultValue(ProviderCompatibility.OpenAi)]
        public ProviderCompatibility Protocol { get; init; } = ProviderCompatibility.OpenAi;
        [CommandOption("--api-key-env <NAME>")]
        [DefaultValue("OPENAI_API_KEY")]
        public string ApiKeyEnvironmentVariable { get; init; } = "OPENAI_API_KEY";
        public override ValidationResult Validate() => string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(Models)
            ? ValidationResult.Error("--base-url and --models are required.") : ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var apiKey = settingsConfiguration[settings.ApiKeyEnvironmentVariable];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (Console.IsInputRedirected)
                throw new InvalidOperationException($"Set {settings.ApiKeyEnvironmentVariable} before adding a provider.");
            apiKey = AnsiConsole.Prompt(new TextPrompt<string>("Upstream API key:").Secret());
        }
        await configuration.AddProviderAsync(settings.Id, settings.BaseUrl, apiKey, settings.Models.Split(','), cancellationToken, settings.Protocol);
        AnsiConsole.WriteLine($"Provider '{settings.Id}' saved. Restart any running gateway to apply changes.");
        return 0;
    }
}

public sealed class ConfigTokenCommand(GatewayConfigurationService configuration, GatewayServiceController controller) : AsyncCommand<ConfigTokenCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--managed")]
        public bool Managed { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (settings.Managed)
        {
            AnsiConsole.WriteLine(await controller.GetManagementTokenAsync(cancellationToken));
            return 0;
        }
        var document = await configuration.LoadAsync(cancellationToken);
        AnsiConsole.WriteLine(document.Server.AuthToken ?? throw new InvalidOperationException("Gateway token is missing."));
        return 0;
    }
}

public sealed class ProviderListCommand(GatewayConfigurationService configuration) : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var document = await configuration.LoadAsync(cancellationToken);
        foreach (var (id, provider) in document.Providers)
            AnsiConsole.WriteLine($"{id}\t{(provider.Enabled ? "enabled" : "disabled")}\t{string.Join(", ", provider.Models)}");
        return 0;
    }
}

public sealed class ProviderRemoveCommand(GatewayConfigurationService configuration) : AsyncCommand<ProviderRemoveCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<ID>")]
        public string Id { get; init; } = "";
        [CommandOption("--if-base-url <URL>")]
        public string? ExpectedBaseUrl { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        await configuration.RemoveProviderAsync(settings.Id, cancellationToken, settings.ExpectedBaseUrl);
        AnsiConsole.WriteLine($"Provider '{settings.Id}' removed. Restart inference to apply changes.");
        return 0;
    }
}

public sealed class ProviderUseCommand(GatewayConfigurationService configuration) : AsyncCommand<ProviderUseCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<ROUTE>")]
        public string Model { get; init; } = "";
        [CommandOption("--tier <TIER>")]
        [DefaultValue(RoutingTier.Default)]
        public RoutingTier Tier { get; init; } = RoutingTier.Default;
    }
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        await configuration.SetRouteAsync(settings.Model, settings.Tier, cancellationToken);
        AnsiConsole.WriteLine($"{settings.Tier} route: {settings.Model}. Restart any running gateway to apply changes.");
        return 0;
    }
}

public sealed class ProviderCheckCommand(GatewayConfigurationService configuration) : AsyncCommand<ProviderCheckCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<ROUTE>")]
        public string Model { get; init; } = "";
    }
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var document = await configuration.LoadAsync(cancellationToken);
        var options = GatewayRuntimeConfiguration.Project(document, new ConfigurationBuilder().Build());
        var model = RouteModelFactory.Resolve(options, settings.Model);
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        AgentRouter.Shared.AIProviders.ClientExtensions.AddAIProviderChatClient(builder, options);
        using var host = builder.Build();
        var client = host.Services.GetRequiredKeyedService<Microsoft.Extensions.AI.IChatClient>($"{model.Name}/{model.Model}");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await client.GetResponseAsync([new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "Reply OK.")], new Microsoft.Extensions.AI.ChatOptions { ModelId = model.Model, MaxOutputTokens = 16 }, timeout.Token);
        AnsiConsole.WriteLine($"Connection succeeded: {model.Name}/{model.Model}");
        return 0;
    }
}

public sealed class ProfileAddCommand(GatewayConfigurationService configuration) : AsyncCommand<ProfileAddCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<NAME>")]
        public string Name { get; init; } = "";
        [CommandOption("--model <ROUTE>")]
        public string? Model { get; init; }
        [CommandOption("--executable <PATH>")]
        [DefaultValue("claude")]
        public string Executable { get; init; } = "claude";
    }
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        await configuration.AddProfileAsync(settings.Name, settings.Model, settings.Executable, cancellationToken);
        AnsiConsole.WriteLine($"Claude Code profile '{settings.Name}' saved. Global agent settings unchanged.");
        return 0;
    }
}

public sealed class ProfileListCommand(GatewayConfigurationService configuration) : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var document = await configuration.LoadAsync(cancellationToken);
        foreach (var profile in document.Profiles ?? [])
            AnsiConsole.WriteLine($"{profile.Name}\t{(profile.Enabled ? "enabled" : "disabled")}\t{profile.Model ?? "default"}");
        return 0;
    }
}

public sealed class LaunchCommand(GatewayConfigurationService configuration) : AsyncCommand<LaunchCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<PROFILE>")]
        public string Profile { get; init; } = "";
        [CommandArgument(1, "[ARGUMENTS]")]
        public string[] Arguments { get; init; } = [];
        [CommandOption("--url <URL>")]
        public Uri? Url { get; init; }
    }
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var document = await configuration.LoadAsync(cancellationToken);
        var url = settings.Url ?? GatewayAgentLauncher.DefaultUrl(document);
        var startInfo = GatewayAgentLauncher.CreateStartInfo(document, settings.Profile, url, settings.Arguments.Concat(context.Remaining.Raw));
        if (!await GatewayProbe.IsRunningAsync(url, document.Server.AuthToken, cancellationToken))
            throw new InvalidOperationException($"Authenticated gateway is not available at {url}. Run 'serve' in another terminal first.");
        return await GatewayAgentLauncher.RunAsync(startInfo, cancellationToken);
    }
}

public sealed class StatusCommand(GatewayConfigurationService configuration, GatewayServiceController controller) : AsyncCommand<StatusCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--url <URL>")]
        public Uri? Url { get; init; }
        [CommandOption("--managed")]
        public bool Managed { get; init; }
    }
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (settings.Managed)
        {
            var state = await controller.StatusAsync(cancellationToken);
            if (state is null)
            {
                AnsiConsole.WriteLine("No managed service is running.");
                return 1;
            }
            AnsiConsole.WriteLine($"Management: {state.Url}");
            AnsiConsole.WriteLine(state.GatewayUrl is null ? "Inference: disabled" : $"Inference: {state.GatewayUrl}");
            AnsiConsole.WriteLine($"Service PID: {state.Identity.ProcessId}");
            return 0;
        }
        var document = await configuration.LoadAsync(cancellationToken);
        var url = settings.Url ?? GatewayAgentLauncher.DefaultUrl(document);
        var running = await GatewayProbe.IsRunningAsync(url, document.Server.AuthToken, cancellationToken);
        AnsiConsole.WriteLine($"Gateway {(running ? "running" : "unavailable")}: {url}");
        return running ? 0 : 1;
    }
}

internal static class GatewayProbe
{
    public static async Task<bool> IsRunningAsync(Uri url, string? token, CancellationToken cancellationToken)
    {
        if (!url.IsAbsoluteUri || !url.IsLoopback || url.Scheme is not ("http" or "https"))
            throw new ArgumentException("Use an absolute loopback HTTP(S) URL.", nameof(url));
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var response = await client.GetAsync(new Uri(url, "/v1/models"), cancellationToken);
            return response.IsSuccessStatusCode
                && response.Headers.TryGetValues("X-Claude-Code-Gateway", out var identity)
                && identity.Contains("1", StringComparer.Ordinal);
        }
        catch (HttpRequestException) { return false; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
    }
}
