using AgentRouter.Core.Contracts;
using AgentRouter.Core.Hosting;
using Anthropic;
using Anthropic.Core;
using Microsoft.Extensions.AI;

namespace AgentRouter.Core.Providers.Anthropic;

internal static class AnthropicProvider
{
    internal static void Register(
        IHostApplicationBuilder builder,
        ClaudeGatewayProvider provider,
        string model,
        object serviceKey,
        bool enableTelemetry,
        int maxProviderAttempts
    )
    {
        var pipeline = builder.Services.AddKeyedChatClient(
            serviceKey,
            _ =>
                new AnthropicClient(
                    new ClientOptions
                    {
                        ApiKey = provider.ApiKey,
                        BaseUrl = provider.BaseAddress,
                        MaxRetries = maxProviderAttempts - 1,
                        Handlers = [new RetryAfterHandler()],
                    }
                ).AsIChatClient(defaultModelId: model, defaultMaxOutputTokens: 1024)
        );
        if (enableTelemetry)
            pipeline.UseOpenTelemetry(
                sourceName: "Experimental.Microsoft.Extensions.AI",
                configure: telemetry =>
                    telemetry.EnableSensitiveData = builder.Configuration.GetValue<bool>(
                        "Telemetry:EnableSensitiveData"
                    )
            );
        pipeline.UseLogging();
    }

    private sealed class RetryAfterHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var delay = response.Headers.TryGetValues("retry-after-ms", out var values)
                ? GatewayResilience.ParseDelay(values.FirstOrDefault(), milliseconds: true)
                : null;
            delay ??= GatewayResilience.ParseDelay(response.Headers.RetryAfter?.ToString());
            GatewayResilience.RecordRetryAfter(delay);
            return response;
        }
    }
}
