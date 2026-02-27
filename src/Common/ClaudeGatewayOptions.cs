using System.ComponentModel.DataAnnotations;
using Common.Routes;

namespace Common;

public class ClaudeGatewayOptions
{
    public bool EnableTelemetry { get; set; } = true;
    public string? GitBashPath { get; set; } = default!;
    public string GatewayBaseAddress { get; set; } = "http://localhost:4000";

    [Required(ErrorMessage = "At least one provider must be configured in 'Providers'.")]
    public List<ClaudeGatewayProvider> Providers { get; set; } = new();

    [Required(ErrorMessage = "RouteModel must be configured.")]
    public RouteModel RouteModel { get; set; } = default!;
}
