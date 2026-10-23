using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Integrations.DevelopmentMail;
using PantryPress.Domain.Managers.Email;

namespace PantryPress.Tests.Integrations;

// PP1.2a: the development sink speaks plain SMTP, so it only ever reaches a mail catcher on this machine.
public sealed class DevelopmentMailTests
{
    private const string Recipient = "cook@example.test";

    private static readonly Guid MessageId = Guid.Parse("0199f0aa-2222-7000-8000-000000000002");

    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void Loopback_hosts_are_accepted(string host)
    {
        Assert.True(new DevelopmentMailOptions { Host = host, Port = 1025 }.IsValid());
    }

    [Theory]
    [InlineData("")]
    [InlineData("smtp.example.test")]
    [InlineData("10.0.0.5")]
    [InlineData("localhost.example.test")]
    public void Other_hosts_are_rejected(string host)
    {
        Assert.False(new DevelopmentMailOptions { Host = host, Port = 1025 }.IsValid());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void Port_must_be_in_range(int port)
    {
        Assert.False(new DevelopmentMailOptions { Host = "localhost", Port = port }.IsValid());
    }

    [Fact]
    public void Timeout_must_be_positive()
    {
        Assert.False(new DevelopmentMailOptions { Host = "localhost", Port = 1025, SendTimeout = TimeSpan.Zero }.IsValid());
    }

    [Fact]
    public void Sender_must_be_an_address()
    {
        Assert.False(new DevelopmentMailOptions { Host = "localhost", Port = 1025, From = "nobody" }.IsValid());
    }

    [Fact]
    public async Task Host_with_a_remote_smtp_host_fails_to_start()
    {
        using var host = BuildHost(options =>
        {
            options.Host = "smtp.example.test";
            options.Port = 25;
        });

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public void Sink_is_the_email_gateway()
    {
        using var host = BuildHost(options =>
        {
            options.Host = "localhost";
            options.Port = 1025;
        });

        Assert.IsType<SmtpDevelopmentMailGateway>(host.Services.GetRequiredService<IEmailGateway>());
    }

    // Port 1 on loopback refuses connections. The failure is the gateway's own type and never quotes the recipient.
    [Fact]
    public async Task Unreachable_catcher_is_a_transient_failure_without_the_recipient()
    {
        using var host = BuildHost(options =>
        {
            options.Host = "127.0.0.1";
            options.Port = 1;
        });

        var error = await Assert.ThrowsAsync<EmailDeliveryException>(() => SendAsync(host, CancellationToken.None));

        Assert.True(error.IsTransient);
        Assert.Equal(MessageId, error.MessageId);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(Recipient, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Catcher_that_never_answers_times_out_as_transient()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var host = BuildHost(options =>
        {
            options.Host = "127.0.0.1";
            options.Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            options.SendTimeout = TimeSpan.FromMilliseconds(300);
        });

        var error = await Assert.ThrowsAsync<EmailDeliveryException>(() => SendAsync(host, CancellationToken.None));

        Assert.True(error.IsTransient);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_a_delivery_failure()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var host = BuildHost(options =>
        {
            options.Host = "127.0.0.1";
            options.Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        });
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendAsync(host, cancelled.Token));
    }

    private static Task SendAsync(IHost host, CancellationToken cancellationToken) =>
        host.Services.GetRequiredService<IEmailGateway>().SendAsync(
            new EmailMessage(MessageId, Recipient, "Subject", "Text"),
            cancellationToken);

    private static IHost BuildHost(Action<DevelopmentMailOptions> configure)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddDevelopmentMailSink(configure);
        return builder.Build();
    }
}
