using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Integrations.DevelopmentMail;
using PantryPress.Domain.Managers.Email;

namespace PantryPress.Tests.Hosting;

// PP1.2a: the development email sink's mail catcher runs only in local Development (external.md, aspire.md).
public sealed class MailSinkTests
{
    [Fact]
    public async Task Mail_catcher_has_assigned_ports_and_a_health_check()
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>();

        var mail = appHost.Resources.Single(r => r.Name == "mail");

        Assert.Single(mail.Annotations.OfType<HealthCheckAnnotation>());
        var endpoints = mail.Annotations.OfType<EndpointAnnotation>().ToList();
        Assert.Equal(["http", "smtp"], endpoints.Select(e => e.Name).Order());
        Assert.All(endpoints, e => Assert.Null(e.Port));
        Assert.DoesNotContain(mail.Annotations, a => a is ContainerMountAnnotation);
    }

    [Fact]
    public async Task Publishing_has_no_mail_catcher()
    {
        var outputPath = Directory.CreateTempSubdirectory("pantrypress-publish-").FullName;
        try
        {
            await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.PantryPress_AppHost>(
                ["--operation", "publish", "--output-path", outputPath]);

            Assert.DoesNotContain(appHost.Resources, r => r.Name == "mail");
        }
        finally
        {
            Directory.Delete(outputPath, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Sink_delivers_text_and_html_to_the_mail_catcher()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await using var appHost = await EphemeralAppHost.CreateAsync(timeout.Token);
        await using var app = await appHost.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("mail", timeout.Token);

        var smtp = app.GetEndpoint("mail", "smtp");
        var services = new ServiceCollection()
            .AddDevelopmentMailSink(options =>
            {
                options.Host = smtp.Host;
                options.Port = smtp.Port;
            })
            .BuildServiceProvider();
        await using (services)
        {
            var id = Guid.NewGuid();
            var subject = $"Confirm your email {id:N}";
            await services.GetRequiredService<IEmailGateway>().SendAsync(
                new EmailMessage(id, "cook@example.test", subject, "Plain words — café", "<p>Plain words — café</p>"),
                timeout.Token);

            using var catcher = new HttpClient { BaseAddress = app.GetEndpoint("mail", "http") };
            var summary = await WaitForMessageAsync(catcher, subject, timeout.Token);
            var message = await catcher.GetFromJsonAsync<MailpitMessage>($"api/v1/message/{summary.Id}", timeout.Token);

            Assert.NotNull(message);
            Assert.Equal($"{id:N}@pantrypress.localhost", message.MessageId);
            Assert.Equal("no-reply@pantrypress.localhost", message.From.Address);
            Assert.Equal("cook@example.test", Assert.Single(message.To).Address);
            Assert.Equal("Plain words — café", message.Text.Trim());
            Assert.Equal("<p>Plain words — café</p>", message.Html.Trim());
        }
    }

    private static async Task<MailpitSummary> WaitForMessageAsync(
        HttpClient catcher,
        string subject,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var list = await catcher.GetFromJsonAsync<MailpitList>("api/v1/messages", cancellationToken);
            var found = list?.Messages.FirstOrDefault(m => m.Subject == subject);
            if (found is not null)
            {
                return found;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }

    private sealed record MailpitList([property: JsonPropertyName("messages")] List<MailpitSummary> Messages);

    private sealed record MailpitSummary(
        [property: JsonPropertyName("ID")] string Id,
        [property: JsonPropertyName("Subject")] string Subject);

    private sealed record MailpitMessage(
        [property: JsonPropertyName("MessageID")] string MessageId,
        [property: JsonPropertyName("From")] MailpitAddress From,
        [property: JsonPropertyName("To")] List<MailpitAddress> To,
        [property: JsonPropertyName("Text")] string Text,
        [property: JsonPropertyName("HTML")] string Html);

    private sealed record MailpitAddress([property: JsonPropertyName("Address")] string Address);
}
