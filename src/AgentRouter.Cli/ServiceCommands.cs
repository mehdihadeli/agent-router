using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentRouter.Cli;

public sealed class ManagementSettings : CommandSettings
{
    [CommandOption("--management-port <PORT>")]
    [DefaultValue(49251)]
    public int ManagementPort { get; init; } = 49251;

    [CommandOption("--no-gateway")]
    public bool NoGateway { get; init; }

    public override ValidationResult Validate() =>
        ManagementPort is < 0 or > 65535
            ? ValidationResult.Error(
                "Management port must be between 0 and 65535 (0 selects a free port)."
            )
            : ValidationResult.Success();
}

public sealed class StartCommand(GatewayServiceController controller)
    : AsyncCommand<ManagementSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        ManagementSettings settings,
        CancellationToken cancellationToken
    )
    {
        var state = await controller.StartAsync(
            settings.ManagementPort,
            !settings.NoGateway,
            cancellationToken
        );
        AnsiConsole.WriteLine($"Management: {state.Url}");
        AnsiConsole.WriteLine(
            state.GatewayUrl is null ? "Inference: disabled" : $"Inference: {state.GatewayUrl}"
        );
        AnsiConsole.WriteLine($"Service PID: {state.Identity.ProcessId}");
        return 0;
    }
}

public sealed class StopCommand(GatewayServiceController controller) : AsyncCommand
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        CancellationToken cancellationToken
    )
    {
        AnsiConsole.WriteLine(
            await controller.StopAsync(cancellationToken)
                ? "Managed service stopped."
                : "No managed service is running."
        );
        return 0;
    }
}

public sealed class WebCommand(GatewayServiceController controller)
    : AsyncCommand<ManagementSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        ManagementSettings settings,
        CancellationToken cancellationToken
    ) =>
        controller.RunForegroundAsync(
            settings.ManagementPort,
            !settings.NoGateway,
            cancellationToken
        );
}
