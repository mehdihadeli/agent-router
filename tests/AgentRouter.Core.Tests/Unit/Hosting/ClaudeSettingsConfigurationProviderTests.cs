using AgentRouter.Core.Hosting;
using Xunit;

namespace AgentRouter.Core.Tests.Unit.Hosting;

public class ClaudeSettingsConfigurationProviderTests
{
    [Fact]
    public void InterpolateEnvironmentVariables_ReplacesDollarVarAndBracedVar_WhenSet()
    {
        Environment.SetEnvironmentVariable("CCG_TEST_VAR", "abc");
        try
        {
            Assert.Equal(
                "value=abc",
                ClaudeSettingsConfigurationProvider.InterpolateEnvironmentVariables(
                    "value=$CCG_TEST_VAR"
                )
            );
            Assert.Equal(
                "value=abc",
                ClaudeSettingsConfigurationProvider.InterpolateEnvironmentVariables(
                    "value=${CCG_TEST_VAR}"
                )
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable("CCG_TEST_VAR", null);
        }
    }

    [Fact]
    public void InterpolateEnvironmentVariables_LeavesPlaceholder_WhenNotSet()
    {
        Environment.SetEnvironmentVariable("CCG_TEST_VAR_MISSING", null);
        Assert.Equal(
            "value=$CCG_TEST_VAR_MISSING",
            ClaudeSettingsConfigurationProvider.InterpolateEnvironmentVariables(
                "value=$CCG_TEST_VAR_MISSING"
            )
        );
        Assert.Equal(
            "value=${CCG_TEST_VAR_MISSING}",
            ClaudeSettingsConfigurationProvider.InterpolateEnvironmentVariables(
                "value=${CCG_TEST_VAR_MISSING}"
            )
        );
    }
}
