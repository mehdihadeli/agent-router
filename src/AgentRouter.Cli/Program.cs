using AgentRouter;
using AgentRouter.Cli;
using AgentRouter.Core.Hosting;
using AgentRouter.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var managedChild = args is ["--managed-child"];
var builder = Host.CreateApplicationBuilder(managedChild ? [] : args);
builder.Configuration.AddDotEnvFile(optional: true);
builder.Services.AddSingleton<GatewayHost>();
builder.Services.AddGatewayStorage();
builder.Services.AddSingleton<GatewayConfigurationService>();
builder.Services.AddSingleton<GatewayServiceController>();

using var host = builder.Build();
if (managedChild)
    return await host
        .Services.GetRequiredService<GatewayServiceController>()
        .RunChildAsync(CancellationToken.None);
var app = CliCommandAppFactory.Create(host.Services);
return await app.RunAsync(args);
