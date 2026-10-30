using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Recipes;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.Tests.Persistence;
using PantryPress.Tests.Recipes;

namespace PantryPress.Tests.TwoSite;

// PP4.3: a recipe and every part of it are site-owned (PP-D06), in the two-site world (tenancy.md, "Two-site test"). Each of
// the twelve tables owes the floor - the filter, the stamp, and the token hold through its set - and the parts owe what
// the keys add: no part can be filed under another site's recipe or group, even by an id that site really has, and a
// delete on one site never reaches the other's rows, even where the two hold the very same ids. No route or repository
// reads these tables yet; their two-site tests come with PP4.4 and PP4.11.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class RecipesTwoSiteTests(MigratedDatabase database) : IAsyncLifetime
{
    private const int ReferenceViolation = 547;

    private TwoSiteHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await TwoSiteHarness.CreateAsync(database);

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task A_recipe_is_isolated() => await IsolatedAsync<Recipe>(
        "rcp-iso-recipe",
        db => RecipeRows.Add(db).Recipe,
        set => set.SetProperty(r => r.Title, "Changed"),
        r => r.Title = "Changed");

    [Fact]
    public async Task An_ingredient_group_is_isolated() => await IsolatedAsync<RecipeIngredientGroup>(
        "rcp-iso-igroup",
        db => RecipeRows.Add(db).IngredientGroup,
        set => set.SetProperty(g => g.Heading, "Changed"),
        g => g.Heading = "Changed");

    [Fact]
    public async Task An_ingredient_line_is_isolated() => await IsolatedAsync<RecipeIngredient>(
        "rcp-iso-line",
        db => RecipeRows.Add(db).Ingredient,
        set => set.SetProperty(i => i.Note, "Changed"),
        i => i.Note = "Changed");

    [Fact]
    public async Task A_step_group_is_isolated() => await IsolatedAsync<RecipeStepGroup>(
        "rcp-iso-sgroup",
        db => RecipeRows.Add(db).StepGroup,
        set => set.SetProperty(g => g.Heading, "Changed"),
        g => g.Heading = "Changed");

    [Fact]
    public async Task A_step_is_isolated() => await IsolatedAsync<RecipeStep>(
        "rcp-iso-step",
        db => RecipeRows.Add(db).Step,
        set => set.SetProperty(s => s.Text, "Changed"),
        s => s.Text = "Changed");

    [Fact]
    public async Task Equipment_is_isolated() => await IsolatedAsync<RecipeEquipment>(
        "rcp-iso-equip",
        db => RecipeRows.Add(db).Equipment,
        set => set.SetProperty(e => e.Text, "Changed"),
        e => e.Text = "Changed");

    [Fact]
    public async Task A_note_is_isolated() => await IsolatedAsync<RecipeNote>(
        "rcp-iso-note",
        db => RecipeRows.Add(db).Note,
        set => set.SetProperty(n => n.Text, "Changed"),
        n => n.Text = "Changed");

    [Fact]
    public async Task A_keyword_is_isolated() => await IsolatedAsync<RecipeKeyword>(
        "rcp-iso-keyword",
        db => RecipeRows.Add(db).Keyword,
        set => set.SetProperty(k => k.Text, "Changed"),
        k => k.Text = "Changed");

    [Fact]
    public async Task A_course_is_isolated() => await IsolatedAsync<RecipeCourse>(
        "rcp-iso-course",
        db => RecipeRows.Add(db).Course,
        set => set.SetProperty(c => c.Position, 9),
        c => c.Position = 9);

    [Fact]
    public async Task A_cuisine_is_isolated() => await IsolatedAsync<RecipeCuisine>(
        "rcp-iso-cuisine",
        db => RecipeRows.Add(db).Cuisine,
        set => set.SetProperty(c => c.Position, 9),
        c => c.Position = 9);

    [Fact]
    public async Task A_diet_is_isolated() => await IsolatedAsync<RecipeDiet>(
        "rcp-iso-diet",
        db => RecipeRows.Add(db).Diet,
        set => set.SetProperty(d => d.Position, 9),
        d => d.Position = 9);

    [Fact]
    public async Task An_allergen_is_isolated() => await IsolatedAsync<RecipeAllergen>(
        "rcp-iso-allergen",
        db => RecipeRows.Add(db).Allergen,
        set => set.SetProperty(a => a.Position, 9),
        a => a.Position = 9);

    // Working as B, a part naming A's recipe or group - an id A really has - is stamped B's and so names a parent B does not
    // have: the parent's composite key refuses it, by name, and A's recipe is untouched. The codes are real ones, so no
    // vocabulary key is what refuses.
    public static TheoryData<string, string> Parts => new()
    {
        { "ingredient-group", "FK_RecipeIngredientGroups_Recipes_SiteId_RecipeId" },
        { "ingredient", "FK_RecipeIngredients_RecipeIngredientGroups_SiteId_GroupId" },
        { "step-group", "FK_RecipeStepGroups_Recipes_SiteId_RecipeId" },
        { "step", "FK_RecipeSteps_RecipeStepGroups_SiteId_GroupId" },
        { "equipment", "FK_RecipeEquipment_Recipes_SiteId_RecipeId" },
        { "note", "FK_RecipeNotes_Recipes_SiteId_RecipeId" },
        { "keyword", "FK_RecipeKeywords_Recipes_SiteId_RecipeId" },
        { "course", "FK_RecipeCourses_Recipes_SiteId_RecipeId" },
        { "cuisine", "FK_RecipeCuisines_Recipes_SiteId_RecipeId" },
        { "diet", "FK_RecipeDiets_Recipes_SiteId_RecipeId" },
        { "allergen", "FK_RecipeAllergens_Recipes_SiteId_RecipeId" },
    };

    [Theory]
    [MemberData(nameof(Parts))]
    public async Task A_part_cannot_be_filed_under_another_sites_recipe(string part, string parentKey)
    {
        var world = await _harness.SeedAsync("rcp-attach");
        RecipeRowSet a = null!;
        await _harness.ChangeAsync(world.A.Id, db => a = RecipeRows.Add(db));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => _harness.ChangeAsync(world.B.Id, db => db.Add(Under(a, part))));

        AssertRefusedBy(Assert.IsType<SqlException>(error.InnerException), parentKey);
        await using var scope = _harness.In(world.A.Id);
        Assert.Equal(12, await RecipeSchemaTests.PartsOfAsync(Db(scope), a));
    }

    // Working as B, B's own part moved under A's recipe or group - by a bulk update, and, where the parent is not part of the
    // key, by a tracked change - keeps B's site and so names a parent B does not have: the same key refuses it, and both
    // recipes are as they were.
    [Theory]
    [MemberData(nameof(Parts))]
    public async Task A_part_cannot_be_moved_under_another_sites_recipe(string part, string parentKey)
    {
        var world = await _harness.SeedAsync("rcp-move");
        RecipeRowSet a = null!, b = null!;
        await _harness.ChangeAsync(world.A.Id, db => a = RecipeRows.Add(db));
        await _harness.ChangeAsync(world.B.Id, db => b = RecipeRows.Add(db));

        await using (var scope = _harness.In(world.B.Id))
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => MoveAsync(Db(scope), part, a, b));
            AssertRefusedBy(error, parentKey);
        }

        if (part is "ingredient-group" or "ingredient" or "step-group" or "step")
        {
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => _harness.ChangeAsync(world.B.Id, db =>
            {
                var (row, property, parent) = part switch
                {
                    "ingredient-group" => ((object)db.Set<RecipeIngredientGroup>().Single(g => g.Id == b.IngredientGroup.Id), "RecipeId", (object)a.Recipe.Id),
                    "ingredient" => (db.Set<RecipeIngredient>().Single(i => i.Id == b.Ingredient.Id), "GroupId", a.IngredientGroup.Id),
                    "step-group" => (db.Set<RecipeStepGroup>().Single(g => g.Id == b.StepGroup.Id), "RecipeId", a.Recipe.Id),
                    _ => (db.Set<RecipeStep>().Single(s => s.Id == b.Step.Id), "GroupId", (object)a.StepGroup.Id),
                };
                db.Entry(row).Property(property).CurrentValue = parent;
            }));
            AssertRefusedBy(Assert.IsType<SqlException>(error.InnerException), parentKey);
        }

        await using (var scope = _harness.In(world.A.Id))
        {
            Assert.Equal(12, await RecipeSchemaTests.PartsOfAsync(Db(scope), a));
        }

        await using (var scope = _harness.In(world.B.Id))
        {
            Assert.Equal(12, await RecipeSchemaTests.PartsOfAsync(Db(scope), b));
        }
    }

    // The key leads with the site, so two sites may hold a recipe and parts under the very same ids. Deleting A's - the
    // recipe and, through the cascade, every part - leaves every one of B's.
    [Fact]
    public async Task Deleting_a_recipe_on_one_site_leaves_the_other_sites_rows_with_the_same_ids()
    {
        var world = await _harness.SeedAsync("rcp-same-ids");
        var ids = (
            Recipe: new RecipeId(Guid.NewGuid()),
            IngredientGroup: new RecipeIngredientGroupId(Guid.NewGuid()),
            Ingredient: new RecipeIngredientId(Guid.NewGuid()),
            StepGroup: new RecipeStepGroupId(Guid.NewGuid()),
            Step: new RecipeStepId(Guid.NewGuid()));
        RecipeRowSet a = null!, b = null!;
        await _harness.ChangeAsync(world.A.Id, db => a = RecipeRows.Add(db, ids.Recipe, ids.IngredientGroup, ids.Ingredient, ids.StepGroup, ids.Step));
        await _harness.ChangeAsync(world.B.Id, db => b = RecipeRows.Add(db, ids.Recipe, ids.IngredientGroup, ids.Ingredient, ids.StepGroup, ids.Step));

        await using (var scope = _harness.In(world.A.Id))
        {
            Assert.Equal(1, await Db(scope).Set<Recipe>().Where(r => r.Id == ids.Recipe).ExecuteDeleteAsync());
            Assert.Equal(0, await RecipeSchemaTests.PartsOfAsync(Db(scope), a));
        }

        await using (var scope = _harness.In(world.B.Id))
        {
            Assert.Equal(12, await RecipeSchemaTests.PartsOfAsync(Db(scope), b));
            var line = await Db(scope).Set<RecipeIngredient>().AsNoTracking().SingleAsync(i => i.Id == ids.Ingredient);
            Assert.Equal(world.B.Id, line.SiteId);
            Assert.Equal(b.Ingredient.OriginalText, line.OriginalText);
        }
    }

    // Working as B, a bulk delete by the recipe id both sites share reaches B's own row alone, and one by A's line and group
    // ids - which B does not have - reaches nothing.
    [Fact]
    public async Task A_bulk_delete_by_a_shared_id_reaches_only_the_entered_site()
    {
        var world = await _harness.SeedAsync("rcp-bulk");
        var id = new RecipeId(Guid.NewGuid());
        RecipeRowSet a = null!;
        await _harness.ChangeAsync(world.A.Id, db => a = RecipeRows.Add(db, id));
        await _harness.ChangeAsync(world.B.Id, db => RecipeRows.Add(db, id));

        await using (var scope = _harness.In(world.B.Id))
        {
            Assert.Equal(1, await Db(scope).Set<RecipeKeyword>().Where(k => k.RecipeId == id).ExecuteDeleteAsync());
            Assert.Equal(0, await Db(scope).Set<RecipeIngredient>().Where(i => i.Id == a.Ingredient.Id || i.GroupId == a.IngredientGroup.Id).ExecuteDeleteAsync());
        }

        await using (var scope = _harness.In(world.A.Id))
        {
            Assert.Equal(12, await RecipeSchemaTests.PartsOfAsync(Db(scope), a));
        }
    }

    private async Task IsolatedAsync<T>(
        string prefix,
        Func<PantryPressDbContext, T> make,
        Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<T>> touch,
        Action<T> change)
        where T : class, ISiteOwned
    {
        var world = await _harness.SeedAsync(prefix);
        await TwoSiteChecks.AssertSiteOwnedIsolatedAsync(_harness.Services, world.A.Id, world.B.Id, db => Task.FromResult(make(db)), touch, change);
    }

    private static Task<int> MoveAsync(PantryPressDbContext db, string part, RecipeRowSet a, RecipeRowSet b)
    {
        var (to, from) = (a.Recipe.Id, b.Recipe.Id);
        return part switch
        {
            "ingredient-group" => db.Set<RecipeIngredientGroup>().Where(g => g.Id == b.IngredientGroup.Id).ExecuteUpdateAsync(s => s.SetProperty(g => g.RecipeId, to)),
            "ingredient" => db.Set<RecipeIngredient>().Where(i => i.Id == b.Ingredient.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.GroupId, a.IngredientGroup.Id)),
            "step-group" => db.Set<RecipeStepGroup>().Where(g => g.Id == b.StepGroup.Id).ExecuteUpdateAsync(s => s.SetProperty(g => g.RecipeId, to)),
            "step" => db.Set<RecipeStep>().Where(s => s.Id == b.Step.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.GroupId, a.StepGroup.Id)),
            "equipment" => db.Set<RecipeEquipment>().Where(e => e.RecipeId == from).ExecuteUpdateAsync(s => s.SetProperty(e => e.RecipeId, to)),
            "note" => db.Set<RecipeNote>().Where(n => n.RecipeId == from).ExecuteUpdateAsync(s => s.SetProperty(n => n.RecipeId, to)),
            "keyword" => db.Set<RecipeKeyword>().Where(k => k.RecipeId == from).ExecuteUpdateAsync(s => s.SetProperty(k => k.RecipeId, to)),
            "course" => db.Set<RecipeCourse>().Where(c => c.RecipeId == from).ExecuteUpdateAsync(s => s.SetProperty(c => c.RecipeId, to)),
            "cuisine" => db.Set<RecipeCuisine>().Where(c => c.RecipeId == from).ExecuteUpdateAsync(s => s.SetProperty(c => c.RecipeId, to)),
            "diet" => db.Set<RecipeDiet>().Where(d => d.RecipeId == from).ExecuteUpdateAsync(s => s.SetProperty(d => d.RecipeId, to)),
            "allergen" => db.Set<RecipeAllergen>().Where(x => x.RecipeId == from).ExecuteUpdateAsync(s => s.SetProperty(x => x.RecipeId, to)),
            _ => throw new ArgumentOutOfRangeException(nameof(part)),
        };
    }

    private static void AssertRefusedBy(SqlException error, string constraint)
    {
        Assert.Equal(ReferenceViolation, error.Number);
        Assert.Contains($"\"{constraint}\"", error.Message, StringComparison.Ordinal);
    }

    private static object Under(RecipeRowSet a, string part)
    {
        var recipe = a.Recipe.Id;
        return part switch
        {
            "ingredient-group" => RecipeRows.NewIngredientGroup(recipe),
            "ingredient" => RecipeRows.NewIngredient(a.IngredientGroup.Id),
            "step-group" => RecipeRows.NewStepGroup(recipe),
            "step" => RecipeRows.NewStep(a.StepGroup.Id),
            "equipment" => new RecipeEquipment { RecipeId = recipe, Position = 1, Text = "Rolling pin" },
            "note" => new RecipeNote { RecipeId = recipe, Position = 1, Text = "Not B's to add." },
            "keyword" => new RecipeKeyword { RecipeId = recipe, Position = 1, Text = "pie" },
            "course" => new RecipeCourse { RecipeId = recipe, CourseCode = "dessert", Position = 0 },
            "cuisine" => new RecipeCuisine { RecipeId = recipe, CuisineCode = "american", Position = 0 },
            "diet" => new RecipeDiet { RecipeId = recipe, DietCode = "vegetarian", Position = 0 },
            "allergen" => new RecipeAllergen { RecipeId = recipe, AllergenCode = "milk", Position = 0 },
            _ => throw new ArgumentOutOfRangeException(nameof(part)),
        };
    }

    private static PantryPressDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
}
