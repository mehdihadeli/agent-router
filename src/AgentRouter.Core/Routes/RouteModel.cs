using System.ComponentModel.DataAnnotations;

namespace AgentRouter.Core.Routes;

public class RouteModel
{
    [Required(ErrorMessage = "Default model must be configured in 'Default'.")]
    public required string DefaultModel { get; set; }
    public required string ThinkingModel { get; set; }
    public required string SearchModel { get; set; }
}
