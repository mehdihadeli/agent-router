using System.Diagnostics;
using System.Text.Json;
using AgentRouter.Core.Contracts.Configuration;
using AgentRouter.Core.Contracts.Defaults;
using AgentRouter.Core.Routes;

namespace AgentRouter.Core.Hosting;

public static class GatewayAgentLauncher
{
    public static ProcessStartInfo CreateStartInfo(
        GatewayConfigDocument document,
        string profileName,
        Uri gatewayUrl,
        IEnumerable<string>? arguments = null
    )
    {
        if (
            !gatewayUrl.IsLoopback
            || gatewayUrl.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(gatewayUrl.UserInfo)
            || gatewayUrl.AbsolutePath != "/"
            || !string.IsNullOrEmpty(gatewayUrl.Query)
            || !string.IsNullOrEmpty(gatewayUrl.Fragment)
        )
            throw new ArgumentException(
                "Agent launch requires a loopback HTTP(S) gateway URL without a path or credentials.",
                nameof(gatewayUrl)
            );
        var profile =
            document.Profiles?.SingleOrDefault(profile =>
                profile.Enabled
                && string.Equals(profile.Name, profileName, StringComparison.OrdinalIgnoreCase)
            )
            ?? throw new InvalidOperationException(
                $"Profile '{profileName}' was not found or is disabled."
            );
        if (string.IsNullOrWhiteSpace(document.Server.AuthToken))
            throw new InvalidOperationException(
                "Gateway authentication token is missing. Run 'init'."
            );
        ValidateProfile(profile);
        if (profile.EntryMode == "app")
            throw new InvalidOperationException(
                $"Profile '{profile.Name}' is app-only and must be opened by an app launcher."
            );
        if (
            profile.Executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            || profile.Executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
        )
            throw new InvalidOperationException(
                "Select a native agent executable; shell launchers are not supported."
            );

        var options = GatewayRuntimeConfiguration.Project(
            document,
            new ConfigurationBuilder().Build()
        );
        var route = string.IsNullOrWhiteSpace(profile.Model)
            ? options.RouteModel.DefaultModel
            : profile.Model;
        RouteModelFactory.Resolve(options, route);
        var startInfo = new ProcessStartInfo(ResolveExecutable(profile))
        {
            UseShellExecute = false,
        };
        if (profile.WorkingDirectory is not null)
            startInfo.WorkingDirectory = profile.WorkingDirectory;
        foreach (var (key, value) in profile.Environment)
            startInfo.Environment[key] = value;
        if (profile.Name.Length > 128 || profile.Name.Any(char.IsControl))
            throw new InvalidOperationException(
                "Profile name cannot be represented as a routing header."
            );
        var telemetry = profile.TelemetryEndpoint is null
            ? null
            : new Uri(profile.TelemetryEndpoint, UriKind.Absolute);
        if (telemetry is not null)
        {
            ValidateTelemetryEndpoint(telemetry);
            foreach (
                var variable in startInfo
                    .Environment.Keys.Where(key =>
                        key.StartsWith("OTEL_EXPORTER_OTLP_", StringComparison.OrdinalIgnoreCase)
                    )
                    .ToArray()
            )
                startInfo.Environment.Remove(variable);
        }
        if (profile.Kind == "codex")
        {
            void Set(string key, string value)
            {
                startInfo.ArgumentList.Add("--config");
                startInfo.ArgumentList.Add(key + "=" + value);
            }
            Set("model_provider", "\"agentrouter\"");
            Set("model", JsonSerializer.Serialize(route));
            Set("model_providers.agentrouter.name", "\"AgentRouter\"");
            Set(
                "model_providers.agentrouter.base_url",
                JsonSerializer.Serialize(gatewayUrl.AbsoluteUri.TrimEnd('/') + "/v1")
            );
            Set("model_providers.agentrouter.env_key", "\"AR_AGENT_TOKEN\"");
            Set("model_providers.agentrouter.wire_api", "\"responses\"");
            Set("model_providers.agentrouter.requires_openai_auth", "false");
            Set("model_providers.agentrouter.supports_websockets", "false");
            Set("model_providers.agentrouter.request_max_retries", "0");
            Set("model_providers.agentrouter.stream_max_retries", "0");
            Set("web_search", "\"disabled\"");
            Set(
                "model_providers.agentrouter.http_headers.X-AR-Agent",
                JsonSerializer.Serialize(profile.Name)
            );
            if (telemetry is not null)
            {
                Set("otel.log_user_prompt", "false");
                Set(
                    "otel.exporter",
                    "{otlp-http={endpoint="
                        + JsonSerializer.Serialize(new Uri(telemetry, "v1/logs").AbsoluteUri)
                        + ",protocol=\"json\",headers={}}}"
                );
                Set(
                    "otel.trace_exporter",
                    "{otlp-http={endpoint="
                        + JsonSerializer.Serialize(new Uri(telemetry, "v1/traces").AbsoluteUri)
                        + ",protocol=\"json\",headers={}}}"
                );
                Set(
                    "otel.metrics_exporter",
                    "{otlp-http={endpoint="
                        + JsonSerializer.Serialize(new Uri(telemetry, "v1/metrics").AbsoluteUri)
                        + ",protocol=\"json\",headers={}}}"
                );
            }
            startInfo.Environment.Remove("OPENAI_API_KEY");
            startInfo.Environment.Remove("OPENAI_BASE_URL");
            startInfo.Environment["AR_AGENT_TOKEN"] = document.Server.AuthToken;
            foreach (var argument in arguments ?? [])
                startInfo.ArgumentList.Add(argument);
            return startInfo;
        }
        if (profile.Kind == "opencode")
        {
            startInfo.Environment.Remove("OPENAI_API_KEY");
            startInfo.Environment.Remove("OPENAI_BASE_URL");
            startInfo.Environment["OPENAI_BASE_URL"] = gatewayUrl.AbsoluteUri.TrimEnd('/') + "/v1";
            startInfo.Environment["OPENAI_API_KEY"] = document.Server.AuthToken;
            startInfo.Environment["OPENCODE_MODEL"] = route;
            startInfo.Environment["X_AR_AGENT"] = profile.Name;
            foreach (var argument in arguments ?? [])
                startInfo.ArgumentList.Add(argument);
            return startInfo;
        }
        foreach (var argument in arguments ?? [])
            startInfo.ArgumentList.Add(argument);
        foreach (
            var variable in new[]
            {
                "ANTHROPIC_API_KEY",
                "CLAUDE_CODE_USE_BEDROCK",
                "CLAUDE_CODE_USE_VERTEX",
                "CLAUDE_CODE_USE_FOUNDRY",
            }
        )
            startInfo.Environment.Remove(variable);
        startInfo.Environment["ANTHROPIC_BASE_URL"] = gatewayUrl.AbsoluteUri.TrimEnd('/');
        startInfo.Environment["ANTHROPIC_AUTH_TOKEN"] = document.Server.AuthToken;
        startInfo.Environment["ANTHROPIC_MODEL"] = route;
        startInfo.Environment.TryGetValue("ANTHROPIC_CUSTOM_HEADERS", out var headers);
        startInfo.Environment["ANTHROPIC_CUSTOM_HEADERS"] =
            (string.IsNullOrWhiteSpace(headers) ? "" : headers + "\n")
            + "X-AR-Agent: "
            + profile.Name;
        if (telemetry is not null)
        {
            startInfo.Environment["CLAUDE_CODE_ENABLE_TELEMETRY"] = "1";
            startInfo.Environment["CLAUDE_CODE_ENHANCED_TELEMETRY_BETA"] = "1";
            startInfo.Environment["CLAUDE_CODE_PROPAGATE_TRACEPARENT"] = "1";
            startInfo.Environment.Remove("ENABLE_BETA_TRACING_DETAILED");
            startInfo.Environment.Remove("BETA_TRACING_ENDPOINT");
            foreach (var signal in new[] { "LOGS", "METRICS", "TRACES" })
                startInfo.Environment[$"OTEL_{signal}_EXPORTER"] = "otlp";
            startInfo.Environment["OTEL_EXPORTER_OTLP_ENDPOINT"] = telemetry.AbsoluteUri.TrimEnd(
                '/'
            );
            startInfo.Environment["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/json";
            foreach (
                var variable in new[]
                {
                    "OTEL_LOG_USER_PROMPTS",
                    "OTEL_LOG_ASSISTANT_RESPONSES",
                    "OTEL_LOG_TOOL_DETAILS",
                    "OTEL_LOG_TOOL_CONTENT",
                    "OTEL_LOG_RAW_API_BODIES",
                }
            )
                startInfo.Environment[variable] = "0";
        }
        foreach (var tier in new[] { "opus", "sonnet", "haiku" })
            startInfo.Environment[$"ANTHROPIC_DEFAULT_{tier.ToUpperInvariant()}_MODEL"] =
                profile.TierModels.GetValueOrDefault(
                    tier,
                    options.TierRoutes.GetValueOrDefault(tier, route)
                );
        foreach (var (key, value) in profile.Environment)
            startInfo.Environment[key] = value;
        return startInfo;
    }

    public static void ValidateProfile(AgentProfile profile)
    {
        if (profile.Kind is not ("claude" or "codex" or "opencode" or "claude-design"))
            throw new InvalidOperationException(
                "Profile kind must be claude, codex, opencode, or claude-design."
            );
        if (profile.EntryMode is not ("cli" or "app" or "cli-and-app"))
            throw new InvalidOperationException(
                "Profile entry mode must be cli, app, or cli-and-app."
            );
        if (profile.Scope is not ("launch" or "system"))
            throw new InvalidOperationException("Profile scope must be launch or system.");
        if (profile.Kind == "claude-design" && profile.EntryMode != "app")
            throw new InvalidOperationException("Claude Design profiles are app-only.");
        if (profile.Kind == "claude-design" && profile.Scope != "launch")
            throw new InvalidOperationException("Claude Design profiles are launch-scoped.");
        if (
            profile.WorkingDirectory is not null
            && !Path.IsPathFullyQualified(profile.WorkingDirectory)
        )
            throw new InvalidOperationException(
                "Profile working directory must be an absolute path."
            );
        foreach (var (key, value) in profile.Environment)
        {
            if (
                string.IsNullOrWhiteSpace(key)
                || key.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_'))
                || char.IsAsciiDigit(key[0])
            )
                throw new InvalidOperationException(
                    $"Profile environment variable '{key}' has an invalid name."
                );
            if (
                key
                is "ANTHROPIC_AUTH_TOKEN"
                    or "ANTHROPIC_BASE_URL"
                    or "ANTHROPIC_MODEL"
                    or "OPENAI_API_KEY"
                    or "OPENAI_BASE_URL"
                    or "OPENCODE_MODEL"
                    or "AR_AGENT_TOKEN"
            )
                throw new InvalidOperationException(
                    $"Profile environment variable '{key}' is controlled by AgentRouter."
                );
            if (value.Contains('\0'))
                throw new InvalidOperationException(
                    $"Profile environment variable '{key}' contains an invalid value."
                );
        }
        foreach (var (tier, model) in profile.TierModels)
        {
            if (tier is not ("opus" or "sonnet" or "haiku") || string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException(
                    "Profile tier models must use opus, sonnet, or haiku."
                );
        }
    }

    public static void ValidateTelemetryEndpoint(Uri endpoint)
    {
        if (
            !endpoint.IsAbsoluteUri
            || endpoint.Scheme is not ("http" or "https")
            || (endpoint.Scheme == "http" && !endpoint.IsLoopback)
            || endpoint.AbsolutePath != "/"
            || endpoint.UserInfo.Length != 0
            || endpoint.Query.Length != 0
            || endpoint.Fragment.Length != 0
        )
            throw new ArgumentException(
                "Telemetry requires an HTTPS or loopback HTTP collector root URL without credentials, query, or fragment."
            );
    }

    private static string ResolveExecutable(AgentProfile profile)
    {
        if (
            !OperatingSystem.IsWindows()
            || profile.Kind != "codex"
            || profile.Executable != "codex"
        )
            return profile.Executable;
        foreach (
            var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(
                Path.PathSeparator
            )
        )
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;
            var native = Path.Combine(directory, "codex.exe");
            if (File.Exists(native))
                return native;
            var architecture = System
                .Runtime
                .InteropServices
                .RuntimeInformation
                .OSArchitecture switch
            {
                System.Runtime.InteropServices.Architecture.X64 => "x64",
                System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
                _ => throw new PlatformNotSupportedException(
                    "Select the native Codex executable for this architecture."
                ),
            };
            var target = architecture == "x64" ? "x86_64" : "aarch64";
            var packageBinary = Path.Combine(
                directory,
                "node_modules",
                "@openai",
                "codex",
                "node_modules",
                "@openai",
                $"codex-win32-{architecture}",
                "vendor",
                $"{target}-pc-windows-msvc",
                "bin",
                "codex.exe"
            );
            if (File.Exists(packageBinary))
                return packageBinary;
        }
        throw new InvalidOperationException(
            "Native Codex executable not found. Set --executable to its full path."
        );
    }

    public static Uri DefaultUrl(GatewayConfigDocument document) =>
        new($"http://{GatewayDefaults.LoopbackHost}:{document.Server.ProxyPort}");

    public static async Task<int> RunAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start agent executable.");
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
