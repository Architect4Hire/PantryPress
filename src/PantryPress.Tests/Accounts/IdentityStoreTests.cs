using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Tests.Persistence;

namespace PantryPress.Tests.Accounts;

// PP1.1: the Identity stores against the schema the migration service applies (ACCT-001).
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class IdentityStoreTests(MigratedDatabase database)
{
    // SQL Server: "Cannot insert duplicate key row in object with unique index".
    private const int DuplicateKeyInUniqueIndex = 2601;

    [Fact]
    public async Task Users_and_roles_round_trip_through_the_stores()
    {
        await using var scope = database.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var email = $"Cook-{Guid.NewGuid():N}@Example.test";
        var role = $"probe-{Guid.NewGuid():N}";

        AssertSucceeded(await users.CreateAsync(new ApplicationUser { UserName = email, Email = email }, "Correct-Horse-1"));
        AssertSucceeded(await roles.CreateAsync(new IdentityRole<Guid>(role)));

        var found = await users.FindByEmailAsync(email.ToLowerInvariant());
        Assert.NotNull(found);
        Assert.NotEqual(Guid.Empty, found.Id);
        Assert.False(found.EmailConfirmed);
        Assert.True(await users.CheckPasswordAsync(found, "Correct-Horse-1"));

        AssertSucceeded(await users.AddToRoleAsync(found, role));
        Assert.True(await users.IsInRoleAsync(found, role));
    }

    // UserManager checks unique emails only when RequireUniqueEmail is on, and never under concurrency.
    [Fact]
    public async Task Database_rejects_a_second_account_with_the_same_email_in_any_casing()
    {
        await using var scope = database.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"dup-{Guid.NewGuid():N}@example.test";

        AssertSucceeded(await users.CreateAsync(new ApplicationUser { UserName = $"first-{email}", Email = email }));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => users.CreateAsync(
            new ApplicationUser { UserName = $"second-{email}", Email = email.ToUpperInvariant() }));
        var sql = Assert.IsType<SqlException>(error.InnerException);
        Assert.Equal(DuplicateKeyInUniqueIndex, sql.Number);
        Assert.Contains("EmailIndex", sql.Message, StringComparison.Ordinal);
    }

    private static void AssertSucceeded(IdentityResult result) =>
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Code)));
}
