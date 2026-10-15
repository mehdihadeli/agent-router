using AgentRouter.Core.Contracts.Configuration;
using AgentRouter.Core.Hosting;

namespace AgentRouter.Core.Routes;

public sealed record SettingsRouteContext(
    string? RequestedModel = null,
    string? Agent = null,
    string? Task = null,
    string? Subagent = null,
    string? Workflow = null,
    string? Text = null
);

public sealed record SettingsRouteDecision(
    string Provider,
    string Model,
    string Reason,
    string? Rule,
    int? MaxOutputTokens,
    float? Temperature,
    IReadOnlyList<string> Candidates,
    int MaxAttempts,
    int DeadlineSeconds
);

public static class SettingsRouteEvaluator
{
    public static SettingsRouteDecision Preview(
        GatewaySettings settings,
        SettingsRouteContext context
    )
    {
        GatewaySettingsLoader.Validate(settings);
        ArgumentNullException.ThrowIfNull(context);
        string selector;
        string reason;
        SettingsRoute? rule = null;
        var alias = context.RequestedModel is null
            ? null
            : settings
                .Routing.Aliases.FirstOrDefault(entry =>
                    entry.Key.Equals(context.RequestedModel, StringComparison.OrdinalIgnoreCase)
                )
                .Value;
        if (context.RequestedModel?.Contains('/') is true || alias is not null)
        {
            selector = alias ?? context.RequestedModel!;
            reason = "explicit";
        }
        else
        {
            rule = settings.Routing.Rules.FirstOrDefault(candidate =>
                candidate.Enabled
                && Matches(candidate.Agent, context.Agent)
                && Matches(candidate.Task, context.Task)
                && Matches(candidate.Subagent, context.Subagent)
                && Matches(candidate.Workflow, context.Workflow)
                && Matches(candidate.RequestedModel, context.RequestedModel)
                && (
                    candidate.ContainsText is null
                    || context.Text?.Contains(
                        candidate.ContainsText,
                        StringComparison.OrdinalIgnoreCase
                    )
                        is true
                )
            );
            if (!string.IsNullOrWhiteSpace(context.RequestedModel) && rule is null)
                throw new InvalidOperationException(
                    "Unknown requested model; configure an alias or matching rule."
                );
            var agent = context.Agent is null
                ? null
                : settings
                    .Agents.FirstOrDefault(entry =>
                        entry.Key.Equals(context.Agent, StringComparison.OrdinalIgnoreCase)
                    )
                    .Value;
            selector = rule?.Model ?? agent?.Model ?? settings.Routing.DefaultModel;
            reason =
                rule is not null ? "rule"
                : agent?.Model is not null ? "agent"
                : "default";
        }
        var (provider, model) = GatewaySettingsLoader.ValidateModel(settings, selector);
        var candidates = new[] { $"{provider}/{model}" }
            .Concat(rule?.FallbackModels ?? [])
            .Concat(settings.Execution.FallbackModels)
            .Select(candidate => GatewaySettingsLoader.ValidateModel(settings, candidate))
            .Select(candidate => $"{candidate.Provider}/{candidate.Model}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new SettingsRouteDecision(
            provider,
            model,
            reason,
            rule?.Id,
            rule?.Rewrite.MaxOutputTokens,
            rule?.Rewrite.Temperature,
            candidates,
            settings.Execution.MaxAttempts,
            settings.Execution.DeadlineSeconds
        );
    }

    private static bool Matches(string? required, string? actual) =>
        required is null || string.Equals(required, actual, StringComparison.OrdinalIgnoreCase);
}
