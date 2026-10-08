var builder = ClaudeCodeGateway.GatewayApplication.CreateBuilder(args);
var app = ClaudeCodeGateway.GatewayApplication.Build(builder);

await app.RunAsync();
