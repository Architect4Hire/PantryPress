using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Platform.Contracts;
using PantryPress.Tests.Api;

namespace PantryPress.Tests.Platform;

// PP1.4: the PlatformAdmin policy needs the role and a session that passed the second step (auth.md, ACCT-005). The
// session that issues both arrives with PP1.6; the policy is asserted directly until a route carries it.
public sealed class PlatformAdminPolicyTests
{
    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        Assert.False(await AuthorizeAsync(Anonymous()));
    }

    [Fact]
    public async Task A_signed_in_blogger_is_refused()
    {
        Assert.False(await AuthorizeAsync(SignedIn()));
    }

    [Fact]
    public async Task The_role_without_a_second_step_is_refused()
    {
        Assert.False(await AuthorizeAsync(SignedIn(new Claim(ClaimTypes.Role, PlatformRoles.PlatformAdmin))));
    }

    [Fact]
    public async Task A_second_step_without_the_role_is_refused()
    {
        Assert.False(await AuthorizeAsync(SignedIn(TwoFactorClaim())));
    }

    [Fact]
    public async Task The_role_and_a_second_step_together_are_allowed()
    {
        Assert.True(await AuthorizeAsync(SignedIn(
            new Claim(ClaimTypes.Role, PlatformRoles.PlatformAdmin),
            TwoFactorClaim())));
    }

    [Fact]
    public async Task A_site_role_name_does_not_stand_in_for_the_platform_role()
    {
        Assert.False(await AuthorizeAsync(SignedIn(new Claim(ClaimTypes.Role, "Owner"), TwoFactorClaim())));
    }

    private static Claim TwoFactorClaim() => new(AccountClaims.TwoFactorVerified, AccountClaims.Yes);

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static ClaimsPrincipal SignedIn(params Claim[] claims) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), .. claims],
            "Test",
            ClaimTypes.NameIdentifier,
            ClaimTypes.Role));

    private static async Task<bool> AuthorizeAsync(ClaimsPrincipal user)
    {
        await using var factory = ApiServiceFactory.WithoutDatabase();
        var authorization = factory.Services.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(user, resource: null, PlatformPolicies.PlatformAdmin);

        return result.Succeeded;
    }
}
