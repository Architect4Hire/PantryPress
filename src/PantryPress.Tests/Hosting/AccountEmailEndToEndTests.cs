using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Gateway.Hosting;

namespace PantryPress.Tests.Hosting;

// PP1.2c: under the real AppHost, a sign-up on ApiService reaches the local mail catcher through the development sink,
// with a confirmation link on the gateway's app.localhost origin. Since PP1.10b the Worker sends it, from the outbox.
// PP1.12: and the phase checkpoint, end to end - a blogger registers, confirms the address from the email, signs in
// through the BFF, comes back to the app, and signs out, without a token ever reaching the browser.
public sealed partial class AccountEmailEndToEndTests
{
    private const string Passphrase = "a long kitchen phrase";

    // One AppHost for the whole flow: each one more that the suite starts costs a minute and risks the others' timeouts.
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_blogger_registers_confirms_signs_in_comes_back_and_signs_out_through_the_gateway()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await using var appHost = await EphemeralAppHost.CreateAsync(timeout.Token);
        await using var app = await appHost.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        foreach (var resource in new[] { "apiservice", "gateway", "worker", "mail" })
        {
            await app.ResourceNotifications.WaitForResourceHealthyAsync(resource, timeout.Token);
        }

        // Over TLS at app.localhost, the only way the gateway is reached; cookies are sent by hand, as a browser would.
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            UseCookies = false,
        };
        using var client = new HttpClient(handler) { BaseAddress = app.GetEndpoint("gateway", "https") };
        var host = $"app.localhost:{client.BaseAddress!.Port}";
        var email = $"cook-{Guid.NewGuid():N}@example.test";
        var credentials = new Dictionary<string, string> { ["email"] = email, ["password"] = Passphrase };

        // The admin app's first call: nobody is signed in, and it gets the antiforgery pair every write needs.
        using var bootstrap = await SendAsync(client, host, HttpMethod.Get, "/api/v1/account/session", timeout.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, bootstrap.StatusCode);
        var tokens = $"{Cookie(bootstrap, GatewayAntiforgery.CookieName)}; {Cookie(bootstrap, GatewayAntiforgery.TokenCookieName)}";
        var xsrf = Cookie(bootstrap, GatewayAntiforgery.TokenCookieName)![(GatewayAntiforgery.TokenCookieName.Length + 1)..];

        // 1. Register.
        using var registered = await SendAsync(
            client, host, HttpMethod.Post, "/api/v1/account/register", timeout.Token, credentials, tokens, xsrf);
        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);

        // 2. Confirm the address from the link the Worker emailed (PP1.10b): the admin app reads it from the fragment.
        // The link opens the admin app on the gateway's own origin, never a host taken from a request.
        using var catcher = new HttpClient { BaseAddress = app.GetEndpoint("mail", "http") };
        var text = await WaitForTextToAsync(catcher, email, timeout.Token);
        Assert.Contains($"https://app.localhost:{client.BaseAddress.Port}/verify-email#account=", text, StringComparison.Ordinal);
        var link = LinkPattern().Match(text);
        Assert.True(link.Success, "The confirmation email has no link.");
        using var confirmed = await SendAsync(
            client,
            host,
            HttpMethod.Post,
            "/api/v1/account/email/verify",
            timeout.Token,
            new VerifyEmailRequest(Guid.Parse(link.Groups["account"].Value), Uri.UnescapeDataString(link.Groups["token"].Value)),
            tokens,
            xsrf);
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);

        // 3. Sign in through the BFF: the browser gets a cookie and a view of the account, never a token or a stamp.
        using var signedIn = await SendAsync(
            client, host, HttpMethod.Post, "/api/v1/account/session", timeout.Token, credentials, tokens, xsrf);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        var session = Cookie(signedIn, SessionCookie.CompleteName);
        Assert.NotNull(session);
        var setCookie = string.Join("\n", signedIn.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);
        using var view = JsonDocument.Parse(await signedIn.Content.ReadAsStringAsync(timeout.Token));
        Assert.Equal(
            ["accountId", "email", "emailVerified", "secondFactorVerified", "stage"],
            view.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.True(view.RootElement.GetProperty("emailVerified").GetBoolean());

        // 4. Come back - a refresh, a new tab: the session is read again, from the database's word.
        using var refreshed = await SendAsync(
            client, host, HttpMethod.Get, "/api/v1/account/session", timeout.Token, cookie: session);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        using var again = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync(timeout.Token));
        Assert.Equal("complete", again.RootElement.GetProperty("stage").GetString());
        Assert.True(again.RootElement.GetProperty("emailVerified").GetBoolean());
        using var signedInRoute = await SendAsync(
            client, host, HttpMethod.Get, "/api/v1/account/two-factor", timeout.Token, cookie: session);
        Assert.Equal(HttpStatusCode.OK, signedInRoute.StatusCode);

        // 5. Sign out: the cookie goes, and a copy of it kept from before reaches nothing (PP1.12).
        using var signedOut = await SendAsync(
            client, host, HttpMethod.Delete, "/api/v1/account/session", timeout.Token, cookie: $"{session}; {tokens}", token: xsrf);
        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
        using var replayed = await SendAsync(
            client, host, HttpMethod.Get, "/api/v1/account/two-factor", timeout.Token, cookie: session);
        Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);
    }

    private static string? Cookie(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(value => value.StartsWith(name + "=", StringComparison.Ordinal))?.Split(';')[0]
            : null;

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string host,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken,
        object? body = null,
        string? cookie = null,
        string? token = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Host = host;
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        if (token is not null)
        {
            request.Headers.Add(GatewayAntiforgery.HeaderName, token);
        }

        return await client.SendAsync(request, cancellationToken);
    }

    [GeneratedRegex(@"/verify-email#account=(?<account>[0-9a-f-]{36})&token=(?<token>[A-Za-z0-9%_.~-]+)")]
    private static partial Regex LinkPattern();

    private static async Task<string> WaitForTextToAsync(HttpClient catcher, string to, CancellationToken cancellationToken)
    {
        while (true)
        {
            var list = await catcher.GetFromJsonAsync<MailpitList>("api/v1/messages", cancellationToken);
            var found = list?.Messages.FirstOrDefault(m => m.To.Any(a => a.Address == to));
            if (found is not null)
            {
                var message = await catcher.GetFromJsonAsync<MailpitMessage>($"api/v1/message/{found.Id}", cancellationToken);
                return message!.Text;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }

    private sealed record MailpitList([property: JsonPropertyName("messages")] List<MailpitSummary> Messages);

    private sealed record MailpitSummary(
        [property: JsonPropertyName("ID")] string Id,
        [property: JsonPropertyName("To")] List<MailpitAddress> To);

    private sealed record MailpitMessage([property: JsonPropertyName("Text")] string Text);

    private sealed record MailpitAddress([property: JsonPropertyName("Address")] string Address);
}
