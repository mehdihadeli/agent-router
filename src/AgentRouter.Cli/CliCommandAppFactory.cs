using Spectre.Console.Cli;

namespace AgentRouter.Cli;

internal static class CliCommandAppFactory
{
    public static CommandApp Create(IServiceProvider services)
    {
        var app = new CommandApp(new ServiceProviderTypeRegistrar(services));
        app.Configure(config =>
        {
            config.UseStrictParsing();
            config.SetApplicationName("ar");
            config
                .AddCommand<UiCommand>("ui")
                .WithDescription(
                    "Start or reuse local management, pair a browser, and open the Angular UI. Does not start inference."
                )
                .WithExample("ui")
                .WithExample("ui", "--no-open");
            config.SetApplicationVersion(
                typeof(CliCommandAppFactory).Assembly.GetName().Version?.ToString() ?? "unknown"
            );
            config
                .AddCommand<ServeCommand>("serve")
                .WithDescription("Run model requests in this terminal; Ctrl+C stops the gateway.")
                .WithExample("serve")
                .WithExample("serve", "--port", "4600")
                .WithExample("serve", "--url", "http://127.0.0.1:4600");
            config
                .AddCommand<InitCommand>("init")
                .WithDescription(
                    "Create local configuration and gateway token; keep existing settings."
                )
                .WithExample("init", "--port", "4600");
            config.AddBranch(
                "service",
                branch =>
                {
                    branch.SetDescription(
                        "Start, inspect or stop the background process that hosts management and the gateway."
                    );
                    branch
                        .AddCommand<StartCommand>("start")
                        .WithDescription(
                            "Run management and inference in background, or reuse a verified service. Exit 0 means management is available; use service status to check inference readiness."
                        )
                        .WithExample("service", "start");
                    branch
                        .AddCommand<StopCommand>("stop")
                        .WithDescription(
                            "Stop the background service and its gateway after active requests finish."
                        )
                        .WithExample("service", "stop");
                    branch
                        .AddCommand<ServiceStatusCommand>("status")
                        .WithDescription(
                            "Show management availability and live inference state separately, with listener addresses and failure details. Exit 0 means management is available even when inference is Stopped or Failed."
                        )
                        .WithExample("service", "status");
                    branch
                        .AddCommand<GatewayStopCommand>("pause")
                        .WithDescription(
                            "Drain active model requests and stop the inference listener. Management and configuration APIs stay available. Use service resume to start model handling again; requires a running service."
                        )
                        .WithExample("service", "pause");
                    branch
                        .AddCommand<GatewayStartCommand>("resume")
                        .WithDescription(
                            "Start stopped inference using saved settings without restarting management. Already-running inference is reused, not reconfigured; use service reload to apply changes. Requires a running service."
                        )
                        .WithExample("service", "resume");
                    branch
                        .AddCommand<GatewayRestartCommand>("reload")
                        .WithDescription(
                            "Validate saved settings, drain active requests and restart only inference; management stays available. Invalid settings leave running inference unchanged; a later startup failure can leave inference stopped. Also starts stopped inference. Not seamless hot reload."
                        )
                        .WithExample("service", "reload");
                    branch
                        .AddCommand<WebCommand>("serve")
                        .WithDescription(
                            "Run management and gateway in this terminal, for debugging or a process supervisor."
                        )
                        .WithExample("service", "serve", "--no-gateway");
                }
            );
            config
                .AddCommand<StatusCommand>("status")
                .WithDescription(
                    "Check a standalone gateway started with serve; use service status for background service."
                )
                .WithExample("status", "--url", "http://127.0.0.1:4600");
            config
                .AddCommand<ObservabilityCommand>("observability")
                .WithDescription(
                    "Show sanitized request health, latency, token, and provider metrics from retained history."
                )
                .WithExample("observability")
                .WithExample("observability", "--hours", "168");
            config.AddBranch(
                "config",
                branch =>
                {
                    branch
                        .AddCommand<ConfigValidateCommand>("validate")
                        .WithDescription(
                            "Validate versioned settings and process-local overrides without saving, resolving credentials or applying them."
                        )
                        .WithExample(
                            "config",
                            "validate",
                            "--file",
                            "settings.json",
                            "--set",
                            "Server:Port=4600"
                        );
                    branch
                        .AddCommand<ConfigShowCommand>("show")
                        .WithDescription("Show saved settings without exposing credentials.")
                        .WithExample("config", "show");
                    branch
                        .AddCommand<ConfigTokenCommand>("token")
                        .WithDescription(
                            "Print a sensitive gateway token for client setup; --managed selects management token. Never share output."
                        )
                        .WithExample("config", "token");
                }
            );
            config.AddBranch(
                "route",
                branch =>
                {
                    branch
                        .AddCommand<RoutePreviewCommand>("preview")
                        .WithDescription(
                            "Evaluate versioned settings against request labels; no provider calls, secret resolution or runtime changes."
                        )
                        .WithExample(
                            "route",
                            "preview",
                            "--file",
                            "settings.json",
                            "--task",
                            "review"
                        );
                    branch.SetDescription(
                        "Choose the provider/model used by default or for a Claude model tier."
                    );
                    branch
                        .AddCommand<ProviderUseCommand>("set")
                        .WithDescription(
                            "Save the default or tier model; restart a running gateway to apply it."
                        )
                        .WithExample("route", "set", "work/model")
                        .WithExample("route", "set", "work/model", "--tier", "Sonnet");
                }
            );
            config.AddBranch(
                "provider",
                branch =>
                {
                    branch
                        .AddCommand<ProviderAddCommand>("add")
                        .WithDescription(
                            "Save an upstream endpoint and model names; read its key from environment or private prompt."
                        )
                        .WithExample(
                            "provider",
                            "add",
                            "work",
                            "--base-url",
                            "https://api.openai.com/v1",
                            "--models",
                            "gpt-4.1"
                        );
                    branch
                        .AddCommand<ProviderListCommand>("list")
                        .WithDescription(
                            "Show saved providers and their model names, without keys."
                        )
                        .WithExample("provider", "list");
                    branch
                        .AddCommand<ProviderRemoveCommand>("remove")
                        .WithDescription(
                            "Delete a provider and its saved key; first remove its route/profile references."
                        )
                        .WithExample("provider", "remove", "unused");
                    branch
                        .AddCommand<ProviderCheckCommand>("check")
                        .WithDescription(
                            "Test an upstream model with a small real request; may incur charges."
                        )
                        .WithExample("provider", "check", "work/model");
                }
            );
            config.AddBranch(
                "profile",
                branch =>
                {
                    branch
                        .AddCommand<ProfileAddCommand>("add")
                        .WithDescription(
                            "Save a named Claude Code launch configuration without changing global agent settings."
                        )
                        .WithExample("profile", "add", "Review", "--model", "work/model");
                    branch
                        .AddCommand<ProfileListCommand>("list")
                        .WithDescription(
                            "Show saved Claude Code profiles and their selected models."
                        )
                        .WithExample("profile", "list");
                    branch
                        .AddCommand<ProfileEditCommand>("edit")
                        .WithDescription(
                            "Update a saved agent profile without changing global agent settings."
                        )
                        .WithExample("profile", "edit", "Review", "--tier-sonnet", "work/model");
                    branch
                        .AddCommand<ProfileRemoveCommand>("remove")
                        .WithDescription("Remove a saved agent profile.")
                        .WithExample("profile", "remove", "Review");
                    branch
                        .AddCommand<LaunchCommand>("launch")
                        .WithDescription(
                            "Launch a saved profile against a running local gateway; pass agent options after --."
                        )
                        .WithExample(
                            "profile",
                            "launch",
                            "Review",
                            "--",
                            "--permission-mode",
                            "plan"
                        );
                }
            );
        });
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
