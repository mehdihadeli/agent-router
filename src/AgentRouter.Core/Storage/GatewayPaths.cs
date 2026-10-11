using AgentRouter.Contracts.Defaults;

namespace AgentRouter.Storage;

/// <summary>
/// Resolves every filesystem location the gateway owns: the SQLite database, the master key,
/// uploaded provider logos, the PID marker, and the log file.
/// </summary>
public sealed class GatewayPaths
{
    private const string ApplicationFolderName = "claude-code-gateway";

    public GatewayPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string RootDirectory { get; }

    public string DatabasePath => Path.Combine(RootDirectory, "gateway.sqlite");

    public string MasterKeyPath => Path.Combine(RootDirectory, "secret.key");

    public string ProviderLogoDirectory => Path.Combine(RootDirectory, "provider-logos");

    public string PidFilePath => Path.Combine(RootDirectory, "gateway.pid");

    public string LogFilePath => Path.Combine(RootDirectory, "gateway.log");

    /// <summary>
    /// Resolves the gateway data directory. Precedence: explicit override, then
    /// <c>CC_GATEWAY_CONFIG_DIR</c>, then the OS configuration directory.
    /// </summary>
    public static GatewayPaths Resolve(string? overrideDirectory = null)
    {
        var configured = overrideDirectory;
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.GetEnvironmentVariable(
                GatewayDefaults.ConfigDirectoryEnvironmentVariable
            );
        }

        return new GatewayPaths(
            string.IsNullOrWhiteSpace(configured) ? GetDefaultRootDirectory() : configured
        );
    }

    private static string GetDefaultRootDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetEnvironmentVariable("APPDATA");
            if (!string.IsNullOrWhiteSpace(appData))
            {
                return Path.Combine(appData, ApplicationFolderName);
            }
        }

        var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdgConfig))
        {
            return Path.Combine(xdgConfig, ApplicationFolderName);
        }

        var home =
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            ?? Environment.GetEnvironmentVariable("HOME")
            ?? Directory.GetCurrentDirectory();

        return Path.Combine(home, ".config", ApplicationFolderName);
    }

    /// <summary>Creates the root data directory when it does not exist yet.</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
    }
}
