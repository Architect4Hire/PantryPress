using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PantryPress.Domain.Modules.Platform.Contracts;

namespace PantryPress.Domain.Modules.Platform;

/// <summary>
/// Seeds the one platform-wide role (PP-D03, ACCT-005) as platform reference data: no <c>SiteId</c>, a fixed key, and
/// the same row in every environment. Seeding it in the model means a migration creates it and no runtime code ever
/// needs to create a role, so "make yourself an admin" has no code path to use.
/// </summary>
internal sealed class PlatformRoleConfiguration : IEntityTypeConfiguration<IdentityRole<Guid>>
{
    // Fixed, because EF Core compares seed data by value: a generated stamp would show up as a model change on
    // every build and produce an empty migration.
    private const string SeedConcurrencyStamp = "f0c4d2f4-6a51-4a9a-9c3f-2a1be0d27c6f";

    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> builder) =>
        builder.HasData(new IdentityRole<Guid>
        {
            Id = PlatformRoles.PlatformAdminId,
            Name = PlatformRoles.PlatformAdmin,
            NormalizedName = PlatformRoles.PlatformAdminNormalized,
            ConcurrencyStamp = SeedConcurrencyStamp,
        });
}
