using FluentValidation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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
    /// Identity itself: the pinned schema, the stores over <see cref="AccountUserStore"/>, the user manager, and the
    /// unit of work the facades commit. <see cref="AddAccounts"/> and <see cref="AddAccountRoles"/> both start here and
    /// no host calls both: the API serves accounts, the operator's process administers the platform role.
    /// </summary>
    private static IdentityBuilder AddAccountsIdentity(this IServiceCollection services)
    {
        services.AddAccountsIdentitySchema();
        var identity = services.AddIdentityCore<ApplicationUser>(ConfigureIdentity)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<PantryPressDbContext>()
            .AddUserManager<AccountUserManager>();
        services.Replace(ServiceDescriptor.Scoped<IUserStore<ApplicationUser>, AccountUserStore>());

        services.AddScoped<AccountsUnitOfWork>();
        services.AddScoped<AccountsDataLayer>();
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
    /// Registers the Accounts module (ACCT-001, ACCT-003): Identity over <see cref="AccountUserStore"/>, the token
    /// providers, the facades, and their validators. The host also registers <c>PantryPressDbContext</c>, the clock,
    /// data protection, and the email queue. The <paramref name="configuration"/> section binds
    /// <see cref="AccountsOptions"/>, checked at startup.
    /// </summary>
    public static IServiceCollection AddAccounts(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.AddOptions<AccountsOptions>()
            .Bind(configuration)
            .Validate(o => o.IsValid(), "Accounts:AppBaseUrl must be the admin app's absolute HTTPS origin.")
            .ValidateOnStart();

        services.AddAccountsIdentity()
            .AddTokenProvider<EmailConfirmationTokenProvider>(EmailConfirmationTokenProvider.Name)
            .AddTokenProvider<PasswordResetTokenProvider>(PasswordResetTokenProvider.Name)
            .AddTokenProvider<AuthenticatorCodeProvider>(AuthenticatorCodeProvider.Name);

        services.AddSingleton(sp => new AccountEmails(sp.GetRequiredService<IOptions<AccountsOptions>>().Value.AppBaseUrl));

        services.AddScoped<IValidator<RegisterAccountRequest>, RegisterAccountRequestValidator>();
        services.AddScoped<IValidator<VerifyEmailRequest>, VerifyEmailRequestValidator>();
        services.AddScoped<IValidator<ResendVerificationRequest>, ResendVerificationRequestValidator>();
        services.AddScoped<IValidator<ForgotPasswordRequest>, ForgotPasswordRequestValidator>();
        services.AddScoped<IValidator<ResetPasswordRequest>, ResetPasswordRequestValidator>();
        services.AddScoped<IValidator<ChangePasswordRequest>, ChangePasswordRequestValidator>();
        services.AddScoped<IValidator<ConfirmPasswordRequest>, ConfirmPasswordRequestValidator>();
        services.AddScoped<IValidator<EnableTwoFactorRequest>, EnableTwoFactorRequestValidator>();
        services.AddScoped<IValidator<SecondFactorRequest>, SecondFactorRequestValidator>();

        services.AddScoped<IAccountRegistrationFacade, AccountRegistrationFacade>();
        services.AddScoped<IPasswordFacade, PasswordFacade>();
        services.AddScoped<ITwoFactorFacade, TwoFactorFacade>();
        services.AddScoped<IAccountPublishingEligibility, AccountPublishingEligibility>();
        return services;
    }

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
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

        options.Tokens.EmailConfirmationTokenProvider = EmailConfirmationTokenProvider.Name;
        options.Tokens.PasswordResetTokenProvider = PasswordResetTokenProvider.Name;
        options.Tokens.AuthenticatorTokenProvider = AuthenticatorCodeProvider.Name;
        options.Tokens.AuthenticatorIssuer = AuthenticatorIssuer;
    }
}
