using AgentRouter;
using AgentRouter.Core.Hosting;

namespace AgentRouter.Cli;

public sealed class GatewayHost
{
    public async Task<int> RunAsync(string? listenUrl, CancellationToken cancellationToken) =>
        await RunAsync(listenUrl, null, cancellationToken);

    public async Task<int> RunAsync(
        string? listenUrl,
        string? settingsFile,
        CancellationToken cancellationToken
    )
    {
        var builder = settingsFile is null
            ? GatewayApplication.CreateBuilder(listenUrl: listenUrl)
            : GatewayApplication.CreateSettingsBuilder(
                GatewaySettingsLoader.Load(settingsFile),
                listenUrl: listenUrl
            );
        var app = GatewayApplication.Build(builder);

        await app.RunAsync(cancellationToken);
        return 0;
    }
}
