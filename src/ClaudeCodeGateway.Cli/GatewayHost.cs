using ClaudeCodeGateway;

namespace ClaudeCodeGateway.Cli;

public sealed class GatewayHost
{
    public async Task<int> RunAsync(string? listenUrl, CancellationToken cancellationToken)
    {
        var builder = GatewayApplication.CreateBuilder(listenUrl: listenUrl);
        var app = GatewayApplication.Build(builder);

        await app.RunAsync(cancellationToken);
        return 0;
    }
}
