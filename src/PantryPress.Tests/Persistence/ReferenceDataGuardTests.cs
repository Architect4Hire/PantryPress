using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Recipes;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.MigrationService;

namespace PantryPress.Tests.Persistence;

// Inside the namespace, so the type wins over the PantryPress.Tests.Site namespace (the public site host's tests).
using PantryPress.Domain.Modules.Sites;
using PantryPress.Domain.Modules.Sites.Contracts;

// PP4.1: platform reference data is read-only to the app (PP-D06). The context refuses a save that adds, changes, or removes
// a row, and a bulk update or delete, before anything reaches the database - so no connection is needed to prove it.
public sealed class ReferenceDataGuardTests
{
    // Every reference type in the model, and nothing else marked: a new reference table marks itself and joins this list.
    private static readonly Type[] ReferenceTypes =
    [
        typeof(ReservedSlug),
        typeof(Unit),
        typeof(UnitAlias),
        typeof(Course),
        typeof(Cuisine),
        typeof(Diet),
        typeof(Allergen),
    ];

    private static CancellationToken Timeout => CancellationToken.None;

    [Fact]
    public void The_marked_types_are_the_reference_tables()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        Assert.Equal(
            ReferenceTypes.Select(t => t.FullName).Order(StringComparer.Ordinal),
            db.Model.GetEntityTypes()
                .Where(e => typeof(IReferenceData).IsAssignableFrom(e.ClrType))
                .Select(e => e.ClrType.FullName)
                .Order(StringComparer.Ordinal));
    }

    public static TheoryData<IReferenceData> Rows => new()
    {
        new ReservedSlug { Slug = "kitchen", Reason = ReservedSlugReason.Product, Since = 1 },
        new Unit { Code = "smidgen", System = UnitSystem.Neutral, Dimension = UnitDimension.Count, Singular = "smidgen", Plural = "smidgens", Since = 1 },
        new UnitAlias { Alias = "tbsps", UnitCode = "tablespoon", Since = 1 },
        new Course { Code = "supper", Name = "Supper", Since = 1 },
        new Cuisine { Code = "nordic", Name = "Nordic", Since = 1 },
        new Diet { Code = "raw", Name = "Raw", Since = 1 },
        new Allergen { Code = "kiwi", Name = "Kiwi", Since = 1 },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Adding_a_row_is_refused(IReferenceData row)
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        db.Add(row);

        await AssertRefusedAsync(() => db.SaveChangesAsync(Timeout));
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Removing_a_row_is_refused(IReferenceData row)
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        db.Attach(row);
        db.Remove(row);

        await AssertRefusedAsync(() => db.SaveChangesAsync(Timeout));
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    // Edited in place on a tracked row, the way a read-then-change would: change detection finds it.
    [Fact]
    public async Task Changing_a_row_is_refused()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var course = new Course { Code = "dessert", Name = "Dessert", Since = 1 };
        db.Attach(course);
        db.Entry(course).Property(c => c.Name).CurrentValue = "Pudding";

        await AssertRefusedAsync(() => db.SaveChangesAsync(Timeout));
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task A_bulk_update_is_refused()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        await AssertRefusedAsync(() => db.Set<Unit>().ExecuteUpdateAsync(u => u.SetProperty(x => x.Plural, "cups"), Timeout));
        await AssertRefusedAsync(() => db.Set<ReservedSlug>().ExecuteUpdateAsync(r => r.SetProperty(x => x.Since, 2), Timeout));
        Assert.Throws<InvalidOperationException>(() => db.Set<Course>().ExecuteUpdate(c => c.SetProperty(x => x.Name, "Pudding")));
    }

    [Fact]
    public async Task A_bulk_delete_is_refused()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        await AssertRefusedAsync(() => db.Set<Allergen>().Where(a => a.Code == "milk").ExecuteDeleteAsync(Timeout));
        await AssertRefusedAsync(() => db.Set<UnitAlias>().ExecuteDeleteAsync(Timeout));
        await AssertRefusedAsync(() => db.Set<ReservedSlug>().ExecuteDeleteAsync(Timeout));
        Assert.Throws<InvalidOperationException>(() => db.Set<Diet>().ExecuteDelete());
    }

    // What PP4.3 will do: read the vocabulary beside a recipe and save the recipe. A reference row tracked and left alone
    // stops nothing; only a change to one does. The save gets past the guard and fails only for want of a database.
    [Fact]
    public async Task A_tracked_unchanged_reference_row_does_not_stop_a_save()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        db.Attach(new Course { Code = "dessert", Name = "Dessert", Since = 1 });
        db.Attach(new UnitAlias { Alias = "T", UnitCode = "tablespoon", CaseSensitive = true, Since = 1 });
        db.Add(new RevokedSession { SessionId = Guid.NewGuid().ToString("N"), AccountId = Guid.NewGuid(), RevokedAt = DateTimeOffset.UnixEpoch, ExpiresAt = DateTimeOffset.UnixEpoch });

        AssertNotRefused(await Record.ExceptionAsync(() => db.SaveChangesAsync(Timeout)));
    }

    [Fact]
    public async Task A_bulk_write_of_anything_else_is_not_refused()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        AssertNotRefused(await Record.ExceptionAsync(() => db.Set<RevokedSession>().ExecuteDeleteAsync(Timeout)));
        AssertNotRefused(await Record.ExceptionAsync(() => db.Set<Site>().ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, "Renamed"), Timeout)));
    }

    // Every seeded table is reference data, so a new one cannot skip the marker unnoticed. The one exception is the
    // PlatformAdmin role row (PP1.4), which Identity owns and the operator's reconciliation assigns.
    [Fact]
    public void Every_seeded_table_is_reference_data()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var model = db.GetService<IDesignTimeModel>().Model;

        var seeded = model.GetEntityTypes()
            .Where(e => e.GetSeedData().Any() && e.ClrType != typeof(IdentityRole<Guid>))
            .Select(e => e.ClrType);

        Assert.Equal(ReferenceTypes.Order(TypeOrder), seeded.Order(TypeOrder));
    }

    private static readonly Comparer<Type> TypeOrder = Comparer<Type>.Create((a, b) => string.CompareOrdinal(a.FullName, b.FullName));

    // A database the design-time context has no connection to is the only failure left once the guard has let a write by.
    private static void AssertNotRefused(Exception? error) =>
        Assert.DoesNotContain("reference data is read-only", error?.Message ?? string.Empty, StringComparison.Ordinal);

    private static async Task AssertRefusedAsync(Func<Task> write)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(write);
        Assert.Contains("reference data is read-only", error.Message, StringComparison.Ordinal);
    }
}
