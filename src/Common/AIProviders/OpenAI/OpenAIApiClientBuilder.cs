using Microsoft.Extensions.Hosting;
using OpenAI;

namespace Common.AIProviders.OpenAI;

public class OpenAIApiClientBuilder(IHostApplicationBuilder hostBuilder, object? serviceKey, bool disableTracing)
{
    /// <summary>
    /// The host application builder used to configure the application.
    /// </summary>
    public IHostApplicationBuilder HostBuilder { get; } = hostBuilder;

    /// <summary>
    /// Gets the service key used to register the <see cref="OpenAIClient"/> service, if any.
    /// </summary>
    public object? ServiceKey { get; } = serviceKey;

    /// <summary>
    /// Gets a flag indicating whether tracing should be disabled.
    /// </summary>
    public bool DisableTracing { get; } = disableTracing;
}
