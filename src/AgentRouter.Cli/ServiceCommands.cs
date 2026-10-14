using System.ComponentModel;
using AgentRouter.Core.Hosting;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentRouter.Cli;

public sealed class ManagementSettings : CommandSettings
{
    [CommandOption("-p|--management-port <PORT>")]
    [DefaultValue(49251)]
    public int ManagementPort { get; init; } = 49251;

    [CommandOption("-n|--no-gateway")]
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
        AnsiConsole.WriteLine($"Service PID: {state.Identity.ProcessId}");
        var inference = await controller.GatewayAsync(null, cancellationToken);
        GatewayCommandOutput.Write(inference);
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

public sealed class ServiceStatusCommand(GatewayServiceController controller) : AsyncCommand
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        CancellationToken cancellationToken
    )
    {
        var state = await controller.StatusAsync(cancellationToken);
        if (state is null)
        {
            AnsiConsole.WriteLine("No managed service is running.");
            return 1;
        }
        var inference = await controller.GatewayAsync(null, cancellationToken);
        AnsiConsole.WriteLine($"Management: Running ({state.Url})");
        GatewayCommandOutput.Write(inference);
        AnsiConsole.WriteLine($"Service PID: {state.Identity.ProcessId}");
        return 0;
    }
}

public sealed class GatewayStartCommand(GatewayServiceController controller) : AsyncCommand
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        CancellationToken cancellationToken
    )
    {
        var status = await controller.GatewayAsync("start", cancellationToken);
        GatewayCommandOutput.Write(status);
        return status.Error is null ? 0 : 1;
    }
}

public sealed class GatewayStopCommand(GatewayServiceController controller) : AsyncCommand
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        CancellationToken cancellationToken
    )
    {
        var status = await controller.GatewayAsync("stop", cancellationToken);
        GatewayCommandOutput.Write(status);
        return status.Error is null ? 0 : 1;
    }
}

public sealed class GatewayRestartCommand(GatewayServiceController controller) : AsyncCommand
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        CancellationToken cancellationToken
    )
    {
        var status = await controller.GatewayAsync("restart", cancellationToken);
        GatewayCommandOutput.Write(status);
        return status.Error is null ? 0 : 1;
    }
}

internal static class GatewayCommandOutput
{
    internal static void Write(GatewayLifecycleResponse status)
    {
        AnsiConsole.WriteLine($"Inference: {status.State}");
        if (status.Url is not null)
            AnsiConsole.WriteLine($"Gateway: {status.Url}");
        if (status.Error is not null)
            AnsiConsole.WriteLine(status.Error);
    }
}
