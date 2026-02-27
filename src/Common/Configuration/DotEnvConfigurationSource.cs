using Microsoft.Extensions.Configuration;

namespace Common.Configuration;

public sealed class DotEnvConfigurationSource : IConfigurationSource
{
    public required string RelativePath { get; set; }

    public bool Optional { get; set; } = true;

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        var cwdPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            RelativePath
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)
        );

        if (File.Exists(cwdPath))
            return new DotEnvConfigurationProvider(cwdPath, Optional);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var homePath = Path.Combine(home, RelativePath);

        return new DotEnvConfigurationProvider(homePath, Optional);
    }
}
