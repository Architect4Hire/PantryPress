using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Recipes;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.Domain.Modules.Recipes.Repositories;
using PantryPress.Tests.Persistence;
using PantryPress.Tests.Recipes;

namespace PantryPress.Tests.TwoSite;

// PP4.4: a recipe revision is site-owned and append-only, in the two-site world (tenancy.md, "Two-site test"). It owes the
// floor - for an append-only type, reads keep to the site and every change or removal is refused, whoever's row it is - and
// its read paths owe the rest: the raw query that locks a recipe and the joins that read its parts see only the entered
// site's rows, a freeze of another site's recipe is answered exactly like an unknown one, and two sites holding the very
// same ids freeze, number, and keep their revisions apart.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class RecipeRevisionsTwoSiteTests(MigratedDatabase database) : IAsyncLifetime
{
    private TwoSiteHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await TwoSiteHarness.CreateAsync(database);

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task A_revision_is_isolated_and_append_only()
    {
        var world = await _harness.SeedAsync("rvs-iso");

        await TwoSiteChecks.AssertAppendOnlySiteOwnedIsolatedAsync<RecipeRevision>(
            _harness.Services,
            world.A.Id,
            world.B.Id,
            db =>
            {
                var rows = RecipeRows.Add(db);
                var revision = RecipeRevisionSchemaTests.NewRevision(rows.Recipe.Id, 1, "{\"title\":\"Apple pie\"}");
                db.Add(revision);
                return Task.FromResult(revision);
            },
            set => set.SetProperty(r => r.Kind, RecipeRevisionKind.Draft),
            nameof(RecipeRevision.Snapshot),
            "{\"title\":\"Changed\"}");
    }

    // The one raw query: the recipe read under its lock. Working as A, it finds A's recipe and never B's, whichever id it
    // is given.
    [Fact]
    public async Task The_locked_read_sees_only_the_entered_sites_recipe()
    {
        var world = await _harness.SeedAsync("rvs-lock");
        var ids = new List<RecipeId>();

        await TwoSiteChecks.AssertReadPathIsolatedAsync<Recipe>(
            _harness.Services,
            world.A.Id,
            world.B.Id,
            db =>
            {
                var recipe = RecipeRows.Add(db).Recipe;
                ids.Add(recipe.Id);
                return Task.FromResult(recipe);
            },
            async services =>
            {
                var db = services.GetRequiredService<PantryPressDbContext>();
                var repository = services.GetRequiredService<RecipeRepository>();
                await using var transaction = await db.Database.BeginTransactionAsync();
                var keys = new List<object?[]>();
                foreach (var id in ids)
                {
                    if (await repository.FindLockedAsync(id, CancellationToken.None) is { } found)
                    {
                        keys.Add([found.SiteId, found.Id]);
                    }
                }

                return keys;
            });
    }

    // The same ids on both sites, so the locked read is asked for exactly the id B has: as A it finds A's row, never B's.
    [Fact]
    public async Task The_locked_read_never_crosses_to_a_twin_with_the_same_id()
    {
        var world = await _harness.SeedAsync("rvs-twin-lock");
        var id = new RecipeId(Guid.NewGuid());
        await _harness.ChangeAsync(world.A.Id, db => RecipeRows.Add(db, recipeId: id).Recipe.Title = "A's pie");
        await _harness.ChangeAsync(world.B.Id, db => RecipeRows.Add(db, recipeId: id).Recipe.Title = "B's pie");

        await using var scope = _harness.In(world.A.Id);
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var found = await scope.ServiceProvider.GetRequiredService<RecipeRepository>().FindLockedAsync(id, CancellationToken.None);

        Assert.Equal((world.A.Id, "A's pie"), (found!.SiteId, found.Title));
    }

    // From review (site-isolation-auditor): the locked read takes one row lock on a hit and none on a miss. A range lock -
    // HOLDLOCK - would lock the gap after the id asked for, up to the next key, which can be another site's recipe, so a
    // freeze of B's id as A would stall B's edits and time them. Read from the server's own lock table, within the read's
    // transaction, so the answer does not depend on how the shared database's sites happen to sort. B also holds a twin of
    // A's recipe, the very same id, so a query that lost its site would lock B's row as well and show two.
    [Fact]
    public async Task The_locked_read_locks_one_row_on_a_hit_and_none_on_a_miss()
    {
        var world = await _harness.SeedAsync("rvs-gap");
        RecipeRowSet a = null!, b = null!;
        await _harness.ChangeAsync(world.A.Id, db => a = RecipeRows.Add(db));
        await _harness.ChangeAsync(world.B.Id, db =>
        {
            RecipeRows.Add(db, recipeId: a.Recipe.Id);
            b = RecipeRows.Add(db);
        });

        await using var scope = _harness.In(world.A.Id);
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<RecipeRepository>();

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            Assert.Null(await repository.FindLockedAsync(b.Recipe.Id, CancellationToken.None));
            Assert.Null(await repository.FindLockedAsync(new RecipeId(Guid.Parse("ffffffff-ffff-4fff-bfff-ffffffffffff")), CancellationToken.None));
            Assert.Empty(await RecipeKeyLocksAsync(db));
        }

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            Assert.NotNull(await repository.FindLockedAsync(a.Recipe.Id, CancellationToken.None));
            Assert.Equal(["U"], await RecipeKeyLocksAsync(db));
        }
    }

    // The revision reads a freeze decides by - the retry check by id, and the latest of a kind - go through the site
    // filter: working as A, each finds A's revisions and never B's.
    [Fact]
    public async Task The_revision_reads_see_only_the_entered_sites_revisions()
    {
        var world = await _harness.SeedAsync("rvs-reads");
        var made = new List<RecipeRevision>();

        await TwoSiteChecks.AssertReadPathIsolatedAsync<RecipeRevision>(
            _harness.Services,
            world.A.Id,
            world.B.Id,
            db =>
            {
                var revision = RecipeRevisionSchemaTests.NewRevision(RecipeRows.Add(db).Recipe.Id, 1, "{}");
                db.Add(revision);
                made.Add(revision);
                return Task.FromResult(revision);
            },
            async services =>
            {
                var revisions = services.GetRequiredService<RecipeRevisionRepository>();
                var site = services.GetRequiredService<ISiteContext>().Required;
                var keys = new List<object?[]>();
                foreach (var revision in made)
                {
                    if (await revisions.FindNumberAsync(revision.Id, CancellationToken.None) is not null)
                    {
                        keys.Add([site, revision.Id]);
                    }

                    if (await revisions.FindLatestAsync(revision.RecipeId, RecipeRevisionKind.Published, CancellationToken.None) is { } head)
                    {
                        keys.Add([site, head.Id]);
                    }
                }

                return keys;
            });
    }

    // From review: the retry check by id never meets another site's revision, even one with the very id the freeze minted -
    // which would make A answer B's number as its own.
    [Fact]
    public async Task The_retry_check_never_meets_another_sites_revision_with_the_same_id()
    {
        var world = await _harness.SeedAsync("rvs-landed");
        RecipeRowSet b = null!;
        await _harness.ChangeAsync(world.B.Id, db => b = RecipeRows.Add(db));
        var theirs = RecipeRevisionSchemaTests.NewRevision(b.Recipe.Id, 5, "{}");
        await _harness.ChangeAsync(world.B.Id, db => db.Add(theirs));

        await using var scope = _harness.In(world.A.Id);
        var revisions = scope.ServiceProvider.GetRequiredService<RecipeRevisionRepository>();

        Assert.Null(await revisions.FindNumberAsync(theirs.Id, CancellationToken.None));
        Assert.Null(await revisions.FindLatestNumberAsync(b.Recipe.Id, CancellationToken.None));
        Assert.Null(await revisions.FindLatestAsync(b.Recipe.Id, RecipeRevisionKind.Published, CancellationToken.None));
    }

    // The parts are read by joins on the recipe's id: asked for B's recipe while working as A, they find none of B's.
    [Fact]
    public async Task The_working_copy_read_finds_no_part_of_another_sites_recipe()
    {
        var world = await _harness.SeedAsync("rvs-parts");
        RecipeRowSet b = null!;
        await _harness.ChangeAsync(world.B.Id, db => b = RecipeRows.Add(db));

        await using var scope = _harness.In(world.A.Id);
        var copy = await scope.ServiceProvider.GetRequiredService<RecipeRepository>()
            .ReadWorkingCopyAsync(new Recipe { Id = b.Recipe.Id, Title = "posing as A's" }, CancellationToken.None);

        Assert.Empty(copy.IngredientGroups);
        Assert.Empty(copy.Ingredients);
        Assert.Empty(copy.StepGroups);
        Assert.Empty(copy.Steps);
        Assert.Empty(copy.Equipment);
        Assert.Empty(copy.Notes);
        Assert.Empty(copy.Keywords);
        Assert.Empty(copy.Courses);
        Assert.Empty(copy.Cuisines);
        Assert.Empty(copy.Diets);
        Assert.Empty(copy.Allergens);
    }

    // Working as A, B's recipe - with its own, true version - is no recipe: the same answer as an id nobody has, and B's
    // recipe and revisions are untouched.
    [Fact]
    public async Task Freezing_another_sites_recipe_is_answered_like_an_unknown_one()
    {
        var world = await _harness.SeedAsync("rvs-other");
        RecipeRowSet b = null!;
        await _harness.ChangeAsync(world.B.Id, db => b = RecipeRows.Add(db));
        var bVersion = await VersionAsync(world.B.Id, b.Recipe.Id);

        var theirs = await FreezeAsync(world.A.Id, b.Recipe.Id, RecipeRevisionKind.Published, bVersion);
        var nobodys = await FreezeAsync(world.A.Id, new RecipeId(Guid.NewGuid()), RecipeRevisionKind.Published, bVersion);

        Assert.Same(RecipeErrors.RecipeNotFound, theirs.Error);
        Assert.Same(nobodys.Error, theirs.Error);
        Assert.Equal(bVersion, await VersionAsync(world.B.Id, b.Recipe.Id));
        Assert.Equal(0, await CountAsync(world.B.Id, b.Recipe.Id));
    }

    // A freeze by the account that belongs to both sites changes nothing on the other: each site's recipe is frozen from its
    // own rows alone, and its revisions are numbered on their own, even where the two hold the very same ids.
    [Fact]
    public async Task Two_sites_with_the_same_ids_freeze_and_number_their_own()
    {
        var world = await _harness.SeedAsync("rvs-twins");
        var (recipe, group, line, stepGroup, step) = (
            new RecipeId(Guid.NewGuid()), new RecipeIngredientGroupId(Guid.NewGuid()), new RecipeIngredientId(Guid.NewGuid()),
            new RecipeStepGroupId(Guid.NewGuid()), new RecipeStepId(Guid.NewGuid()));
        await _harness.ChangeAsync(world.A.Id, db => RecipeRows.Add(db, recipe, group, line, stepGroup, step).Ingredient.OriginalText = "A's flour");
        await _harness.ChangeAsync(world.B.Id, db => RecipeRows.Add(db, recipe, group, line, stepGroup, step).Ingredient.OriginalText = "B's flour");

        var a1 = Succeeded(await FreezeAsync(world.A.Id, recipe, RecipeRevisionKind.Published, await VersionAsync(world.A.Id, recipe)));
        var a2 = Succeeded(await FreezeAsync(world.A.Id, recipe, RecipeRevisionKind.Draft, await VersionAsync(world.A.Id, recipe)));
        var b1 = Succeeded(await FreezeAsync(world.B.Id, recipe, RecipeRevisionKind.Published, await VersionAsync(world.B.Id, recipe)));

        Assert.Equal((1, 2, 1), (a1.Number, a2.Number, b1.Number));
        Assert.True(b1.Created);
        Assert.Equal("A's flour", Line(await RevisionAsync(world.A.Id, a1.Id)));
        Assert.Equal("B's flour", Line(await RevisionAsync(world.B.Id, b1.Id)));
        Assert.Equal(2, await CountAsync(world.A.Id, recipe));
        Assert.Equal(1, await CountAsync(world.B.Id, recipe));

        // Neither site's revision can be found from the other, though the recipe ids are one.
        await using var scope = _harness.In(world.B.Id);
        Assert.False(await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<RecipeRevision>()
            .AnyAsync(r => r.Id == a1.Id || r.Id == a2.Id));
    }

    // A repeat is decided by the entered site's revisions alone: B holding exactly A's recipe already frozen does not make A's
    // first freeze a repeat.
    [Fact]
    public async Task A_repeat_is_never_of_another_sites_revision()
    {
        var world = await _harness.SeedAsync("rvs-repeat");
        var (recipe, group, line, stepGroup, step) = (
            new RecipeId(Guid.NewGuid()), new RecipeIngredientGroupId(Guid.NewGuid()), new RecipeIngredientId(Guid.NewGuid()),
            new RecipeStepGroupId(Guid.NewGuid()), new RecipeStepId(Guid.NewGuid()));
        await _harness.ChangeAsync(world.A.Id, db => RecipeRows.Add(db, recipe, group, line, stepGroup, step));
        await _harness.ChangeAsync(world.B.Id, db => RecipeRows.Add(db, recipe, group, line, stepGroup, step));

        var b1 = Succeeded(await FreezeAsync(world.B.Id, recipe, RecipeRevisionKind.Published, await VersionAsync(world.B.Id, recipe)));
        var a1 = Succeeded(await FreezeAsync(world.A.Id, recipe, RecipeRevisionKind.Published, await VersionAsync(world.A.Id, recipe)));

        Assert.True(a1.Created);
        Assert.NotEqual(b1.Id, a1.Id);
        Assert.Equal((await RevisionAsync(world.B.Id, b1.Id)).ContentHash, (await RevisionAsync(world.A.Id, a1.Id)).ContentHash);
    }

    // The key locks this connection holds on the recipes table, by mode: KEY rows, and any key-range lock a HOLDLOCK takes.
    private static async Task<List<string>> RecipeKeyLocksAsync(PantryPressDbContext db) =>
        await db.Database.SqlQueryRaw<string>(
                "SELECT l.request_mode AS [Value] FROM sys.dm_tran_locks AS l "
                + "JOIN sys.partitions AS p ON p.hobt_id = l.resource_associated_entity_id "
                + "WHERE l.request_session_id = @@SPID AND l.resource_type = 'KEY' AND p.object_id = OBJECT_ID(N'[Recipes]')")
            .ToListAsync();

    private static string Line(RecipeRevision revision) =>
        RecipeSnapshotJson.Read(revision.SnapshotVersion, revision.Snapshot).IngredientGroups.Single().Lines.Single().OriginalText;

    private static RecipeRevisionCreated Succeeded(Result<RecipeRevisionCreated> result)
    {
        Assert.True(result.IsSuccess, result.Error?.Code);
        return result.Value;
    }

    private async Task<Result<RecipeRevisionCreated>> FreezeAsync(SiteId site, RecipeId recipe, RecipeRevisionKind kind, byte[] version)
    {
        await using var scope = _harness.In(site);
        return await scope.ServiceProvider.GetRequiredService<IRecipeRevisionsFacade>()
            .CreateRevisionAsync(new CreateRecipeRevisionRequest(recipe, kind, version), CancellationToken.None);
    }

    private async Task<byte[]> VersionAsync(SiteId site, RecipeId recipe)
    {
        await using var scope = _harness.In(site);
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<Recipe>()
            .Where(r => r.Id == recipe).Select(r => r.Version).SingleAsync();
    }

    private async Task<RecipeRevision> RevisionAsync(SiteId site, RecipeRevisionId id)
    {
        await using var scope = _harness.In(site);
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<RecipeRevision>().AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private async Task<int> CountAsync(SiteId site, RecipeId recipe)
    {
        await using var scope = _harness.In(site);
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<RecipeRevision>().CountAsync(r => r.RecipeId == recipe);
    }
}
