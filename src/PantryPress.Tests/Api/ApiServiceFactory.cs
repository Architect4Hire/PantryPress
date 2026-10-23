using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PantryPress.ApiService.Controllers;
using PantryPress.Domain.Managers.Email;

namespace PantryPress.Tests.Api;

// PantryPress.ApiService in memory. Settings go in as host settings, which Program reads while it registers services.
// Data protection is ephemeral so tests never touch the developer's key ring. Tests sign in by sending the account id
// in a header; the API's own scheme still answers challenges, so a missing header gets the real 401 problem.
internal sealed class ApiServiceFactory(
    string environment,
    IReadOnlyDictionary<string, string?> settings,
    Action<IServiceCollection>? configureServices = null,
    bool signInWithTestHeader = true) : WebApplicationFactory<AccountController>
{
    public const string TestAccountHeader = "X-Test-Account";

    // Nothing listens here; hosts that never open a connection still need a well-formed connection string.
    public const string UnreachableDatabase =
        "Server=127.0.0.1,1;Database=none;User Id=none;Password=none;TrustServerCertificate=true;Connect Timeout=1";

    public static ApiServiceFactory WithoutDatabase(
        string environment = "Testing",
        IReadOnlyDictionary<string, string?>? extraSettings = null,
        Action<IServiceCollection>? configureServices = null,
        bool signInWithTestHeader = true)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:pantrypress"] = UnreachableDatabase,
            ["Accounts:AppBaseUrl"] = "https://app.localhost",

            // The API refuses to start without the gateway's secret (PP1.6).
            ["Gateway:SharedSecret"] = TestSecrets.GatewaySecret,
        };
        foreach (var (key, value) in extraSettings ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        return new ApiServiceFactory(
            environment,
            settings,
            services =>
            {
                if (environment != "Production")
                {
                    services.AddSingleton<IEmailGateway>(new Fakes.RecordingEmailGateway());
                }

                configureServices?.Invoke(services);
            },
            signInWithTestHeader);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IDataProtectionProvider, EphemeralDataProtectionProvider>());

            // Most tests sign in with a header, so a route's own rules are what they exercise. The ones that test the
            // forwarded session itself leave the real GatewaySession scheme in place.
            if (signInWithTestHeader)
            {
                services.AddAuthentication(options => options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, configureOptions: null);
            }

            configureServices?.Invoke(services);
        });
    }

    public HttpClient CreateClientFor(Guid? account)
    {
        var client = CreateClient();
        if (account is { } id)
        {
            client.DefaultRequestHeaders.Add(TestAccountHeader, id.ToString());
        }

        return client;
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(TestAccountHeader, out var value))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, value.ToString())], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
