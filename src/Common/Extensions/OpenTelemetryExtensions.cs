using Common.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Common.Extensions;

public static class OpenTelemetryExtensions
{
    public static IHostApplicationBuilder AddCustomOpenTelemetry(
        this IHostApplicationBuilder builder
    )
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();
        });

        return builder;
    }

    public static IHostApplicationBuilder ConfigureOpenTelemetry(
        this IHostApplicationBuilder builder
    )
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder
            .Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Don't trace requests to the health endpoint to avoid filling the dashboard with noise
                        tracing.Filter = httpContext =>
                            !(
                                httpContext.Request.Path.StartsWithSegments(
                                    HealthCheck.HealthEndpointPath
                                )
                                || httpContext.Request.Path.StartsWithSegments(
                                    HealthCheck.AlivenessEndpointPath
                                )
                            )
                    )
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(
        this IHostApplicationBuilder builder
    )
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
        );

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }
}
