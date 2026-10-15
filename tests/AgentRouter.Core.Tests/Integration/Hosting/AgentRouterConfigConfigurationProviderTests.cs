using AgentRouter.Core.Hosting;
using AgentRouter.Core.Tests.Support.Storage;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AgentRouter.Core.Tests.Integration.Hosting;

public class AgentRouterConfigConfigurationProviderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UserConfig_Uses_AgentRouter_Folder_And_Respects_CreateIfMissing(
        bool createIfMissing
    )
    {
        using var workspace = new TempWorkspace();
        var oldDirectory = Path.Combine(workspace.Root, "claude-code-gateway");
        Directory.CreateDirectory(oldDirectory);
        var oldPath = Path.Combine(oldDirectory, "config.json");
        File.WriteAllText(oldPath, "not JSON: old configuration must not be read or changed");
        var path = Path.Combine(workspace.Root, ".agent-router", "config.json");
        using var config = (ConfigurationRoot)
            new ConfigurationBuilder()
                .AddAgentRouterConfig(workspace.Root, createIfMissing)
                .Build();
        Assert.Equal(createIfMissing, File.Exists(path));
        if (createIfMissing)
        {
            Assert.Equal("http://localhost:4000", config["GatewayBaseAddress"]);
            Assert.Equal("True", config["EnableTelemetry"]);
        }
        else
        {
            Assert.Empty(config.AsEnumerable());
            Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
        }
        Assert.Equal(
            "not JSON: old configuration must not be read or changed",
            File.ReadAllText(oldPath)
        );
    }

    [Fact]
    public void FileDefaults_PreserveEnvironmentAndCommandLinePrecedence()
    {
        var home = Path.Combine(
            Path.GetTempPath(),
            "ar-precedence-" + Guid.NewGuid().ToString("N")
        );
        var folder = Path.Combine(home, ".agent-router");
        Directory.CreateDirectory(folder);
        var dotenv = Path.Combine(home, ".env");
        var variable = "AR_PRECEDENCE_GatewayBaseAddress";
        var previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            File.WriteAllText(
                Path.Combine(folder, "config.json"),
                "{\"GatewayBaseAddress\":\"http://127.0.0.1:4601\"}"
            );
            File.WriteAllText(dotenv, "GatewayBaseAddress=http://127.0.0.1:4602");
            var files = new ConfigurationBuilder()
                .AddAgentRouterConfig(home, false)
                .AddDotEnvFile(dotenv)
                .Build();
            Assert.Equal("http://127.0.0.1:4602", files["GatewayBaseAddress"]);
            Environment.SetEnvironmentVariable(variable, "http://127.0.0.1:4603");
            var environment = new ConfigurationBuilder()
                .AddEnvironmentVariables("AR_PRECEDENCE_")
                .AddAgentRouterConfig(home, false)
                .AddDotEnvFile(dotenv)
                .Build();
            Assert.Equal("http://127.0.0.1:4603", environment["GatewayBaseAddress"]);
            var arguments = new ConfigurationBuilder()
                .AddEnvironmentVariables("AR_PRECEDENCE_")
                .AddCommandLine(["--GatewayBaseAddress=http://127.0.0.1:4604"])
                .AddClaudeSettings(home)
                .AddAgentRouterConfig(home, false)
                .AddDotEnvFile(dotenv)
                .Build();
            Assert.Equal("http://127.0.0.1:4604", arguments["GatewayBaseAddress"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void FlatSchema_IsMappedInto_ClaudeGatewayOptionsSection()
    {
        var tempHome = Path.Combine(Path.GetTempPath(), "ccg-home-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(tempHome, ".agent-router");
        Directory.CreateDirectory(folder);

        File.WriteAllText(
            Path.Combine(folder, "config.json"),
            "{\"GatewayBaseAddress\":\"http://localhost:4000\",\"DefaultProviderType\":\"Azure\"}"
        );

        var config = new ConfigurationBuilder()
            .Add(
                new AgentRouterConfigConfigurationSource
                {
                    HomeOverridePath = tempHome,
                    CreateIfMissing = false,
                }
            )
            .Build();

        Assert.Equal("http://localhost:4000", config["GatewayBaseAddress"]);
        Assert.Equal("Azure", config["DefaultProviderType"]);
    }

    [Fact]
    public void FlatSchema_Interpolates_EnvironmentVariables()
    {
        var tempHome = Path.Combine(Path.GetTempPath(), "ccg-home-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(tempHome, ".agent-router");
        Directory.CreateDirectory(folder);

        Environment.SetEnvironmentVariable("GW", "http://env-host:1234");
        try
        {
            File.WriteAllText(
                Path.Combine(folder, "config.json"),
                "{\"GatewayBaseAddress\":\"$GW\",\"DefaultProviderType\":\"Azure\"}"
            );

            var config = new ConfigurationBuilder()
                .Add(
                    new AgentRouterConfigConfigurationSource
                    {
                        HomeOverridePath = tempHome,
                        CreateIfMissing = false,
                    }
                )
                .Build();

            Assert.Equal("http://env-host:1234", config["GatewayBaseAddress"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GW", null);
        }
    }
}
