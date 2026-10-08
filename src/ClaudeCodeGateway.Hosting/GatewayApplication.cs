using ClaudeCodeGateway.Extensions;
using ClaudeCodeGateway.Features.CreatingMessage;
using ClaudeCodeGateway.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace ClaudeCodeGateway;

public static class GatewayApplication
{
    public static WebApplicationBuilder CreateBuilder(
        string[]? args = null,
        string? listenUrl = null
    )
    {
        var builder = WebApplication.CreateBuilder(args ?? []);

        builder.Configuration.AddDotEnvFile(optional: true);
        builder.Configuration.AddClaudeSettings();
        builder.Configuration.AddClaudeCodeGatewayConfig();

        if (!string.IsNullOrWhiteSpace(listenUrl))
        {
            builder.WebHost.UseUrls(listenUrl);
        }

        builder.AddInfrastructure();
        return builder;
    }

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        var app = builder.Build();

        app.UseInfrastructure();

        var options = app.Services.GetRequiredService<IOptions<ClaudeGatewayOptions>>();
        if (!string.IsNullOrWhiteSpace(options.Value.GatewayBaseAddress))
        {
            ClaudeCodeSettingsWriter.EnsureClaudeCodeSettings(options.Value);
        }

        app.MapGet("/", () => "`Claude Code Gateway` is up and running!");
        app.MapCreatingMessage();

        return app;
    }
}
