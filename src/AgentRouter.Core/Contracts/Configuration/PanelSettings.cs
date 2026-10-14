namespace AgentRouter.Core.Contracts.Configuration;

/// <summary>Non-secret UI state persisted by the panel.</summary>
public sealed class PanelSettings
{
    /// <summary>Favourite provider ids, in the user's preferred display order.</summary>
    public List<string> FavoriteProviders { get; set; } = new();

    public bool FavoritesTipDismissed { get; set; }
}
