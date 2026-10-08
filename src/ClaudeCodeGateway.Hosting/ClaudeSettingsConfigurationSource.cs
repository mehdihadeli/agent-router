using Microsoft.Extensions.Configuration;

namespace ClaudeCodeGateway.Shared.Configuration;

/// <summary>
/// Loads Claude Code settings from %USERPROFILE%/.claude/settings.json (or $HOME/.claude/settings.json)
/// and exposes them as an IConfiguration source.
/// </summary>
public sealed class ClaudeSettingsConfigurationSource : IConfigurationSource
{
    public string? HomeOverridePath { get; set; }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new ClaudeSettingsConfigurationProvider(HomeOverridePath);
    }
}
