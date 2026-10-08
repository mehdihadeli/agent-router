using Spectre.Console.Cli;

namespace ClaudeCodeGateway.Cli;

internal static class CliCommandAppFactory
{
    public static CommandApp Create(IServiceProvider services)
    {
        var app = new CommandApp(new ServiceProviderTypeRegistrar(services));
        app.Configure(config =>
        {
            config.SetApplicationName("claude-code-gateway");
            config.SetApplicationVersion(
                typeof(CliCommandAppFactory).Assembly.GetName().Version?.ToString() ?? "unknown"
            );
            config
                .AddCommand<ServeCommand>("serve")
                .WithAlias("s")
                .WithAlias("run")
                .WithDescription("Run the Claude Code gateway.")
                .WithExample("serve")
                .WithExample("serve", "--port", "4600")
                .WithExample("s", "-u", "http://127.0.0.1:4600");
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
