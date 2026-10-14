using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentRouter.Cli;

public sealed class ServeCommand(GatewayHost gatewayHost) : AsyncCommand<ServeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-u|--url <URL>")]
        public string? Url { get; init; }

        [CommandOption("-p|--port <PORT>")]
        public int? Port { get; init; }

        [CommandOption("--settings-file <PATH>")]
        public string? SettingsFile { get; init; }

        public override ValidationResult Validate()
        {
            if (Port is < 1 or > 65535)
            {
                return ValidationResult.Error("Port must be between 1 and 65535.");
            }

            if (
                !string.IsNullOrWhiteSpace(Url)
                && (
                    !Uri.TryCreate(Url, UriKind.Absolute, out var uri)
                    || uri.Scheme is not ("http" or "https")
                )
            )
            {
                return ValidationResult.Error("URL must be an absolute HTTP or HTTPS URL.");
            }

            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        var listenUrl = settings.Url;
        if (listenUrl is null && settings.Port is not null)
        {
            listenUrl = $"http://127.0.0.1:{settings.Port}";
        }

        AnsiConsole.Write(
            new Panel(
                $"[green]Listening[/] {(listenUrl is null ? "configured host URLs" : Markup.Escape(listenUrl))}"
            )
                .Header("[aqua]Agent Router[/]")
                .Border(BoxBorder.Rounded)
        );

        return await gatewayHost.RunAsync(listenUrl, settings.SettingsFile, cancellationToken);
    }
}
