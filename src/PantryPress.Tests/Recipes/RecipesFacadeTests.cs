using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Recipes;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.Tests.Persistence;
using PantryPress.Tests.TwoSite;

namespace PantryPress.Tests.Recipes;

// PP4.11 (RCP-001, RCP-007; Templates B and D): the recipe routes' facade against the migrated database, signed in as the
// seeded members. A save keeps every word, moves the recipe's version with any change to any part, repeats nothing when
// nothing changed, refuses a stale read rather than writing over it, and leaves nothing when its commit fails. Who may
// change a recipe follows SITE-010. The routes over ApiService, and the other site, are RecipesApiTwoSiteTests'.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class RecipesFacadeTests(MigratedDatabase database) : IAsyncLifetime
{
    private readonly FailingSave _failingSave = new();
    private TwoSiteHarness _harness = null!;

    public async Task InitializeAsync() =>
        _harness = await TwoSiteHarness.CreateAsync(database, configure: options => options.AddInterceptors(_failingSave));

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task A_new_recipe_keeps_every_word_and_its_maker()
    {
        var world = await _harness.SeedAsync("rcf-create");
        var lines = RecipeSchemaTests.Wording.Where(w => w.Length <= RecipeLimits.MaxIngredientTextLength && !w.Contains('\n', StringComparison.Ordinal) && !w.Contains('\r', StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(w)).ToList();
        var content = RecipeEditorTests.Content(ingredients: [new RecipeIngredientGroupInput(null, " For the crust ", [.. lines.Select(l => new RecipeIngredientLineInput(null, l))])]);

        var created = Succeeded(await CallAsync(world.A, world.A.Author, f => f.CreateAsync(world.A.Author, world.A.Slug, content, default)));
        var read = Succeeded(await CallAsync(world.A, world.A.Author, f => f.GetAsync(world.A.Author, world.A.Slug, new RecipeId(created.Id), default)));

        Assert.Equal(lines, read.IngredientGroups[0].Lines.Select(l => l.OriginalText));
        Assert.Equal(" For the crust ", read.IngredientGroups[0].Heading);
        Assert.Equal(created.Version, read.Version);
        Assert.True(read.CanEdit);
        Assert.Equal(410.5m, read.Nutrition!.Calories);
        Assert.Equal(new RecipeQuantity(QuantityKind.Range, new(6, 1), new(8, 1)), read.Yield);
        Assert.Equal(["milk", "wheat"], read.Allergens);
        Assert.Equal(world.A.Author, (await RowAsync(world.A.Id, created.Id)).CreatedByAccountId);
    }

    [Fact]
    public async Task A_save_replaces_the_working_copy_and_moves_the_version()
    {
        var world = await _harness.SeedAsync("rcf-save");
        var created = await CreateAsync(world.A, world.A.Owner);

        var saved = Succeeded(await UpdateAsync(world.A, world.A.Owner, created, Echo(created) with { Title = "Plum tart", Keywords = ["summer"] }));

        Assert.Equal("Plum tart", saved.Title);
        Assert.Equal(["summer"], saved.Keywords);
        Assert.NotEqual(created.Version, saved.Version);
        Assert.True(saved.UpdatedAt > created.UpdatedAt);
        var row = await RowAsync(world.A.Id, created.Id);
        Assert.Equal((saved.Version, world.A.Owner), (Convert.ToBase64String(row.Version), row.CreatedByAccountId!.Value));
    }

    // The version guards the whole recipe (PP4.3 hand-off): a change to one part alone moves the recipe's row.
    [Fact]
    public async Task Changing_one_part_alone_moves_the_recipes_version()
    {
        var world = await _harness.SeedAsync("rcf-part");
        var created = await CreateAsync(world.A, world.A.Owner);
        var echo = Echo(created);
        var steps = echo.StepGroups![0]!;

        var saved = Succeeded(await UpdateAsync(world.A, world.A.Owner, created, echo with
        {
            StepGroups = [steps with { Steps = [steps.Steps![0], steps.Steps[1]! with { Text = "Bake until golden." }] }],
        }));

        Assert.NotEqual(created.Version, saved.Version);
        Assert.Equal(created.StepGroups[0].Steps.Select(s => s.Id), saved.StepGroups[0].Steps.Select(s => s.Id));
        Assert.Equal("Bake until golden.", saved.StepGroups[0].Steps[1].Text);
    }

    // No silent last-write-wins (Template D): a save from a stale read is refused, and nothing it carried is written.
    [Fact]
    public async Task A_save_from_a_stale_read_is_refused_and_saves_nothing()
    {
        var world = await _harness.SeedAsync("rcf-stale");
        var created = await CreateAsync(world.A, world.A.Owner);
        var first = Succeeded(await UpdateAsync(world.A, world.A.Editor, created, Echo(created) with { Title = "First tab" }));

        var second = await UpdateAsync(world.A, world.A.Owner, created, Echo(created) with { Title = "Second tab" });

        Assert.Equal(RecipeErrors.RecipeChanged, second.Error);
        var now = await GetAsync(world.A, world.A.Owner, created.Id);
        Assert.Equal(("First tab", first.Version), (now.Title, now.Version));
    }

    // Sending what the recipe already holds saves nothing and answers the recipe as it is, whatever version came with it:
    // the retry of a lost answer, or an autosave with nothing new, never moves the version or meets a conflict.
    [Fact]
    public async Task Saving_what_the_recipe_already_holds_writes_nothing()
    {
        var world = await _harness.SeedAsync("rcf-repeat");
        var created = await CreateAsync(world.A, world.A.Owner);
        var saved = Succeeded(await UpdateAsync(world.A, world.A.Owner, created, Echo(created) with { Title = "Plum tart" }));

        var again = Succeeded(await UpdateAsync(world.A, world.A.Owner, created, Echo(saved)));

        Assert.Equal((saved.Version, saved.UpdatedAt), (again.Version, again.UpdatedAt));
        AssertSame(saved, again);
    }

    // The table's two-place columns hold 14.50 and 14.5 alike, so writing either again is the same recipe.
    [Fact]
    public async Task The_same_amount_written_another_way_is_the_same_recipe()
    {
        var world = await _harness.SeedAsync("rcf-trailing");
        var created = await CreateAsync(world.A, world.A.Owner);
        var content = Echo(created) with { Nutrition = created.Nutrition! with { Calories = 410.50m } };

        var again = Succeeded(await UpdateAsync(world.A, world.A.Owner, created, content));

        Assert.Equal(created.Version, again.Version);
    }

    [Fact]
    public async Task A_line_moved_out_of_a_removed_group_keeps_its_id_in_the_database()
    {
        var world = await _harness.SeedAsync("rcf-move");
        var created = Succeeded(await CallAsync(world.A, world.A.Owner, f => f.CreateAsync(world.A.Owner, world.A.Slug, RecipeEditorTests.Content(ingredients:
        [
            new RecipeIngredientGroupInput(null, "Crust", [new(null, "2 cups flour"), new(null, "1 tsp salt")]),
            new RecipeIngredientGroupInput(null, "Filling", [new(null, "6 apples")]),
        ]), default)));
        var flour = created.IngredientGroups[0].Lines[0].Id;
        var filling = created.IngredientGroups[1];

        var saved = Succeeded(await UpdateAsync(world.A, world.A.Owner, created, Echo(created) with
        {
            IngredientGroups = [new RecipeIngredientGroupInput(filling.Id, "Filling", [new(filling.Lines[0].Id, "6 apples"), new(flour, "2 cups flour")])],
        }));

        var group = Assert.Single(saved.IngredientGroups);
        Assert.Equal([filling.Lines[0].Id, flour], group.Lines.Select(l => l.Id));
        AssertSame(saved, await GetAsync(world.A, world.A.Owner, created.Id));
    }

    // A part named by an id that is not this recipe's - another recipe's on the same site, or unknown - is a field error at
    // that id, and nothing is saved.
    [Fact]
    public async Task A_part_that_is_not_this_recipes_is_refused_at_its_id()
    {
        var world = await _harness.SeedAsync("rcf-parts");
        var created = await CreateAsync(world.A, world.A.Owner);
        var other = await CreateAsync(world.A, world.A.Owner);
        var echo = Echo(created);

        var result = await UpdateAsync(world.A, world.A.Owner, created, echo with
        {
            IngredientGroups = [new RecipeIngredientGroupInput(other.IngredientGroups[0].Id, null, [new(other.IngredientGroups[0].Lines[0].Id, "1 egg")])],
            StepGroups = [new RecipeStepGroupInput(null, null, [new(Guid.NewGuid(), "Stir.")])],
        });

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Equal(
            ["content.ingredientGroups[0].id", "content.ingredientGroups[0].lines[0].id", "content.stepGroups[0].steps[0].id"],
            result.Error.Fields.Keys.Order(StringComparer.Ordinal));
        AssertSame(created, await GetAsync(world.A, world.A.Owner, created.Id));
    }

    // SITE-010: an Author changes only their own; anyone else's they read, list, and copy. Owners and Editors change any.
    [Fact]
    public async Task An_author_changes_only_their_own_recipes()
    {
        var world = await _harness.SeedAsync("rcf-author");
        var owners = await CreateAsync(world.A, world.A.Owner);
        var authors = await CreateAsync(world.A, world.A.Author);

        var read = await GetAsync(world.A, world.A.Author, owners.Id);
        Assert.False(read.CanEdit);
        Assert.Equal(RecipeErrors.RecipeNotYours, (await UpdateAsync(world.A, world.A.Author, owners, Echo(owners) with { Title = "Mine now" })).Error);
        Assert.Equal(RecipeErrors.RecipeNotYours, (await CallAsync(world.A, world.A.Author, f => f.ArchiveAsync(world.A.Author, world.A.Slug, new RecipeId(owners.Id), default))).Error);
        Assert.Equal(RecipeErrors.RecipeNotYours, (await CallAsync(world.A, world.A.Author, f => f.RestoreAsync(world.A.Author, world.A.Slug, new RecipeId(owners.Id), default))).Error);

        // The shared account is an Author on A too, and made nothing there.
        Assert.Equal(RecipeErrors.RecipeNotYours, (await UpdateAsync(world.A, world.Shared, authors, Echo(authors) with { Title = "Not mine" })).Error);

        Succeeded(await UpdateAsync(world.A, world.A.Author, authors, Echo(authors) with { Title = "Mine" }));
        var edited = Succeeded(await UpdateAsync(world.A, world.A.Editor, owners, Echo(owners) with { Title = "Edited" }));
        Assert.Equal(world.A.Owner, (await RowAsync(world.A.Id, owners.Id)).CreatedByAccountId);
        Assert.Equal("Copy of Edited", Succeeded(await CallAsync(world.A, world.A.Author, f => f.DuplicateAsync(world.A.Author, world.A.Slug, new RecipeId(edited.Id), default))).Title);
    }

    [Fact]
    public async Task An_archived_recipe_leaves_the_library_and_is_restored_unchanged()
    {
        var world = await _harness.SeedAsync("rcf-archive");
        var created = await CreateAsync(world.A, world.A.Owner);
        var id = new RecipeId(created.Id);

        Assert.True((await CallAsync(world.A, world.A.Owner, f => f.ArchiveAsync(world.A.Owner, world.A.Slug, id, default))).IsSuccess);
        var archived = await GetAsync(world.A, world.A.Owner, created.Id);
        Assert.True((await CallAsync(world.A, world.A.Owner, f => f.ArchiveAsync(world.A.Owner, world.A.Slug, id, default))).IsSuccess);

        Assert.NotNull(archived.ArchivedAt);
        Assert.Equal(created.UpdatedAt, archived.UpdatedAt);
        Assert.Equal(archived.Version, (await GetAsync(world.A, world.A.Owner, created.Id)).Version);
        Assert.Equal(RecipeErrors.RecipeArchived, (await UpdateAsync(world.A, world.A.Owner, archived, Echo(archived) with { Title = "No" })).Error);
        Assert.DoesNotContain(created.Id, (await ListAsync(world.A, world.A.Owner, new ListRecipesQuery(null, null, null))).Select(r => r.Id));
        Assert.Contains(created.Id, (await ListAsync(world.A, world.A.Owner, new ListRecipesQuery(null, null, true))).Select(r => r.Id));

        Assert.True((await CallAsync(world.A, world.A.Owner, f => f.RestoreAsync(world.A.Owner, world.A.Slug, id, default))).IsSuccess);
        var restored = await GetAsync(world.A, world.A.Owner, created.Id);
        Assert.Null(restored.ArchivedAt);
        AssertSame(created with { Version = restored.Version }, restored);
    }

    [Fact]
    public async Task A_duplicate_is_a_new_recipe_of_the_same_words_made_by_the_caller()
    {
        var world = await _harness.SeedAsync("rcf-copy");
        var source = await CreateAsync(world.A, world.A.Owner);
        Assert.True((await CallAsync(world.A, world.A.Owner, f => f.ArchiveAsync(world.A.Owner, world.A.Slug, new RecipeId(source.Id), default))).IsSuccess);

        var copy = Succeeded(await CallAsync(world.A, world.A.Editor, f => f.DuplicateAsync(world.A.Editor, world.A.Slug, new RecipeId(source.Id), default)));

        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal("Copy of Apple pie", copy.Title);
        Assert.Null(copy.ArchivedAt);
        Assert.Equal(
            source.IngredientGroups.SelectMany(g => g.Lines).Select(l => l with { Id = default }),
            copy.IngredientGroups.SelectMany(g => g.Lines).Select(l => l with { Id = default }));
        Assert.Empty(source.IngredientGroups.SelectMany(g => g.Lines.Select(l => l.Id).Prepend(g.Id))
            .Intersect(copy.IngredientGroups.SelectMany(g => g.Lines.Select(l => l.Id).Prepend(g.Id))));
        Assert.Equal((source.Nutrition, source.Yield), (copy.Nutrition, copy.Yield));
        Assert.Equal(source.Allergens, copy.Allergens);
        Assert.Equal(world.A.Editor, (await RowAsync(world.A.Id, copy.Id)).CreatedByAccountId);
        Assert.NotNull((await GetAsync(world.A, world.A.Owner, source.Id)).ArchivedAt);
    }

    [Fact]
    public async Task The_library_is_read_most_recently_edited_first_a_page_at_a_time()
    {
        var world = await _harness.SeedAsync("rcf-list");
        var made = new List<RecipeDetail>();
        for (var i = 0; i < 5; i++)
        {
            made.Add(await CreateAsync(world.A, i % 2 == 0 ? world.A.Author : world.A.Owner));
        }

        Succeeded(await UpdateAsync(world.A, world.A.Owner, made[0], Echo(made[0]) with { Title = "Edited last" }));

        var seen = new List<RecipeSummary>();
        string? cursor = null;
        do
        {
            var page = Succeeded(await CallAsync(world.A, world.A.Author, f => f.ListAsync(world.A.Author, world.A.Slug, new ListRecipesQuery(2, cursor, null), default)));
            Assert.InRange(page.Items.Count, 1, 2);
            seen.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(made.Select(m => m.Id).OrderBy(id => id), seen.Select(s => s.Id).OrderBy(id => id));
        Assert.Equal(made[0].Id, seen[0].Id);
        Assert.Equal("Edited last", seen[0].Title);
        Assert.Equal(seen.OrderByDescending(s => s.UpdatedAt).Select(s => s.UpdatedAt), seen.Select(s => s.UpdatedAt));
        Assert.Equal([true, false, true, false, true], made.Select(m => seen.Single(s => s.Id == m.Id).CanEdit));
    }

    // PP4.12a: a search finds words anywhere in the title, ignoring case and accents, and a wildcard only as itself; each
    // code narrows by its own list; filters combine; the archived tab is searched alone; and a filtered list pages whole.
    [Fact]
    public async Task The_library_is_searched_and_filtered_on_the_server()
    {
        var world = await _harness.SeedAsync("rcf-search");
        var creme = await CreateAsync(world.A, world.A.Owner, RecipeEditorTests.Content("Crème brûlée") with { Courses = ["dessert"], Cuisines = ["french"], Diets = [] });
        var percent = await CreateAsync(world.A, world.A.Owner, RecipeEditorTests.Content("100% rye bread") with { Courses = ["bread"], Cuisines = [], Diets = ["vegan"] });
        var under = await CreateAsync(world.A, world.A.Owner, RecipeEditorTests.Content("Pie_crust") with { Courses = ["dessert"], Cuisines = ["american"], Diets = ["vegetarian"] });
        var plain = await CreateAsync(world.A, world.A.Owner, RecipeEditorTests.Content("Pie crust") with { Courses = ["dessert"], Cuisines = ["american"], Diets = ["vegetarian"] });
        var archived = await CreateAsync(world.A, world.A.Owner, RecipeEditorTests.Content("Crème caramel"));
        Assert.True((await CallAsync(world.A, world.A.Owner, f => f.ArchiveAsync(world.A.Owner, world.A.Slug, new RecipeId(archived.Id), default))).IsSuccess);

        async Task<Guid[]> Ids(ListRecipesQuery query) => [.. (await ListAsync(world.A, world.A.Owner, query)).Select(r => r.Id).Order()];
        Guid[] Of(params RecipeDetail[] recipes) => [.. recipes.Select(r => r.Id).Order()];

        Assert.Equal(Of(creme), await Ids(new(null, null, null, "creme")));
        Assert.Equal(Of(creme), await Ids(new(null, null, null, "  BRULEE ")));
        Assert.Equal(Of(archived), await Ids(new(null, null, true, "creme")));
        Assert.Equal(Of(percent), await Ids(new(null, null, null, "0%")));
        Assert.Equal(Of(under), await Ids(new(null, null, null, "e_c")));
        Assert.Equal(Of(under, plain), await Ids(new(null, null, null, "crust")));
        Assert.Equal(Of(creme, under, plain), await Ids(new(null, null, null, null, "dessert")));
        Assert.Equal(Of(creme), await Ids(new(null, null, null, null, "dessert", "french")));
        Assert.Equal(Of(percent), await Ids(new(null, null, null, null, null, null, "vegan")));
        Assert.Equal(Of(plain), await Ids(new(null, null, null, "pie crust", "dessert", "american", "vegetarian")));
        Assert.Empty(await Ids(new(null, null, null, "pie", "bread")));
        Assert.Equal(Of(creme, percent, under, plain), await Ids(new(null, null, null, " ")));

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = Succeeded(await CallAsync(world.A, world.A.Owner, f => f.ListAsync(world.A.Owner, world.A.Slug, new ListRecipesQuery(1, cursor, null, null, "dessert"), default)));
            seen.AddRange(page.Items.Select(i => i.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(Of(creme, under, plain), seen.Order().ToArray());
    }

    // Unknown and missing are one answer, and nothing is entered or written for them.
    [Fact]
    public async Task An_unknown_recipe_is_not_found_on_every_call()
    {
        var world = await _harness.SeedAsync("rcf-missing");
        var missing = new RecipeId(Guid.NewGuid());
        var real = await CreateAsync(world.A, world.A.Owner);

        Assert.Equal(RecipeErrors.RecipeNotFound, (await CallAsync(world.A, world.A.Owner, f => f.GetAsync(world.A.Owner, world.A.Slug, missing, default))).Error);
        Assert.Equal(RecipeErrors.RecipeNotFound, (await CallAsync(world.A, world.A.Owner, f => f.UpdateAsync(world.A.Owner, world.A.Slug, missing, new UpdateRecipeRequest(real.Version, Echo(real)), default))).Error);
        Assert.Equal(RecipeErrors.RecipeNotFound, (await CallAsync(world.A, world.A.Owner, f => f.ArchiveAsync(world.A.Owner, world.A.Slug, missing, default))).Error);
        Assert.Equal(RecipeErrors.RecipeNotFound, (await CallAsync(world.A, world.A.Owner, f => f.RestoreAsync(world.A.Owner, world.A.Slug, missing, default))).Error);
        Assert.Equal(RecipeErrors.RecipeNotFound, (await CallAsync(world.A, world.A.Owner, f => f.DuplicateAsync(world.A.Owner, world.A.Slug, missing, default))).Error);
    }

    // A commit that fails leaves the recipe exactly as it was, and the next save goes through.
    [Fact]
    public async Task A_save_whose_commit_fails_leaves_nothing()
    {
        var world = await _harness.SeedAsync("rcf-rollback");
        var created = await CreateAsync(world.A, world.A.Owner);

        _failingSave.Fail = true;
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => UpdateAsync(world.A, world.A.Owner, created, Echo(created) with { Title = "Lost", Keywords = [] }));
            await Assert.ThrowsAsync<DbUpdateException>(() => CallAsync(world.A, world.A.Owner, f => f.CreateAsync(world.A.Owner, world.A.Slug, RecipeEditorTests.Content("Never"), default)));
        }
        finally
        {
            _failingSave.Fail = false;
        }

        AssertSame(created, await GetAsync(world.A, world.A.Owner, created.Id));
        Assert.DoesNotContain("Never", (await ListAsync(world.A, world.A.Owner, new ListRecipesQuery(null, null, null))).Select(r => r.Title));
        Succeeded(await UpdateAsync(world.A, world.A.Owner, created, Echo(created) with { Title = "Kept" }));
    }

    // Two saves from the same read at once: one is saved, the other refused, never both written over each other.
    [Fact]
    public async Task Two_saves_from_one_read_at_once_save_one()
    {
        var world = await _harness.SeedAsync("rcf-race");
        var created = await CreateAsync(world.A, world.A.Owner);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i =>
            UpdateAsync(world.A, world.A.Owner, created, Echo(created) with { Title = $"Tab {i}" })));

        Assert.Single(results, r => r.IsSuccess);
        Assert.All(results.Where(r => !r.IsSuccess), r => Assert.Equal(RecipeErrors.RecipeChanged, r.Error));
        Assert.Equal(results.Single(r => r.IsSuccess).Value.Title, (await GetAsync(world.A, world.A.Owner, created.Id)).Title);
    }

    // A save moves the version, so a freeze from the version read before it is refused, and one from after holds the save.
    [Fact]
    public async Task A_freeze_after_a_save_holds_the_save()
    {
        var world = await _harness.SeedAsync("rcf-freeze");
        var created = await CreateAsync(world.A, world.A.Owner);
        var saved = Succeeded(await UpdateAsync(world.A, world.A.Owner, created, Echo(created) with { Title = "Frozen title" }));

        await using var scope = _harness.In(world.A.Id);
        var revisions = scope.ServiceProvider.GetRequiredService<IRecipeRevisionsFacade>();
        var stale = await revisions.CreateRevisionAsync(new CreateRecipeRevisionRequest(new RecipeId(created.Id), RecipeRevisionKind.Draft, Convert.FromBase64String(created.Version)), default);
        var fresh = await revisions.CreateRevisionAsync(new CreateRecipeRevisionRequest(new RecipeId(created.Id), RecipeRevisionKind.Draft, Convert.FromBase64String(saved.Version)), default);

        Assert.Equal(RecipeErrors.RecipeChanged, stale.Error);
        Assert.True(fresh.IsSuccess);
        var snapshot = await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<RecipeRevision>()
            .Where(r => r.Id == fresh.Value.Id).Select(r => r.Snapshot).SingleAsync();
        Assert.Contains("\"title\":\"Frozen title\"", snapshot, StringComparison.Ordinal);
    }

    // From review (all four reviewers): a create never takes a key from its caller. A body read from another recipe and
    // posted back, or any id at all, is a field error at the id, never a row under a key the caller chose, nor a 500.
    [Fact]
    public async Task A_create_naming_part_ids_is_refused_at_their_ids_and_writes_nothing()
    {
        var world = await _harness.SeedAsync("rcf-create-ids");
        var other = await CreateAsync(world.A, world.A.Owner);
        var before = (await ListAsync(world.A, world.A.Owner, new ListRecipesQuery(null, null, null))).Count;

        var reposted = await CallAsync(world.A, world.A.Owner, f => f.CreateAsync(world.A.Owner, world.A.Slug, Echo(other), default));
        var unknown = await CallAsync(world.A, world.A.Owner, f => f.CreateAsync(world.A.Owner, world.A.Slug, RecipeEditorTests.Content(
            steps: [new RecipeStepGroupInput(null, null, [new(Guid.NewGuid(), "Stir.")])]), default));

        Assert.Equal(ErrorKind.Validation, reposted.Error!.Kind);
        Assert.Equal(
            ["ingredientGroups[0].id", "ingredientGroups[0].lines[0].id", "ingredientGroups[0].lines[1].id", "stepGroups[0].id", "stepGroups[0].steps[0].id", "stepGroups[0].steps[1].id"],
            reposted.Error.Fields.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["stepGroups[0].steps[0].id"], unknown.Error!.Fields.Keys);
        Assert.Equal(before, (await ListAsync(world.A, world.A.Owner, new ListRecipesQuery(null, null, null))).Count);
    }

    // From review: a commit that landed and lost its answer is retried into the rows it made - one recipe, one copy, one
    // save - never a second row, a 500, or a conflict with itself.
    [Fact]
    public async Task A_commit_that_landed_but_lost_its_answer_is_retried_into_its_own_rows()
    {
        var world = await _harness.SeedAsync("rcf-landed");
        var source = await CreateAsync(world.A, world.A.Owner);

        async Task<(T Result, int Attempts)> OnceLostAsync<T>(Func<IRecipesFacade, Task<T>> call)
        {
            var fault = new RecipeRevisionsFacadeTests.LandedCommitFault();
            await using var retrying = await TwoSiteHarness.CreateAsync(database, configure: fault.Configure);
            await using var scope = retrying.In(world.A.Id);
            return (await call(scope.ServiceProvider.GetRequiredService<IRecipesFacade>()), fault.Attempts);
        }

        var before = (await ListAsync(world.A, world.A.Owner, new ListRecipesQuery(100, null, null))).Count;
        var (created, createAttempts) = await OnceLostAsync(f => f.CreateAsync(world.A.Owner, world.A.Slug, RecipeEditorTests.Content("Landed"), default));
        var (copy, copyAttempts) = await OnceLostAsync(f => f.DuplicateAsync(world.A.Owner, world.A.Slug, new RecipeId(source.Id), default));
        var added = Echo(source) with { Keywords = ["landed"], Equipment = [.. source.Equipment, "Whisk"] };
        var (saved, saveAttempts) = await OnceLostAsync(f => f.UpdateAsync(world.A.Owner, world.A.Slug, new RecipeId(source.Id), new UpdateRecipeRequest(source.Version, added), default));

        Assert.Equal((2, 2, 2), (createAttempts, copyAttempts, saveAttempts));
        Assert.Equal(before + 2, (await ListAsync(world.A, world.A.Owner, new ListRecipesQuery(100, null, null))).Count);
        AssertSame(Succeeded(created), await GetAsync(world.A, world.A.Owner, Succeeded(created).Id));
        AssertSame(Succeeded(copy), await GetAsync(world.A, world.A.Owner, Succeeded(copy).Id));
        AssertSame(Succeeded(saved), await GetAsync(world.A, world.A.Owner, source.Id));
        Assert.Equal(["landed"], Succeeded(saved).Keywords);
    }

    // From review: the keyset breaks a tie in edit time by id, in SQL Server's order, so recipes edited at the very same
    // moment page with no repeat and no gap.
    [Fact]
    public async Task Recipes_edited_at_the_same_moment_page_with_no_repeat_or_gap()
    {
        await using var fixedTime = await TwoSiteHarness.CreateAsync(
            database,
            domainServices: s => s.AddSingleton<PantryPress.Domain.Managers.Time.IClock>(new RecipeRevisionsFacadeTests.FixedClock(new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero))));
        var world = await fixedTime.SeedAsync("rcf-ties");
        var made = new List<Guid>();
        for (var i = 0; i < 7; i++)
        {
            await using var scope = fixedTime.In(world.A.Id);
            made.Add(Succeeded(await scope.ServiceProvider.GetRequiredService<IRecipesFacade>().CreateAsync(world.A.Owner, world.A.Slug, RecipeEditorTests.Content($"Tie {i}"), default)).Id);
        }

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            await using var scope = fixedTime.In(world.A.Id);
            var page = Succeeded(await scope.ServiceProvider.GetRequiredService<IRecipesFacade>().ListAsync(world.A.Owner, world.A.Slug, new ListRecipesQuery(2, cursor, null), default));
            seen.AddRange(page.Items.Select(i => i.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(7, seen.Count);
        Assert.Equal(made.Order(), seen.Order());
    }

    // From review: every field and part a save holds is kept, and changing any one alone moves the version - so no field
    // slips past the "nothing changed" check, and a freeze from the version before it is refused.
    public static TheoryData<string, Func<RecipeContent, RecipeContent>> SingleChanges => new()
    {
        { "summary", c => c with { Summary = "Changed." } },
        { "yield", c => c with { Yield = new RecipeQuantity(QuantityKind.Amount, new(12, 1), null) } },
        { "yieldUnit", c => c with { YieldUnit = "slices" } },
        { "prepMinutes", c => c with { PrepMinutes = 0 } },
        { "cookMinutes", c => c with { CookMinutes = null } },
        { "additionalMinutes", c => c with { AdditionalMinutes = 30 } },
        { "additionalTimeLabel", c => c with { AdditionalTimeLabel = "Chill" } },
        { "videoUrl", c => c with { VideoUrl = "https://vimeo.com/1" } },
        { "calories", c => c with { Nutrition = c.Nutrition! with { Calories = 400m } } },
        { "servingSize", c => c with { Nutrition = c.Nutrition! with { ServingSize = "1 wedge" } } },
        { "ingredient heading", c => c with { IngredientGroups = [c.IngredientGroups![0]! with { Heading = "Crust" }] } },
        { "step heading", c => c with { StepGroups = [c.StepGroups![0]! with { Heading = "Bake" }] } },
        { "line text", c => c with { IngredientGroups = [c.IngredientGroups![0]! with { Lines = [c.IngredientGroups[0]!.Lines![0]! with { OriginalText = "3 cups flour" }, c.IngredientGroups[0]!.Lines![1]] }] } },
        { "line removed", c => c with { IngredientGroups = [c.IngredientGroups![0]! with { Lines = [c.IngredientGroups[0]!.Lines![1]] }] } },
        { "lines reordered", c => c with { IngredientGroups = [c.IngredientGroups![0]! with { Lines = [c.IngredientGroups[0]!.Lines![1], c.IngredientGroups[0]!.Lines![0]] }] } },
        { "empty group added", c => c with { IngredientGroups = [c.IngredientGroups![0], new RecipeIngredientGroupInput(null, "Filling", [])] } },
        { "equipment", c => c with { Equipment = ["Rolling pin"] } },
        { "notes", c => c with { Notes = [] } },
        { "courses", c => c with { Courses = ["dessert", "snack"] } },
        { "cuisines", c => c with { Cuisines = [] } },
        { "diets", c => c with { Diets = ["vegan"] } },
        { "allergens reordered", c => c with { Allergens = ["wheat", "milk"] } },
    };

    [Theory]
    [MemberData(nameof(SingleChanges))]
    public async Task Changing_only_this_moves_the_version_and_is_kept(string what, Func<RecipeContent, RecipeContent> change)
    {
        var world = await _harness.SeedAsync("rcf-one");
        _ = what;
        var created = await CreateAsync(world.A, world.A.Owner);
        var content = change(Echo(created));

        var saved = Succeeded(await UpdateAsync(world.A, world.A.Owner, created, content));

        Assert.NotEqual(created.Version, saved.Version);
        var read = await GetAsync(world.A, world.A.Owner, created.Id);
        AssertSame(saved, read);
        Assert.Equal(JsonSerializer.Serialize(WithoutNewIds(content, content)), JsonSerializer.Serialize(WithoutNewIds(Echo(read), content)));
        await using var scope = _harness.In(world.A.Id);
        var stale = await scope.ServiceProvider.GetRequiredService<IRecipeRevisionsFacade>().CreateRevisionAsync(
            new CreateRecipeRevisionRequest(new RecipeId(created.Id), RecipeRevisionKind.Draft, Convert.FromBase64String(created.Version)), default);
        Assert.Equal(RecipeErrors.RecipeChanged, stale.Error);
    }

    [Fact]
    public async Task Empty_groups_are_stored_and_read_back()
    {
        var world = await _harness.SeedAsync("rcf-empty");
        var created = Succeeded(await CallAsync(world.A, world.A.Owner, f => f.CreateAsync(world.A.Owner, world.A.Slug, RecipeEditorTests.Content(
            ingredients: [new RecipeIngredientGroupInput(null, "For the crust", []), new RecipeIngredientGroupInput(null, null, null)],
            steps: [new RecipeStepGroupInput(null, null, [])]), default)));

        var read = await GetAsync(world.A, world.A.Owner, created.Id);

        Assert.Equal(["For the crust", null], read.IngredientGroups.Select(g => g.Heading));
        Assert.All(read.IngredientGroups, g => Assert.Empty(g.Lines));
        Assert.Empty(Assert.Single(read.StepGroups).Steps);
        Assert.Equal(read.Version, Succeeded(await UpdateAsync(world.A, world.A.Owner, read, Echo(read))).Version);
    }

    // From review: each nutrient holds a different value, so two swapped anywhere between the API and its column show.
    [Fact]
    public async Task Every_nutrient_is_kept_in_its_own_column()
    {
        var world = await _harness.SeedAsync("rcf-nutrients");
        var nutrition = new RecipeNutrition("1 slice", 1.01m, 2.02m, 3.03m, 4.04m, 5.05m, 6.06m, 7.07m, 8.08m, 9.09m, 10.1m, 9_999_999.99m);
        var created = Succeeded(await CallAsync(world.A, world.A.Owner, f => f.CreateAsync(world.A.Owner, world.A.Slug, RecipeEditorTests.Content() with { Nutrition = nutrition }, default)));

        var row = await RowAsync(world.A.Id, created.Id);

        Assert.Equal(nutrition, (await GetAsync(world.A, world.A.Owner, created.Id)).Nutrition);
        Assert.Equal(
            new decimal?[] { 1.01m, 2.02m, 3.03m, 4.04m, 5.05m, 6.06m, 7.07m, 8.08m, 9.09m, 10.1m, 9_999_999.99m },
            new[] { row.Calories, row.FatGrams, row.SaturatedFatGrams, row.TransFatGrams, row.UnsaturatedFatGrams, row.CholesterolMilligrams, row.SodiumMilligrams, row.CarbohydrateGrams, row.FiberGrams, row.SugarGrams, row.ProteinGrams });
        Assert.Equal("1 slice", row.ServingSize);
    }

    // From review: a duplicate holds every field of the original, parts included, but its ids, title, and bookkeeping.
    [Fact]
    public async Task A_duplicate_holds_every_field_of_the_original()
    {
        var world = await _harness.SeedAsync("rcf-copy-all");
        var source = await CreateAsync(world.A, world.A.Owner);

        var copy = Succeeded(await CallAsync(world.A, world.A.Owner, f => f.DuplicateAsync(world.A.Owner, world.A.Slug, new RecipeId(source.Id), default)));

        Assert.Equal(
            JsonSerializer.Serialize(WithoutNewIds(Echo(source)) with { Title = "Copy of " + source.Title }),
            JsonSerializer.Serialize(WithoutNewIds(Echo(copy))));
    }

    // From review: the order the checks are made in. An Author on someone else's archived recipe hears it is not theirs; an
    // archived recipe is archived whatever the save holds; and a part that is not the recipe's is named before a stale
    // version is.
    [Fact]
    public async Task The_checks_come_in_their_order()
    {
        var world = await _harness.SeedAsync("rcf-order");
        var owners = await CreateAsync(world.A, world.A.Owner);
        var fresh = await CreateAsync(world.A, world.A.Owner);
        Succeeded(await UpdateAsync(world.A, world.A.Owner, fresh, Echo(fresh) with { Title = "Moved on" }));
        Assert.True((await CallAsync(world.A, world.A.Owner, f => f.ArchiveAsync(world.A.Owner, world.A.Slug, new RecipeId(owners.Id), default))).IsSuccess);
        var archived = await GetAsync(world.A, world.A.Owner, owners.Id);

        Assert.Equal(RecipeErrors.RecipeNotYours, (await UpdateAsync(world.A, world.A.Author, archived, Echo(archived))).Error);
        Assert.Equal(RecipeErrors.RecipeArchived, (await UpdateAsync(world.A, world.A.Owner, archived, Echo(archived))).Error);
        var stalePart = await UpdateAsync(world.A, world.A.Owner, fresh, Echo(fresh) with { StepGroups = [new RecipeStepGroupInput(Guid.NewGuid(), null, [])] });
        Assert.Equal(ErrorKind.Validation, stalePart.Error!.Kind);
    }

    [Fact]
    public async Task An_author_archives_and_restores_their_own_and_restoring_twice_changes_nothing()
    {
        var world = await _harness.SeedAsync("rcf-author-archive");
        var authors = await CreateAsync(world.A, world.A.Author);
        var id = new RecipeId(authors.Id);

        Assert.True((await CallAsync(world.A, world.A.Author, f => f.ArchiveAsync(world.A.Author, world.A.Slug, id, default))).IsSuccess);
        Assert.True((await CallAsync(world.A, world.A.Author, f => f.RestoreAsync(world.A.Author, world.A.Slug, id, default))).IsSuccess);
        var restored = await GetAsync(world.A, world.A.Author, authors.Id);
        Assert.True((await CallAsync(world.A, world.A.Author, f => f.RestoreAsync(world.A.Author, world.A.Slug, id, default))).IsSuccess);

        AssertSame(restored, await GetAsync(world.A, world.A.Author, authors.Id));
    }

    // Part ids that are new in a save are minted on the server, so they are compared by position; ids the content named
    // stay. With no second content, every id is cleared.
    private static RecipeContent WithoutNewIds(RecipeContent content, RecipeContent? named = null) => content with
    {
        IngredientGroups = [.. content.IngredientGroups!.Select((g, i) => g! with
        {
            Id = Named(g.Id, named?.IngredientGroups?.ElementAtOrDefault(i)?.Id),
            Lines = [.. (g.Lines ?? []).Select((l, j) => l! with { Id = Named(l.Id, named?.IngredientGroups?.ElementAtOrDefault(i)?.Lines?.ElementAtOrDefault(j)?.Id) })],
        })],
        StepGroups = [.. content.StepGroups!.Select((g, i) => g! with
        {
            Id = Named(g.Id, named?.StepGroups?.ElementAtOrDefault(i)?.Id),
            Steps = [.. (g.Steps ?? []).Select((s, j) => s! with { Id = Named(s.Id, named?.StepGroups?.ElementAtOrDefault(i)?.Steps?.ElementAtOrDefault(j)?.Id) })],
        })],
    };

    private static Guid? Named(Guid? id, Guid? named) => named is not null && id == named ? id : null;

    private async Task<RecipeDetail> CreateAsync(SeededSite site, AccountId account, RecipeContent? content = null) =>
        Succeeded(await CallAsync(site, account, f => f.CreateAsync(account, site.Slug, content ?? RecipeEditorTests.Content(), default)));

    private Task<Result<RecipeDetail>> UpdateAsync(SeededSite site, AccountId account, RecipeDetail read, RecipeContent content) =>
        CallAsync(site, account, f => f.UpdateAsync(account, site.Slug, new RecipeId(read.Id), new UpdateRecipeRequest(read.Version, content), default));

    private async Task<RecipeDetail> GetAsync(SeededSite site, AccountId account, Guid recipe) =>
        Succeeded(await CallAsync(site, account, f => f.GetAsync(account, site.Slug, new RecipeId(recipe), default)));

    private async Task<IReadOnlyList<RecipeSummary>> ListAsync(SeededSite site, AccountId account, ListRecipesQuery query) =>
        Succeeded(await CallAsync(site, account, f => f.ListAsync(account, site.Slug, query, default))).Items;

    // Each call in a scope of its own, entered as the route's site policy would enter it.
    private async Task<T> CallAsync<T>(SeededSite site, AccountId account, Func<IRecipesFacade, Task<T>> call)
    {
        _ = account;
        await using var scope = _harness.In(site.Id);
        return await call(scope.ServiceProvider.GetRequiredService<IRecipesFacade>());
    }

    private async Task<Recipe> RowAsync(SiteId site, Guid recipe)
    {
        await using var scope = _harness.In(site);
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<Recipe>().AsNoTracking()
            .SingleAsync(r => r.Id == new RecipeId(recipe));
    }

    // What the editor sends back for a recipe it read: every part by its id, in order.
    internal static RecipeContent Echo(RecipeDetail detail) => new(
        detail.Title,
        detail.Summary,
        detail.Yield,
        detail.YieldUnit,
        detail.PrepMinutes,
        detail.CookMinutes,
        detail.AdditionalMinutes,
        detail.AdditionalTimeLabel,
        detail.VideoUrl,
        detail.Nutrition,
        [.. detail.IngredientGroups.Select(g => new RecipeIngredientGroupInput(g.Id, g.Heading, [.. g.Lines.Select(l => new RecipeIngredientLineInput(l.Id, l.OriginalText))]))],
        [.. detail.StepGroups.Select(g => new RecipeStepGroupInput(g.Id, g.Heading, [.. g.Steps.Select(s => new RecipeStepInput(s.Id, s.Text))]))],
        detail.Equipment,
        detail.Notes,
        detail.Keywords,
        detail.Courses,
        detail.Cuisines,
        detail.Diets,
        detail.Allergens);

    // Two answers alike, field by field and part by part, as the client reads them.
    private static void AssertSame(RecipeDetail expected, RecipeDetail actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));

    private static T Succeeded<T>(Result<T> result)
    {
        Assert.True(result.IsSuccess, result.IsSuccess ? null : $"{result.Error!.Code}: {string.Join("; ", result.Error.Fields.Select(f => f.Key + " " + string.Join(",", f.Value)))}");
        return result.Value;
    }

    // Fails every save while Fail is set, after the attempt has staged its rows, as a lost connection at commit would.
    private sealed class FailingSave : SaveChangesInterceptor
    {
        public volatile bool Fail;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            Fail ? throw new DbUpdateException("The save failed (test).") : base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
