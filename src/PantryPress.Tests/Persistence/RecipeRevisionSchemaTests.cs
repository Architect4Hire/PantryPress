using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Recipes;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.Tests.Recipes;
using PantryPress.Tests.TwoSite;

namespace PantryPress.Tests.Persistence;

// PP4.4 (RCP-007): the recipe revisions table against the schema the migration service applies. A revision comes back
// exactly as written; beneath the context's guard, the table's trigger refuses every update and delete - raw SQL, as a bug
// or an operator's script would send it - and its checks and keys refuse what the facade would never write. A recipe that
// has a revision cannot be deleted, so its revisions never go with it.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class RecipeRevisionSchemaTests(MigratedDatabase database) : IAsyncLifetime
{
    private const int CheckOrReferenceViolation = 547;
    private const int CannotInsertNull = 515;
    private const int DuplicateIndexKey = 2601;
    private const int ImmutableRefusal = 51013;
    private const int RollbackRefused = 51014;

    private TwoSiteHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await TwoSiteHarness.CreateAsync(database);

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task A_revision_comes_back_exactly_as_written()
    {
        var (site, rows) = await SeedRecipeAsync("rev-whole");
        var revision = NewRevision(rows.Recipe.Id, 1, "{\"title\":\"Apple pie \\u00BD\"}");
        await _harness.ChangeAsync(site, db => db.Add(revision));

        await using var scope = _harness.In(site);
        var read = await Db(scope).Set<RecipeRevision>().AsNoTracking().SingleAsync(r => r.Id == revision.Id);

        Assert.Equal(site, read.SiteId);
        Assert.Equal(
            (revision.RecipeId, revision.Number, revision.Kind, revision.SnapshotVersion, revision.Snapshot, revision.CreatedAt),
            (read.RecipeId, read.Number, read.Kind, read.SnapshotVersion, read.Snapshot, read.CreatedAt));
        Assert.Equal(revision.ContentHash, read.ContentHash);
    }

    // The trigger, past every .NET rule: an update of any column, the site's own key included, and a delete.
    [Theory]
    [InlineData("[Snapshot] = N'[]'")]
    [InlineData("[Kind] = 1")]
    [InlineData("[Number] = 9")]
    [InlineData("[CreatedAt] = SYSDATETIMEOFFSET()")]
    [InlineData("[ContentHash] = 0x00")]
    [InlineData("[SnapshotVersion] = [SnapshotVersion]")]
    public async Task An_update_is_refused_by_the_database(string set)
    {
        var (site, revision) = await SeedRevisionAsync("rev-update");

        // The clause is the cases' own constant; the row's key goes as parameters.
        var error = await RefusedAsync(
            string.Concat("UPDATE [RecipeRevisions] SET ", set, " WHERE [SiteId] = @site AND [Id] = @id"),
            site,
            revision.Id.Value);

        Assert.Equal(ImmutableRefusal, error.Number);
        await AssertUnchangedAsync(site, revision);
    }

    [Fact]
    public async Task A_delete_is_refused_by_the_database()
    {
        var (site, revision) = await SeedRevisionAsync("rev-delete");

        var error = await RefusedAsync("DELETE FROM [RecipeRevisions] WHERE [SiteId] = @site AND [Id] = @id", site, revision.Id.Value);

        Assert.Equal(ImmutableRefusal, error.Number);
        await AssertUnchangedAsync(site, revision);
    }

    // The trigger answers for the whole statement, so a delete that would reach many rows reaches none.
    [Fact]
    public async Task A_bulk_delete_reaches_no_row()
    {
        var (site, first) = await SeedRevisionAsync("rev-bulk");
        var second = NewRevision(first.RecipeId, 2, "{}");
        await _harness.ChangeAsync(site, db => db.Add(second));

        var error = await RefusedAsync("DELETE FROM [RecipeRevisions] WHERE [SiteId] = @site AND [Id] <> @id", site, Guid.Empty);

        Assert.Equal(ImmutableRefusal, error.Number);
        await AssertUnchangedAsync(site, first);
        await AssertUnchangedAsync(site, second);
    }

    // RCP-007: a recipe that has a revision is kept; deleting it is refused, and the parts its cascade would have taken stay.
    [Fact]
    public async Task A_recipe_with_a_revision_cannot_be_deleted()
    {
        var (site, rows) = await SeedRecipeAsync("rev-keep");
        await _harness.ChangeAsync(site, db => db.Add(NewRevision(rows.Recipe.Id, 1, "{}")));

        var error = await RefusedAsync("DELETE FROM [Recipes] WHERE [SiteId] = @site AND [Id] = @id", site, rows.Recipe.Id.Value);
        Assert.Equal(CheckOrReferenceViolation, error.Number);
        Assert.Contains("FK_RecipeRevisions_Recipes_SiteId_RecipeId", error.Message, StringComparison.Ordinal);

        await using var scope = _harness.In(site);
        var db = Db(scope);
        Assert.True(await db.Set<Recipe>().AnyAsync(r => r.Id == rows.Recipe.Id));
        Assert.True(await db.Set<RecipeIngredient>().AnyAsync(i => i.Id == rows.Ingredient.Id));
        Assert.Equal(1, await db.Set<RecipeRevision>().CountAsync(r => r.RecipeId == rows.Recipe.Id));
    }

    // Through the context too: the delete reaches the database, and the key refuses it there.
    [Fact]
    public async Task A_recipe_with_a_revision_cannot_be_removed_through_the_context()
    {
        var (site, rows) = await SeedRecipeAsync("rev-keep-ef");
        await _harness.ChangeAsync(site, db => db.Add(NewRevision(rows.Recipe.Id, 1, "{}")));

        await using var scope = _harness.In(site);
        var db = Db(scope);
        db.Remove(await db.Set<Recipe>().SingleAsync(r => r.Id == rows.Recipe.Id));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(CheckOrReferenceViolation, Assert.IsType<SqlException>(error.InnerException).Number);
    }

    // A revision under a recipe only the other site has is refused by the composite key, however the id is known.
    [Fact]
    public async Task A_revision_under_another_sites_recipe_is_refused()
    {
        var world = await _harness.SeedAsync("rev-other");
        RecipeRowSet rows = null!;
        await _harness.ChangeAsync(world.B.Id, db => rows = RecipeRows.Add(db));

        await using var scope = _harness.In(world.A.Id);
        var db = Db(scope);
        db.Add(NewRevision(rows.Recipe.Id, 1, "{}"));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var sql = Assert.IsType<SqlException>(error.InnerException);
        Assert.Equal(CheckOrReferenceViolation, sql.Number);
        Assert.Contains("FK_RecipeRevisions_Recipes_SiteId_RecipeId", sql.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_number_is_used_once_per_recipe()
    {
        var (site, first) = await SeedRevisionAsync("rev-number");

        await using var scope = _harness.In(site);
        var db = Db(scope);
        db.Add(NewRevision(first.RecipeId, first.Number, "{}"));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var sql = Assert.IsType<SqlException>(error.InnerException);
        Assert.Equal(DuplicateIndexKey, sql.Number);
        Assert.Contains(RecipeRevisionConfiguration.NumberIndexName, sql.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> Refusals => new()
    {
        { "@number = 0", RecipeRevisionConfiguration.NumberConstraintName },
        { "@number = -1", RecipeRevisionConfiguration.NumberConstraintName },
        { "@kind = 0", RecipeRevisionConfiguration.KindConstraintName },
        { "@kind = 3", RecipeRevisionConfiguration.KindConstraintName },
        { "@version = 0", RecipeRevisionConfiguration.SnapshotVersionConstraintName },
        { "@snapshot = N'not json'", RecipeRevisionConfiguration.SnapshotConstraintName },
        { "@snapshot = N''", RecipeRevisionConfiguration.SnapshotConstraintName },
        { "@snapshot = N'[\"title\"'", RecipeRevisionConfiguration.SnapshotConstraintName },
    };

    // Each case breaks one check alone, through a raw insert past every .NET rule.
    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task A_check_refuses_its_bad_row(string assignment, string constraint)
    {
        var (site, rows) = await SeedRecipeAsync("rev-check");

        var error = await InsertRefusedAsync(site, rows.Recipe.Id, assignment);

        Assert.Equal(CheckOrReferenceViolation, error.Number);
        Assert.Contains($"\"{constraint}\"", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_revision_with_no_snapshot_or_hash_is_refused()
    {
        var (site, rows) = await SeedRecipeAsync("rev-null");

        Assert.Equal(CannotInsertNull, (await InsertRefusedAsync(site, rows.Recipe.Id, "@snapshot = NULL")).Number);
        Assert.Equal(CannotInsertNull, (await InsertRefusedAsync(site, rows.Recipe.Id, "@hash = NULL")).Number);
    }

    // The accept side of the raw insert the refusals use, so a refusal is the case's and not the statement's.
    [Fact]
    public async Task The_raw_insert_is_accepted_as_written()
    {
        var (site, rows) = await SeedRecipeAsync("rev-raw");

        await using var scope = _harness.Scope();
        Assert.Equal(1, await Insert(Db(scope).Database, site, rows.Recipe.Id, "@number = 1"));
    }

    // Rolling the migration back refuses while any revision exists, before it drops anything. Run inside a transaction that
    // is rolled back, so the shared database keeps its tables.
    [Fact]
    public async Task Rolling_back_refuses_while_a_revision_exists()
    {
        var down = new PantryPress.MigrationService.Migrations.RecipeRevisions().DownOperations;
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(down[0]);
        Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropTableOperation>(Assert.Single(down.Skip(1)));

        await SeedRevisionAsync("rev-rollback");
        await using var scope = _harness.Scope();
        var database = Db(scope).Database;
        await using var transaction = await database.BeginTransactionAsync();

        var error = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteSqlRawAsync(guard.Sql));
        Assert.Equal(RollbackRefused, error.Number);
        await transaction.RollbackAsync();
    }

    [Fact]
    public void The_migration_creates_the_trigger_the_model_declares()
    {
        var up = new PantryPress.MigrationService.Migrations.RecipeRevisions().UpOperations;
        var trigger = Assert.Single(up.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());

        Assert.Contains($"CREATE TRIGGER [{RecipeRevisionConfiguration.ImmutableTriggerName}] ON [RecipeRevisions]", trigger.Sql, StringComparison.Ordinal);
        Assert.Contains("AFTER UPDATE, DELETE", trigger.Sql, StringComparison.Ordinal);
        Assert.Contains($"THROW {ImmutableRefusal},", trigger.Sql, StringComparison.Ordinal);
    }

    internal static RecipeRevision NewRevision(RecipeId recipe, int number, string snapshot) => new()
    {
        Id = new RecipeRevisionId(Guid.NewGuid()),
        RecipeId = recipe,
        Number = number,
        Kind = RecipeRevisionKind.Published,
        SnapshotVersion = 1,
        Snapshot = snapshot,
        ContentHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(snapshot)),
        CreatedAt = RecipeRows.Created,
    };

    private async Task<(SiteId Site, RecipeRowSet Rows)> SeedRecipeAsync(string prefix)
    {
        var world = await _harness.SeedAsync(prefix);
        RecipeRowSet rows = null!;
        await _harness.ChangeAsync(world.A.Id, db => rows = RecipeRows.Add(db));
        return (world.A.Id, rows);
    }

    private async Task<(SiteId Site, RecipeRevision Revision)> SeedRevisionAsync(string prefix)
    {
        var (site, rows) = await SeedRecipeAsync(prefix);
        var revision = NewRevision(rows.Recipe.Id, 1, "{\"title\":\"Apple pie\"}");
        await _harness.ChangeAsync(site, db => db.Add(revision));
        return (site, revision);
    }

    private async Task AssertUnchangedAsync(SiteId site, RecipeRevision written)
    {
        await using var scope = _harness.In(site);
        var read = await Db(scope).Set<RecipeRevision>().AsNoTracking().SingleAsync(r => r.Id == written.Id);
        Assert.Equal(
            (written.RecipeId, written.Number, written.Kind, written.SnapshotVersion, written.Snapshot, written.CreatedAt),
            (read.RecipeId, read.Number, read.Kind, read.SnapshotVersion, read.Snapshot, read.CreatedAt));
        Assert.Equal(written.ContentHash, read.ContentHash);
    }

    private async Task<SqlException> RefusedAsync(string statement, SiteId site, Guid id)
    {
        await using var scope = _harness.Scope();
        return await Assert.ThrowsAsync<SqlException>(() => Db(scope).Database.ExecuteSqlRawAsync(
            statement,
            new SqlParameter("@site", site.Value),
            new SqlParameter("@id", id)));
    }

    private async Task<SqlException> InsertRefusedAsync(SiteId site, RecipeId recipe, string assignment)
    {
        await using var scope = _harness.Scope();
        return await Assert.ThrowsAsync<SqlException>(() => Insert(Db(scope).Database, site, recipe, assignment));
    }

    // A whole, valid row in variables, one of them then set by the case's own constant, and inserted as the facade never
    // would: past the context, its guard, and its stamp. No braces: raw SQL with parameters is read as a format string.
    private static Task<int> Insert(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade database, SiteId site, RecipeId recipe, string assignment) =>
        database.ExecuteSqlRawAsync(
            string.Concat(
                "DECLARE @number int = 1, @kind int = 2, @version int = 1, @snapshot nvarchar(max) = N'[]', ",
                "@hash binary(32) = HASHBYTES('SHA2_256', N'[]'); SELECT ",
                assignment,
                "; INSERT INTO [RecipeRevisions] ([Id], [SiteId], [RecipeId], [Number], [Kind], [SnapshotVersion], [Snapshot], [ContentHash], [CreatedAt]) ",
                "VALUES (NEWID(), @site, @recipe, @number, @kind, @version, @snapshot, @hash, SYSDATETIMEOFFSET());"),
            new SqlParameter("@site", site.Value),
            new SqlParameter("@recipe", recipe.Value));

    private static PantryPressDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
}
