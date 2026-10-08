using Microsoft.Extensions.Configuration;

namespace ClaudeCodeGateway.Shared.Configuration;

public sealed class DotEnvConfigurationSource : IConfigurationSource
{
    public required string RelativePath { get; set; }

    public bool Optional { get; set; } = true;

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        var normalizedRelativePath = RelativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        var rootSearchPath = FindPathInCurrentDirectoryOrParents(
            Directory.GetCurrentDirectory(),
            normalizedRelativePath
        );
        if (rootSearchPath is not null)
        {
            return new DotEnvConfigurationProvider(rootSearchPath, Optional);
        }

        var baseDirectorySearchPath = FindPathInCurrentDirectoryOrParents(
            AppContext.BaseDirectory,
            normalizedRelativePath
        );
        if (baseDirectorySearchPath is not null)
        {
            return new DotEnvConfigurationProvider(baseDirectorySearchPath, Optional);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var homePath = Path.Combine(home, normalizedRelativePath);

        return new DotEnvConfigurationProvider(homePath, Optional);
    }

    private static string? FindPathInCurrentDirectoryOrParents(
        string startDirectory,
        string relativePath
    )
    {
        var current = new DirectoryInfo(Path.GetFullPath(startDirectory));

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }
}
