namespace AgentRouter.Core.Contracts.Defaults;

/// <summary>
/// Canonical secret-store keys. Storage, the config splitter, and the panel must all agree on
/// these, so they live in the shared contracts project rather than in any single layer.
/// </summary>
public static class SecretKeys
{
    public const string ServerAuthToken = "server.authToken";

    public static string ProviderApiKey(string providerId) => $"provider.{providerId}.apiKey";

    public static string ProviderOAuthAccessToken(string providerId) =>
        $"provider.{providerId}.oauth.accessToken";

    public static string ProviderOAuthRefreshToken(string providerId) =>
        $"provider.{providerId}.oauth.refreshToken";

    public static string ProviderOAuthCopilotToken(string providerId) =>
        $"provider.{providerId}.oauth.copilotToken";
}
