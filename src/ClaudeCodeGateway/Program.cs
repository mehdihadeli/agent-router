using ClaudeCodeGateway.Extensions;
using ClaudeCodeGateway.Features.CreatingMessage;
using Common;
using Common.Configuration;
using Common.Extensions;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddDotEnvFile(optional: true);
builder.Configuration.AddClaudeSettings();
builder.Configuration.AddClaudeCodeGatewayConfig();

builder.AddInfrastructure();

var app = builder.Build();

app.UseInfrastructure();

// Keep Claude Code settings in sync with the configured gateway base address.
var opts = app.Services.GetRequiredService<IOptions<ClaudeGatewayOptions>>();
if (!string.IsNullOrWhiteSpace(opts.Value.GatewayBaseAddress))
{
    ClaudeCodeSettingsWriter.EnsureClaudeCodeSettings(opts.Value);
}

app.MapGet("/", () => "`Claude Code Gateway` is up and running!");
app.MapCreatingMessage();

app.Run();
