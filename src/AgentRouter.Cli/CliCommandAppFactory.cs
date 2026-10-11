using Spectre.Console.Cli;

namespace AgentRouter.Cli;

internal static class CliCommandAppFactory
{
    public static CommandApp Create(IServiceProvider services)
    {
        var app = new CommandApp(new ServiceProviderTypeRegistrar(services));
        app.Configure(config =>
        {
            config.SetApplicationName("ar");
            config.SetApplicationVersion(
                typeof(CliCommandAppFactory).Assembly.GetName().Version?.ToString() ?? "unknown"
            );
            config
                .AddCommand<ServeCommand>("serve")
                .WithAlias("s")
                .WithAlias("run")
                .WithDescription("Run the Agent Router model gateway.")
                .WithExample("serve")
                .WithExample("serve", "--port", "4600")
                .WithExample("s", "-u", "http://127.0.0.1:4600");
            config.AddCommand<InitCommand>("init").WithDescription("Initialize encrypted local configuration.");
            config.AddCommand<StartCommand>("start").WithDescription("Start or reuse an authenticated background management service.");
            config.AddCommand<StopCommand>("stop").WithDescription("Gracefully stop the verified managed service.");
            config.AddCommand<WebCommand>("web").WithDescription("Run authenticated management API in foreground (UI not yet included).");
            config.AddCommand<StatusCommand>("status").WithDescription("Check authenticated gateway availability.");
            config.AddCommand<LaunchCommand>("launch").WithDescription("Launch a Claude Code profile without changing global settings.");
            config.AddBranch("config", branch =>
            {
                branch.AddCommand<ConfigShowCommand>("show").WithDescription("Show configuration with secrets masked.");
                branch.AddCommand<ConfigTokenCommand>("token").WithDescription("Print sensitive client token, or management token with --managed. Do not share this output.");
            });
            config.AddBranch("provider", branch =>
            {
                branch.AddCommand<ProviderAddCommand>("add").WithDescription("Add an OpenAI or Anthropic API-key provider.");
                branch.AddCommand<ProviderListCommand>("list").WithDescription("List configured providers and models.");
                branch.AddCommand<ProviderRemoveCommand>("remove").WithDescription("Remove an unreferenced provider and its encrypted credentials.");
                branch.AddCommand<ProviderUseCommand>("use").WithDescription("Set the default or tier model route.");
                branch.AddCommand<ProviderCheckCommand>("check").WithDescription("Send a small real upstream request (may incur charges).");
            });
            config.AddBranch("profile", branch =>
            {
                branch.AddCommand<ProfileAddCommand>("add").WithDescription("Create an isolated Claude Code launch profile.");
                branch.AddCommand<ProfileListCommand>("list").WithDescription("List saved launch profiles.");
            });
        });
        app.SetDefaultCommand<ServeCommand>();
        return app;
    }
}

internal sealed class ServiceProviderTypeRegistrar(IServiceProvider services) : ITypeRegistrar
{
    public ITypeResolver Build() => new ServiceProviderTypeResolver(services);

    public void Register(Type service, Type implementation) { }

    public void RegisterInstance(Type service, object implementation) { }

    public void RegisterLazy(Type service, Func<object> factory) { }
}

internal sealed class ServiceProviderTypeResolver(IServiceProvider services) : ITypeResolver
{
    public object? Resolve(Type? type) =>
        type is null
            ? null
            : services.GetService(type) ?? ActivatorUtilities.CreateInstance(services, type);
}
