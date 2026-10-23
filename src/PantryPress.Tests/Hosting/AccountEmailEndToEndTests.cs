using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.Tests.Hosting;

// PP1.2c: under the real AppHost, a sign-up on ApiService reaches the local mail catcher through the development sink,
// with a confirmation link on the gateway's app.localhost origin.
public sealed class AccountEmailEndToEndTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Sign_up_on_the_running_ApiService_lands_in_the_mail_catcher_with_a_gateway_link()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await using var appHost = await EphemeralAppHost.CreateAsync(timeout.Token);
        await using var app = await appHost.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("apiservice", timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("mail", timeout.Token);

        var email = $"cook-{Guid.NewGuid():N}@example.test";
        using var api = app.CreateHttpClient("apiservice", "http");
        using var response = await api.PostAsJsonAsync(
            "/api/v1/account/register",
            new RegisterAccountRequest(email, "a long kitchen phrase"),
            timeout.Token);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var catcher = new HttpClient { BaseAddress = app.GetEndpoint("mail", "http") };
        var text = await WaitForTextToAsync(catcher, email, timeout.Token);
        var gateway = app.GetEndpoint("gateway", "https");
        Assert.Contains($"https://app.localhost:{gateway.Port}/verify-email#account=", text, StringComparison.Ordinal);
    }

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
