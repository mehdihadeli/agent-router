namespace AgentRouter.Core.Hosting;

/// <summary>Inference lifecycle state without credentials or upstream diagnostics.</summary>
public sealed record GatewayLifecycleResponse(string State, string? Url, string? Error);

public sealed class GatewayLifecycleService(GatewayConfigurationService configuration)
    : IHostedService,
        IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private WebApplication? gateway;
    private GatewayLifecycleResponse status = new("Stopped", null, null);

    public GatewayLifecycleResponse Status => Volatile.Read(ref status);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async ValueTask DisposeAsync() => await StopGatewayAsync(CancellationToken.None);

    public async Task StopAsync(CancellationToken cancellationToken) =>
        await StopGatewayAsync(cancellationToken);

    public async Task<GatewayLifecycleResponse> StartGatewayAsync(
        CancellationToken cancellationToken
    ) => await ChangeAsync(false, cancellationToken);

    public async Task<GatewayLifecycleResponse> RestartGatewayAsync(
        CancellationToken cancellationToken
    ) => await ChangeAsync(true, cancellationToken);

    public async Task<GatewayLifecycleResponse> StopGatewayAsync(
        CancellationToken cancellationToken
    )
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await StopHostAsync();
            return status = new("Stopped", null, null);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<GatewayLifecycleResponse> ChangeAsync(
        bool restart,
        CancellationToken cancellationToken
    )
    {
        await gate.WaitAsync(cancellationToken);
        WebApplication? candidate = null;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            if (gateway is not null && !restart)
                return status = new("Running", gateway.Urls.FirstOrDefault(), null);
            var document = await configuration.LoadAsync(deadline.Token);
            if (!document.Providers.Values.Any(provider => provider.Enabled))
                return status = new(
                    gateway is null ? "Failed" : "Running",
                    status.Url,
                    "Configure an enabled provider and model before starting inference."
                );
            var builder = GatewayApplication.CreateBuilder(document: document);
            builder.Logging.AddFilter<Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider>(
                static level => false
            );
            candidate = GatewayApplication.Build(builder);
            cancellationToken.ThrowIfCancellationRequested();
            await StopHostAsync();
            status = new("Starting", null, null);
            await candidate.StartAsync(deadline.Token);
            gateway = candidate;
            candidate = null;
            return status = new("Running", gateway.Urls.FirstOrDefault(), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = new(
                gateway is null ? "Stopped" : "Running",
                gateway?.Urls.FirstOrDefault(),
                null
            );
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return status = new(
                gateway is null ? "Failed" : "Running",
                gateway?.Urls.FirstOrDefault(),
                "Inference could not start. Check enabled providers, models, routes and listener availability."
            );
        }
        finally
        {
            try
            {
                if (candidate is not null)
                    await candidate.DisposeAsync();
            }
            finally
            {
                gate.Release();
            }
        }
    }

    private async Task StopHostAsync()
    {
        if (gateway is null)
            return;
        status = new("Stopping", status.Url, null);
        var current = gateway;
        gateway = null;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await current.StopAsync(deadline.Token);
        }
        finally
        {
            await current.DisposeAsync();
        }
    }
}
