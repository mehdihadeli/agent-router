using System.Net.Http.Json;
using System.Text.Json;
using AgentRouter.Shared.AIProviders;
using AgentRouter.Shared.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AgentRouter.Providers.OpenAI.Tests;

public class LiveProviderTests
{
    [Theory]
    [Trait("Category", "Live")]
    [InlineData(ProviderType.OpenAI, false)]
    [InlineData(ProviderType.OpenAI, true)]
    [InlineData(ProviderType.Anthropic, false)]
    [InlineData(ProviderType.Anthropic, true)]
    public async Task ConfiguredProvider_ReturnsTextThroughGateway(ProviderType protocol, bool stream)
    {
        if (Environment.GetEnvironmentVariable("CC_GATEWAY_LIVE_TESTS") != "1")
            Assert.Skip("Paid upstream requests require CC_GATEWAY_LIVE_TESTS=1 in process environment.");

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var configuration = new ConfigurationBuilder().SetBasePath(directory.FullName)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .AddDotEnvFile(Path.Combine(directory.FullName, ".env"), optional: true)
            .Build();
        var settings = configuration.GetSection($"LiveProviders:{protocol}");
        if (!settings.GetValue<bool>("Enabled"))
            Assert.Skip($"LiveProviders:{protocol}:Enabled is false.");
        var endpoint = settings["Endpoint"];
        var key = settings["ApiKey"];
        var model = settings["Model"];
        Assert.False(string.IsNullOrWhiteSpace(endpoint), $"Missing LiveProviders:{protocol}:Endpoint");
        Assert.False(string.IsNullOrWhiteSpace(key), $"Missing LiveProviders:{protocol}:ApiKey");
        Assert.False(string.IsNullOrWhiteSpace(model), $"Missing LiveProviders:{protocol}:Model");
        Assert.True(Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback) && string.IsNullOrEmpty(uri.UserInfo) &&
            string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment), "Endpoint must use HTTPS or loopback, without embedded credentials/query/fragment.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        await using var gateway = ProviderTransportTests.CreateGateway(protocol, endpoint!, key!, model!);
        await gateway.StartAsync(timeout.Token);
        using var http = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()) };
        http.DefaultRequestHeaders.Add("x-api-key", "gateway-secret");
        using var response = await http.PostAsJsonAsync("/v1/messages", new
        {
            model = "test/" + model, max_tokens = 32, stream,
            messages = new[] { new { role = "user", content = "Reply with OK only." } },
        }, timeout.Token);
        Assert.True(response.IsSuccessStatusCode, $"Gateway HTTP status: {(int)response.StatusCode}; inspect upstream settings privately.");
        var content = await response.Content.ReadAsStringAsync(timeout.Token);
        if (stream)
        {
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("event: content_block_delta", content, StringComparison.Ordinal);
            Assert.Contains("event: message_stop", content, StringComparison.Ordinal);
        }
        else
        {
            using var json = JsonDocument.Parse(content);
            Assert.Equal("message", json.RootElement.GetProperty("type").GetString());
            Assert.Contains(json.RootElement.GetProperty("content").EnumerateArray(), block =>
                block.GetProperty("type").GetString() == "text" && !string.IsNullOrWhiteSpace(block.GetProperty("text").GetString()));
        }
    }
}
