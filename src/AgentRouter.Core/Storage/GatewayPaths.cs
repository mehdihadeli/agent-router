using AgentRouter.Core.Contracts.Defaults;

namespace AgentRouter.Core.Storage;

/// <summary>
/// Resolves every filesystem location the gateway owns: the SQLite database, the master key,
/// uploaded provider logos, the PID marker, and the log file.
/// </summary>
public sealed class GatewayPaths
{
    private const string ApplicationFolderName = ".agent-router";

    public GatewayPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string RootDirectory { get; }

    public string DatabasePath => Path.Combine(RootDirectory, "gateway.sqlite");

    public string ConfigPath => Path.Combine(RootDirectory, "config.json");

    public string MasterKeyPath => Path.Combine(RootDirectory, "secret.key");

    public string ProviderLogoDirectory => Path.Combine(RootDirectory, "provider-logos");

    public string PidFilePath => Path.Combine(RootDirectory, "gateway.pid");

    public string LogFilePath => Path.Combine(RootDirectory, "gateway.log");

    /// <summary>
    /// Resolves the gateway data directory. Precedence: explicit override, then
    /// <c>AR_CONFIG_DIR</c>, then .agent-router inside the user profile directory.
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
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetEnvironmentVariable(
                OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME"
            );
        }
        if (string.IsNullOrWhiteSpace(home))
        {
            throw new InvalidOperationException(
                "Cannot resolve the user profile directory. Set AR_CONFIG_DIR explicitly."
            );
        }

        return Path.Combine(home, ApplicationFolderName);
    }

    /// <summary>Creates the root data directory when it does not exist yet.</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
    }
}
