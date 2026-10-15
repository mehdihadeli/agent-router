using System.Globalization;
using AgentRouter.Core.Storage;
using AgentRouter.Core.Storage.Sqlite;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentRouter.Cli;

public sealed class ObservabilityCommand(SqliteConnectionFactory connections)
    : AsyncCommand<ObservabilityCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--hours <HOURS>")]
        public int Hours { get; init; } = 24;

        public override ValidationResult Validate() =>
            Hours is < 1 or > 720
                ? ValidationResult.Error("Hours must be between 1 and 720.")
                : ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        var summary = await GatewayRequestHistory.SummarizeAsync(
            connections,
            TimeSpan.FromHours(settings.Hours),
            cancellationToken
        );
        AnsiConsole.WriteLine($"Window: {summary.From:O} to {summary.To:O}");
        AnsiConsole.WriteLine($"Requests: {summary.Requests}");
        AnsiConsole.WriteLine(
            $"Success rate: {summary.SuccessRate.ToString("0.0%", CultureInfo.InvariantCulture)}"
        );
        AnsiConsole.WriteLine($"Errors: {summary.FailedRequests}");
        AnsiConsole.WriteLine($"Average latency: {summary.AverageLatencyMs:0.0} ms");
        AnsiConsole.WriteLine($"P95 latency: {summary.P95LatencyMs:0.0} ms");
        AnsiConsole.WriteLine($"Input tokens: {summary.InputTokens}");
        AnsiConsole.WriteLine($"Output tokens: {summary.OutputTokens}");
        AnsiConsole.WriteLine(
            $"Estimated cost: {summary.CostUsd?.ToString("0.####") ?? "unknown"}"
        );
        AnsiConsole.WriteLine($"Top provider: {summary.TopProvider ?? "unknown"}");
        AnsiConsole.WriteLine($"Top model: {summary.TopModel ?? "unknown"}");
        AnsiConsole.WriteLine($"Last request: {summary.LastRequestAt?.ToString("O") ?? "none"}");
        var recent = await GatewayRequestHistory.ReadRecentAsync(connections, 5, cancellationToken);
        AnsiConsole.WriteLine("Recent activity:");
        foreach (var request in recent)
            AnsiConsole.WriteLine(
                $"  {request.Timestamp:O} {request.Outcome} {request.Provider ?? "unknown"}/{request.Model ?? "unknown"} {request.LatencyMs:0.0} ms"
            );
        return 0;
    }
}
