using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.MigrationService;

namespace PantryPress.Tests.Sites;

// Inside the namespace, so the types win over the PantryPress.Tests.Site namespace (the public site host's tests).
using PantryPress.Domain.Modules.Sites;

// PP2.9: a site's hosts (SITE-005) are part of the tenant root, read by Host before any site is known, so like the site
// and its members they carry a required SiteId with no query filter (tenancy.md, "Tenant root"). No database is needed.
public sealed class SiteHostModelTests
{
    // The design-time model: the runtime one drops check constraints and triggers, which only migrations need.
    private static IEntityType HostEntity(DbContext db) =>
        db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(SiteHost))!;

    [Fact]
    public void A_host_carries_a_required_site_and_no_filter()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var host = HostEntity(db);

        Assert.Equal(SiteHostConfiguration.TableName, host.GetTableName());
        Assert.Equal([nameof(SiteHost.Id)], host.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal(typeof(SiteHostId), host.FindProperty(nameof(SiteHost.Id))!.ClrType);
        Assert.Equal(typeof(SiteId), host.FindProperty(nameof(SiteHost.SiteId))!.ClrType);
        Assert.False(typeof(ISiteOwned).IsAssignableFrom(typeof(SiteHost)));
        Assert.Empty(host.GetDeclaredQueryFilters());
        Assert.All(host.GetProperties(), p => Assert.False(p.IsNullable, $"{p.Name} is optional."));
    }

    [Fact]
    public void A_host_is_stored_as_bounded_ascii_compared_byte_for_byte()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var host = HostEntity(db).FindProperty(nameof(SiteHost.Host))!;

        Assert.Equal(SiteLimits.MaxHostLength, host.GetMaxLength());
        Assert.False(host.IsUnicode());
        Assert.Equal(SiteConfiguration.BinaryCollation, host.GetCollation());
    }

    // The host index is the written exception to SiteId-leading indexes; the other two lead with the site.
    [Fact]
    public void A_host_is_unique_and_a_site_has_at_most_one_canonical_host()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var indexes = HostEntity(db).GetIndexes().ToDictionary(i => i.GetDatabaseName()!);

        Assert.Equal(
            new[] { SiteHostConfiguration.HostIndexName, SiteHostConfiguration.CanonicalIndexName, SiteHostConfiguration.SiteIndexName }.Order(),
            indexes.Keys.Order());

        var byHost = indexes[SiteHostConfiguration.HostIndexName];
        Assert.Equal([nameof(SiteHost.Host)], byHost.Properties.Select(p => p.Name));
        Assert.True(byHost.IsUnique);
        Assert.Null(byHost.GetFilter());

        var canonical = indexes[SiteHostConfiguration.CanonicalIndexName];
        Assert.Equal([nameof(SiteHost.SiteId)], canonical.Properties.Select(p => p.Name));
        Assert.True(canonical.IsUnique);
        Assert.Equal("[IsCanonical] = 1", canonical.GetFilter());

        var bySite = indexes[SiteHostConfiguration.SiteIndexName];
        Assert.Equal([nameof(SiteHost.SiteId), nameof(SiteHost.Kind)], bySite.Properties.Select(p => p.Name));
        Assert.False(bySite.IsUnique);
    }

    [Fact]
    public void Its_site_is_a_foreign_key_that_never_cascades()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        var key = Assert.Single(HostEntity(db).GetForeignKeys());
        Assert.Equal(typeof(Site), key.PrincipalEntityType.ClrType);
        Assert.Equal([nameof(SiteHost.SiteId)], key.Properties.Select(p => p.Name));
        Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior);
    }

    [Fact]
    public void It_declares_its_trigger_checks_and_row_version()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var host = HostEntity(db);
        var version = host.FindProperty(nameof(SiteHost.Version))!;

        Assert.Equal([SiteHostConfiguration.GuardTriggerName], host.GetDeclaredTriggers().Select(t => t.GetDatabaseName()));
        Assert.Equal(
            new[]
            {
                SiteHostConfiguration.CanonicalConstraintName,
                SiteHostConfiguration.HostShapeConstraintName,
                SiteHostConfiguration.KindConstraintName,
                SiteHostConfiguration.StatusConstraintName,
            }.Order(),
            host.GetCheckConstraints().Select(c => c.Name).Order());
        Assert.True(version.IsConcurrencyToken);
        Assert.Equal("rowversion", version.GetColumnType());
    }

    // Kinds and statuses are stored as numbers, and the canonical check names Active and WwwVariant by number: the numbers
    // are schema. The trigger names a deleted site as 5.
    [Fact]
    public void Kind_and_status_numbers_are_fixed()
    {
        Assert.Equal(
            [(SiteHostKind.Subdomain, 0), (SiteHostKind.Custom, 1), (SiteHostKind.WwwVariant, 2)],
            Enum.GetValues<SiteHostKind>().Select(k => (k, (int)k)));
        Assert.Equal(
            [(SiteHostStatus.Active, 0), (SiteHostStatus.Pending, 1)],
            Enum.GetValues<SiteHostStatus>().Select(s => (s, (int)s)));
        Assert.Equal(5, (int)SiteStatus.Deleted);
    }

    // A host never changes site or spelling (the trigger refuses it in the database too), so .NET cannot set either after
    // a row is made.
    [Fact]
    public void Its_site_and_host_are_set_only_when_it_is_made()
    {
        Assert.All(
            new[] { nameof(SiteHost.Id), nameof(SiteHost.SiteId), nameof(SiteHost.Host) },
            name => Assert.True(
                typeof(SiteHost).GetProperty(name)!.SetMethod!.ReturnParameter
                    .GetRequiredCustomModifiers()
                    .Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)),
                $"{name} can be set after the host is made."));
    }
}
