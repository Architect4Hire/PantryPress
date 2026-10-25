using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.MigrationService;

namespace PantryPress.Tests.Sites;

// Inside the namespace, so the type wins over the PantryPress.Tests.Site namespace (the public site host's tests).
using PantryPress.Domain.Modules.Sites;

// PP2.2: the site (SITE-001) is the tenant root. Its id is the SiteId every site-owned row carries, so it has no SiteId
// of its own and nothing filters it (tenancy.md, "Tenant root"). No database is needed.
public sealed class SiteModelTests
{
    // The design-time model: the runtime one drops collations and check constraints, which only migrations need.
    private static IEntityType SiteEntity(DbContext db) => db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Site))!;

    [Fact]
    public void The_tenant_root_carries_no_site_id_and_no_query_filter()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var site = SiteEntity(db);

        Assert.Equal(SiteConfiguration.TableName, site.GetTableName());
        Assert.DoesNotContain(site.GetProperties(), p => p.Name == "SiteId");
        Assert.Empty(site.GetDeclaredQueryFilters());
        Assert.Equal(["Id"], site.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal(typeof(SiteId), site.FindProperty(nameof(Site.Id))!.ClrType);
        Assert.Equal(ValueGenerated.Never, site.FindProperty(nameof(Site.Id))!.ValueGenerated);
    }

    [Fact]
    public void The_slug_is_unique_across_the_platform_and_compared_byte_for_byte()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var site = SiteEntity(db);
        var slug = site.FindProperty(nameof(Site.Slug))!;

        var index = Assert.Single(site.GetIndexes());
        Assert.Equal([nameof(Site.Slug)], index.Properties.Select(p => p.Name));
        Assert.True(index.IsUnique);
        Assert.Null(index.GetFilter());
        Assert.Equal(SiteConfiguration.SlugIndexName, index.GetDatabaseName());
        Assert.Equal(SiteLimits.MaxSlugLength, slug.GetMaxLength());
        Assert.False(slug.IsUnicode());
        Assert.Equal(SiteConfiguration.BinaryCollation, slug.GetCollation());
    }

    [Fact]
    public void The_version_is_a_row_version_concurrency_token()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var version = SiteEntity(db).FindProperty(nameof(Site.Version))!;

        Assert.True(version.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
        Assert.Equal("rowversion", version.GetColumnType());
    }

    [Fact]
    public void Fields_are_stored_at_the_shared_limits_and_none_is_optional()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var site = SiteEntity(db);

        Assert.Equal(SiteLimits.MaxNameLength, site.FindProperty(nameof(Site.Name))!.GetMaxLength());
        Assert.Equal(SiteLimits.MaxTaglineLength, site.FindProperty(nameof(Site.Tagline))!.GetMaxLength());
        Assert.Equal(SiteLimits.MaxTimeZoneLength, site.FindProperty(nameof(Site.TimeZone))!.GetMaxLength());
        Assert.Equal(SiteLimits.MaxLocaleLength, site.FindProperty(nameof(Site.Locale))!.GetMaxLength());
        Assert.All(site.GetProperties(), p => Assert.False(p.IsNullable, $"{p.Name} is optional."));
        Assert.Equal(
            [SiteConfiguration.SlugShapeConstraintName, SiteConfiguration.StatusConstraintName],
            site.GetCheckConstraints().Select(c => c.Name).Order());
    }

    // Statuses are stored as numbers, so the numbers are part of the schema: a reorder would change what rows mean.
    [Fact]
    public void Status_numbers_are_fixed()
    {
        Assert.Equal(
            [
                (SiteStatus.Provisioning, 0), (SiteStatus.Live, 1), (SiteStatus.Suspended, 2), (SiteStatus.ReadOnly, 3),
                (SiteStatus.Offline, 4), (SiteStatus.Deleted, 5),
            ],
            Enum.GetValues<SiteStatus>().Select(s => (s, (int)s)));
    }
}
