using AgentRouter.Shared.AIProviders;
using AgentRouter.Shared.Configuration;
using AgentRouter.Shared.Contracts;
using AgentRouter.Shared.Routes;
using Xunit;

namespace AgentRouter.Tests;

public class ClaudeCodeSettingsWriterTests
{
    [Fact]
    public void EnsureClaudeCodeSettings_WritesExpectedEnvKeys()
    {
        var originalHome = Environment.GetEnvironmentVariable("USERPROFILE");
        var tempHome = Path.Combine(Path.GetTempPath(), "ccg-home-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempHome);

        Environment.SetEnvironmentVariable("USERPROFILE", tempHome);
        try
        {
            var opts = new ClaudeGatewayOptions
            {
                GatewayBaseAddress = "http://localhost:4000",
                RouteModel = new RouteModel
                {
                    DefaultModel = "azure/gpt-4.1",
                    ThinkingModel = "azure/gpt-4.1",
                    SearchModel = "azure/gpt-4.1",
                },
                Providers =
                [
                    new ClaudeGatewayProvider
                    {
                        ProviderType = ProviderType.Azure,
                        BaseAddress = "https://example/",
                        ApiKey = "k",
                        DeploymentName = "gpt-4.1",
                        Models = ["gpt-4.1"],
                    },
                ],
            };

            ClaudeCodeSettingsWriter.EnsureClaudeCodeSettings(opts);

            var settingsPath = Path.Combine(tempHome, ".claude", "settings.json");
            Assert.True(File.Exists(settingsPath));

            var json = File.ReadAllText(settingsPath);
            Assert.Contains("\"ANTHROPIC_BASE_URL\": \"http://localhost:4000\"", json);
            Assert.Contains("\"ANTHROPIC_AUTH_TOKEN\": \"default\"", json);

            Assert.Contains("\"ANTHROPIC_DEFAULT_OPUS_MODEL\": \"azure/gpt-4.1\"", json);
            Assert.Contains("\"ANTHROPIC_DEFAULT_SONNET_MODEL\": \"azure/gpt-4.1\"", json);
            Assert.Contains("\"ANTHROPIC_DEFAULT_HAIKU_MODEL\": \"azure/gpt-4.1\"", json);
        }
        finally
        {
            Environment.SetEnvironmentVariable("USERPROFILE", originalHome);
            try
            {
                Directory.Delete(tempHome, recursive: true);
            }
            catch
            {
                // ignore cleanup issues
            }
        }
    }
}
