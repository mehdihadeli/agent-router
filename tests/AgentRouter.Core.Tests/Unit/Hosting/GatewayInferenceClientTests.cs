using System.Net;
using System.Runtime.CompilerServices;
using AgentRouter.Core;
using AgentRouter.Core.Contracts;
using AgentRouter.Core.Contracts.Configuration;
using AgentRouter.Core.Hosting;
using AgentRouter.Core.Routes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgentRouter.Core.Tests.Unit.Hosting;

public sealed class GatewayInferenceClientTests
{
    [Theory]
    [InlineData(503, 2, 2)]
    [InlineData(0, 2, 2)]
    [InlineData(503, 1, 1)]
    [InlineData(401, 2, 1)]
    [InlineData(400, 2, 1)]
    public async Task ResiliencePipeline_RetriesOnlyTransientFailuresWithinCap(
        int status,
        int cap,
        int expectedCalls
    )
    {
        var (options, _) = Policy();
        using var services = GatewayResilience
            .Register(new ServiceCollection(), options)
            .BuildServiceProvider();
        var resilience = services.GetRequiredService<GatewayResilience>();
        var calls = 0;
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await resilience.ExecuteAsync<int>(
                cap,
                (_, _) =>
                {
                    calls++;
                    return ValueTask.FromException<int>(
                        new HttpRequestException(
                            "synthetic private error",
                            null,
                            status == 0 ? null : (HttpStatusCode)status
                        )
                    );
                },
                TestContext.Current.CancellationToken
            )
        );
        Assert.Equal(expectedCalls, calls);
    }

    [Theory]
    [InlineData(503, true)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    public async Task CircuitBreaker_IsSharedPerModelAndExcludesAuthorizationFailures(
        int status,
        bool opens
    )
    {
        var (options, _) = Policy();
        using var services = GatewayResilience
            .Register(new ServiceCollection(), options, circuitMinimumThroughput: 2)
            .BuildServiceProvider();
        var resilience = services.GetRequiredService<GatewayResilience>();
        var calls = 0;
        for (var index = 0; index < 2; index++)
            await Assert.ThrowsAsync<HttpRequestException>(async () =>
                await resilience.ExecuteProviderAsync<int>(
                    "first/owner/main",
                    _ =>
                    {
                        calls++;
                        return ValueTask.FromException<int>(
                            new HttpRequestException("synthetic", null, (HttpStatusCode)status)
                        );
                    },
                    TestContext.Current.CancellationToken
                )
            );
        using var scope = services.CreateScope();
        var samePolicy = scope.ServiceProvider.GetRequiredService<GatewayResilience>();
        async Task<int> Execute() =>
            await samePolicy.ExecuteProviderAsync<int>(
                "first/owner/main",
                _ =>
                {
                    calls++;
                    return ValueTask.FromResult(7);
                },
                TestContext.Current.CancellationToken
            );
        if (opens)
            await Assert.ThrowsAsync<Polly.CircuitBreaker.BrokenCircuitException>(Execute);
        else
            Assert.Equal(7, await Execute());
        Assert.Equal(opens ? 2 : 3, calls);
        Assert.Equal(
            11,
            await samePolicy.ExecuteProviderAsync<int>(
                "second/backup",
                _ => ValueTask.FromResult(11),
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task CallerCancellation_DuringServerBackoff_DoesNotReplay()
    {
        var (options, _) = Policy();
        using var services = GatewayResilience
            .Register(new ServiceCollection(), options)
            .BuildServiceProvider();
        var resilience = services.GetRequiredService<GatewayResilience>();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var attempted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var calls = 0;
        var pending = resilience
            .ExecuteAsync<int>(
                3,
                (_, _) =>
                {
                    calls++;
                    GatewayResilience.RecordRetryAfter(TimeSpan.FromSeconds(60));
                    attempted.SetResult();
                    return ValueTask.FromException<int>(
                        new HttpRequestException(
                            "synthetic",
                            null,
                            HttpStatusCode.ServiceUnavailable
                        )
                    );
                },
                cancellation.Token
            )
            .AsTask();
        await attempted.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StreamAdmission_IsHeldUntilCompletionOrDisposal(bool complete)
    {
        var (options, context) = Policy(1);
        var primary = new FakeClient();
        using var services = GatewayResilience
            .Register(new ServiceCollection(), options, concurrencyLimit: 1)
            .AddKeyedSingleton<IChatClient>("first/owner/main", primary)
            .BuildServiceProvider();
        var client = new GatewayInferenceClient(services, options, context, null);
        await using var updates = client
            .GetStreamingResponseAsync(
                [new(ChatRole.User, "hello")],
                cancellationToken: TestContext.Current.CancellationToken
            )
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await updates.MoveNextAsync());
        await Assert.ThrowsAsync<Polly.RateLimiting.RateLimiterRejectedException>(() =>
            client.GetResponseAsync(
                [new(ChatRole.User, "blocked")],
                cancellationToken: TestContext.Current.CancellationToken
            )
        );
        Assert.Equal(1, primary.Calls);
        if (complete)
        {
            Assert.True(await updates.MoveNextAsync());
            Assert.Equal(ChatFinishReason.Stop, updates.Current.FinishReason);
            Assert.False(await updates.MoveNextAsync());
        }
        else
            await updates.DisposeAsync();
        var response = await client.GetResponseAsync(
            [new(ChatRole.User, "admitted")],
            cancellationToken: TestContext.Current.CancellationToken
        );
        Assert.Equal("hello", response.Text);
        Assert.Equal(2, primary.Calls);
        Assert.Equal(1, primary.StreamsDisposed);
    }

    [Fact]
    public async Task StreamingDeadline_AfterFirstUpdate_ReleasesAdmissionWithoutFallback()
    {
        var (options, context) = Policy();
        options.RoutingSettings!.Execution.DeadlineSeconds = 1;
        var primary = new FakeClient(stallAfterFirst: true);
        var backup = new FakeClient();
        using var services = GatewayResilience
            .Register(new ServiceCollection(), options, concurrencyLimit: 1)
            .AddKeyedSingleton<IChatClient>("first/owner/main", primary)
            .AddKeyedSingleton<IChatClient>("second/backup", backup)
            .BuildServiceProvider();
        var client = new GatewayInferenceClient(services, options, context, null);
        await using var updates = client
            .GetStreamingResponseAsync(
                [new(ChatRole.User, "hello")],
                cancellationToken: TestContext.Current.CancellationToken
            )
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await updates.MoveNextAsync());
        await Assert.ThrowsAsync<TimeoutException>(async () => await updates.MoveNextAsync());
        Assert.Equal(1, primary.Calls);
        Assert.Equal(0, backup.Calls);
        Assert.Equal(1, primary.StreamsDisposed);
        using var lease = await services
            .GetRequiredService<GatewayResilience>()
            .AcquireAsync("first/owner/main", TestContext.Current.CancellationToken);
        Assert.True(lease.IsAcquired);
    }

    [Fact]
    public async Task Fallback_ExhaustsCandidatesThenRetriesLastWithinSharedCap()
    {
        var (options, context) = Policy(3);
        var failure = new HttpRequestException(
            "synthetic",
            null,
            HttpStatusCode.ServiceUnavailable
        );
        var primary = new FakeClient(failure);
        var backup = new FakeClient(failure);
        using var services = GatewayResilience
            .Register(new ServiceCollection(), options)
            .AddKeyedSingleton<IChatClient>("first/owner/main", primary)
            .AddKeyedSingleton<IChatClient>("second/backup", backup)
            .BuildServiceProvider();
        var client = new GatewayInferenceClient(services, options, context, null);
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetResponseAsync(
                [new(ChatRole.User, "hello")],
                cancellationToken: TestContext.Current.CancellationToken
            )
        );
        Assert.Equal(1, primary.Calls);
        Assert.Equal(2, backup.Calls);
    }

    [Theory]
    [InlineData("1.25", false, 1250d)]
    [InlineData("125", true, 125d)]
    [InlineData("-1", false, null)]
    [InlineData("NaN", false, null)]
    [InlineData("invalid", false, null)]
    [InlineData("Thu, 01 Jan 1970 00:00:00 GMT", false, 0d)]
    [InlineData("Fri, 31 Dec 9999 23:59:59 GMT", false, 2147483646d)]
    public void RetryAfter_ParsesNumericAndHttpDatesWithoutInvalidTimerDelays(
        string header,
        bool milliseconds,
        double? expectedMilliseconds
    )
    {
        var delay = GatewayResilience.ParseDelay(header, milliseconds);
        Assert.Equal(expectedMilliseconds, delay?.TotalMilliseconds);
    }

    private static (ClaudeGatewayOptions Options, DefaultHttpContext Context) Policy(
        int maxAttempts = 2
    )
    {
        var settings = new GatewaySettings
        {
            Providers = new()
            {
                ["first"] = new()
                {
                    Endpoint = "https://first.test/v1",
                    Models = ["owner/main"],
                    Credentials = [new() { Environment = "FIRST_KEY" }],
                },
                ["second"] = new()
                {
                    Endpoint = "https://second.test/v1",
                    Models = ["backup"],
                    Credentials = [new() { Environment = "SECOND_KEY" }],
                },
            },
            Routing = new() { DefaultModel = "first/owner/main" },
            Execution = new() { MaxAttempts = maxAttempts, FallbackModels = ["second/backup"] },
        };
        return (
            new ClaudeGatewayOptions
            {
                RoutingSettings = settings,
                Providers =
                [
                    new ClaudeGatewayProvider
                    {
                        ProviderName = "first",
                        ProviderType = ProviderType.OpenAI,
                        BaseAddress = "https://first.test/v1",
                        ApiKey = "synthetic",
                        Models = ["owner/main"],
                    },
                    new ClaudeGatewayProvider
                    {
                        ProviderName = "second",
                        ProviderType = ProviderType.OpenAI,
                        BaseAddress = "https://second.test/v1",
                        ApiKey = "synthetic",
                        Models = ["backup"],
                    },
                ],
                RouteModel = new RouteModel
                {
                    DefaultModel = "first/owner/main",
                    ThinkingModel = "first/owner/main",
                    SearchModel = "first/owner/main",
                },
            },
            new DefaultHttpContext()
        );
    }

    [Fact]
    public async Task LiveDispatch_MatchesAllContextConditionsAndAppliesNumericRewrites()
    {
        var (options, context) = Policy();
        options.RoutingSettings!.Routing.Rules.Add(
            new SettingsRoute
            {
                Id = "review",
                Agent = "worker",
                Task = "review",
                Subagent = "critic",
                Workflow = "pull-request",
                ContainsText = "security",
                Model = "second/backup",
                Rewrite = new() { MaxOutputTokens = 77, Temperature = 0.2f },
            }
        );
        context.Request.Headers["X-AR-Agent"] = "worker";
        context.Request.Headers["X-AR-Task"] = "review";
        context.Request.Headers["X-AR-Subagent"] = "critic";
        context.Request.Headers["X-AR-Workflow"] = "pull-request";
        var selected = new FakeClient();
        using var services = GatewayResilience
            .Register(new ServiceCollection(), options)
            .AddKeyedSingleton<IChatClient>("second/backup", selected)
            .BuildServiceProvider();
        var client = new GatewayInferenceClient(services, options, context, null);
        var original = new ChatOptions
        {
            ModelId = "original",
            MaxOutputTokens = 100,
            Temperature = 0.7f,
        };
        var response = await client.GetResponseAsync(
            [new(ChatRole.User, "Review security")],
            original,
            TestContext.Current.CancellationToken
        );
        Assert.Equal("backup", response.ModelId);
        Assert.Equal(77, selected.LastOptions!.MaxOutputTokens);
        Assert.Equal(0.2f, selected.LastOptions.Temperature);
        Assert.Equal("original", original.ModelId);
        Assert.Equal(100, original.MaxOutputTokens);
        var preview = SettingsRouteEvaluator.Preview(
            options.RoutingSettings,
            new(null, "worker", "review", "critic", "pull-request", "Review security")
        );
        Assert.Equal(preview.Model, selected.LastOptions.ModelId);
        context.Request.Headers["X-AR-Workflow"] = "other";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetResponseAsync(
                [new(ChatRole.User, "Review security")],
                cancellationToken: TestContext.Current.CancellationToken
            )
        );
        Assert.Equal(1, selected.Calls);
    }

    [Theory]
    [InlineData(503, 2, true)]
    [InlineData(429, 2, true)]
    [InlineData(401, 2, false)]
    [InlineData(400, 2, false)]
    [InlineData(503, 1, false)]
    public async Task Fallback_UsesOnlyTransientErrorsWithinAggregateAttemptCap(
        int status,
        int cap,
        bool succeeds
    )
    {
        var (options, context) = Policy(cap);
        var primary = new FakeClient(
            new HttpRequestException("private upstream detail", null, (HttpStatusCode)status)
        );
        var backup = new FakeClient();
        using var services = GatewayResilience
            .Register(new ServiceCollection(), options)
            .AddKeyedSingleton<IChatClient>("first/owner/main", primary)
            .AddKeyedSingleton<IChatClient>("second/backup", backup)
            .BuildServiceProvider();
        var client = new GatewayInferenceClient(services, options, context, "first/owner/main");
        if (succeeds)
        {
            var response = await client.GetResponseAsync(
                [new(ChatRole.User, "hello")],
                cancellationToken: TestContext.Current.CancellationToken
            );
            Assert.Equal("backup", response.ModelId);
            Assert.Equal("backup", backup.LastOptions!.ModelId);
        }
        else
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                client.GetResponseAsync(
                    [new(ChatRole.User, "hello")],
                    cancellationToken: TestContext.Current.CancellationToken
                )
            );
        Assert.Equal(1, primary.Calls);
        Assert.Equal(succeeds ? 1 : 0, backup.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stream_FallsBackOnlyBeforeFirstUpdate(bool emitsFirst)
    {
        var (options, context) = Policy();
        var primary = new FakeClient(
            new HttpRequestException("transient", null, HttpStatusCode.ServiceUnavailable),
            emitsFirst
        );
        var backup = new FakeClient();
        using var services = GatewayResilience
            .Register(new ServiceCollection(), options)
            .AddKeyedSingleton<IChatClient>("first/owner/main", primary)
            .AddKeyedSingleton<IChatClient>("second/backup", backup)
            .BuildServiceProvider();
        var client = new GatewayInferenceClient(services, options, context, null);
        var updates = new List<ChatResponseUpdate>();
        async Task Read()
        {
            await foreach (
                var update in client.GetStreamingResponseAsync(
                    [new(ChatRole.User, "hello")],
                    cancellationToken: TestContext.Current.CancellationToken
                )
            )
                updates.Add(update);
        }
        if (emitsFirst)
            await Assert.ThrowsAsync<HttpRequestException>(Read);
        else
            await Read();
        Assert.Equal(emitsFirst ? 0 : 1, backup.Calls);
        Assert.Equal(emitsFirst ? "owner/main" : "backup", updates[0].ModelId);
        Assert.Equal("hello", updates[0].Text);
        Assert.Equal(1, primary.StreamsDisposed);
        Assert.Equal(emitsFirst ? 0 : 1, backup.StreamsDisposed);
        using var primaryLease = await services
            .GetRequiredService<GatewayResilience>()
            .AcquireAsync("first/owner/main", TestContext.Current.CancellationToken);
        Assert.True(primaryLease.IsAcquired);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deadline_IsTimeoutNotCallerCancellationAndNeverFallsBack(bool stream)
    {
        var (options, context) = Policy();
        options.RoutingSettings!.Execution.DeadlineSeconds = 1;
        var primary = new FakeClient(stall: true);
        var backup = new FakeClient();
        using var services = GatewayResilience
            .Register(new ServiceCollection(), options)
            .AddKeyedSingleton<IChatClient>("first/owner/main", primary)
            .AddKeyedSingleton<IChatClient>("second/backup", backup)
            .BuildServiceProvider();
        var client = new GatewayInferenceClient(services, options, context, null);
        async Task Execute()
        {
            if (stream)
            {
                await foreach (
                    var update in client.GetStreamingResponseAsync(
                        [new(ChatRole.User, "hello")],
                        cancellationToken: TestContext.Current.CancellationToken
                    )
                )
                    Assert.Fail("Stalled provider must not emit output.");
            }
            else
                await client.GetResponseAsync(
                    [new(ChatRole.User, "hello")],
                    cancellationToken: TestContext.Current.CancellationToken
                );
        }
        await Assert.ThrowsAsync<TimeoutException>(Execute);
        Assert.Equal(1, primary.Calls);
        Assert.Equal(0, backup.Calls);
        Assert.False(TestContext.Current.CancellationToken.IsCancellationRequested);
    }

    private sealed class FakeClient(
        Exception? failure = null,
        bool emitsFirst = false,
        bool stall = false,
        bool stallAfterFirst = false
    ) : IChatClient
    {
        public int Calls { get; private set; }
        public int StreamsDisposed { get; private set; }
        public ChatOptions? LastOptions { get; private set; }

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastOptions = options;
            if (stall)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (failure is not null)
                throw failure;
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "hello"));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            try
            {
                Calls++;
                LastOptions = options;
                cancellationToken.ThrowIfCancellationRequested();
                if (stall)
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                if (emitsFirst || failure is null)
                    yield return new ChatResponseUpdate(ChatRole.Assistant, "hello");
                if (stallAfterFirst)
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                await Task.Yield();
                if (failure is not null)
                    throw failure;
                yield return new ChatResponseUpdate { FinishReason = ChatFinishReason.Stop };
            }
            finally
            {
                StreamsDisposed++;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
