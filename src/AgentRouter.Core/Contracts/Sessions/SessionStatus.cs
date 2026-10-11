namespace AgentRouter.Contracts.Sessions;

/// <summary>Lifecycle state of a recorded session.</summary>
public enum SessionStatus
{
    Running,
    Completed,
    Crashed,
}
