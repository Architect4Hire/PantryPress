using Microsoft.EntityFrameworkCore;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Recipes;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.MigrationService;
using PantryPress.Tests.Recipes;

namespace PantryPress.Tests.Persistence;

// PP4.4 (RCP-007): an append-only row is written once and never changed or removed. The context refuses a save that changes
// or removes one, and a bulk update or delete of one, before anything reaches the database - so no connection is needed to
// prove it. Adding one is let by. The table's trigger, which catches raw SQL, is proved against the migrated schema
// (RecipeRevisionSchemaTests).
public sealed class AppendOnlyGuardTests
{
    // Every append-only type in the model: a new one marks itself and joins this list on purpose.
    private static readonly Type[] AppendOnlyTypes = [typeof(RecipeRevision)];

    private static CancellationToken Timeout => CancellationToken.None;

    [Fact]
    public void The_marked_types_are_the_append_only_tables()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        Assert.Equal(
            AppendOnlyTypes.Select(t => t.FullName).Order(StringComparer.Ordinal),
            db.Model.GetEntityTypes()
                .Where(e => typeof(IAppendOnly).IsAssignableFrom(e.ClrType))
                .Select(e => e.ClrType.FullName)
                .Order(StringComparer.Ordinal));
    }

    // Each marked table declares the trigger that refuses raw SQL, so EF Core never uses an OUTPUT clause on it.
    [Fact]
    public void Every_append_only_table_declares_its_trigger()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        Assert.All(
            db.Model.GetEntityTypes().Where(e => typeof(IAppendOnly).IsAssignableFrom(e.ClrType)),
            e => Assert.NotEmpty(e.GetDeclaredTriggers()));
    }

    [Fact]
    public async Task Changing_a_row_is_refused()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var revision = Revision();
        db.Attach(revision);
        db.Entry(revision).Property(r => r.Snapshot).CurrentValue = "{\"edited\":true}";

        await AssertRefusedAsync(() => db.SaveChangesAsync(Timeout));
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task Marking_a_row_modified_is_refused_even_with_nothing_changed()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        db.Update(Revision());

        await AssertRefusedAsync(() => db.SaveChangesAsync(Timeout));
    }

    [Fact]
    public async Task Removing_a_row_is_refused()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var revision = Revision();
        db.Attach(revision);
        db.Remove(revision);

        await AssertRefusedAsync(() => db.SaveChangesAsync(Timeout));
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task A_bulk_update_is_refused()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        await AssertRefusedAsync(() => db.Set<RecipeRevision>().ExecuteUpdateAsync(r => r.SetProperty(x => x.Kind, RecipeRevisionKind.Draft), Timeout));
        Assert.Throws<InvalidOperationException>(() => db.Set<RecipeRevision>().Where(r => r.Number == 1).ExecuteUpdate(r => r.SetProperty(x => x.Snapshot, "{}")));
    }

    [Fact]
    public async Task A_bulk_delete_is_refused()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        await AssertRefusedAsync(() => db.Set<RecipeRevision>().Where(r => r.Kind == RecipeRevisionKind.Draft).ExecuteDeleteAsync(Timeout));
        Assert.Throws<InvalidOperationException>(() => db.Set<RecipeRevision>().ExecuteDelete());
    }

    // Adding is the one write there is. The save gets past this guard and stops only at the site stamp, since the design-time
    // context works on no site.
    [Fact]
    public async Task Adding_a_row_is_let_by()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        db.Add(Revision());

        AssertNotRefused(await Record.ExceptionAsync(() => db.SaveChangesAsync(Timeout)));
    }

    // A revision tracked and left alone stops nothing: reading one beside a recipe and saving the recipe is fine.
    [Fact]
    public async Task A_tracked_unchanged_row_does_not_stop_a_save()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        db.Attach(Revision());
        db.Add(RecipeRows.NewRecipe());

        AssertNotRefused(await Record.ExceptionAsync(() => db.SaveChangesAsync(Timeout)));
    }

    [Fact]
    public async Task A_bulk_write_of_anything_else_is_not_refused()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        AssertNotRefused(await Record.ExceptionAsync(() => db.Set<Recipe>().ExecuteUpdateAsync(r => r.SetProperty(x => x.Title, "Renamed"), Timeout)));
        AssertNotRefused(await Record.ExceptionAsync(() => db.Set<RecipeKeyword>().ExecuteDeleteAsync(Timeout)));
    }

    private static RecipeRevision Revision() => new()
    {
        Id = new RecipeRevisionId(Guid.NewGuid()),
        RecipeId = new RecipeId(Guid.NewGuid()),
        Number = 1,
        Kind = RecipeRevisionKind.Published,
        SnapshotVersion = 1,
        Snapshot = "{}",
        ContentHash = new byte[32],
        CreatedAt = RecipeRows.Created,
    };

    private static void AssertNotRefused(Exception? error) =>
        Assert.DoesNotContain("append-only", error?.ToString() ?? string.Empty, StringComparison.Ordinal);

    private static async Task AssertRefusedAsync(Func<Task> write)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(write);
        Assert.Contains("append-only", error.Message, StringComparison.Ordinal);
    }
}
