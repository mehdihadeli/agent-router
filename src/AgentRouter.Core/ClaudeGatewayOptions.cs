using System.ComponentModel.DataAnnotations;
using AgentRouter.Core.Contracts;
using AgentRouter.Core.Contracts.Configuration;
using AgentRouter.Core.Routes;

namespace AgentRouter.Core;

public class ClaudeGatewayOptions
{
    public bool EnableTelemetry { get; set; } = true;

    [Range(1, 3600)]
    public int RequestTimeoutSeconds { get; set; } = 120;

    [Range(1, 8)]
    public int MaxProviderAttempts { get; set; } = 1;

    public GatewaySettings? RoutingSettings { get; internal set; }

    public string? GitBashPath { get; set; } = default!;
    public string GatewayBaseAddress { get; set; } = "http://localhost:4000";

    public Dictionary<string, string> TierRoutes { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    [Required(ErrorMessage = "At least one provider must be configured in 'Providers'.")]
    public List<ClaudeGatewayProvider> Providers { get; set; } = new();

    [Required(ErrorMessage = "RouteModel must be configured.")]
    public RouteModel RouteModel { get; set; } = default!;
}
