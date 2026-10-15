using System.Text;
using AgentRouter.Core;
using AgentRouter.Core.Contracts;
using AgentRouter.Core.Hosting;
using AgentRouter.Core.Routes;
using AgentRouter.Core.Tests.Integration.Api;
using AgentRouter.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace AgentRouter.Core.Tests.Integration.Hosting;

public sealed class GatewayTelemetryTests
{
    [Fact]
    public async Task Otlp_ExportsGatewayLogsTracesAndMetricsWithoutPromptBodies()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var traces = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var metrics = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var logs = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var receiverBuilder = WebApplication.CreateBuilder();
        receiverBuilder.Logging.ClearProviders();
        receiverBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var receiver = receiverBuilder.Build();
        receiver.MapPost(
            "/{**path}",
            async (HttpContext context) =>
            {
                using var body = new MemoryStream();
                await context.Request.Body.CopyToAsync(body, deadline.Token);
                var payload = Encoding.UTF8.GetString(body.ToArray());
                if (payload.Contains("gateway.inference", StringComparison.Ordinal))
                    traces.TrySetResult(payload);
                if (payload.Contains("agentrouter.inference.requests", StringComparison.Ordinal))
                    metrics.TrySetResult(payload);
                if (payload.Contains("Gateway inference", StringComparison.Ordinal))
                    logs.TrySetResult(payload);
                context.Response.ContentType = "application/x-protobuf";
            }
        );
        await receiver.StartAsync(deadline.Token);
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.Logging.ClearProviders();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = receiver.Urls.Single();
        builder.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf";
        builder.ConfigureOpenTelemetry();
        builder.Services.AddKeyedSingleton<IChatClient>(
            "test/model",
            new ResponsesEndpointTests.StubClient()
        );
        var options = new ClaudeGatewayOptions
        {
            Providers =
            [
                new ClaudeGatewayProvider
                {
                    ProviderName = "test",
                    ProviderType = ProviderType.OpenAI,
                    BaseAddress = "https://example.test/v1",
                    ApiKey = "synthetic",
                    Models = ["model"],
                },
            ],
            RouteModel = new RouteModel
            {
                DefaultModel = "test/model",
                ThinkingModel = "test/model",
                SearchModel = "test/model",
            },
        };
        GatewayResilience.Register(builder.Services, options);
        using var host = builder.Build();
        await host.StartAsync(deadline.Token);
        var client = new GatewayInferenceClient(
            host.Services,
            options,
            new DefaultHttpContext(),
            "test/model"
        );
        await client.GetResponseAsync(
            [new(ChatRole.User, "SENSITIVE_BODY_MUST_NOT_EXPORT")],
            cancellationToken: deadline.Token
        );
        Assert.True(host.Services.GetRequiredService<TracerProvider>().ForceFlush(5000));
        Assert.True(host.Services.GetRequiredService<MeterProvider>().ForceFlush(5000));
        await host.StopAsync(deadline.Token);
        host.Dispose();
        foreach (
            var payload in await Task.WhenAll(traces.Task, metrics.Task, logs.Task)
                .WaitAsync(deadline.Token)
        )
        {
            Assert.DoesNotContain(
                "SENSITIVE_BODY_MUST_NOT_EXPORT",
                payload,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain("synthetic", payload, StringComparison.Ordinal);
        }
        Assert.Contains("AgentRouter.Gateway", await traces.Task, StringComparison.Ordinal);
        Assert.Contains(
            "agentrouter.inference.duration",
            await metrics.Task,
            StringComparison.Ordinal
        );
    }
}
