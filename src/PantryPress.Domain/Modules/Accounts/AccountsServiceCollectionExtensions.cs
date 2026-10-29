using FluentValidation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Outbox;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Accounts.Business;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Accounts.DataLayer;
using PantryPress.Domain.Modules.Accounts.Facades;
using PantryPress.Domain.Modules.Accounts.Repositories;
using PantryPress.Domain.Modules.Accounts.Validation;

namespace PantryPress.Domain.Modules.Accounts;

public static class AccountsServiceCollectionExtensions
{
    /// <summary>
    /// Version 2 adds column lengths to Version 1; Version 3 adds passkeys, which ACCT-001 does not include.
    /// </summary>
    public static readonly Version IdentitySchemaVersion = IdentitySchemaVersions.Version2;

    /// <summary>Version 2's key length when none is set; pinned so a host setting cannot widen the key columns.</summary>
    public const int IdentityMaxLengthForKeys = 128;

    /// <summary>The issuer authenticator apps show next to the account.</summary>
    public const string AuthenticatorIssuer = "PantryPress";

    /// <summary>
    /// Pins the store options that shape Identity's tables. Identity builds its part of the model from
    /// <see cref="IdentityOptions"/> in the application's services, so every host that creates
    /// <c>PantryPressDbContext</c>, and the design-time factory, registers this or the model would drift from the
    /// migrations. PostConfigure runs after every host Configure, so a host cannot change them by accident. Personal
    /// data protection stays off: it would add a value converter whose output can outgrow the columns. The context
    /// refuses to build its model unless all three match.
    /// </summary>
    public static IServiceCollection AddAccountsIdentitySchema(this IServiceCollection services) =>
        services.PostConfigure<IdentityOptions>(options =>
        {
            options.Stores.SchemaVersion = IdentitySchemaVersion;
            options.Stores.MaxLengthForKeys = IdentityMaxLengthForKeys;
            options.Stores.ProtectPersonalData = false;
        });

    /// <summary>
    /// Identity itself: the pinned schema, the stores over <see cref="AccountUserStore"/>, the user manager, the unit
    /// of work the facades commit, and the audit log they record security changes in (PP1.11). <see cref="AddAccounts"/> and <see cref="AddAccountRoles"/> both start here and
    /// no host calls both: the API serves accounts, the operator's process administers the platform role.
    /// </summary>
    private static IdentityBuilder AddAccountsIdentity(this IServiceCollection services)
    {
        // The migration job registers the platform role and, in local Development, the seed (PP3.9a); both start here,
        // so a second call must not add a second copy of Identity's stores, managers, or validators.
        if (services.Any(d => d.ServiceType == typeof(AccountUserManager)))
        {
            return new IdentityBuilder(typeof(ApplicationUser), typeof(IdentityRole<Guid>), services);
        }

        services.AddAccountsIdentitySchema();
        var identity = services.AddIdentityCore<ApplicationUser>(ConfigureIdentity)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<PantryPressDbContext>()
            .AddUserManager<AccountUserManager>();
        services.Replace(ServiceDescriptor.Scoped<IUserStore<ApplicationUser>, AccountUserStore>());

        services.AddScoped<AccountsUnitOfWork>();
        services.AddScoped<SessionRevocationRepository>();
        services.AddScoped<AccountAddressRepository>();
        services.AddScoped<AccountsDataLayer>();
        services.AddAuditLog();
        return identity;
    }

    /// <summary>
    /// Registers only what it takes to grant and revoke the platform role (ACCT-005; PP1.4): no email, no link tokens,
    /// no account routes. Called by the operator's process alone, which is what keeps <see cref="IAccountRoles"/> out
    /// of reach of every request-serving host.
    /// </summary>
    public static IServiceCollection AddAccountRoles(this IServiceCollection services)
    {
        services.AddAccountsIdentity();

        // The account store is built with a key ring it must never use here; see UnusedDataProtection.
        services.TryAddSingleton<IDataProtectionProvider, UnusedDataProtection>();

        // Only this process can compose a role change: the shared Accounts data layer has no role calls at all.
        services.AddScoped<AccountRolesDataLayer>();
        services.AddScoped<IAccountRoles, AccountRolesFacade>();
        return services;
    }

    /// <summary>
    /// What the local development seed needs from this module (PP3.9a): an account with a confirmed address, and the
    /// three narrow read contracts the Sites module's facades take, since the seed creates its sites through
    /// <c>ISitesFacade</c> exactly as the API does. No email, no link tokens, no account routes, no sign-up: the seed's
    /// own operation is the only way in, and it is registered by the migration job in local Development alone, which is
    /// what keeps <see cref="IDevelopmentAccounts"/> out of reach of every request-serving host.
    /// </summary>
    public static IServiceCollection AddAccountsDevelopmentSeed(this IServiceCollection services)
    {
        services.AddAccountsIdentity();

        // The account store is built with a key ring it must never use here; see UnusedDataProtection. The same
        // registration as AddAccountRoles', which this job also calls - both are TryAdd, so either order is one ring.
        services.TryAddSingleton<IDataProtectionProvider, UnusedDataProtection>();

        services.TryAddScoped<IDevelopmentAccounts, DevelopmentAccountsFacade>();
        services.TryAddScoped<IAccountEmailConfirmation, AccountEmailConfirmation>();
        services.TryAddScoped<IAccountAddressCheck, AccountAddressCheck>();
        services.TryAddScoped<IAccountDirectory, AccountDirectory>();
        return services;
    }

    /// <summary>
    /// What the Worker does for the Accounts module (PP1.10b, PP1.12): Identity over <see cref="AccountUserStore"/> with
    /// the link-token providers, the account emails it delivers from the outbox, and the housekeeping its sweep runs.
    /// The <paramref name="configuration"/> section binds <see cref="AccountsOptions"/> - the admin app's origin for the
    /// links - checked at startup. The host also registers <c>PantryPressDbContext</c>, the clock, the outbox dispatcher,
    /// an <see cref="Managers.Email.IEmailGateway"/>, and data protection with the API's application name, since the
    /// links it makes are checked by the API and the store reads what the API protected.
    /// </summary>
    /// <remarks>
    /// <paramref name="sendsEmail"/> is false where there is no email provider yet - anywhere but local Development
    /// until PP13.1. The Worker then claims no account email at all: messages wait in the outbox, visible in its backlog
    /// gauge, and go out once a provider is configured, while the rest of the Worker carries on (PP1.12).
    /// </remarks>
    public static IServiceCollection AddAccountsWorker(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        bool sendsEmail)
    {
        services.AddAccountsIdentity().AddAccountTokenProviders();
        if (sendsEmail)
        {
            services.AddAccountEmailDelivery(configuration);
        }

        services.AddScoped<IAccountMaintenance, AccountMaintenanceFacade>();
        return services;
    }

    /// <summary>
    /// The outbox handler that sends account email, and what it builds the email from (PP1.10b). Part of
    /// <see cref="AddAccountsWorker"/>; on its own only for a test host that already registered <see cref="AddAccounts"/>
    /// and delivers its own outbox.
    /// </summary>
    internal static IServiceCollection AddAccountEmailDelivery(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        // Both checked at startup: a host that sends account email has the link origin and a provider, or it refuses to
        // start rather than claim account email and poison every message for want of one.
        services.AddOptions<AccountsOptions>()
            .Bind(configuration)
            .Validate(o => o.IsValid(), "Accounts:AppBaseUrl must be the admin app's absolute HTTPS origin.")
            .Validate<IServiceProvider>(
                (_, provider) => provider.GetService<Managers.Email.IEmailGateway>() is not null,
                "No email gateway is registered. Account email is sent from the outbox here (PP1.10b); outside "
                + "Development a provider arrives with PP13.1.")
            .ValidateOnStart();
        services.AddSingleton(sp => new AccountEmails(sp.GetRequiredService<IOptions<AccountsOptions>>().Value.AppBaseUrl));
        services.AddScoped<AccountMailDataLayer>();
        services.AddOutboxHandler<AccountEmailMessage, AccountEmailHandler>();
        return services;
    }

    /// <summary>
    /// Registers the Accounts module (ACCT-001, ACCT-003): Identity over <see cref="AccountUserStore"/>, the token
    /// providers, the facades, and their validators, and the outbox the facades stage account email in (PP1.10b). The
    /// host also registers <c>PantryPressDbContext</c>, the clock, and data protection. The API sends no email itself:
    /// the Worker does, from the outbox, so this host needs no link origin and no email gateway.
    /// </summary>
    public static IServiceCollection AddAccounts(this IServiceCollection services)
    {
        services.AddAccountsIdentity().AddAccountTokenProviders();
        services.AddOutbox();

        services.AddScoped<IValidator<RegisterAccountRequest>, RegisterAccountRequestValidator>();
        services.AddScoped<IValidator<VerifyEmailRequest>, VerifyEmailRequestValidator>();
        services.AddScoped<IValidator<ResendVerificationRequest>, ResendVerificationRequestValidator>();
        services.AddScoped<IValidator<ForgotPasswordRequest>, ForgotPasswordRequestValidator>();
        services.AddScoped<IValidator<ResetPasswordRequest>, ResetPasswordRequestValidator>();
        services.AddScoped<IValidator<ChangePasswordRequest>, ChangePasswordRequestValidator>();
        services.AddScoped<IValidator<ConfirmPasswordRequest>, ConfirmPasswordRequestValidator>();
        services.AddScoped<IValidator<EnableTwoFactorRequest>, EnableTwoFactorRequestValidator>();
        services.AddScoped<IValidator<SecondFactorRequest>, SecondFactorRequestValidator>();
        services.AddScoped<IValidator<SignInRequest>, SignInRequestValidator>();
        services.AddScoped<IValidator<CurrentSessionRequest>, CurrentSessionRequestValidator>();
        services.AddScoped<IValidator<EndSessionRequest>, EndSessionRequestValidator>();

        services.AddScoped<IAccountRegistrationFacade, AccountRegistrationFacade>();
        services.AddScoped<IPasswordFacade, PasswordFacade>();
        services.AddScoped<ITwoFactorFacade, TwoFactorFacade>();
        services.AddScoped<ISessionFacade, SessionFacade>();
        services.AddScoped<IAccountPublishingEligibility, AccountPublishingEligibility>();
        services.AddScoped<IAccountEmailConfirmation, AccountEmailConfirmation>();
        services.AddScoped<IAccountAddressCheck, AccountAddressCheck>();
        services.AddScoped<IAccountDirectory, AccountDirectory>();
        return services;
    }

    // The API checks the links the Worker makes, so both register the same providers under the same names.
    private static IdentityBuilder AddAccountTokenProviders(this IdentityBuilder identity) =>
        identity
            .AddTokenProvider<EmailConfirmationTokenProvider>(EmailConfirmationTokenProvider.Name)
            .AddTokenProvider<PasswordResetTokenProvider>(PasswordResetTokenProvider.Name)
            .AddTokenProvider<AuthenticatorCodeProvider>(AuthenticatorCodeProvider.Name);

    // Approved at PP1.2 and PP1.3: 12–128 characters with no composition rules, unique emails that double as user
    // names, sign-in before confirmation (publishing waits for it instead), and lockout after five wrong passwords or
    // codes for 15 minutes.
    private static void ConfigureIdentity(IdentityOptions options)
    {
        options.Password.RequiredLength = PasswordRules.MinLength;
        options.Password.RequiredUniqueChars = 1;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;

        options.User.RequireUniqueEmail = true;
        options.User.AllowedUserNameCharacters = string.Empty;

        options.SignIn.RequireConfirmedEmail = false;
        options.SignIn.RequireConfirmedAccount = false;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = LockoutRules.MaxFailedAttempts;
        options.Lockout.DefaultLockoutTimeSpan = LockoutRules.Lockout;

        options.Tokens.EmailConfirmationTokenProvider = EmailConfirmationTokenProvider.Name;
        options.Tokens.PasswordResetTokenProvider = PasswordResetTokenProvider.Name;
        options.Tokens.AuthenticatorTokenProvider = AuthenticatorCodeProvider.Name;
        options.Tokens.AuthenticatorIssuer = AuthenticatorIssuer;
    }
}
