using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Threading.RateLimiting;
using AgentRouter.Core.Api.ErrorHandling.Exceptions;
using AgentRouter.Core.Constants;
using AgentRouter.Core.Contracts.Sessions;
using AgentRouter.Core.Routes;
using AgentRouter.Core.Storage;
using Microsoft.Extensions.AI;

namespace AgentRouter.Core.Hosting;

public sealed class GatewayInferenceClient(
    IServiceProvider services,
    ClaudeGatewayOptions gateway,
    HttpContext context,
    string? requestedModel
) : IChatClient
{
    private readonly GatewayResilience _resilience =
        services.GetRequiredService<GatewayResilience>();
    public const string TelemetryName = "AgentRouter.Gateway";
    private static readonly ActivitySource Activities = new(TelemetryName);
    private static readonly Meter Meter = new(TelemetryName);
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>(
        "agentrouter.inference.requests"
    );
    private static readonly Counter<long> Attempts = Meter.CreateCounter<long>(
        "agentrouter.inference.attempts"
    );
    private static readonly UpDownCounter<long> ActiveRequests = Meter.CreateUpDownCounter<long>(
        "agentrouter.inference.active"
    );
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "agentrouter.inference.duration",
        "s"
    );
    private static readonly Histogram<long> InputTokens = Meter.CreateHistogram<long>(
        "agentrouter.inference.input_tokens",
        "{token}"
    );
    private static readonly Histogram<long> OutputTokens = Meter.CreateHistogram<long>(
        "agentrouter.inference.output_tokens",
        "{token}"
    );

    private SettingsRouteDecision Decide(IReadOnlyList<ChatMessage> messages)
    {
        if (gateway.RoutingSettings is { } settings)
        {
            try
            {
                return SettingsRouteEvaluator.Preview(
                    settings,
                    new SettingsRouteContext(
                        requestedModel,
                        Header("X-AR-Agent"),
                        Header("X-AR-Task"),
                        Header("X-AR-Subagent"),
                        Header("X-AR-Workflow"),
                        string.Join('\n', messages.Select(message => message.Text))
                    )
                );
            }
            catch (InvalidOperationException)
            {
                throw new BadRequestException(
                    "Requested model or routing context does not match the active policy."
                );
            }
        }
        var route = RouteModelFactory.ResolveRequestModel(gateway, requestedModel);
        return new SettingsRouteDecision(
            route.Name,
            route.Model,
            "legacy",
            null,
            null,
            null,
            [$"{route.Name}/{route.Model}"],
            gateway.MaxProviderAttempts,
            gateway.RequestTimeoutSeconds
        );
    }

    private string? Header(string name)
    {
        var value = context.Request.Headers[name].ToString();
        if (value.Length > 128 || value.Any(char.IsControl))
            throw new BadRequestException("Invalid routing context header.");
        return value.Length == 0 ? null : value;
    }

    private (IChatClient Client, ChatOptions Options, string Provider, string Model) Attempt(
        string selector,
        ChatOptions? original,
        SettingsRouteDecision decision
    )
    {
        var route = RouteModelFactory.Resolve(gateway, selector);
        var options = original?.Clone() ?? new ChatOptions();
        options.ModelId = route.Model;
        options.MaxOutputTokens = decision.MaxOutputTokens ?? options.MaxOutputTokens;
        options.Temperature = decision.Temperature ?? options.Temperature;
        var client =
            services.GetKeyedService<IChatClient>(selector)
            ?? (
                gateway.RoutingSettings is null
                    ? services.GetKeyedService<IChatClient>(RouteModelKeys.Default)
                        ?? services.GetService<IChatClient>()
                    : null
            )
            ?? throw new InvalidOperationException("No client registered for the selected route.");
        Attempts.Add(1, new KeyValuePair<string, object?>("provider", route.Name));
        return (client, options, route.Name, route.Model);
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var input = messages.ToList();
        var decision = Decide(input);
        var callerToken = cancellationToken;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(decision.DeadlineSeconds));
        cancellationToken = deadline.Token;
        using var activity = Activities.StartActivity("gateway.inference");
        activity?.SetTag("agentrouter.requested_model", requestedModel);
        activity?.SetTag("agentrouter.streaming", false);
        activity?.SetTag("agentrouter.input_messages", input.Count);
        activity?.SetTag("agentrouter.route.rule", decision.Rule);
        ActiveRequests.Add(1);
        var started = Stopwatch.GetTimestamp();
        var outcome = "error";
        var dispatches = 0;
        string? provider = null;
        string? model = null;
        UsageDetails? usage = null;
        try
        {
            var candidates = decision.Candidates.Take(decision.MaxAttempts).ToArray();
            var response = await _resilience.ExecuteAsync<ChatResponse>(
                decision.MaxAttempts,
                async (index, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    var selector = candidates[Math.Min(index, candidates.Length - 1)];
                    using var lease = await _resilience.AcquireAsync(selector, token);
                    return await _resilience.ExecuteProviderAsync<ChatResponse>(
                        selector,
                        async providerToken =>
                        {
                            var attempt = Attempt(selector, options, decision);
                            dispatches++;
                            provider = attempt.Provider;
                            model = attempt.Model;
                            activity?.SetTag("gen_ai.provider.name", attempt.Provider);
                            activity?.SetTag("gen_ai.request.model", attempt.Model);
                            return await attempt.Client.GetResponseAsync(
                                input,
                                attempt.Options,
                                providerToken
                            );
                        },
                        token
                    );
                },
                cancellationToken
            );
            response.ModelId = model;
            usage = response.Usage;
            outcome = "ok";
            return response;
        }
        catch (OperationCanceledException)
            when (deadline.IsCancellationRequested && !callerToken.IsCancellationRequested)
        {
            throw new TimeoutException("Inference request deadline exceeded.");
        }
        finally
        {
            var finalOutcome =
                callerToken.IsCancellationRequested ? "cancelled"
                : deadline.IsCancellationRequested ? "timeout"
                : outcome;
            Record(activity, started, finalOutcome, dispatches, provider, model, usage, false);
            ActiveRequests.Add(-1);
            RecordSummary(started, finalOutcome, dispatches, provider, model, usage);
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var input = messages.ToList();
        var decision = Decide(input);
        var callerToken = cancellationToken;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(decision.DeadlineSeconds));
        cancellationToken = deadline.Token;
        using var activity = Activities.StartActivity("gateway.inference");
        activity?.SetTag("agentrouter.requested_model", requestedModel);
        activity?.SetTag("agentrouter.streaming", true);
        activity?.SetTag("agentrouter.input_messages", input.Count);
        activity?.SetTag("agentrouter.route.rule", decision.Rule);
        ActiveRequests.Add(1);
        var started = Stopwatch.GetTimestamp();
        var outcome = "error";
        var dispatches = 0;
        string? provider = null;
        string? model = null;
        var usage = new UsageDetails();
        var finished = false;
        IAsyncEnumerator<ChatResponseUpdate>? updates = null;
        RateLimitLease? lease = null;
        try
        {
            var candidates = decision.Candidates.Take(decision.MaxAttempts).ToArray();
            try
            {
                await _resilience.ExecuteAsync<bool>(
                    decision.MaxAttempts,
                    async (index, token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        var selector = candidates[Math.Min(index, candidates.Length - 1)];
                        lease = await _resilience.AcquireAsync(selector, token);
                        try
                        {
                            return await _resilience.ExecuteProviderAsync<bool>(
                                selector,
                                async providerToken =>
                                {
                                    var attempt = Attempt(selector, options, decision);
                                    dispatches++;
                                    provider = attempt.Provider;
                                    model = attempt.Model;
                                    activity?.SetTag("gen_ai.provider.name", attempt.Provider);
                                    activity?.SetTag("gen_ai.request.model", attempt.Model);
                                    updates = attempt
                                        .Client.GetStreamingResponseAsync(
                                            input,
                                            attempt.Options,
                                            providerToken
                                        )
                                        .GetAsyncEnumerator(providerToken);
                                    if (!await AdvanceAsync(updates, deadline.Token, callerToken))
                                        throw new InvalidOperationException(
                                            "Upstream returned an empty stream."
                                        );
                                    return true;
                                },
                                token
                            );
                        }
                        catch
                        {
                            try
                            {
                                if (updates is not null)
                                    await updates.DisposeAsync();
                            }
                            finally
                            {
                                updates = null;
                                lease.Dispose();
                                lease = null;
                            }
                            throw;
                        }
                    },
                    cancellationToken
                );
            }
            catch (OperationCanceledException)
                when (deadline.IsCancellationRequested && !callerToken.IsCancellationRequested)
            {
                throw new TimeoutException("Inference request deadline exceeded.");
            }
            do
            {
                updates!.Current.ModelId = model;
                finished |= updates.Current.FinishReason is not null;
                foreach (var reported in updates.Current.Contents.OfType<UsageContent>())
                    usage.Add(reported.Details);
                yield return updates.Current;
            } while (await AdvanceAsync(updates, deadline.Token, callerToken));
            if (!finished)
                throw new InvalidOperationException(
                    "Upstream stream ended without a finish reason."
                );
            outcome = "ok";
        }
        finally
        {
            try
            {
                if (updates is not null)
                    await updates.DisposeAsync();
            }
            finally
            {
                lease?.Dispose();
            }
            var finalOutcome =
                callerToken.IsCancellationRequested ? "cancelled"
                : deadline.IsCancellationRequested ? "timeout"
                : outcome;
            Record(activity, started, finalOutcome, dispatches, provider, model, usage, true);
            ActiveRequests.Add(-1);
            RecordSummary(started, finalOutcome, dispatches, provider, model, usage);
        }
    }

    private static async ValueTask<bool> AdvanceAsync(
        IAsyncEnumerator<ChatResponseUpdate> updates,
        CancellationToken deadline,
        CancellationToken caller
    )
    {
        try
        {
            return await updates.MoveNextAsync();
        }
        catch (OperationCanceledException)
            when (deadline.IsCancellationRequested && !caller.IsCancellationRequested)
        {
            throw new TimeoutException("Inference request deadline exceeded.");
        }
    }

    private static void Record(
        Activity? activity,
        long started,
        string outcome,
        int dispatches,
        string? provider,
        string? model,
        UsageDetails? usage,
        bool streaming
    )
    {
        activity?.SetTag("agentrouter.outcome", outcome);
        activity?.SetTag("agentrouter.dispatches", dispatches);
        activity?.SetTag("gen_ai.response.model", model);
        activity?.SetTag("gen_ai.usage.input_tokens", usage?.InputTokenCount);
        activity?.SetTag("gen_ai.usage.output_tokens", usage?.OutputTokenCount);
        if (outcome != "ok")
            activity?.SetStatus(ActivityStatusCode.Error);
        var tags = new TagList
        {
            { "outcome", outcome },
            { "streaming", streaming },
            { "provider", provider },
        };
        Requests.Add(1, tags);
        Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
        if (usage?.InputTokenCount is { } inputTokens)
            InputTokens.Record(inputTokens, tags);
        if (usage?.OutputTokenCount is { } outputTokens)
            OutputTokens.Record(outputTokens, tags);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    private void RecordSummary(
        long started,
        string outcome,
        int dispatches,
        string? provider,
        string? model,
        UsageDetails? usage
    )
    {
        try
        {
            services
                .GetService<GatewayRequestHistory>()
                ?.TryRecord(
                    new GatewayRequestSummary(
                        Guid.NewGuid().ToString("N"),
                        DateTimeOffset.UtcNow,
                        requestedModel is { Length: <= 256 } ? requestedModel : null,
                        provider,
                        model,
                        Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                        outcome,
                        dispatches,
                        gateway.MaxProviderAttempts,
                        usage?.InputTokenCount,
                        usage?.OutputTokenCount
                    )
                );
            services
                .GetService<ILogger<GatewayInferenceClient>>()
                ?.LogInformation(
                    "Gateway inference {Outcome}; provider {Provider}; model {Model}; dispatches {Dispatches}; elapsed {ElapsedMs} ms.",
                    outcome,
                    provider,
                    model,
                    dispatches,
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds
                );
        }
        catch (Exception error) when (error is not OutOfMemoryException) { }
    }

    public void Dispose() { }
}
