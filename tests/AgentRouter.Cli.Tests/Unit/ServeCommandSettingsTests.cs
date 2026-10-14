using AgentRouter.Cli;
using Xunit;

namespace AgentRouter.Cli.Tests.Unit;

public sealed class ServeCommandSettingsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(65535)]
    public void Validate_AcceptsDefaultAndBoundaryPorts(int? port)
    {
        var result = new ServeCommand.Settings { Port = port }.Validate();
        Assert.True(result.Successful);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Validate_RejectsOutOfRangePorts(int port)
    {
        var result = new ServeCommand.Settings { Port = port }.Validate();
        Assert.False(result.Successful);
        Assert.Equal("Port must be between 1 and 65535.", result.Message);
    }

    [Theory]
    [InlineData("http://127.0.0.1:4600")]
    [InlineData("https://localhost:4600")]
    public void Validate_AcceptsAbsoluteHttpUrls(string url)
    {
        Assert.True(
            new ServeCommand.Settings { Url = url }
                .Validate()
                .Successful
        );
    }

    [Theory]
    [InlineData("localhost:4600")]
    [InlineData("/relative")]
    [InlineData("ftp://localhost")]
    public void Validate_RejectsRelativeOrUnsupportedUrls(string url)
    {
        var result = new ServeCommand.Settings { Url = url }.Validate();
        Assert.False(result.Successful);
        Assert.Equal("URL must be an absolute HTTP or HTTPS URL.", result.Message);
    }
}
