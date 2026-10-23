using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PantryPress.Tests.Gateway;

// Shared scaffolding for the PP1.6 session tests: what ApiService answers the gateway, and how a test reads the
// cookies the gateway sets.
internal static class SessionFixtures
{
    public const string Email = "cook@example.test";
    public const string Passphrase = "a long kitchen phrase";
    public const string Stamp = "STAMP-ONE";
    public const string RotatedStamp = "STAMP-TWO";

    public static readonly Guid Account = new("7f1f5c9a-1d2b-4c3d-9e4f-5a6b7c8d9e0f");

    /// <summary>ApiService accepting a sign-in, as its internal surface answers it.</summary>
    public static HttpResponseMessage Accepted(
        bool secondStepRequired = false,
        bool emailVerified = true,
        string stamp = Stamp) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                accountId = Account,
                email = Email,
                securityStamp = stamp,
                secondStepRequired,
                emailVerified,
            }),
        };

    /// <summary>ApiService refusing one, with the problem document it really writes.</summary>
    public static HttpResponseMessage Refused(
        HttpStatusCode status = HttpStatusCode.BadRequest,
        string code = "invalid-credentials",
        string title = "That email address and password don't match.") =>
        new(status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    type = "https://tools.ietf.org/html/rfc9110",
                    title,
                    status = (int)status,
                    code,
                }),
                System.Text.Encoding.UTF8,
                new MediaTypeHeaderValue("application/problem+json")),
        };

    /// <summary>The Set-Cookie value for one cookie name, or null when the response set none.</summary>
    public static string? SetCookie(this HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(value => value.StartsWith(name + "=", StringComparison.Ordinal))
            : null;

    /// <summary>The cookie's value, as a browser would store it.</summary>
    public static string? CookieValue(this HttpResponseMessage response, string name)
    {
        var header = response.SetCookie(name);
        if (header is null)
        {
            return null;
        }

        var value = header[(name.Length + 1)..].Split(';')[0];
        return value.Length == 0 ? null : value;
    }

    /// <summary>Signs in through the gateway and returns the browser that now holds the session.</summary>
    public static async Task<HttpClient> SignedInAsync(this GatewayFactory gateway, bool secondFactor = false)
    {
        var browser = gateway.CreateBrowser();
        using var response = await browser.PostAsJsonAsync(
            "/api/v1/account/session",
            new { email = Email, password = Passphrase });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        if (!secondFactor)
        {
            return browser;
        }

        using var completed = await browser.PostAsJsonAsync(
            "/api/v1/account/session/second-factor",
            new { code = "123456" });
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        return browser;
    }
}
