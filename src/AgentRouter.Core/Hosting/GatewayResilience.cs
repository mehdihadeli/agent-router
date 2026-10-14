using System.Globalization;
using System.Threading.RateLimiting;
using AgentRouter.Core.Routes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Registry;
using Polly.Retry;
using Polly.Telemetry;

namespace AgentRouter.Core.Hosting;

internal sealed class GatewayResilience(
    ClaudeGatewayOptions options,
    ResiliencePipelineProvider<string> pipelines,
    int concurrencyLimit
) : IDisposable
{
    private readonly Dictionary<string, ConcurrencyLimiter> _limiters = Selectors(options)
        .ToDictionary(
            selector => selector,
            _ => new ConcurrencyLimiter(
                new ConcurrencyLimiterOptions { PermitLimit = concurrencyLimit, QueueLimit = 0 }
            ),
            StringComparer.Ordinal
        );
    private const string RetryPipeline = "agent-router-retry";
    private const string ProviderPrefix = "agent-router-provider:";
    private static readonly ResiliencePropertyKey<int> AttemptLimit = new("gateway-attempt-limit");
    private static readonly ResiliencePropertyKey<RetryMetadata> MetadataKey = new(
        "gateway-retry-metadata"
    );
    private static readonly AsyncLocal<RetryMetadata?> CurrentMetadata = new();

    private sealed class RetryMetadata
    {
        public TimeSpan? Delay { get; set; }
    }

    internal static IServiceCollection Register(
        IServiceCollection services,
        ClaudeGatewayOptions? options = null,
        int concurrencyLimit = 64,
        int circuitMinimumThroughput = 10
    )
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(GatewayResilience)))
            return services;
        services.AddSingleton(provider => new GatewayResilience(
            options ?? provider.GetRequiredService<IOptions<ClaudeGatewayOptions>>().Value,
            provider.GetRequiredService<ResiliencePipelineProvider<string>>(),
            concurrencyLimit
        ));
        services.AddResiliencePipeline(
            RetryPipeline,
            builder =>
            {
                builder.AddRetry(
                    new RetryStrategyOptions
                    {
                        MaxRetryAttempts = 7,
                        Delay = TimeSpan.FromMilliseconds(200),
                        MaxDelay = TimeSpan.FromSeconds(5),
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                        ShouldHandle = args =>
                            ValueTask.FromResult(
                                !args.Context.CancellationToken.IsCancellationRequested
                                    && args.AttemptNumber + 1
                                        < args.Context.Properties.GetValue(AttemptLimit, 1)
                                    && IsTransient(args.Outcome.Exception)
                            ),
                        DelayGenerator = args =>
                            ValueTask.FromResult(
                                RetryDelay(args.Outcome.Exception)
                                    ?? args.Context.Properties.GetValue(
                                        MetadataKey,
                                        new RetryMetadata()
                                    ).Delay
                            ),
                    }
                );
                builder.ConfigureTelemetry(
                    new TelemetryOptions { LoggerFactory = NullLoggerFactory.Instance }
                );
            }
        );
        services.AddResiliencePipelines<string>(context =>
        {
            var configured =
                options
                ?? context
                    .ServiceProvider.GetRequiredService<IOptions<ClaudeGatewayOptions>>()
                    .Value;
            foreach (var key in Selectors(configured))
                context.AddResiliencePipeline(
                    ProviderPrefix + key,
                    (builder, _) =>
                    {
                        builder.AddCircuitBreaker(
                            new CircuitBreakerStrategyOptions
                            {
                                FailureRatio = 0.5,
                                MinimumThroughput = circuitMinimumThroughput,
                                SamplingDuration = TimeSpan.FromSeconds(30),
                                BreakDuration = TimeSpan.FromSeconds(15),
                                ShouldHandle = args =>
                                    ValueTask.FromResult(IsTransient(args.Outcome.Exception)),
                            }
                        );
                        builder.ConfigureTelemetry(
                            new TelemetryOptions { LoggerFactory = NullLoggerFactory.Instance }
                        );
                    }
                );
        });
        return services;
    }

    private static IEnumerable<string> Selectors(ClaudeGatewayOptions options) =>
        options
            .Providers.SelectMany(provider =>
                provider.Models.Select(model =>
                    RouteModelFactory.GetProviderModelName(provider, model)
                )
            )
            .Distinct(StringComparer.Ordinal);

    public void Dispose()
    {
        foreach (var limiter in _limiters.Values)
            limiter.Dispose();
    }

    internal async ValueTask<TResult> ExecuteAsync<TResult>(
        int maxAttempts,
        Func<int, CancellationToken, ValueTask<TResult>> callback,
        CancellationToken cancellationToken
    )
    {
        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        var metadata = new RetryMetadata();
        context.Properties.Set(AttemptLimit, maxAttempts);
        context.Properties.Set(MetadataKey, metadata);
        var attempt = 0;
        try
        {
            return await pipelines
                .GetPipeline(RetryPipeline)
                .ExecuteAsync(
                    async execution =>
                    {
                        var previous = CurrentMetadata.Value;
                        metadata.Delay = null;
                        CurrentMetadata.Value = metadata;
                        try
                        {
                            return await callback(attempt++, execution.CancellationToken)
                                .ConfigureAwait(false);
                        }
                        finally
                        {
                            CurrentMetadata.Value = previous;
                        }
                    },
                    context
                )
                .ConfigureAwait(false);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    internal ValueTask<TResult> ExecuteProviderAsync<TResult>(
        string selector,
        Func<CancellationToken, ValueTask<TResult>> callback,
        CancellationToken cancellationToken
    ) => pipelines.GetPipeline(ProviderPrefix + selector).ExecuteAsync(callback, cancellationToken);

    internal async ValueTask<RateLimitLease> AcquireAsync(
        string selector,
        CancellationToken cancellationToken
    )
    {
        var lease = await _limiters[selector]
            .AcquireAsync(1, cancellationToken)
            .ConfigureAwait(false);
        if (lease.IsAcquired)
            return lease;
        lease.Dispose();
        throw new RateLimiterRejectedException();
    }

    internal static void RecordRetryAfter(TimeSpan? delay)
    {
        if (CurrentMetadata.Value is { } metadata && delay is { } value && value >= TimeSpan.Zero)
            metadata.Delay = value;
    }

    internal static bool IsTransient(Exception? error)
    {
        var status = error switch
        {
            HttpRequestException http => (int?)http.StatusCode,
            System.ClientModel.ClientResultException sdk => sdk.Status == 0 ? null : sdk.Status,
            Azure.RequestFailedException azure => azure.Status == 0 ? null : azure.Status,
            Anthropic.Exceptions.AnthropicApiException anthropic => (int)anthropic.StatusCode,
            Anthropic.Exceptions.AnthropicServiceException
            {
                InnerException: HttpRequestException http
            } => (int?)http.StatusCode,
            BrokenCircuitException => 503,
            RateLimiterRejectedException => 429,
            _ => 0,
        };
        return status is null or 408 or 429 or >= 500;
    }

    private static TimeSpan? RetryDelay(Exception? error)
    {
        string? value;
        if (
            error is System.ClientModel.ClientResultException sdk
            && sdk.GetRawResponse() is { } response
        )
        {
            if (
                response.Headers.TryGetValue("retry-after-ms", out value)
                && ParseDelay(value, milliseconds: true) is { } delay
            )
                return delay;
            if (response.Headers.TryGetValue("Retry-After", out value))
                return ParseDelay(value);
        }
        if (
            error is Azure.RequestFailedException azure
            && azure.GetRawResponse() is { } azureResponse
        )
        {
            if (
                azureResponse.Headers.TryGetValue("retry-after-ms", out value)
                && ParseDelay(value, milliseconds: true) is { } delay
            )
                return delay;
            if (azureResponse.Headers.TryGetValue("Retry-After", out value))
                return ParseDelay(value);
        }
        return null;
    }

    internal static TimeSpan? ParseDelay(string? value, bool milliseconds = false)
    {
        if (
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number)
            && number >= 0
        )
            return TimeSpan.FromMilliseconds(
                Math.Min(number * (milliseconds ? 1 : 1000), int.MaxValue - 1)
            );
        if (
            !milliseconds
            && DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var date
            )
        )
        {
            var remaining = date - DateTimeOffset.UtcNow;
            return TimeSpan.FromMilliseconds(
                Math.Clamp(remaining.TotalMilliseconds, 0, int.MaxValue - 1)
            );
        }
        return null;
    }
}
