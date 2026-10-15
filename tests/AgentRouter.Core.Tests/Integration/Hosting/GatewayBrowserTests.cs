using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AgentRouter.Core.Hosting;
using AgentRouter.Core.Hosting.Browser;
using AgentRouter.Core.Tests.Support.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgentRouter.Core.Tests.Integration.Hosting;

public sealed class GatewayBrowserTests
{
    private const string Token = "browser-test-management-token-long-enough";

    [Fact]
    public async Task Pairing_RequiresCliAuthorityAndRejectsReplayCsrfAndLoggedOutSession()
    {
        using var workspace = new TempWorkspace();
        await using var app = GatewayManagementApplication.Build(
            GatewayManagementApplication.CreateBuilder(Token, 0, workspace.Paths),
            Token,
            Guid.NewGuid().ToString("N")
        );
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var cli = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var browser = new HttpClient { BaseAddress = cli.BaseAddress };
        browser.DefaultRequestHeaders.Add(
            "Origin",
            cli.BaseAddress.GetLeftPart(UriPartial.Authority)
        );
        using var denied = await browser.PostAsync(
            "/management/v1/browser/pairings",
            null,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        cli.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        using var created = await cli.PostAsync(
            "/management/v1/browser/pairings",
            null,
            TestContext.Current.CancellationToken
        );
        var pairing = await created.Content.ReadFromJsonAsync<BrowserPairingResponse>(
            TestContext.Current.CancellationToken
        );
        using var paired = await browser.PostAsJsonAsync(
            "/management/v1/browser/session",
            new BrowserPairingRequest(pairing!.Code),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.OK, paired.StatusCode);
        var cookie = Assert.Single(paired.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Token, cookie, StringComparison.Ordinal);
        var session = await paired.Content.ReadFromJsonAsync<BrowserSessionResponse>(
            TestContext.Current.CancellationToken
        );
        using var replay = await browser.PostAsJsonAsync(
            "/management/v1/browser/session",
            new BrowserPairingRequest(pairing.Code),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        using var identity = await browser.GetAsync(
            "/management/v1/identity",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.OK, identity.StatusCode);
        using var missingCsrf = await browser.DeleteAsync(
            "/management/v1/browser/session",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Forbidden, missingCsrf.StatusCode);
        browser.DefaultRequestHeaders.Add("X-AR-CSRF", session!.CsrfToken);
        using var browserPairing = await browser.PostAsync(
            "/management/v1/browser/pairings/",
            null,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Unauthorized, browserPairing.StatusCode);
        browser.DefaultRequestHeaders.Remove("Origin");
        browser.DefaultRequestHeaders.Add("Origin", "http://attacker.invalid");
        using var crossOrigin = await browser.DeleteAsync(
            "/management/v1/browser/session",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Forbidden, crossOrigin.StatusCode);
        browser.DefaultRequestHeaders.Remove("Origin");
        browser.DefaultRequestHeaders.Add(
            "Origin",
            cli.BaseAddress.GetLeftPart(UriPartial.Authority)
        );
        using var logout = await browser.DeleteAsync(
            "/management/v1/browser/session",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var loggedOut = await browser.GetAsync(
            "/management/v1/identity",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Unauthorized, loggedOut.StatusCode);
    }

    [Fact]
    public async Task PairingAndSession_ExpireWithoutReadingOrChangingUserConfiguration()
    {
        using var workspace = new TempWorkspace();
        var clock = new BrowserClock();
        var builder = GatewayManagementApplication.CreateBuilder(Token, 0, workspace.Paths);
        builder.Services.AddSingleton<TimeProvider>(clock);
        await using var app = GatewayManagementApplication.Build(
            builder,
            Token,
            Guid.NewGuid().ToString("N")
        );
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        browser.DefaultRequestHeaders.Add(
            "Origin",
            browser.BaseAddress.GetLeftPart(UriPartial.Authority)
        );
        var access = app.Services.GetRequiredService<GatewayBrowserAccess>();
        var expired = access.CreatePairing();
        clock.Now = clock.Now.AddMinutes(3);
        using var denied = await browser.PostAsJsonAsync(
            "/management/v1/browser/session",
            new BrowserPairingRequest(expired.Code),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        var pairing = access.CreatePairing();
        using var paired = await browser.PostAsJsonAsync(
            "/management/v1/browser/session",
            new BrowserPairingRequest(pairing.Code),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.OK, paired.StatusCode);
        clock.Now = clock.Now.AddMinutes(16);
        using var session = await browser.GetAsync(
            "/management/v1/browser/session",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
    }

    [Fact]
    public async Task Ui_IsPublicButAssetsCannotEscapeAndApiNeverFallsBackToHtml()
    {
        using var workspace = new TempWorkspace();
        var directory = Path.Combine(workspace.Paths.RootDirectory, "ui");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "index.html"),
            "<h1>AgentRouter test UI</h1>",
            TestContext.Current.CancellationToken
        );
        await File.WriteAllTextAsync(
            Path.Combine(directory, "styles.css"),
            "body { color: green; }",
            TestContext.Current.CancellationToken
        );
        await File.WriteAllTextAsync(
            Path.Combine(workspace.Paths.RootDirectory, "private.txt"),
            "private-marker",
            TestContext.Current.CancellationToken
        );
        var builder = GatewayManagementApplication.CreateBuilder(Token, 0, workspace.Paths);
        builder.Configuration["BrowserUi:Directory"] = directory;
        await using var app = GatewayManagementApplication.Build(
            builder,
            Token,
            Guid.NewGuid().ToString("N")
        );
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var ui = await client.GetAsync("/ui/", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
        Assert.Contains(
            "AgentRouter test UI",
            await ui.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal
        );
        Assert.Contains(
            "frame-ancestors 'none'",
            Assert.Single(ui.Headers.GetValues("Content-Security-Policy")),
            StringComparison.Ordinal
        );
        using var stylesheet = await client.GetAsync(
            "/ui/styles.css",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.OK, stylesheet.StatusCode);
        Assert.Equal("text/css", stylesheet.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "body { color: green; }",
            await stylesheet.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        using var escaped = await client.GetAsync(
            "/ui/%2e%2e%2fprivate.txt",
            TestContext.Current.CancellationToken
        );
        Assert.NotEqual(HttpStatusCode.OK, escaped.StatusCode);
        Assert.DoesNotContain(
            "private-marker",
            await escaped.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal
        );
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        using var api = await client.GetAsync(
            "/management/v1/missing",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NotFound, api.StatusCode);
        using var asset = await client.GetAsync(
            "/ui/missing.js",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NotFound, asset.StatusCode);
    }

    private sealed class BrowserClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
