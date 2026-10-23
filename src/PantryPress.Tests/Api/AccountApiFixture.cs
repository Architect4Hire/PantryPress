using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using PantryPress.Domain.Managers.Email;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Accounts.DataLayer;
using PantryPress.MigrationService;
using PantryPress.Tests.Fakes;
using PantryPress.Tests.Hosting;

namespace PantryPress.Tests.Api;

[CollectionDefinition(Name)]
public sealed class AccountApiCollection : ICollectionFixture<AccountApiFixture>
{
    public const string Name = "Account API";
}

// One ephemeral AppHost for every account API test: the migration service builds the schema in a SQL container, then
// ApiService runs in memory against it with recorded email and a clock the tests move. Tests share the database, so
// each one works with its own fresh account.
public sealed partial class AccountApiFixture : IAsyncLifetime
{
    public const string FirstPassphrase = "a long kitchen phrase";
    public const string SecondPassphrase = "another long kitchen phrase";

    private readonly CancellationTokenSource _timeout = new(TimeSpan.FromMinutes(5));

    // One key ring for both hosts, as one deployed service has one (PP19 makes it persistent): authenticator keys are
    // encrypted with it, so a host with a ring of its own could not read what the other stored.
    private readonly EphemeralDataProtectionProvider _keys = new();
    private IDistributedApplicationTestingBuilder? _appHost;
    private DistributedApplication? _app;

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

    internal RecordingEmailGateway Email { get; } = new();

    internal ApiServiceFactory Api { get; private set; } = null!;

    /// <summary>
    /// The same API against the same database, with the real <c>GatewaySession</c> scheme left in place, so a test can
    /// send what the gateway sends and watch the API decide (PP1.6).
    /// </summary>
    internal ApiServiceFactory Forwarded { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _appHost = await EphemeralAppHost.CreateAsync(_timeout.Token);
        _app = await _appHost.BuildAsync(_timeout.Token);
        await _app.StartAsync(_timeout.Token);

        var finished = await _app.ResourceNotifications.WaitForResourceAsync(
            "migrationservice",
            e => KnownResourceStates.TerminalStates.Contains(e.Snapshot.State?.Text),
            _timeout.Token);
        Assert.Equal(0, finished.Snapshot.ExitCode);

        var connectionString = await _app.GetConnectionStringAsync(MigrationServiceExtensions.ConnectionName, _timeout.Token);
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:pantrypress"] = connectionString,
            ["Accounts:AppBaseUrl"] = "https://app.localhost",
            ["Gateway:SharedSecret"] = TestSecrets.GatewaySecret,
            ["RateLimits:AnonymousAccountPerMinute"] = "100000",
            ["RateLimits:SignedInAccountPerMinute"] = "100000",
            ["RateLimits:SignInPerMinute"] = "100000",
        };

        void Configure(IServiceCollection services)
        {
            services.AddSingleton<IEmailGateway>(Email);
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Time));
            services.Replace(ServiceDescriptor.Singleton<IDataProtectionProvider>(_keys));
        }

        Api = new ApiServiceFactory("Testing", settings, Configure);
        _ = Api.Server;
        Forwarded = new ApiServiceFactory("Testing", settings, Configure, signInWithTestHeader: false);
        _ = Forwarded.Server;
    }

    public async Task DisposeAsync()
    {
        if (Forwarded is not null)
        {
            await Forwarded.DisposeAsync();
        }

        if (Api is not null)
        {
            await Api.DisposeAsync();
        }

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        if (_appHost is not null)
        {
            await _appHost.DisposeAsync();
        }

        _timeout.Dispose();
    }

    internal HttpClient Client(AccountId? signedIn = null) => Api.CreateClientFor(signedIn?.Value);

    internal DateTimeOffset Now => Api.Services.GetRequiredService<IClock>().UtcNow;

    /// <summary>
    /// A client that sends what the gateway sends for a complete session: the secret, the account, and the security
    /// stamp the session is bound to. <paramref name="secret"/> and <paramref name="stamp"/> are overridable so a test
    /// can forge either one.
    /// </summary>
    internal HttpClient ForwardedClient(
        AccountId account,
        string stamp,
        bool secondFactorVerified = false,
        string? secret = null)
    {
        var client = Forwarded.CreateClient();
        client.DefaultRequestHeaders.Add("X-PP-Gateway", secret ?? TestSecrets.GatewaySecret);
        client.DefaultRequestHeaders.Add("X-PP-Account", account.ToString());
        client.DefaultRequestHeaders.Add("X-PP-Stamp", stamp);
        if (secondFactorVerified)
        {
            client.DefaultRequestHeaders.Add("X-PP-Mfa", "true");
        }

        return client;
    }

    /// <summary>The account's current security stamp, as the gateway would have been told at sign-in.</summary>
    internal Task<string> StampAsync(AccountId account) =>
        WithAccountAsync(account, (users, user) => users.GetSecurityStampAsync(user));

    /// <summary>
    /// Grants the platform role the way the operator's job does (ACCT-005; PP1.4), against this fixture's database.
    /// A real grant rotates the security stamp, so a session started before it is over.
    /// </summary>
    internal async Task GrantPlatformAdminAsync(AccountId account)
    {
        await using var operators = await Persistence.TestDatabase.CreateOperatorServicesAsync(
            _app!,
            string.Empty,
            _timeout.Token);
        var granted = await operators.GetRequiredService<IAccountRoles>()
            .GrantPlatformAdminAsync(account, _timeout.Token);
        Assert.True(granted.IsSuccess);
    }

    internal static string NewEmail() => $"cook-{Guid.NewGuid():N}@example.test";

    /// <summary>Signs up through the API with <see cref="FirstPassphrase"/> and returns the account id.</summary>
    internal async Task<AccountId> RegisterAsync(string email)
    {
        using var response = await Client().PostAsJsonAsync(
            "/api/v1/account/register",
            new RegisterAccountRequest(email, FirstPassphrase));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await using var scope = Api.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<AccountUserManager>().FindByEmailAsync(email);
        return new AccountId(Assert.IsType<ApplicationUser>(user).Id);
    }

    /// <summary>Signs up and confirms the email through the link in the confirmation email.</summary>
    internal async Task<AccountId> RegisterConfirmedAsync(string email)
    {
        var account = await RegisterAsync(email);
        var (linkAccount, token) = ReadLink(await NextEmailToAsync(email, "Confirm your email"), "verify-email");
        using var response = await Client().PostAsJsonAsync(
            "/api/v1/account/email/verify",
            new VerifyEmailRequest(linkAccount, token));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return account;
    }

    /// <summary>
    /// Waits until everything queued so far has been sent: the queue has one reader, so once a marker queued now is
    /// sent, so is every earlier message. Assertions that an email was not sent come after this.
    /// </summary>
    internal async Task DrainEmailAsync()
    {
        var marker = new EmailMessage(Guid.NewGuid(), $"drain-{Guid.NewGuid():N}@example.test", "drain", "drain");
        Api.Services.GetRequiredService<IEmailQueue>().Enqueue(marker);
        await WaitForAsync(() => Email.Sent.Any(m => m.Id == marker.Id));
    }

    internal IReadOnlyList<EmailMessage> EmailsTo(string email) => [.. Email.Sent.Where(m => m.To == email)];

    /// <summary>The newest email to the address with the subject, once everything queued has been sent.</summary>
    internal async Task<EmailMessage> NextEmailToAsync(string email, string subjectStart)
    {
        await DrainEmailAsync();
        return EmailsTo(email).Last(m => m.Subject.StartsWith(subjectStart, StringComparison.Ordinal));
    }

    internal static (Guid Account, string Token) ReadLink(EmailMessage message, string page)
    {
        var match = LinkPattern().Match(message.TextBody);
        Assert.True(match.Success, "The email has no account link.");
        Assert.Equal(page, match.Groups["page"].Value);
        return (Guid.Parse(match.Groups["account"].Value), Uri.UnescapeDataString(match.Groups["token"].Value));
    }

    /// <summary>Runs <paramref name="work"/> against the stored account in a fresh scope.</summary>
    internal async Task<T> WithAccountAsync<T>(AccountId account, Func<AccountUserManager, ApplicationUser, Task<T>> work)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<AccountUserManager>();
        var user = Assert.IsType<ApplicationUser>(await users.FindByIdAsync(account.ToString()));
        return await work(users, user);
    }

    internal async Task<T> WithServiceAsync<TService, T>(Func<TService, Task<T>> work)
        where TService : notnull
    {
        await using var scope = Api.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    [GeneratedRegex(@"https://app\.localhost/(?<page>[a-z-]+)#account=(?<account>[0-9a-f-]{36})&token=(?<token>[A-Za-z0-9%_.~-]+)")]
    private static partial Regex LinkPattern();
}
