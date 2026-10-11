using AgentRouter.Cli;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AgentRouter;
using AgentRouter.Storage;
using AgentRouter.Shared.Configuration;

var managedChild = args is ["--managed-child"];
var builder = Host.CreateApplicationBuilder(managedChild ? [] : args);
builder.Configuration.AddDotEnvFile(optional: true);
builder.Services.AddSingleton<GatewayHost>();
builder.Services.AddGatewayStorage();
builder.Services.AddSingleton<GatewayConfigurationService>();
builder.Services.AddSingleton<GatewayServiceController>();

using var host = builder.Build();
if (managedChild)
	return await host.Services.GetRequiredService<GatewayServiceController>().RunChildAsync(CancellationToken.None);
var app = CliCommandAppFactory.Create(host.Services);
return await app.RunAsync(args);
