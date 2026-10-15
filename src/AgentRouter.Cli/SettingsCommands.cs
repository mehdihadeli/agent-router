using System.ComponentModel;
using System.Text.Json;
using AgentRouter.Core.Contracts.Configuration;
using AgentRouter.Core.Contracts.Serialization;
using AgentRouter.Core.Hosting;
using AgentRouter.Core.Routes;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentRouter.Cli;

public class SettingsFileOptions : CommandSettings
{
    [CommandOption("--file <PATH>")]
    [Description(
        "Versioned settings JSON; defaults to AR_SETTINGS_FILE or settings.json in the gateway data directory."
    )]
    public string? File { get; init; }

    [CommandOption("--project-root <PATH>")]
    [Description("Directory used to locate the nearest project root and its .env file.")]
    public string? ProjectRoot { get; init; }

    [CommandOption("--set <KEY=VALUE>")]
    [Description(
        "Process-local typed setting override, repeatable; e.g. Server:Port=4600. Raw credentials are unsupported."
    )]
    public string[] Overrides { get; init; } = [];

    public GatewaySettings Load()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Overrides)
        {
            var separator = entry.IndexOf('=');
            if (separator <= 0)
                throw new InvalidOperationException("Override requires KEY=VALUE.");
            var key = entry[..separator].Replace("__", ":", StringComparison.Ordinal);
            if (!values.TryAdd(key, entry[(separator + 1)..]))
                throw new InvalidOperationException("Duplicate override key.");
        }
        return GatewaySettingsLoader.Load(File, ProjectRoot, values);
    }
}

public sealed class ConfigValidateCommand : Command<SettingsFileOptions>
{
    public override int Execute(
        CommandContext context,
        SettingsFileOptions settings,
        CancellationToken cancellationToken
    ) =>
        SettingsCommandOutput.Run(() =>
        {
            var candidate = settings.Load();
            return new
            {
                Valid = true,
                Applied = false,
                candidate.SchemaVersion,
                candidate.Server.Port,
            };
        });
}

public sealed class RoutePreviewOptions : SettingsFileOptions
{
    [CommandOption("--model <MODEL>")]
    public string? Model { get; init; }

    [CommandOption("--agent <PROFILE>")]
    public string? Agent { get; init; }

    [CommandOption("--task <TASK>")]
    public string? Task { get; init; }

    [CommandOption("--subagent <SUBAGENT>")]
    public string? Subagent { get; init; }

    [CommandOption("--workflow <WORKFLOW>")]
    public string? Workflow { get; init; }

    [CommandOption("--text <TEXT>")]
    public string? Text { get; init; }
}

public sealed class RoutePreviewCommand : Command<RoutePreviewOptions>
{
    public override int Execute(
        CommandContext context,
        RoutePreviewOptions settings,
        CancellationToken cancellationToken
    ) =>
        SettingsCommandOutput.Run(() =>
            SettingsRouteEvaluator.Preview(
                settings.Load(),
                new(
                    settings.Model,
                    settings.Agent,
                    settings.Task,
                    settings.Subagent,
                    settings.Workflow,
                    settings.Text
                )
            )
        );
}

internal static class SettingsCommandOutput
{
    public static int Run(Func<object> action)
    {
        try
        {
            Console.WriteLine(JsonSerializer.Serialize(action(), GatewayJson.Options));
            return 0;
        }
        catch (Exception error)
            when (error
                    is InvalidOperationException
                        or IOException
                        or ArgumentException
                        or UnauthorizedAccessException
                        or FormatException
            )
        {
            AnsiConsole.MarkupLine("[red]Invalid settings or overrides. No settings applied.[/]");
            return 1;
        }
    }
}
