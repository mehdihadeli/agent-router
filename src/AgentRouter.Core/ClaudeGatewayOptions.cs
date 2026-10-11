using System.ComponentModel.DataAnnotations;
using AgentRouter.Shared.Contracts;
using AgentRouter.Shared.Routes;

namespace AgentRouter.Shared.Configuration;

public class ClaudeGatewayOptions
{
    public bool EnableTelemetry { get; set; } = true;
    public string? GitBashPath { get; set; } = default!;
    public string GatewayBaseAddress { get; set; } = "http://localhost:4000";

    public Dictionary<string, string> TierRoutes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [Required(ErrorMessage = "At least one provider must be configured in 'Providers'.")]
    public List<ClaudeGatewayProvider> Providers { get; set; } = new();

    [Required(ErrorMessage = "RouteModel must be configured.")]
    public RouteModel RouteModel { get; set; } = default!;
}
