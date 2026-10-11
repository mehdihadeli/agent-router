using System.Diagnostics;
using AgentRouter.Contracts.Configuration;
using AgentRouter.Contracts.Defaults;
using AgentRouter.Shared.Routes;

namespace AgentRouter;

public static class GatewayAgentLauncher
{
    public static ProcessStartInfo CreateStartInfo(GatewayConfigDocument document, string profileName, Uri gatewayUrl, IEnumerable<string>? arguments = null)
    {
        if (!gatewayUrl.IsLoopback || gatewayUrl.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(gatewayUrl.UserInfo) || gatewayUrl.AbsolutePath != "/"
            || !string.IsNullOrEmpty(gatewayUrl.Query) || !string.IsNullOrEmpty(gatewayUrl.Fragment))
            throw new ArgumentException("Agent launch requires a loopback HTTP(S) gateway URL without a path or credentials.", nameof(gatewayUrl));
        var profile = document.Profiles?.SingleOrDefault(profile => profile.Enabled && string.Equals(profile.Name, profileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Profile '{profileName}' was not found or is disabled.");
        if (string.IsNullOrWhiteSpace(document.Server.AuthToken))
            throw new InvalidOperationException("Gateway authentication token is missing. Run 'init'.");
        if (profile.Executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || profile.Executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Install native Claude Code or select its executable; shell launchers are not supported.");

        var options = GatewayRuntimeConfiguration.Project(document, new ConfigurationBuilder().Build());
        var route = string.IsNullOrWhiteSpace(profile.Model) ? options.RouteModel.DefaultModel : profile.Model;
        RouteModelFactory.Resolve(options, route);
        var startInfo = new ProcessStartInfo(profile.Executable) { UseShellExecute = false };
        foreach (var argument in arguments ?? [])
            startInfo.ArgumentList.Add(argument);
        foreach (var variable in new[] { "ANTHROPIC_API_KEY", "CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_VERTEX", "CLAUDE_CODE_USE_FOUNDRY" })
            startInfo.Environment.Remove(variable);
        startInfo.Environment["ANTHROPIC_BASE_URL"] = gatewayUrl.AbsoluteUri.TrimEnd('/');
        startInfo.Environment["ANTHROPIC_AUTH_TOKEN"] = document.Server.AuthToken;
        startInfo.Environment["ANTHROPIC_MODEL"] = route;
        foreach (var tier in new[] { "opus", "sonnet", "haiku" })
            startInfo.Environment[$"ANTHROPIC_DEFAULT_{tier.ToUpperInvariant()}_MODEL"] = options.TierRoutes.GetValueOrDefault(tier, route);
        return startInfo;
    }

    public static Uri DefaultUrl(GatewayConfigDocument document) =>
        new($"http://{GatewayDefaults.LoopbackHost}:{document.Server.ProxyPort}");

    public static async Task<int> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start agent executable.");
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }
}
