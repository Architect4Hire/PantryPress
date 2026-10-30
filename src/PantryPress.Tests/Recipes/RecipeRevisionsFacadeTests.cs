using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Recipes;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.Tests.Persistence;
using PantryPress.Tests.TwoSite;

namespace PantryPress.Tests.Recipes;

// PP4.4 (RCP-007, Template D): freezing a recipe into a revision, through the facade, against the migrated database. A
// revision holds the recipe exactly as it was when frozen, and editing afterwards changes only the working copy; a repeat
// adds nothing; a stale read is refused rather than frozen over; a failed commit leaves nothing; and two freezes at once
// never collide. The two-site cases are RecipeRevisionsTwoSiteTests'.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class RecipeRevisionsFacadeTests(MigratedDatabase database) : IAsyncLifetime
{
    private TwoSiteHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await TwoSiteHarness.CreateAsync(database);

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task Publishing_freezes_the_recipe_as_its_first_revision()
    {
        var (site, rows) = await SeedRecipeAsync("rvf-first");
        var version = await VersionAsync(site, rows.Recipe.Id);

        var result = await FreezeAsync(site, rows.Recipe.Id, RecipeRevisionKind.Published, version);

        var created = Succeeded(result);
        Assert.Equal((1, RecipeRevisionKind.Published, true), (created.Number, created.Kind, created.Created));

        var revision = await RevisionAsync(site, created.Id);
        Assert.Equal(
            (rows.Recipe.Id, 1, RecipeRevisionKind.Published, RecipeSnapshotJson.CurrentVersion),
            (revision.RecipeId, revision.Number, revision.Kind, revision.SnapshotVersion));

        // The snapshot is exactly the working copy's, as the snapshot tests write it, and its hash is of those bytes.
        var expected = RecipeSnapshotJson.Write(RecipeRows.WorkingCopy(rows).ToSnapshot());
        Assert.Equal(expected.Json, revision.Snapshot);
        Assert.Equal(expected.Hash, revision.ContentHash);
        Assert.Equal(version, await VersionAsync(site, rows.Recipe.Id));
    }

    // A repeat - the same recipe frozen again as the same kind - is the revision already there, and adds nothing.
    [Fact]
    public async Task Freezing_the_same_recipe_again_adds_nothing()
    {
        var (site, rows) = await SeedRecipeAsync("rvf-repeat");
        var version = await VersionAsync(site, rows.Recipe.Id);

        var first = Succeeded(await FreezeAsync(site, rows.Recipe.Id, RecipeRevisionKind.Published, version));
        var again = Succeeded(await FreezeAsync(site, rows.Recipe.Id, RecipeRevisionKind.Published, version));

        Assert.Equal(first with { Created = false }, again);
        Assert.Equal(1, await CountAsync(site, rows.Recipe.Id));
    }

    // Each kind is compared with its own: a draft and a published revision of the same recipe are two revisions, numbered
    // together, and a later draft of the same recipe repeats the draft.
    [Fact]
    public async Task Drafts_and_published_revisions_are_numbered_together_and_repeat_their_own_kind()
    {
        var (site, rows) = await SeedRecipeAsync("rvf-kinds");
        var version = await VersionAsync(site, rows.Recipe.Id);

        var draft = Succeeded(await FreezeAsync(site, rows.Recipe.Id, RecipeRevisionKind.Draft, version));
        var published = Succeeded(await FreezeAsync(site, rows.Recipe.Id, RecipeRevisionKind.Published, version));
        var draftAgain = Succeeded(await FreezeAsync(site, rows.Recipe.Id, RecipeRevisionKind.Draft, version));

        Assert.Equal((1, RecipeRevisionKind.Draft, true), (draft.Number, draft.Kind, draft.Created));
        Assert.Equal((2, RecipeRevisionKind.Published, true), (published.Number, published.Kind, published.Created));
        Assert.Equal(draft with { Created = false }, draftAgain);
        Assert.Equal((await RevisionAsync(site, draft.Id)).ContentHash, (await RevisionAsync(site, published.Id)).ContentHash);
    }

    // RCP-007, the core of it: editing a published recipe changes the working copy and nothing a reader was shown. The
    // published revision is byte for byte what it was, and the next publish is a new revision.
    [Fact]
    public async Task Editing_after_publishing_leaves_the_published_revision_exactly_as_it_was()
    {
        var (site, rows) = await SeedRecipeAsync("rvf-edit");
        var first = Succeeded(await FreezeAsync(site, rows.Recipe.Id, RecipeRevisionKind.Published, await VersionAsync(site, rows.Recipe.Id)));
        var before = await RevisionAsync(site, first.Id);

        await EditAsync(site, rows, db =>
        {
            db.Set<RecipeIngredient>().Single(i => i.Id == rows.Ingredient.Id).OriginalText = "3 cups all-purpose flour, sifted";
            db.Set<Recipe>().Single(r => r.Id == rows.Recipe.Id).Title = "Apple pie, the big one";
        });

        var after = await RevisionAsync(site, first.Id);
        Assert.Equal(before.Snapshot, after.Snapshot);
        Assert.Equal(before.ContentHash, after.ContentHash);
        var frozen = RecipeSnapshotJson.Read(after.SnapshotVersion, after.Snapshot);
        Assert.Equal("Apple pie", frozen.Title);
        Assert.Equal("2½ cups all-purpose flour, sifted", frozen.IngredientGroups.Single().Lines.Single().OriginalText);

        var second = Succeeded(await FreezeAsync(site, rows.Recipe.Id, RecipeRevisionKind.Published, await VersionAsync(site, rows.Recipe.Id)));
        Assert.Equal((2, true), (second.Number, second.Created));
        var edited = RecipeSnapshotJson.Read(1, (await RevisionAsync(site, second.Id)).Snapshot);
        Assert.Equal("Apple pie, the big one", edited.Title);
        Assert.Equal("3 cups all-purpose flour, sifted", edited.IngredientGroups.Single().Lines.Single().OriginalText);
    }

    // A repeat is only of the latest of the kind: a recipe edited and then put back as it was is published again, since
    // what readers see changed twice.
    [Fact]
    public async Task Putting_a_recipe_back_as_it_was_is_a_new_revision()
    {
        var (site, rows) = await SeedRecipeAsync("rvf-back");
        var first = Succeeded(await FreezeAsync(site, rows.Recipe.Id, RecipeRevisionKind.Published, await VersionAsync(site, rows.Recipe.Id)));
        await EditAsync(site, rows, db => db.Set<Recipe>().Single(r => r.Id == rows.Recipe.Id).Title = "Pie");
        Succeeded(await FreezeAsync(site, rows.Recipe.Id, RecipeRevisionKind.Published, await VersionAsync(site, rows.Recipe.Id)));
        await EditAsync(site, rows, db => db.Set<Recipe>().Single(r => r.Id == rows.Recipe.Id).Title = "Apple pie");

        var third = Succeeded(await FreezeAsync(site, rows.Recipe.Id, RecipeRevisionKind.Published, await VersionAsync(site, rows.Recipe.Id)));

        Assert.Equal((3, true), (third.Number, third.Created));
        Assert.Equal((await RevisionAsync(site, first.Id)).ContentHash, (await RevisionAsync(site, third.Id)).ContentHash);
    }

    // Template D: no silent last-write-wins. A caller who read the recipe before someone else changed it is refused, and
    // nothing is frozen with edits it has not seen.
    [Fact]
    public async Task A_stale_version_is_refused_and_nothing_is_saved()
    {
        var (site, rows) = await SeedRecipeAsync("rvf-stale");
        var stale = await VersionAsync(site, rows.Recipe.Id);
        await EditAsync(site, rows, db => db.Set<Recipe>().Single(r => r.Id == rows.Recipe.Id).Title = "Changed elsewhere");

        var result = await FreezeAsync(site, rows.Recipe.Id, RecipeRevisionKind.Published, stale);

        Assert.Same(RecipeErrors.RecipeChanged, result.Error);
        Assert.Equal(0, await CountAsync(site, rows.Recipe.Id));
    }

    // A repeat is still refused when stale: the caller's view is checked first, whatever is already frozen.
    [Fact]
    public async Task A_stale_version_is_refused_even_when_the_content_is_already_frozen()
    {
        var (site, rows) = await SeedRecipeAsync("rvf-stale-repeat");
        var stale = await VersionAsync(site, rows.Recipe.Id);
        await EditAsync(site, rows, db => db.Set<Recipe>().Single(r => r.Id == rows.Recipe.Id).Summary = "Changed elsewhere");
        Succeeded(await FreezeAsync(site, rows.Recipe.Id, RecipeRevisionKind.Published, await VersionAsync(site, rows.Recipe.Id)));

        Assert.Same(RecipeErrors.RecipeChanged, (await FreezeAsync(site, rows.Recipe.Id, RecipeRevisionKind.Published, stale)).Error);
        Assert.Equal(1, await CountAsync(site, rows.Recipe.Id));
    }

    [Fact]
    public async Task An_unknown_recipe_is_not_found()
    {
        var world = await _harness.SeedAsync("rvf-unknown");

        var result = await FreezeAsync(world.A.Id, new RecipeId(Guid.NewGuid()), RecipeRevisionKind.Published, new byte[8]);

        Assert.Same(RecipeErrors.RecipeNotFound, result.Error);
    }

    public static TheoryData<string, CreateRecipeRevisionRequest> Invalid => new()
    {
        { "recipeId", new CreateRecipeRevisionRequest(default, RecipeRevisionKind.Published, new byte[8]) },
        { "kind", new CreateRecipeRevisionRequest(new RecipeId(Guid.NewGuid()), 0, new byte[8]) },
        { "kind", new CreateRecipeRevisionRequest(new RecipeId(Guid.NewGuid()), (RecipeRevisionKind)3, new byte[8]) },
        { "expectedVersion", new CreateRecipeRevisionRequest(new RecipeId(Guid.NewGuid()), RecipeRevisionKind.Draft, null) },
        { "expectedVersion", new CreateRecipeRevisionRequest(new RecipeId(Guid.NewGuid()), RecipeRevisionKind.Draft, new byte[7]) },
        { "expectedVersion", new CreateRecipeRevisionRequest(new RecipeId(Guid.NewGuid()), RecipeRevisionKind.Draft, new byte[9]) },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task A_request_that_breaks_a_rule_is_refused_before_anything_is_read(string field, CreateRecipeRevisionRequest request)
    {
        var world = await _harness.SeedAsync("rvf-invalid");
        await using var scope = _harness.In(world.A.Id);

        var result = await Facade(scope).CreateRevisionAsync(request, CancellationToken.None);

        Assert.Equal(ErrorKind.Validation, result.Error?.Kind);
        Assert.Contains(field, result.Error!.Fields.Keys);
    }

    // Site work without a site is a bug, never an empty answer (tenancy.md rule 2).
    [Fact]
    public async Task Freezing_with_no_site_entered_is_a_bug()
    {
        await using var scope = _harness.Scope();

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Facade(scope).CreateRevisionAsync(
            new CreateRecipeRevisionRequest(new RecipeId(Guid.NewGuid()), RecipeRevisionKind.Published, new byte[8]),
            CancellationToken.None));
    }

    // RCP-002: the blogger's wording reaches the revision byte for byte, through the snapshot's escaping and the column.
    [Fact]
    public async Task The_bloggers_wording_is_frozen_exactly()
    {
        var (site, rows) = await SeedRecipeAsync("rvf-words");
        await EditAsync(site, rows, db =>
        {
            for (var i = 0; i < RecipeSchemaTests.Wording.Count; i++)
            {
                db.Add(new RecipeIngredient
                {
                    Id = new RecipeIngredientId(Guid.NewGuid()),
                    GroupId = rows.IngredientGroup.Id,
                    Position = i + 1,
                    OriginalText = RecipeSchemaTests.Wording[i],
                    ParseStatus = IngredientParseStatus.Unparsed,
                });
            }
        });

        var created = Succeeded(await FreezeAsync(site, rows.Recipe.Id, RecipeRevisionKind.Published, await VersionAsync(site, rows.Recipe.Id)));

        var frozen = RecipeSnapshotJson.Read(1, (await RevisionAsync(site, created.Id)).Snapshot);
        Assert.Equal(RecipeSchemaTests.Wording, frozen.IngredientGroups.Single().Lines.Skip(1).Select(l => l.OriginalText));
    }

    // A commit that fails before it reaches the server is retried, and the retry saves the one revision, under its number.
    [Fact]
    public async Task A_failed_commit_is_retried_into_one_revision()
    {
        var fault = new PlantedCommitFault();
        await using var retrying = await TwoSiteHarness.CreateAsync(database, configure: fault.Configure);
        var (site, rows) = await SeedRecipeAsync("rvf-retry");
        var version = await VersionAsync(site, rows.Recipe.Id);

        await using var scope = retrying.In(site);
        var result = await Facade(scope).CreateRevisionAsync(
            new CreateRecipeRevisionRequest(rows.Recipe.Id, RecipeRevisionKind.Published, version),
            CancellationToken.None);

        Assert.Equal((1, true), (Succeeded(result).Number, Succeeded(result).Created));
        Assert.Equal(2, fault.Attempts);
        Assert.Equal(1, await CountAsync(site, rows.Recipe.Id));
        Assert.DoesNotContain(
            scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().ChangeTracker.Entries<RecipeRevision>(),
            e => e.State != EntityState.Unchanged);
    }

    // A commit that fails for good rolls everything back: no revision, nothing left staged for a later save, and a later
    // freeze saves revision 1 as if the first had never been tried.
    [Fact]
    public async Task A_failed_commit_leaves_nothing_behind()
    {
        var fault = new FailingCommit();
        await using var failing = await TwoSiteHarness.CreateAsync(database, configure: o => o.AddInterceptors(fault));
        var (site, rows) = await SeedRecipeAsync("rvf-rollback");
        var version = await VersionAsync(site, rows.Recipe.Id);

        await using (var scope = failing.In(site))
        {
            await Assert.ThrowsAsync<FailingCommit.Fault>(() => Facade(scope).CreateRevisionAsync(
                new CreateRecipeRevisionRequest(rows.Recipe.Id, RecipeRevisionKind.Published, version),
                CancellationToken.None));
            Assert.Empty(scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().ChangeTracker.Entries());
        }

        Assert.Equal(0, await CountAsync(site, rows.Recipe.Id));

        var later = Succeeded(await FreezeAsync(site, rows.Recipe.Id, RecipeRevisionKind.Published, version));
        Assert.Equal((1, true), (later.Number, later.Created));
    }

    // Freezes of one recipe take turns on its lock: started together, they never take one number twice, and two of the same
    // kind and content end as one revision and its repeat.
    [Fact]
    public async Task Freezes_started_together_take_turns()
    {
        var (site, rows) = await SeedRecipeAsync("rvf-race");
        var version = await VersionAsync(site, rows.Recipe.Id);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(i => Task.Run(() => FreezeAsync(
                site, rows.Recipe.Id, i % 2 == 0 ? RecipeRevisionKind.Draft : RecipeRevisionKind.Published, version))));

        var created = results.Select(Succeeded).ToList();
        Assert.Equal(2, created.Count(c => c.Created));
        Assert.Equal([1, 2], created.Where(c => c.Created).Select(c => c.Number).Order());
        Assert.All(created.GroupBy(c => c.Kind), kind => Assert.Single(kind.Select(c => c.Id).Distinct()));
        Assert.Equal(2, await CountAsync(site, rows.Recipe.Id));
    }

    // An edit holding the recipe's row makes a freeze wait for it; once it commits, the freeze sees a recipe the caller did
    // not read and refuses it. Without the lock, the freeze would read the version from before the edit and save it.
    [Fact]
    public async Task A_freeze_waits_for_an_edit_holding_the_recipe_and_then_refuses_the_stale_read()
    {
        var (site, rows) = await SeedRecipeAsync("rvf-wait");
        var version = await VersionAsync(site, rows.Recipe.Id);

        await using var editor = _harness.In(site);
        var editorDb = editor.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        await using var transaction = await editorDb.Database.BeginTransactionAsync();
        await editorDb.Set<Recipe>().Where(r => r.Id == rows.Recipe.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.Title, "Edited while frozen"));

        var freeze = FreezeAsync(site, rows.Recipe.Id, RecipeRevisionKind.Published, version);
        Assert.NotSame(freeze, await Task.WhenAny(freeze, Task.Delay(TimeSpan.FromMilliseconds(750))));
        await transaction.CommitAsync();

        Assert.Same(RecipeErrors.RecipeChanged, (await freeze).Error);
        Assert.Equal(0, await CountAsync(site, rows.Recipe.Id));
    }

    // An edit that has changed a part but not yet the recipe's row cannot commit until the freeze does, so the freeze reads
    // the last committed recipe, whole - never the edit's half - and the edit then lands after it.
    [Fact]
    public async Task A_freeze_never_takes_half_of_an_edit()
    {
        var (site, rows) = await SeedRecipeAsync("rvf-half");
        var version = await VersionAsync(site, rows.Recipe.Id);

        await using var editor = _harness.In(site);
        var editorDb = editor.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        await using var transaction = await editorDb.Database.BeginTransactionAsync();
        await editorDb.Set<RecipeIngredient>().Where(i => i.Id == rows.Ingredient.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.Note, "half of an edit"));

        var created = Succeeded(await FreezeAsync(site, rows.Recipe.Id, RecipeRevisionKind.Published, version));

        // The edit's second half, the recipe's row, waits for the freeze's lock - long gone - and then commits.
        await editorDb.Set<Recipe>().Where(r => r.Id == rows.Recipe.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.UpdatedAt, r => r.UpdatedAt.AddMinutes(1)));
        await transaction.CommitAsync();

        var frozen = RecipeSnapshotJson.Read(1, (await RevisionAsync(site, created.Id)).Snapshot);
        Assert.Equal("sifted", frozen.IngredientGroups.Single().Lines.Single().Note);
        Assert.NotEqual(version, await VersionAsync(site, rows.Recipe.Id));
    }

    // From review (test-gap-analyzer): a commit that reached the server but whose answer was lost is retried, and the retry
    // meets its own revision under the lock - the one saved, answered as created, never a second row or a "repeat".
    [Fact]
    public async Task A_commit_that_landed_but_lost_its_answer_is_retried_into_the_same_revision()
    {
        var fault = new LandedCommitFault();
        await using var retrying = await TwoSiteHarness.CreateAsync(database, configure: fault.Configure);
        var (site, rows) = await SeedRecipeAsync("rvf-landed");
        var version = await VersionAsync(site, rows.Recipe.Id);

        await using var scope = retrying.In(site);
        var created = Succeeded(await Facade(scope).CreateRevisionAsync(
            new CreateRecipeRevisionRequest(rows.Recipe.Id, RecipeRevisionKind.Published, version),
            CancellationToken.None));

        Assert.Equal((1, true), (created.Number, created.Created));
        Assert.Equal(2, fault.Attempts);
        Assert.Equal(1, await CountAsync(site, rows.Recipe.Id));
        Assert.Equal(created.Id, (await RevisionAsync(site, created.Id)).Id);
        Assert.DoesNotContain(
            scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().ChangeTracker.Entries<RecipeRevision>(),
            e => e.State == EntityState.Added);
    }

    // From review: every nutrient is frozen under its own name, from its own column (RCP-006). Each holds a different value,
    // so two swapped anywhere between the column and the snapshot would show.
    [Fact]
    public async Task Every_nutrition_column_is_frozen_under_its_own_name()
    {
        var (site, rows) = await SeedRecipeAsync("rvf-nutrition");
        await EditAsync(site, rows, db =>
        {
            var recipe = db.Set<Recipe>().Single(r => r.Id == rows.Recipe.Id);
            (recipe.Calories, recipe.FatGrams, recipe.SaturatedFatGrams, recipe.TransFatGrams) = (1.01m, 2.02m, 3.03m, 4.04m);
            (recipe.UnsaturatedFatGrams, recipe.CholesterolMilligrams, recipe.SodiumMilligrams) = (5.05m, 6.06m, 7.07m);
            (recipe.CarbohydrateGrams, recipe.FiberGrams, recipe.SugarGrams, recipe.ProteinGrams) = (8.08m, 9.09m, 10.1m, 1_234_567.89m);
            recipe.ServingSize = "1 wedge";
        });

        var created = Succeeded(await FreezeAsync(site, rows.Recipe.Id, RecipeRevisionKind.Published, await VersionAsync(site, rows.Recipe.Id)));

        var frozen = RecipeSnapshotJson.Read(1, (await RevisionAsync(site, created.Id)).Snapshot);
        Assert.Equal(new RecipeSnapshotNutrition("1 wedge", 101, 202, 303, 404, 505, 606, 707, 808, 909, 1_010, 123_456_789), frozen.Nutrition);
    }

    // From review: a site holds many recipes. Freezing one takes its own parts and none of a sibling's, by every join.
    [Fact]
    public async Task Freezing_one_recipe_takes_only_its_own_parts_beside_a_sibling()
    {
        var world = await _harness.SeedAsync("rvf-sibling");
        RecipeRowSet first = null!, second = null!;
        await _harness.ChangeAsync(world.A.Id, db =>
        {
            first = RecipeRows.Add(db);
            second = RecipeRows.Add(db);
            second.Recipe.Title = "Plum tart";
            second.Ingredient.OriginalText = "6 plums, halved";
            second.Ingredient.Note = "halved";
            second.Ingredient.Item = "plums";
            second.Ingredient.Quantity = Quantity.Amount(6);
            second.Ingredient.QuantityLength = 1;
            (second.Ingredient.UnitCode, second.Ingredient.UnitStart, second.Ingredient.UnitLength) = (null, null, null);
            second.Step.Text = "Lay the plums on the pastry.";
            second.Keyword.Text = "summer baking";
        });

        foreach (var rows in new[] { first, second })
        {
            var created = Succeeded(await FreezeAsync(world.A.Id, rows.Recipe.Id, RecipeRevisionKind.Published, await VersionAsync(world.A.Id, rows.Recipe.Id)));
            Assert.Equal(1, created.Number);
            Assert.Equal(
                RecipeSnapshotJson.Write(RecipeRows.WorkingCopy(rows).ToSnapshot()).Json,
                (await RevisionAsync(world.A.Id, created.Id)).Snapshot);
        }
    }

    // A save that moves the version but changes nothing a reader sees is a repeat, not a new revision.
    [Fact]
    public async Task A_new_version_with_the_same_content_is_a_repeat()
    {
        var (site, rows) = await SeedRecipeAsync("rvf-same");
        var first = Succeeded(await FreezeAsync(site, rows.Recipe.Id, RecipeRevisionKind.Published, await VersionAsync(site, rows.Recipe.Id)));
        await EditAsync(site, rows, _ => { });

        var again = Succeeded(await FreezeAsync(site, rows.Recipe.Id, RecipeRevisionKind.Published, await VersionAsync(site, rows.Recipe.Id)));

        Assert.Equal(first with { Created = false }, again);
        Assert.Equal(1, await CountAsync(site, rows.Recipe.Id));
    }

    // From review: a revision is dated by the application clock (backend.md), never the system's.
    [Fact]
    public async Task A_revision_is_dated_by_the_clock()
    {
        var now = new DateTimeOffset(2026, 11, 2, 8, 30, 15, TimeSpan.Zero);
        await using var dated = await TwoSiteHarness.CreateAsync(
            database,
            domainServices: s => s.AddSingleton<PantryPress.Domain.Managers.Time.IClock>(new FixedClock(now)));
        var (site, rows) = await SeedRecipeAsync("rvf-clock");

        await using var scope = dated.In(site);
        var created = Succeeded(await Facade(scope).CreateRevisionAsync(
            new CreateRecipeRevisionRequest(rows.Recipe.Id, RecipeRevisionKind.Draft, await VersionAsync(site, rows.Recipe.Id)),
            CancellationToken.None));

        Assert.Equal(now, (await RevisionAsync(site, created.Id)).CreatedAt);
    }

    // From review: RCP-002 through the database for every place a recipe keeps the blogger's text, not only a line's.
    [Fact]
    public async Task Every_text_field_is_frozen_exactly_through_the_database()
    {
        var words = RecipeSchemaTests.Wording.Where(w => w.Length <= RecipeLimits.MaxKeywordLength).ToList();
        var (site, rows) = await SeedRecipeAsync("rvf-every-word");
        var recipe = rows.Recipe.Id;
        await EditAsync(site, rows, db =>
        {
            var r = db.Set<Recipe>().Single(x => x.Id == recipe);
            (r.Title, r.Summary, r.YieldUnit, r.ServingSize) = (words[0], words[1], words[2][..Math.Min(words[2].Length, RecipeLimits.MaxYieldUnitLength)], words[3]);
            db.Set<RecipeIngredientGroup>().Single(g => g.RecipeId == recipe).Heading = words[4];
            db.Set<RecipeStepGroup>().Single(g => g.RecipeId == recipe).Heading = words[5];
            var line = db.Set<RecipeIngredient>().Single(i => i.Id == rows.Ingredient.Id);
            (line.Item, line.Note) = (words[6], words[7]);
            db.Set<RecipeStep>().Single(s => s.Id == rows.Step.Id).Text = words[8];
            db.Remove(db.Set<RecipeEquipment>().Single(e => e.RecipeId == recipe));
            db.Remove(db.Set<RecipeNote>().Single(n => n.RecipeId == recipe));
            db.Remove(db.Set<RecipeKeyword>().Single(k => k.RecipeId == recipe));
            // From position 1, so no new row shares a key with the one removed in the same save.
            for (var i = 0; i < words.Count; i++)
            {
                db.Add(new RecipeEquipment { RecipeId = recipe, Position = i + 1, Text = words[i] });
                db.Add(new RecipeNote { RecipeId = recipe, Position = i + 1, Text = words[i] });
                db.Add(new RecipeKeyword { RecipeId = recipe, Position = i + 1, Text = words[i] });
            }
        });

        var created = Succeeded(await FreezeAsync(site, recipe, RecipeRevisionKind.Published, await VersionAsync(site, recipe)));

        var frozen = RecipeSnapshotJson.Read(1, (await RevisionAsync(site, created.Id)).Snapshot);
        Assert.Equal((words[0], words[1], words[3]), (frozen.Title, frozen.Summary, frozen.Nutrition.ServingSize));
        Assert.Equal(words[4], frozen.IngredientGroups.Single().Heading);
        Assert.Equal(words[5], frozen.StepGroups.Single().Heading);
        Assert.Equal((words[6], words[7]), (frozen.IngredientGroups.Single().Lines.Single().Item, frozen.IngredientGroups.Single().Lines.Single().Note));
        Assert.Equal(words[8], frozen.StepGroups.Single().Steps.Single().Text);
        Assert.Equal(words, frozen.Equipment);
        Assert.Equal(words, frozen.Notes);
        Assert.Equal(words, frozen.Keywords);
    }

    // From review: a lone surrogate the column holds - planted past every .NET rule, as only raw SQL could - never reaches
    // .NET as itself: SqlClient reads it back as U+FFFD (found at PP4.4). So the snapshot's refusal guards only text that
    // never passed through the column, and the column's replacement is what a freeze then keeps, exactly as the working copy
    // reads it. Keeping the blogger's words is the input's job: the recipe routes, import, and the publishing API refuse
    // ill-formed text before it is stored (PP4.11, PP18, PP20).
    [Fact]
    public async Task A_lone_surrogate_planted_in_a_column_is_read_back_replaced_and_frozen_as_read()
    {
        var (site, rows) = await SeedRecipeAsync("rvf-lone");
        await using (var plant = _harness.Scope())
        {
            await plant.ServiceProvider.GetRequiredService<PantryPressDbContext>().Database.ExecuteSqlRawAsync(
                "UPDATE [RecipeSteps] SET [Text] = N'half ' + NCHAR(55356) + N' a moon' WHERE [SiteId] = @site AND [Id] = @id",
                new SqlParameter("@site", site.Value),
                new SqlParameter("@id", rows.Step.Id.Value));
        }

        var replaced = string.Concat("half ", (char)0xFFFD, " a moon");
        await using var scope = _harness.In(site);
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        Assert.Equal(replaced, await db.Set<RecipeStep>().AsNoTracking().Where(s => s.Id == rows.Step.Id).Select(s => s.Text).SingleAsync());

        var created = Succeeded(await Facade(scope).CreateRevisionAsync(
            new CreateRecipeRevisionRequest(rows.Recipe.Id, RecipeRevisionKind.Published, await VersionAsync(site, rows.Recipe.Id)),
            CancellationToken.None));

        Assert.Equal(replaced, RecipeSnapshotJson.Read(1, (await RevisionAsync(site, created.Id)).Snapshot).StepGroups.Single().Steps.Single().Text);
    }

    // From review: a failure inside an attempt - not at its commit - rolls everything back and leaves nothing staged for a
    // later save on the same context.
    [Fact]
    public async Task An_attempt_that_fails_after_staging_leaves_nothing_behind()
    {
        var (site, rows) = await SeedRecipeAsync("rvf-attempt");
        await using var scope = _harness.In(site);
        var data = scope.ServiceProvider.GetRequiredService<PantryPress.Domain.Modules.Recipes.DataLayer.RecipeRevisionsDataLayer>();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => data.RunLockedAsync<int>(
            async token =>
            {
                Assert.NotNull(await data.FindLockedAsync(rows.Recipe.Id, token));
                data.StageRevision(RecipeRevisionSchemaTests.NewRevision(rows.Recipe.Id, 1, "{}"));
                throw new InvalidOperationException("A rule that failed after staging.");
            },
            CancellationToken.None));

        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(0, await CountAsync(site, rows.Recipe.Id));
    }

    // An attempt that answers without staging commits nothing, and leaves nothing tracked from its reads.
    [Fact]
    public async Task An_attempt_that_stages_nothing_saves_nothing()
    {
        var (site, rows) = await SeedRecipeAsync("rvf-nothing");
        await using var scope = _harness.In(site);
        var data = scope.ServiceProvider.GetRequiredService<PantryPress.Domain.Modules.Recipes.DataLayer.RecipeRevisionsDataLayer>();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        var before = await VersionAsync(site, rows.Recipe.Id);

        var answer = await data.RunLockedAsync(
            async token => (await data.FindLockedAsync(rows.Recipe.Id, token))?.Title,
            CancellationToken.None);

        Assert.Equal("Apple pie", answer);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(before, await VersionAsync(site, rows.Recipe.Id));
        Assert.Equal(0, await CountAsync(site, rows.Recipe.Id));
    }

    [Fact]
    public async Task A_cancelled_freeze_leaves_nothing()
    {
        var (site, rows) = await SeedRecipeAsync("rvf-cancel");
        var request = new CreateRecipeRevisionRequest(rows.Recipe.Id, RecipeRevisionKind.Published, await VersionAsync(site, rows.Recipe.Id));
        await using var scope = _harness.In(site);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Facade(scope).CreateRevisionAsync(request, cancelled.Token));

        Assert.Empty(scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().ChangeTracker.Entries());
        Assert.Equal(0, await CountAsync(site, rows.Recipe.Id));
    }

    private static RecipeRevisionCreated Succeeded(Result<RecipeRevisionCreated> result)
    {
        Assert.True(result.IsSuccess, result.Error?.Code);
        return result.Value;
    }

    private static IRecipeRevisionsFacade Facade(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IRecipeRevisionsFacade>();

    private async Task<Result<RecipeRevisionCreated>> FreezeAsync(SiteId site, RecipeId recipe, RecipeRevisionKind kind, byte[] version)
    {
        await using var scope = _harness.In(site);
        return await Facade(scope).CreateRevisionAsync(new CreateRecipeRevisionRequest(recipe, kind, version), CancellationToken.None);
    }

    private async Task<(SiteId Site, RecipeRowSet Rows)> SeedRecipeAsync(string prefix)
    {
        var world = await _harness.SeedAsync(prefix);
        RecipeRowSet rows = null!;
        await _harness.ChangeAsync(world.A.Id, db => rows = RecipeRows.Add(db));
        return (world.A.Id, rows);
    }

    // An edit as PP4.11 owes it: the parts and the recipe's row in one save, so the recipe's version moves with every change.
    private async Task EditAsync(SiteId site, RecipeRowSet rows, Action<PantryPressDbContext> edit) =>
        await _harness.ChangeAsync(site, db =>
        {
            edit(db);
            var recipe = db.Set<Recipe>().Single(r => r.Id == rows.Recipe.Id);
            recipe.UpdatedAt = recipe.UpdatedAt.AddMinutes(1);
        });

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

    private sealed class FixedClock(DateTimeOffset now) : PantryPress.Domain.Managers.Time.IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    // A commit that reaches the server and then loses its answer once, as a dropped connection would, with a strategy that
    // retries exactly that fault once.
    private sealed class LandedCommitFault : Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor
    {
        public int Attempts { get; private set; }

        public void Configure(DbContextOptionsBuilder options)
        {
            new Microsoft.EntityFrameworkCore.Infrastructure.SqlServerDbContextOptionsBuilder(options).ExecutionStrategy(d => new RetryOnce(d));
            options.AddInterceptors(this);
        }

        public override Task TransactionCommittedAsync(
            System.Data.Common.DbTransaction transaction,
            Microsoft.EntityFrameworkCore.Diagnostics.TransactionEndEventData eventData,
            CancellationToken cancellationToken = default) =>
            ++Attempts == 1 ? throw new Lost() : Task.CompletedTask;

        private sealed class Lost() : Exception("A commit that landed, and whose answer was lost.");

        private sealed class RetryOnce(Microsoft.EntityFrameworkCore.Storage.ExecutionStrategyDependencies dependencies)
            : Microsoft.EntityFrameworkCore.Storage.ExecutionStrategy(dependencies, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
        {
            protected override bool ShouldRetryOn(Exception exception) => exception is Lost;
        }
    }

    // A commit that always fails, before it reaches the server, with no retry strategy to try again.
    private sealed class FailingCommit : Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult> TransactionCommittingAsync(
            System.Data.Common.DbTransaction transaction,
            Microsoft.EntityFrameworkCore.Diagnostics.TransactionEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            throw new Fault();

        public sealed class Fault() : Exception("A commit that failed before it reached the server.");
    }
}
