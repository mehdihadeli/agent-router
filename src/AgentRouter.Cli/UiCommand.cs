using System.ComponentModel;
using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentRouter.Cli;

public sealed class UiSettings : CommandSettings
{
    [CommandOption("--no-open")]
    [Description("Print the UI address and pairing code without opening a browser.")]
    public bool NoOpen { get; init; }

    [CommandOption("--management-port <PORT>")]
    [DefaultValue(49251)]
    public int ManagementPort { get; init; } = 49251;

    public override ValidationResult Validate() =>
        ManagementPort is < 0 or > 65535
            ? ValidationResult.Error("Management port must be between 0 and 65535.")
            : ValidationResult.Success();
}

public sealed class UiCommand(GatewayServiceController controller) : AsyncCommand<UiSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        UiSettings settings,
        CancellationToken cancellationToken
    )
    {
        var state = await controller.StartAsync(settings.ManagementPort, false, cancellationToken);
        var pairing = await controller.CreateBrowserPairingAsync(state, cancellationToken);
        var url = state.Url.TrimEnd('/') + "/ui/";
        AnsiConsole.WriteLine($"Management UI: {url}");
        AnsiConsole.WriteLine($"Pairing code: {pairing.Code}");
        AnsiConsole.WriteLine(
            "Code expires in 2 minutes. Keep it private; it grants management access."
        );
        if (!settings.NoOpen)
        {
            try
            {
                using var browser = Process.Start(
                    new ProcessStartInfo(url) { UseShellExecute = true }
                );
            }
            catch (Exception error)
                when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                AnsiConsole.WriteLine(
                    "Browser could not be opened. Open the address above manually; service remains running."
                );
            }
        }
        return 0;
    }
}
