using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.MigrationService;

namespace PantryPress.Tests.Sites;

// Inside the namespace, so the type wins over the PantryPress.Tests.Site namespace (the public site host's tests).
using PantryPress.Domain.Modules.Sites;

// PP2.8: the reserved list's table is platform reference data (ADR 0006) - no SiteId, no filter - seeded from the one list
// in code, so the two cannot drift. No database is needed.
public sealed class ReservedSlugModelTests
{
    // The design-time model: the runtime one drops collations, check constraints, and seed data, which only migrations need.
    private static IEntityType Entity<T>(DbContext db) => db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(T))!;

    [Fact]
    public void The_table_is_platform_reference_data()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var reserved = Entity<ReservedSlug>(db);

        Assert.Equal(ReservedSlugConfiguration.TableName, reserved.GetTableName());
        Assert.DoesNotContain(reserved.GetProperties(), p => p.Name == "SiteId");
        Assert.Empty(reserved.GetDeclaredQueryFilters());
        Assert.Equal([nameof(ReservedSlug.Slug)], reserved.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal([ReservedSlugConfiguration.ReasonConstraintName], reserved.GetCheckConstraints().Select(c => c.Name));
        Assert.Equal([ReservedSlugConfiguration.NotHeldTriggerName], reserved.GetDeclaredTriggers().Select(t => t.GetDatabaseName()));
    }

    // The same column as the site's slug, so the triggers compare them byte for byte.
    [Fact]
    public void The_slug_is_stored_like_a_sites()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var slug = Entity<ReservedSlug>(db).FindProperty(nameof(ReservedSlug.Slug))!;
        var siteSlug = Entity<Site>(db).FindProperty(nameof(Site.Slug))!;

        Assert.Equal(siteSlug.GetMaxLength(), slug.GetMaxLength());
        Assert.False(slug.IsUnicode());
        Assert.Equal(siteSlug.GetCollation(), slug.GetCollation());
    }

    [Fact]
    public void The_seed_is_the_list_exactly()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        var seeded = Entity<ReservedSlug>(db).GetSeedData()
            .Select(row => new ReservedSlugEntry(
                (string)row[nameof(ReservedSlug.Slug)]!,
                (ReservedSlugReason)row[nameof(ReservedSlug.Reason)]!,
                (int)row[nameof(ReservedSlug.Since)]!))
            .OrderBy(e => e.Slug, StringComparer.Ordinal);

        Assert.Equal(ReservedSlugs.All.OrderBy(e => e.Slug, StringComparer.Ordinal), seeded);
    }

    [Fact]
    public void The_sites_table_declares_its_reserved_slug_trigger()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        Assert.Equal([SiteConfiguration.SlugNotReservedTriggerName], Entity<Site>(db).GetDeclaredTriggers().Select(t => t.GetDatabaseName()));
    }

    // Reasons are stored as numbers, so the numbers are part of the schema.
    [Fact]
    public void Reason_numbers_are_fixed()
    {
        Assert.Equal(
            [
                (ReservedSlugReason.PlatformHost, 0), (ReservedSlugReason.Infrastructure, 1),
                (ReservedSlugReason.Impersonation, 2), (ReservedSlugReason.Product, 3),
            ],
            Enum.GetValues<ReservedSlugReason>().Select(r => (r, (int)r)));
    }
}
