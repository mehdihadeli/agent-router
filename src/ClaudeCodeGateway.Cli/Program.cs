using ClaudeCodeGateway.Cli;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<GatewayHost>();

using var host = builder.Build();
var app = CliCommandAppFactory.Create(host.Services);
return await app.RunAsync(args);
