using System.Security.Cryptography;
using System.Text;

namespace AgentRouter.Core.Hosting.Browser;

/// <summary>A single-use code displayed by the local CLI, never included in a URL.</summary>
public sealed record BrowserPairingResponse(string Code, DateTimeOffset ExpiresAt);

/// <summary>A pairing code entered by the user in the local browser.</summary>
public sealed record BrowserPairingRequest(string Code);

/// <summary>Browser-session metadata and the in-memory mutation token.</summary>
public sealed record BrowserSessionResponse(string CsrfToken, DateTimeOffset ExpiresAt);

public sealed class GatewayBrowserAccess(TimeProvider clock)
{
    private const string CookieName = "ar.browser";
    private readonly object sync = new();
    private readonly Dictionary<string, DateTimeOffset> pairings = [];
    private readonly Dictionary<string, BrowserSessionResponse> sessions = [];
    private DateTimeOffset attemptWindow = clock.GetUtcNow();
    private int attempts;

    public BrowserPairingResponse CreatePairing()
    {
        lock (sync)
        {
            Prune();
            if (pairings.Count >= 8)
                pairings.Remove(pairings.Keys.First());
            var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
            var expires = clock.GetUtcNow().AddMinutes(2);
            pairings.Add(Hash(code), expires);
            return new(code, expires);
        }
    }

    public IResult Pair(HttpContext context, BrowserPairingRequest request)
    {
        lock (sync)
        {
            Prune();
            if (clock.GetUtcNow() - attemptWindow >= TimeSpan.FromMinutes(1))
            {
                attemptWindow = clock.GetUtcNow();
                attempts = 0;
            }
            if (++attempts > 10)
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            var code = request.Code?.Trim().ToUpperInvariant();
            if (code is null || code.Length != 12 || !pairings.Remove(Hash(code)))
                return Results.Unauthorized();
            if (sessions.Count >= 32)
                sessions.Remove(sessions.Keys.First());
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var session = new BrowserSessionResponse(
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                clock.GetUtcNow().AddMinutes(15)
            );
            sessions.Add(Hash(token), session);
            context.Response.Cookies.Append(
                CookieName,
                token,
                new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Secure = context.Request.IsHttps,
                    Path = "/management/v1",
                    MaxAge = TimeSpan.FromMinutes(15),
                    IsEssential = true,
                }
            );
            return Results.Ok(session);
        }
    }

    public BrowserSessionResponse? FindSession(HttpContext context)
    {
        lock (sync)
        {
            Prune();
            return context.Request.Cookies.TryGetValue(CookieName, out var token)
                ? sessions.GetValueOrDefault(Hash(token))
                : null;
        }
    }

    public IResult Logout(HttpContext context)
    {
        lock (sync)
        {
            if (context.Request.Cookies.TryGetValue(CookieName, out var token))
                sessions.Remove(Hash(token));
            context.Response.Cookies.Delete(
                CookieName,
                new CookieOptions { Path = "/management/v1" }
            );
            return Results.NoContent();
        }
    }

    public static bool Matches(string expected, string supplied) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied))
        );

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private void Prune()
    {
        var now = clock.GetUtcNow();
        foreach (
            var key in pairings
                .Where(entry => entry.Value <= now)
                .Select(entry => entry.Key)
                .ToArray()
        )
            pairings.Remove(key);
        foreach (
            var key in sessions
                .Where(entry => entry.Value.ExpiresAt <= now)
                .Select(entry => entry.Key)
                .ToArray()
        )
            sessions.Remove(key);
    }
}
