using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using AgentRouter.Core.Hosting;
using AgentRouter.Core.Hosting.Browser;
using AgentRouter.Core.Storage;
using AgentRouter.Core.Storage.Secrets;
using AgentRouter.Core.Storage.Sqlite;
using Microsoft.AspNetCore.Builder;

namespace AgentRouter.Cli;

public sealed record GatewayManagedState(
    GatewayServiceIdentity Identity,
    string Url,
    string? GatewayUrl
);

internal sealed record GatewayServiceBootstrap(
    string InstanceId,
    string Token,
    int ManagementPort,
    bool StartGateway
);

public sealed class GatewayServiceController(
    GatewayPaths paths,
    GatewayDatabase database,
    ISecretStore secrets
)
{
    private const uint HandleInherit = 1;
    private const int StandardInputHandle = -10;
    private const int StandardOutputHandle = -11;
    private const int StandardErrorHandle = -12;
    private string StatePath => Path.Combine(paths.RootDirectory, "service.json");
    private string LockPath => Path.Combine(paths.RootDirectory, "service.lock");

    private static string SecretName(string instanceId) => $"management.{instanceId}";

    public async Task<GatewayManagedState> StartAsync(
        int port,
        bool startGateway,
        CancellationToken cancellationToken
    )
    {
        ValidatePort(port);
        await using var startupLock = await AcquireLockAsync(cancellationToken);
        var existing = ReadState();
        if (existing is not null)
        {
            if (IsProcessAlive(existing.Identity))
            {
                if (!await VerifyAsync(existing, cancellationToken))
                    throw new InvalidOperationException(
                        "Existing service identity could not be verified. No process was stopped or replaced."
                    );
                var inference = await GatewayAsync(
                    existing,
                    startGateway ? "start" : null,
                    cancellationToken
                );
                return existing with { GatewayUrl = inference.Url };
            }
            await RemoveStateAsync(existing, cancellationToken);
        }
        await database.InitializeAsync(cancellationToken);
        var bootstrap = new GatewayServiceBootstrap(
            Guid.NewGuid().ToString("N"),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            port,
            startGateway
        );
        await secrets.SetAsync(
            SecretName(bootstrap.InstanceId),
            bootstrap.Token,
            cancellationToken
        );
        using var child = new Process { StartInfo = ChildStartInfo() };
        try
        {
            if (!StartDetached(child))
                throw new InvalidOperationException("Unable to start managed service.");
            child.BeginOutputReadLine();
            child.BeginErrorReadLine();
            await child.StandardInput.WriteLineAsync(
                JsonSerializer.Serialize(bootstrap).AsMemory(),
                cancellationToken
            );
            child.StandardInput.Close();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            while (!child.HasExited)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var state = ReadState();
                if (
                    state?.Identity.InstanceId == bootstrap.InstanceId
                    && state.Identity.ProcessId == child.Id
                    && await VerifyAsync(state, deadline.Token)
                )
                    return state;
                await Task.Delay(100, deadline.Token);
            }
            throw new InvalidOperationException(
                "Managed service startup failed. Check configured providers and listener ports."
            );
        }
        catch
        {
            try
            {
                if (child.Id > 0 && !child.HasExited)
                {
                    child.Kill(entireProcessTree: true);
                    await child.WaitForExitAsync(CancellationToken.None);
                }
            }
            catch (InvalidOperationException) { }
            var failedState = ReadState();
            if (failedState?.Identity.InstanceId == bootstrap.InstanceId)
                File.Delete(StatePath);
            await secrets.RemoveAsync(SecretName(bootstrap.InstanceId), CancellationToken.None);
            throw;
        }
    }

    public async Task<GatewayManagedState?> StatusAsync(CancellationToken cancellationToken)
    {
        await using var startupLock = await AcquireLockAsync(cancellationToken);
        var state = ReadState();
        if (state is null)
            return null;
        if (!IsProcessAlive(state.Identity))
        {
            await RemoveStateAsync(state, cancellationToken);
            return null;
        }
        if (!await VerifyAsync(state, cancellationToken))
            throw new InvalidOperationException(
                "Service identity could not be verified. State was not changed."
            );
        var inference = await GatewayAsync(state, null, cancellationToken);
        return state with { GatewayUrl = inference.Url };
    }

    public async Task<bool> StopAsync(CancellationToken cancellationToken)
    {
        await using var startupLock = await AcquireLockAsync(cancellationToken);
        var state = ReadState();
        if (state is null)
            return false;
        if (!IsProcessAlive(state.Identity))
        {
            await RemoveStateAsync(state, cancellationToken);
            return false;
        }
        if (!await VerifyAsync(state, cancellationToken))
            throw new InvalidOperationException(
                "Service identity could not be verified. Refusing to stop an unverified process."
            );
        using var process = Process.GetProcessById(state.Identity.ProcessId);
        if (process.StartTime.ToUniversalTime().Ticks != state.Identity.ProcessStartTicks)
            throw new InvalidOperationException(
                "Service process identity changed. No process was stopped."
            );
        using var client = await ClientAsync(state, cancellationToken);
        using var response = await client.PostAsync(
            "/management/v1/shutdown",
            null,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        await process.WaitForExitAsync(deadline.Token);
        await RemoveStateAsync(state, cancellationToken);
        return true;
    }

    public async Task<string> GetManagementTokenAsync(CancellationToken cancellationToken)
    {
        var state =
            await StatusAsync(cancellationToken)
            ?? throw new InvalidOperationException("No managed service is running.");
        return await secrets.GetAsync(SecretName(state.Identity.InstanceId), cancellationToken)
            ?? throw new InvalidOperationException("Management token is unavailable.");
    }

    public async Task<BrowserPairingResponse> CreateBrowserPairingAsync(
        GatewayManagedState state,
        CancellationToken cancellationToken
    )
    {
        if (!await VerifyAsync(state, cancellationToken))
            throw new InvalidOperationException("Service identity could not be verified.");
        using var client = await ClientAsync(state, cancellationToken);
        using var response = await client.PostAsync(
            "/management/v1/browser/pairings",
            null,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BrowserPairingResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Browser pairing was unavailable.");
    }

    public async Task<GatewayLifecycleResponse> GatewayAsync(
        string? action,
        CancellationToken cancellationToken
    )
    {
        if (action is not (null or "start" or "stop" or "restart"))
            throw new ArgumentException("Invalid gateway action.", nameof(action));
        var state =
            await StatusAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "No managed service is running. Run 'ar service start --no-gateway' first."
            );
        return await GatewayAsync(state, action, cancellationToken);
    }

    private async Task<GatewayLifecycleResponse> GatewayAsync(
        GatewayManagedState state,
        string? action,
        CancellationToken cancellationToken
    )
    {
        using var client = await ClientAsync(state, cancellationToken);
        client.Timeout = TimeSpan.FromSeconds(45);
        using var response = action is null
            ? await client.GetAsync("/management/v1/gateway", cancellationToken)
            : await client.PostAsync($"/management/v1/gateway/{action}", null, cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.Conflict)
            response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GatewayLifecycleResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Gateway status is unavailable.");
    }

    public async Task<int> RunForegroundAsync(
        int port,
        bool startGateway,
        CancellationToken cancellationToken
    )
    {
        ValidatePort(port);
        var bootstrap = new GatewayServiceBootstrap(
            Guid.NewGuid().ToString("N"),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            port,
            startGateway
        );
        await using (var startupLock = await AcquireLockAsync(cancellationToken))
        {
            var existing = ReadState();
            if (existing is not null && IsProcessAlive(existing.Identity))
                throw new InvalidOperationException(
                    "A managed service already exists. Use 'ar service status' or 'ar service stop'."
                );
            if (existing is not null)
                await RemoveStateAsync(existing, cancellationToken);
            await database.InitializeAsync(cancellationToken);
            await secrets.SetAsync(
                SecretName(bootstrap.InstanceId),
                bootstrap.Token,
                cancellationToken
            );
            return await RunHostsAsync(bootstrap, startupLock, cancellationToken);
        }
    }

    public async Task<int> RunChildAsync(CancellationToken cancellationToken)
    {
        var payload = await Console.In.ReadLineAsync(cancellationToken);
        var bootstrap =
            JsonSerializer.Deserialize<GatewayServiceBootstrap>(payload ?? "")
            ?? throw new InvalidOperationException("Managed bootstrap is missing.");
        ValidatePort(bootstrap.ManagementPort);
        return await RunHostsAsync(bootstrap, null, cancellationToken);
    }

    private async Task<int> RunHostsAsync(
        GatewayServiceBootstrap bootstrap,
        FileStream? startupLock,
        CancellationToken cancellationToken
    )
    {
        await using var management = GatewayManagementApplication.Build(
            GatewayManagementApplication.CreateBuilder(bootstrap.Token, bootstrap.ManagementPort),
            bootstrap.Token,
            bootstrap.InstanceId
        );
        var lifecycle = management.Services.GetRequiredService<GatewayLifecycleService>();
        try
        {
            await management.StartAsync(cancellationToken);
            if (bootstrap.StartGateway)
                await lifecycle.StartGatewayAsync(cancellationToken);
            using var process = Process.GetCurrentProcess();
            var identity = new GatewayServiceIdentity(
                bootstrap.InstanceId,
                process.Id,
                process.StartTime.ToUniversalTime().Ticks,
                typeof(GatewayManagementApplication).Assembly.GetName().Version?.ToString()
                    ?? "unknown"
            );
            WriteState(
                new GatewayManagedState(identity, management.Urls.Single(), lifecycle.Status.Url)
            );
            if (startupLock is not null)
            {
                await startupLock.DisposeAsync();
                Console.WriteLine($"Management listening: {management.Urls.Single()}");
            }
            await management.WaitForShutdownAsync(cancellationToken);
            return 0;
        }
        finally
        {
            await lifecycle.StopGatewayAsync(CancellationToken.None);
        }
    }

    private async Task<bool> VerifyAsync(
        GatewayManagedState state,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var client = await ClientAsync(state, cancellationToken);
            var identity = await client.GetFromJsonAsync<GatewayServiceIdentity>(
                "/management/v1/identity",
                cancellationToken
            );
            return identity == state.Identity && IsProcessAlive(state.Identity);
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task<HttpClient> ClientAsync(
        GatewayManagedState state,
        CancellationToken cancellationToken
    )
    {
        if (
            !Uri.TryCreate(state.Url, UriKind.Absolute, out var url)
            || url.Scheme != "http"
            || url.Host != "127.0.0.1"
            || url.Port is < 1 or > 65535
            || url.AbsolutePath != "/"
            || url.Query.Length > 0
            || url.Fragment.Length > 0
            || url.UserInfo.Length > 0
        )
            throw new InvalidOperationException("Managed service URL is invalid.");
        await database.InitializeAsync(cancellationToken);
        var token = await secrets.GetAsync(
            SecretName(state.Identity.InstanceId),
            cancellationToken
        );
        var client = new HttpClient(
            new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }
        )
        {
            BaseAddress = url,
            Timeout = TimeSpan.FromSeconds(3),
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token ?? "missing"
        );
        return client;
    }

    private GatewayManagedState? ReadState()
    {
        if (!File.Exists(StatePath))
            return null;
        try
        {
            var state = JsonSerializer.Deserialize<GatewayManagedState>(
                File.ReadAllText(StatePath)
            );
            if (
                state?.Identity is null
                || !Guid.TryParseExact(state.Identity.InstanceId, "N", out _)
                || state.Identity.ProcessId <= 0
                || state.Identity.ProcessStartTicks <= 0
            )
                throw new InvalidOperationException(
                    "Managed service state is invalid. No process was changed."
                );
            return state;
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                "Managed service state is malformed. No process was changed."
            );
        }
    }

    private void WriteState(GatewayManagedState state)
    {
        var temporary = StatePath + "." + state.Identity.InstanceId;
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(state));
            File.Move(temporary, StatePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private async Task RemoveStateAsync(
        GatewayManagedState state,
        CancellationToken cancellationToken
    )
    {
        if (ReadState()?.Identity.InstanceId == state.Identity.InstanceId)
            File.Delete(StatePath);
        await database.InitializeAsync(cancellationToken);
        await secrets.RemoveAsync(SecretName(state.Identity.InstanceId), cancellationToken);
    }

    private async Task<FileStream> AcquireLockAsync(CancellationToken cancellationToken)
    {
        paths.EnsureCreated();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(35));
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    LockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None
                );
            }
            catch (IOException)
            {
                await Task.Delay(100, deadline.Token);
            }
        }
    }

    private static bool IsProcessAlive(GatewayServiceIdentity identity)
    {
        try
        {
            using var process = Process.GetProcessById(identity.ProcessId);
            return !process.HasExited
                && process.StartTime.ToUniversalTime().Ticks == identity.ProcessStartTicks;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException(
                "Cannot inspect service process identity. No process was changed."
            );
        }
    }

    private static ProcessStartInfo ChildStartInfo()
    {
        var start = new ProcessStartInfo(
            Environment.ProcessPath
                ?? throw new InvalidOperationException("Current executable path is unavailable.")
        )
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (
            Path.GetFileNameWithoutExtension(start.FileName)
                .Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        )
            start.ArgumentList.Add(typeof(GatewayHost).Assembly.Location);
        start.ArgumentList.Add("--managed-child");
        return start;
    }

    private static void ValidatePort(int port)
    {
        if (port is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
    }

    private static bool StartDetached(Process child)
    {
        if (!OperatingSystem.IsWindows())
            return child.Start();
        var handles = new List<(IntPtr Handle, uint Flags)>();
        try
        {
            foreach (
                var standardHandle in new[]
                {
                    StandardInputHandle,
                    StandardOutputHandle,
                    StandardErrorHandle,
                }
            )
            {
                var handle = GetStdHandle(standardHandle);
                if (GetHandleInformation(handle, out var flags) && (flags & HandleInherit) != 0)
                {
                    if (!SetHandleInformation(handle, HandleInherit, 0))
                        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    handles.Add((handle, flags));
                }
            }
            return child.Start();
        }
        finally
        {
            foreach (var (handle, flags) in handles)
                SetHandleInformation(handle, HandleInherit, flags & HandleInherit);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetHandleInformation(IntPtr handle, out uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
}
