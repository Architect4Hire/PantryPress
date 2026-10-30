using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Recipes;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.Tests.Recipes;
using PantryPress.Tests.TwoSite;

namespace PantryPress.Tests.Persistence;

// PP4.3: the recipe tables against the schema the migration service applies. A recipe and every part of it come back
// exactly as written - the blogger's words byte for byte (RCP-002), quantities as the exact fractions they were - and,
// beneath the .NET rules, each table refuses what its checks and keys rule out. Each refusal case breaks one check alone,
// so the constraint that answers is the one under test.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class RecipeSchemaTests(MigratedDatabase database) : IAsyncLifetime
{
    private const int CheckOrReferenceViolation = 547;
    private const int DuplicateKey = 2627;
    private const int RollbackRefused = 51012;

    // String or binary data would be truncated: 2628 names the column (SQL Server 2019 on), 8152 is the older message.
    private static readonly int[] Truncation = [2628, 8152];

    private TwoSiteHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await TwoSiteHarness.CreateAsync(database);

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // The blogger's wording in the shapes a recipe really has, and the ones a careless store would bend: Unicode fractions
    // and dashes, accents composed and decomposed, emoji (surrogate pairs and a joined sequence), full-width digits,
    // markup-looking text, tabs and line breaks, and spaces at either end, none of them trimmed.
    public static IReadOnlyList<string> Wording { get; } =
    [
        "2½ cups all-purpose flour, sifted",
        "1 1/2 cups whole milk",
        "1.5 cups whole milk",
        "2–3 cloves garlic, minced",
        "⅓ cup + 1 tablespoon sugar",
        "crème fraîche",
        string.Concat("cre", (char)0x0300, "me frai", (char)0x0302, "che"),
        "jalapeño, seeded (or not!)",
        "🍎 3 apples",
        string.Concat("chef's kiss ", char.ConvertFromUtf32(0x1F469), (char)0x200D, char.ConvertFromUtf32(0x1F373)),
        "２ cups water",
        "Salt & pepper <to taste>",
        "\tindented with a tab",
        "two lines\nof one ingredient",
        "  two spaces before",
        "three spaces after   ",
        new string('x', RecipeLimits.MaxIngredientTextLength),
    ];

    [Fact]
    public async Task A_recipe_and_every_part_come_back_exactly()
    {
        var world = await _harness.SeedAsync("rcp-whole");
        RecipeRowSet rows = null!;
        await _harness.ChangeAsync(world.A.Id, db => rows = RecipeRows.Add(db));

        await using var scope = _harness.In(world.A.Id);
        var db = Db(scope);
        var recipe = await db.Set<Recipe>().AsNoTracking().SingleAsync(r => r.Id == rows.Recipe.Id);
        var line = await db.Set<RecipeIngredient>().AsNoTracking().SingleAsync(i => i.Id == rows.Ingredient.Id);

        Assert.Equal(world.A.Id, recipe.SiteId);
        Assert.Equal(rows.Recipe.Title, recipe.Title);
        Assert.Equal(rows.Recipe.Summary, recipe.Summary);
        Assert.Equal(Quantity.Range(6, 8), recipe.Yield);
        Assert.Equal("servings", recipe.YieldUnit);
        Assert.Equal((45, 60, 120, "Cool"), (recipe.PrepMinutes!.Value, recipe.CookMinutes!.Value, recipe.AdditionalMinutes!.Value, recipe.AdditionalTimeLabel));
        Assert.Equal(rows.Recipe.VideoUrl, recipe.VideoUrl);
        Assert.Equal((410.5m, 19.25m, 230m, 0m), (recipe.Calories!.Value, recipe.FatGrams!.Value, recipe.SodiumMilligrams!.Value, recipe.ProteinGrams!.Value));
        Assert.Null(recipe.SugarGrams);
        Assert.Equal(RecipeRows.Created, recipe.CreatedAt);
        Assert.NotEmpty(recipe.Version);

        Assert.Equal(rows.Ingredient.OriginalText, line.OriginalText);
        Assert.Equal(Quantity.Amount(Rational.Create(5, 2)), line.Quantity);
        Assert.Equal((0, 2, 3, 4), (line.QuantityStart!.Value, line.QuantityLength!.Value, line.UnitStart!.Value, line.UnitLength!.Value));
        Assert.Equal("2½", line.OriginalText.Substring(line.QuantityStart.Value, line.QuantityLength.Value));
        Assert.Equal("cups", line.OriginalText.Substring(line.UnitStart.Value, line.UnitLength.Value));
        Assert.Equal(("cup", "all-purpose flour", "sifted"), (line.UnitCode, line.Item, line.Note));

        Assert.Equal(rows.Step.Text, (await db.Set<RecipeStep>().AsNoTracking().SingleAsync(s => s.Id == rows.Step.Id)).Text);
        Assert.Equal(["dessert"], await db.Set<RecipeCourse>().Where(c => c.RecipeId == rows.Recipe.Id).Select(c => c.CourseCode).ToListAsync());
        Assert.Equal(["vegetarian"], await db.Set<RecipeDiet>().Where(d => d.RecipeId == rows.Recipe.Id).Select(d => d.DietCode).ToListAsync());
        Assert.Equal(["milk"], await db.Set<RecipeAllergen>().Where(a => a.RecipeId == rows.Recipe.Id).Select(a => a.AllergenCode).ToListAsync());
    }

    // RCP-002: the original text is never trimmed, normalized, or re-encoded, here or in a step.
    [Fact]
    public async Task The_bloggers_wording_comes_back_byte_for_byte()
    {
        var world = await _harness.SeedAsync("rcp-words");
        var recipe = RecipeRows.NewRecipe();
        var ingredients = RecipeRows.NewIngredientGroup(recipe.Id);
        var steps = RecipeRows.NewStepGroup(recipe.Id);
        await _harness.ChangeAsync(world.A.Id, db =>
        {
            db.AddRange(recipe, ingredients, steps);
            for (var i = 0; i < Wording.Count; i++)
            {
                db.Add(new RecipeIngredient
                {
                    Id = new RecipeIngredientId(Guid.NewGuid()),
                    GroupId = ingredients.Id,
                    Position = i,
                    OriginalText = Wording[i],
                    ParseStatus = IngredientParseStatus.Unparsed,
                });
                db.Add(new RecipeStep { Id = new RecipeStepId(Guid.NewGuid()), GroupId = steps.Id, Position = i, Text = Wording[i] });
            }
        });

        await using var scope = _harness.In(world.A.Id);
        var db = Db(scope);
        var lines = await db.Set<RecipeIngredient>().Where(l => l.GroupId == ingredients.Id).OrderBy(l => l.Position).Select(l => l.OriginalText).ToListAsync();
        var texts = await db.Set<RecipeStep>().Where(s => s.GroupId == steps.Id).OrderBy(s => s.Position).Select(s => s.Text).ToListAsync();

        Assert.Equal(Wording, lines, StringComparer.Ordinal);
        Assert.Equal(Wording, texts, StringComparer.Ordinal);
    }

    // A span's end may sit exactly at the end of the text, counted in UTF-16 code units, trailing spaces included.
    [Fact]
    public async Task A_span_may_end_at_the_last_code_unit()
    {
        var world = await _harness.SeedAsync("rcp-span");
        var recipe = RecipeRows.NewRecipe();
        var group = RecipeRows.NewIngredientGroup(recipe.Id);
        const string text = "flour 🍎 ½  ";
        await _harness.ChangeAsync(world.A.Id, db => db.AddRange(recipe, group, new RecipeIngredient
        {
            Id = new RecipeIngredientId(Guid.NewGuid()),
            GroupId = group.Id,
            OriginalText = text,
            ParseStatus = IngredientParseStatus.Parsed,
            Quantity = Quantity.Amount(Rational.Create(1, 2)),
            QuantityStart = text.IndexOf('½', StringComparison.Ordinal),
            QuantityLength = text.Length - text.IndexOf('½', StringComparison.Ordinal),
            Item = "flour",
        }));

        await using var scope = _harness.In(world.A.Id);
        Assert.Equal(1, await Db(scope).Set<RecipeIngredient>().CountAsync(i => i.GroupId == group.Id));
    }

    // The accepting side of every limit, through EF Core and read back: the barest recipe a blogger can save, each time at 0
    // and at its most, the term cap at both ends of a range and on a yield, the shortest HTTPS address, spans that touch
    // (metric lines are written "200g") in either order, a "to taste" line with no span, and a range with a unit. A refusal
    // case names only the first check a row breaks, so what a check lets through is shown here on its own.
    [Fact]
    public async Task Rows_at_every_limit_are_accepted_and_read_back()
    {
        var world = await _harness.SeedAsync("rcp-limits");
        var bare = new Recipe { Id = new RecipeId(Guid.NewGuid()), Title = "Toast", CreatedAt = RecipeRows.Created, UpdatedAt = RecipeRows.Created };
        var bareGroup = new RecipeIngredientGroup { Id = new RecipeIngredientGroupId(Guid.NewGuid()), RecipeId = bare.Id };
        var none = RecipeRows.NewRecipe();
        (none.PrepMinutes, none.CookMinutes, none.AdditionalMinutes, none.AdditionalTimeLabel) = (0, 0, 0, null);
        none.VideoUrl = "https://a";
        var most = RecipeRows.NewRecipe();
        (most.PrepMinutes, most.CookMinutes, most.AdditionalMinutes) = (RecipeLimits.MaxMinutes, RecipeLimits.MaxMinutes, RecipeLimits.MaxMinutes);
        most.Yield = Quantity.Amount(RecipeLimits.MaxQuantityTerm);
        var group = RecipeRows.NewIngredientGroup(most.Id);

        var capped = Quantity.Range(Rational.Create(RecipeLimits.MaxQuantityTerm - 1, RecipeLimits.MaxQuantityTerm), RecipeLimits.MaxQuantityTerm);
        var lines = new (string Text, Quantity? Quantity, (int, int)? QuantitySpan, string? Unit, (int, int)? UnitSpan, string Item)[]
        {
            ("999999/1000000–1000000 sprinkles", capped, (0, 22), null, null, "sprinkles"),
            ("200g flour", Quantity.Amount(200), (0, 3), "gram", (3, 1), "flour"),
            ("grams: 200, of flour", Quantity.Amount(200), (7, 3), "gram", (0, 5), "flour"),
            ("salt, to taste", Quantity.ToTaste, null, null, null, "salt"),
            ("2–3 cloves garlic", Quantity.Range(2, 3), (0, 3), "clove", (4, 6), "garlic"),
        };
        var ids = lines.Select(_ => new RecipeIngredientId(Guid.NewGuid())).ToArray();

        await _harness.ChangeAsync(world.A.Id, db =>
        {
            db.AddRange(bare, bareGroup, none, most, group);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                db.Add(new RecipeIngredient
                {
                    Id = ids[i],
                    GroupId = group.Id,
                    Position = i,
                    OriginalText = line.Text,
                    ParseStatus = IngredientParseStatus.Parsed,
                    Quantity = line.Quantity,
                    QuantityStart = line.QuantitySpan?.Item1,
                    QuantityLength = line.QuantitySpan?.Item2,
                    UnitCode = line.Unit,
                    UnitStart = line.UnitSpan?.Item1,
                    UnitLength = line.UnitSpan?.Item2,
                    Item = line.Item,
                });
            }
        });

        await using var scope = _harness.In(world.A.Id);
        var db = Db(scope);
        var readBare = await db.Set<Recipe>().AsNoTracking().SingleAsync(r => r.Id == bare.Id);
        Assert.Null(readBare.Yield);
        Assert.Null(readBare.YieldUnit);
        Assert.Equal((null, null, null), (readBare.PrepMinutes, readBare.CookMinutes, readBare.AdditionalMinutes));
        Assert.Null(readBare.VideoUrl);
        Assert.Null(readBare.Summary);
        Assert.Null(readBare.Calories);
        Assert.Null((await db.Set<RecipeIngredientGroup>().AsNoTracking().SingleAsync(g => g.Id == bareGroup.Id)).Heading);

        var readNone = await db.Set<Recipe>().AsNoTracking().SingleAsync(r => r.Id == none.Id);
        Assert.Equal((0, 0, 0), (readNone.PrepMinutes!.Value, readNone.CookMinutes!.Value, readNone.AdditionalMinutes!.Value));
        Assert.Equal("https://a", readNone.VideoUrl);

        var readMost = await db.Set<Recipe>().AsNoTracking().SingleAsync(r => r.Id == most.Id);
        Assert.Equal(RecipeLimits.MaxMinutes, readMost.AdditionalMinutes);
        Assert.Equal(Quantity.Amount(RecipeLimits.MaxQuantityTerm), readMost.Yield);

        for (var i = 0; i < lines.Length; i++)
        {
            var read = await db.Set<RecipeIngredient>().AsNoTracking().SingleAsync(l => l.Id == ids[i]);
            Assert.Equal(lines[i].Text, read.OriginalText);
            Assert.Equal(lines[i].Quantity, read.Quantity);
            Assert.Equal(lines[i].Unit, read.UnitCode);
        }
    }

    // RCP-002: words past a column's size are refused, never cut short - through EF Core and through raw SQL alike - and the
    // row that was there is unchanged.
    [Theory]
    [InlineData("ingredient", RecipeLimits.MaxIngredientTextLength)]
    [InlineData("step", RecipeLimits.MaxStepTextLength)]
    [InlineData("note", RecipeLimits.MaxNoteLength)]
    public async Task Text_one_past_its_limit_is_refused_not_cut(string part, int limit)
    {
        var (site, rows) = await SeedRecipeAsync("rcp-too-long");
        var tooLong = new string('x', limit + 1);

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => _harness.ChangeAsync(site, db =>
        {
            switch (part)
            {
                case "ingredient":
                    var line = RecipeRows.NewIngredient(rows.IngredientGroup.Id);
                    (line.OriginalText, line.Position) = (tooLong, 1);
                    db.Add(line);
                    break;
                case "step":
                    var step = RecipeRows.NewStep(rows.StepGroup.Id);
                    (step.Text, step.Position) = (tooLong, 1);
                    db.Add(step);
                    break;
                default:
                    db.Add(new RecipeNote { RecipeId = rows.Recipe.Id, Position = 1, Text = tooLong });
                    break;
            }
        }));
        Assert.Contains(Assert.IsType<SqlException>(error.InnerException).Number, Truncation);

        var (table, column, key, id) = part switch
        {
            "ingredient" => ("RecipeIngredients", "OriginalText", "Id", rows.Ingredient.Id.Value),
            "step" => ("RecipeSteps", "Text", "Id", rows.Step.Id.Value),
            _ => ("RecipeNotes", "Text", "RecipeId", rows.Recipe.Id.Value),
        };
        await using (var scope = _harness.Scope())
        {
            var statement = string.Concat("UPDATE [", table, "] SET [", column, "] = @text WHERE [SiteId] = @site AND [", key, "] = @id");
            var raw = await Assert.ThrowsAsync<SqlException>(() => Db(scope).Database.ExecuteSqlRawAsync(
                statement,
                new SqlParameter("@text", System.Data.SqlDbType.NVarChar, -1) { Value = tooLong },
                new SqlParameter("@site", site.Value),
                new SqlParameter("@id", id)));
            Assert.Contains(raw.Number, Truncation);
        }

        await using (var scope = _harness.In(site))
        {
            var db = Db(scope);
            Assert.Equal(12, await PartsOfAsync(db, rows));
            Assert.Equal(rows.Ingredient.OriginalText, (await db.Set<RecipeIngredient>().AsNoTracking().SingleAsync(i => i.Id == rows.Ingredient.Id)).OriginalText);
            Assert.Equal(rows.Step.Text, (await db.Set<RecipeStep>().AsNoTracking().SingleAsync(s => s.Id == rows.Step.Id)).Text);
            Assert.Equal(rows.Note.Text, (await db.Set<RecipeNote>().AsNoTracking().SingleAsync(n => n.RecipeId == rows.Recipe.Id)).Text);
        }
    }

    // A tracked change to a quantity or a yield goes through the same fields: a range that becomes an amount clears its high
    // end's columns, and a yield taken away clears every one.
    [Fact]
    public async Task Changing_a_quantity_and_a_yield_rewrites_their_columns()
    {
        var (site, rows) = await SeedRecipeAsync("rcp-change");

        await _harness.ChangeAsync(site, db =>
            db.Set<RecipeIngredient>().Single(i => i.Id == rows.Ingredient.Id).Quantity = Quantity.Range(2, 3));
        await using (var scope = _harness.In(site))
        {
            Assert.Equal(Quantity.Range(2, 3), (await Db(scope).Set<RecipeIngredient>().AsNoTracking().SingleAsync(i => i.Id == rows.Ingredient.Id)).Quantity);
        }

        await _harness.ChangeAsync(site, db =>
        {
            db.Set<RecipeIngredient>().Single(i => i.Id == rows.Ingredient.Id).Quantity = Quantity.Amount(1);
            var recipe = db.Set<Recipe>().Single(r => r.Id == rows.Recipe.Id);
            (recipe.Yield, recipe.YieldUnit) = (null, null);
        });

        await using (var scope = _harness.In(site))
        {
            var db = Db(scope);
            var line = await db.Set<RecipeIngredient>().AsNoTracking().SingleAsync(i => i.Id == rows.Ingredient.Id);
            Assert.Equal(Quantity.Amount(1), line.Quantity);
            Assert.All(
                await db.Set<RecipeIngredient>().Where(i => i.Id == rows.Ingredient.Id)
                    .Select(i => new[] { EF.Property<long?>(i, "_quantityHighNumerator"), EF.Property<long?>(i, "_quantityHighDenominator") })
                    .SingleAsync(),
                term => Assert.Null(term));

            var recipe = await db.Set<Recipe>().AsNoTracking().SingleAsync(r => r.Id == rows.Recipe.Id);
            Assert.Null(recipe.Yield);
            Assert.Null(await db.Set<Recipe>().Where(r => r.Id == rows.Recipe.Id).Select(r => EF.Property<QuantityKind?>(r, "_yieldKind")).SingleAsync());
        }
    }

    // The recipe's row version refuses a save from a stale read, so two editors never silently overwrite each other.
    [Fact]
    public async Task A_stale_recipe_save_is_refused()
    {
        var (site, rows) = await SeedRecipeAsync("rcp-stale");
        await using var first = _harness.In(site);
        var stale = await Db(first).Set<Recipe>().SingleAsync(r => r.Id == rows.Recipe.Id);

        await _harness.ChangeAsync(site, db => db.Set<Recipe>().Single(r => r.Id == rows.Recipe.Id).Title = "Apple pie, again");

        stale.Title = "Apple pie, stale";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Db(first).SaveChangesAsync());
    }

    // One code unit past the end of the text is refused, emoji and trailing spaces counted as .NET counts them.
    [Fact]
    public async Task A_span_one_past_the_last_code_unit_is_refused()
    {
        var world = await _harness.SeedAsync("rcp-span-past");
        var recipe = RecipeRows.NewRecipe();
        var group = RecipeRows.NewIngredientGroup(recipe.Id);
        const string text = "flour 🍎 ½  ";
        await _harness.ChangeAsync(world.A.Id, db => db.AddRange(recipe, group));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => _harness.ChangeAsync(world.A.Id, db => db.Add(new RecipeIngredient
        {
            Id = new RecipeIngredientId(Guid.NewGuid()),
            GroupId = group.Id,
            OriginalText = text,
            ParseStatus = IngredientParseStatus.Parsed,
            Quantity = Quantity.Amount(Rational.Create(1, 2)),
            QuantityStart = text.IndexOf('½', StringComparison.Ordinal),
            QuantityLength = text.Length - text.IndexOf('½', StringComparison.Ordinal) + 1,
            Item = "flour",
        })));

        var sql = Assert.IsType<SqlException>(error.InnerException);
        Assert.Equal(CheckOrReferenceViolation, sql.Number);
        Assert.Contains($"\"{RecipeIngredientConfiguration.QuantitySpanConstraintName}\"", sql.Message, StringComparison.Ordinal);
    }

    // PP4.3: rolling the migration back refuses while any recipe exists, before it drops anything. Run inside a transaction
    // that is rolled back, so the shared database keeps its tables.
    [Fact]
    public async Task Rolling_back_refuses_while_a_recipe_exists()
    {
        var down = new PantryPress.MigrationService.Migrations.Recipes().DownOperations;
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(down[0]);
        Assert.All(down.Skip(1), operation => Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropTableOperation>(operation));

        await SeedRecipeAsync("rcp-rollback");
        await using var scope = _harness.Scope();
        var database = Db(scope).Database;
        await using var transaction = await database.BeginTransactionAsync();

        var error = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteSqlRawAsync(guard.Sql));
        Assert.Equal(RollbackRefused, error.Number);
        await transaction.RollbackAsync();
    }

    public static TheoryData<string, string[]> RecipeRefusals => new()
    {
        { "[Title] = N''", [RecipeConfiguration.TitleConstraintName] },
        { "[Title] = N'   '", [RecipeConfiguration.TitleConstraintName] },
        { "[Summary] = N''", [RecipeConfiguration.TextConstraintName] },
        { "[ServingSize] = N' '", [RecipeConfiguration.TextConstraintName] },
        { "[YieldKind] = 2, [YieldLowNumerator] = NULL, [YieldLowDenominator] = NULL, [YieldHighNumerator] = NULL, [YieldHighDenominator] = NULL", ["CK_Recipes_YieldKind"] },
        { "[YieldKind] = 0", ["CK_Recipes_YieldShape"] },
        { "[YieldKind] = NULL, [YieldUnit] = NULL", ["CK_Recipes_YieldShape"] },
        { "[YieldKind] = NULL, [YieldUnit] = NULL, [YieldHighNumerator] = NULL, [YieldHighDenominator] = NULL", ["CK_Recipes_YieldShape"] },
        { "[YieldHighNumerator] = NULL", ["CK_Recipes_YieldShape"] },
        { "[YieldLowNumerator] = 0", ["CK_Recipes_YieldTerms"] },
        { "[YieldLowNumerator] = -1", ["CK_Recipes_YieldTerms"] },
        { "[YieldLowDenominator] = 0", ["CK_Recipes_YieldTerms", "CK_Recipes_YieldRange"] },
        { "[YieldHighDenominator] = -1", ["CK_Recipes_YieldTerms"] },
        { "[YieldLowNumerator] = NULL", ["CK_Recipes_YieldShape"] },
        { "[YieldLowNumerator] = 1, [YieldLowDenominator] = 2, [YieldHighNumerator] = 2, [YieldHighDenominator] = 4", ["CK_Recipes_YieldRange"] },
        { "[YieldHighNumerator] = 1000001", ["CK_Recipes_YieldTerms"] },
        { "[YieldHighNumerator] = 6", ["CK_Recipes_YieldRange"] },
        { "[YieldLowNumerator] = 9", ["CK_Recipes_YieldRange"] },
        { "[YieldKind] = NULL, [YieldLowNumerator] = NULL, [YieldLowDenominator] = NULL, [YieldHighNumerator] = NULL, [YieldHighDenominator] = NULL", [RecipeConfiguration.YieldUnitConstraintName] },
        { "[YieldUnit] = N''", [RecipeConfiguration.YieldUnitConstraintName] },
        { "[PrepMinutes] = -1", [RecipeConfiguration.TimesConstraintName] },
        { "[PrepMinutes] = 43201", [RecipeConfiguration.TimesConstraintName] },
        { "[CookMinutes] = -1", [RecipeConfiguration.TimesConstraintName] },
        { "[CookMinutes] = 43201", [RecipeConfiguration.TimesConstraintName] },
        { "[AdditionalMinutes] = -1", [RecipeConfiguration.TimesConstraintName] },
        { "[AdditionalMinutes] = 43201", [RecipeConfiguration.TimesConstraintName] },
        { "[AdditionalMinutes] = NULL", [RecipeConfiguration.AdditionalTimeLabelConstraintName] },
        { "[AdditionalTimeLabel] = N''", [RecipeConfiguration.AdditionalTimeLabelConstraintName] },
        { "[VideoUrl] = N'http://video.example/pie'", [RecipeConfiguration.VideoUrlConstraintName] },
        { "[VideoUrl] = N'HTTPS://video.example/pie'", [RecipeConfiguration.VideoUrlConstraintName] },
        { "[VideoUrl] = N'https://'", [RecipeConfiguration.VideoUrlConstraintName] },
        { "[VideoUrl] = N'javascript:alert(1)'", [RecipeConfiguration.VideoUrlConstraintName] },
        { "[Calories] = -0.01", [RecipeConfiguration.NutritionConstraintName] },
        { "[FatGrams] = -0.01", [RecipeConfiguration.NutritionConstraintName] },
        { "[SaturatedFatGrams] = -0.01", [RecipeConfiguration.NutritionConstraintName] },
        { "[TransFatGrams] = -0.01", [RecipeConfiguration.NutritionConstraintName] },
        { "[UnsaturatedFatGrams] = -0.01", [RecipeConfiguration.NutritionConstraintName] },
        { "[CholesterolMilligrams] = -0.01", [RecipeConfiguration.NutritionConstraintName] },
        { "[SodiumMilligrams] = -0.01", [RecipeConfiguration.NutritionConstraintName] },
        { "[CarbohydrateGrams] = -0.01", [RecipeConfiguration.NutritionConstraintName] },
        { "[FiberGrams] = -0.01", [RecipeConfiguration.NutritionConstraintName] },
        { "[SugarGrams] = -0.01", [RecipeConfiguration.NutritionConstraintName] },
        { "[ProteinGrams] = -0.01", [RecipeConfiguration.NutritionConstraintName] },
        { "[UpdatedAt] = DATEADD(second, -1, [CreatedAt])", [RecipeConfiguration.UpdatedAtConstraintName] },
    };

    [Theory]
    [MemberData(nameof(RecipeRefusals))]
    public async Task The_recipes_table_refuses_a_bad_row(string set, string[] constraints)
    {
        var (site, rows) = await SeedRecipeAsync("rcp-no-recipe");
        await AssertRefusedAsync("Recipes", "Id", site, rows.Recipe.Id.Value, set, constraints);
    }

    // The parsed line is "2½ cups all-purpose flour, sifted" (quantity at 0 for 2, unit at 3 for 4); the unparsed one is
    // "A handful of love", with no parts. Where one change breaks more than one check - an unknown kind, a span on a line
    // with no parts - any of them may answer, and each is named. A null kind beside an amount's columns is refused, though a
    // comparison with a null kind is unknown and an unknown check passes; and a span far past the text is refused by the
    // span check, never by an overflow.
    public static TheoryData<bool, string, string[]> IngredientRefusals => new()
    {
        { false, "[Position] = -1", [RecipeIngredientConfiguration.PositionConstraintName] },
        { false, "[OriginalText] = N''", [RecipeIngredientConfiguration.OriginalTextConstraintName] },
        { false, "[OriginalText] = N'    '", [RecipeIngredientConfiguration.OriginalTextConstraintName] },
        { true, "[ParseStatus] = 5", [RecipeIngredientConfiguration.ParseStatusConstraintName] },
        { true, "[ParseStatus] = 1", [RecipeIngredientConfiguration.UnparsedConstraintName] },
        { false, "[Note] = N'kept anyway'", [RecipeIngredientConfiguration.UnparsedConstraintName] },
        { false, "[Item] = N'love'", [RecipeIngredientConfiguration.UnparsedConstraintName] },
        { false, "[QuantityKind] = 2", [RecipeIngredientConfiguration.UnparsedConstraintName] },
        { true, "[Item] = NULL", [RecipeIngredientConfiguration.ParsedConstraintName] },
        { true, "[Item] = N''", [RecipeIngredientConfiguration.ParsedConstraintName] },
        { true, "[Note] = N' '", [RecipeIngredientConfiguration.ParsedConstraintName] },
        { true, "[QuantityKind] = 2, [QuantityLowNumerator] = NULL, [QuantityLowDenominator] = NULL", [RecipeIngredientConfiguration.UnitConstraintName] },
        { true, "[QuantityKind] = NULL, [QuantityLowNumerator] = NULL, [QuantityLowDenominator] = NULL, [QuantityStart] = NULL, [QuantityLength] = NULL", [RecipeIngredientConfiguration.UnitConstraintName] },
        { true, "[QuantityKind] = 9", ["CK_RecipeIngredients_QuantityKind", "CK_RecipeIngredients_QuantityShape", RecipeIngredientConfiguration.UnitConstraintName] },
        { true, "[QuantityHighNumerator] = 3", ["CK_RecipeIngredients_QuantityShape"] },
        { false, "[QuantityLowNumerator] = 1, [QuantityLowDenominator] = 1", ["CK_RecipeIngredients_QuantityShape"] },
        { false, "[QuantityLowNumerator] = 1, [QuantityLowDenominator] = 1, [QuantityHighNumerator] = 2, [QuantityHighDenominator] = 1", ["CK_RecipeIngredients_QuantityShape"] },
        { true, "[QuantityLowDenominator] = 1000001", ["CK_RecipeIngredients_QuantityTerms"] },
        { true, "[QuantityLowDenominator] = 0", ["CK_RecipeIngredients_QuantityTerms"] },
        { true, "[QuantityKind] = 1, [QuantityHighNumerator] = 3, [QuantityHighDenominator] = 1, [QuantityLowNumerator] = NULL", ["CK_RecipeIngredients_QuantityShape"] },
        { true, "[QuantityKind] = 1, [QuantityHighNumerator] = 1, [QuantityHighDenominator] = 1", ["CK_RecipeIngredients_QuantityRange"] },
        { true, "[QuantityStart] = NULL, [QuantityLength] = NULL", [RecipeIngredientConfiguration.QuantitySpanConstraintName] },
        { true, "[QuantityLength] = NULL", [RecipeIngredientConfiguration.QuantitySpanConstraintName] },
        { true, "[QuantityLength] = 0", [RecipeIngredientConfiguration.QuantitySpanConstraintName] },
        { true, "[QuantityStart] = -1", [RecipeIngredientConfiguration.QuantitySpanConstraintName] },
        { true, "[QuantityStart] = 40", [RecipeIngredientConfiguration.QuantitySpanConstraintName] },
        { true, "[QuantityStart] = 33, [QuantityLength] = 1, [UnitStart] = 3", [RecipeIngredientConfiguration.QuantitySpanConstraintName] },
        { true, "[QuantityStart] = 2147483647", [RecipeIngredientConfiguration.QuantitySpanConstraintName] },
        { true, "[UnitLength] = 2147483647", [RecipeIngredientConfiguration.UnitSpanConstraintName] },
        { false, "[QuantityStart] = 0, [QuantityLength] = 1", [RecipeIngredientConfiguration.UnparsedConstraintName, RecipeIngredientConfiguration.QuantitySpanConstraintName] },
        { true, "[UnitStart] = NULL, [UnitLength] = NULL", [RecipeIngredientConfiguration.UnitSpanConstraintName] },
        { true, "[UnitCode] = NULL", [RecipeIngredientConfiguration.UnitSpanConstraintName] },
        { true, "[UnitLength] = 31", [RecipeIngredientConfiguration.UnitSpanConstraintName] },
        { true, "[UnitStart] = 1", [RecipeIngredientConfiguration.SpansApartConstraintName] },
    };

    [Theory]
    [MemberData(nameof(IngredientRefusals))]
    public async Task The_ingredients_table_refuses_a_bad_row(bool parsed, string set, string[] constraints)
    {
        var (site, rows) = await SeedRecipeAsync("rcp-no-line");
        var unparsed = new RecipeIngredient
        {
            Id = new RecipeIngredientId(Guid.NewGuid()),
            GroupId = rows.IngredientGroup.Id,
            Position = 1,
            OriginalText = "A handful of love",
            ParseStatus = IngredientParseStatus.Unparsed,
        };
        await _harness.ChangeAsync(site, db => db.Add(unparsed));

        var id = parsed ? rows.Ingredient.Id.Value : unparsed.Id.Value;
        await AssertRefusedAsync("RecipeIngredients", "Id", site, id, set, constraints);
    }

    public static TheoryData<string, string, string, string> PartRefusals => new()
    {
        { "RecipeIngredientGroups", "Id", "[Position] = -1", RecipeIngredientGroupConfiguration.PositionConstraintName },
        { "RecipeIngredientGroups", "Id", "[Heading] = N''", RecipeIngredientGroupConfiguration.HeadingConstraintName },
        { "RecipeStepGroups", "Id", "[Position] = -1", RecipeStepGroupConfiguration.PositionConstraintName },
        { "RecipeStepGroups", "Id", "[Heading] = N'  '", RecipeStepGroupConfiguration.HeadingConstraintName },
        { "RecipeSteps", "Id", "[Position] = -1", RecipeStepConfiguration.PositionConstraintName },
        { "RecipeSteps", "Id", "[Text] = N''", RecipeStepConfiguration.TextConstraintName },
        { "RecipeEquipment", "RecipeId", "[Position] = -1", RecipeEquipmentConfiguration.PositionConstraintName },
        { "RecipeEquipment", "RecipeId", "[Text] = N''", RecipeEquipmentConfiguration.TextConstraintName },
        { "RecipeNotes", "RecipeId", "[Position] = -1", RecipeNoteConfiguration.PositionConstraintName },
        { "RecipeNotes", "RecipeId", "[Text] = N' '", RecipeNoteConfiguration.TextConstraintName },
        { "RecipeKeywords", "RecipeId", "[Position] = -1", RecipeKeywordConfiguration.PositionConstraintName },
        { "RecipeKeywords", "RecipeId", "[Text] = N''", RecipeKeywordConfiguration.TextConstraintName },
        { "RecipeCourses", "RecipeId", "[Position] = -1", RecipeCourseConfiguration.PositionConstraintName },
        { "RecipeCuisines", "RecipeId", "[Position] = -1", RecipeCuisineConfiguration.PositionConstraintName },
        { "RecipeDiets", "RecipeId", "[Position] = -1", RecipeDietConfiguration.PositionConstraintName },
        { "RecipeAllergens", "RecipeId", "[Position] = -1", RecipeAllergenConfiguration.PositionConstraintName },
    };

    [Theory]
    [MemberData(nameof(PartRefusals))]
    public async Task Each_part_refuses_a_bad_row(string table, string keyColumn, string set, string constraint)
    {
        var (site, rows) = await SeedRecipeAsync("rcp-no-part");
        var id = (table, keyColumn) switch
        {
            ("RecipeIngredientGroups", _) => rows.IngredientGroup.Id.Value,
            ("RecipeStepGroups", _) => rows.StepGroup.Id.Value,
            ("RecipeSteps", _) => rows.Step.Id.Value,
            _ => rows.Recipe.Id.Value,
        };

        await AssertRefusedAsync(table, keyColumn, site, id, set, [constraint]);
    }

    // A code the vocabulary does not hold is refused by its foreign key, byte for byte: a capitalized spelling of a real
    // code is not that code.
    [Theory]
    [InlineData("course", "not-a-course")]
    [InlineData("course", "Dessert")]
    [InlineData("cuisine", "atlantean")]
    [InlineData("diet", "carnivore")]
    [InlineData("allergen", "kryptonite")]
    [InlineData("unit", "Cup")]
    [InlineData("unit", "smidgen-and-a-half")]
    public async Task A_code_the_vocabulary_lacks_is_refused(string vocabulary, string code)
    {
        var (site, rows) = await SeedRecipeAsync("rcp-no-code");

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => _harness.ChangeAsync(site, db =>
        {
            switch (vocabulary)
            {
                case "course":
                    db.Add(new RecipeCourse { RecipeId = rows.Recipe.Id, CourseCode = code, Position = 1 });
                    break;
                case "cuisine":
                    db.Add(new RecipeCuisine { RecipeId = rows.Recipe.Id, CuisineCode = code, Position = 1 });
                    break;
                case "diet":
                    db.Add(new RecipeDiet { RecipeId = rows.Recipe.Id, DietCode = code, Position = 1 });
                    break;
                case "allergen":
                    db.Add(new RecipeAllergen { RecipeId = rows.Recipe.Id, AllergenCode = code, Position = 1 });
                    break;
                default:
                    var line = RecipeRows.NewIngredient(rows.IngredientGroup.Id);
                    line.UnitCode = code;
                    line.Position = 1;
                    db.Add(line);
                    break;
            }
        }));

        var sql = Assert.IsType<SqlException>(error.InnerException);
        Assert.Equal(CheckOrReferenceViolation, sql.Number);
        Assert.Contains("FOREIGN KEY", sql.Message, StringComparison.Ordinal);
    }

    // A recipe names each course once; a second row for the same one is refused by the key.
    [Fact]
    public async Task A_recipe_names_a_course_once()
    {
        var (site, rows) = await SeedRecipeAsync("rcp-twice");

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => _harness.ChangeAsync(site, db =>
            db.Add(new RecipeCourse { RecipeId = rows.Recipe.Id, CourseCode = "dessert", Position = 1 })));

        Assert.Equal(DuplicateKey, Assert.IsType<SqlException>(error.InnerException).Number);
    }

    // Deleting a recipe takes every part with it, and only its own.
    [Fact]
    public async Task Deleting_a_recipe_takes_its_parts()
    {
        var world = await _harness.SeedAsync("rcp-delete");
        RecipeRowSet gone = null!, kept = null!;
        await _harness.ChangeAsync(world.A.Id, db =>
        {
            gone = RecipeRows.Add(db);
            kept = RecipeRows.Add(db);
        });

        await using (var scope = _harness.In(world.A.Id))
        {
            Assert.Equal(1, await Db(scope).Set<Recipe>().Where(r => r.Id == gone.Recipe.Id).ExecuteDeleteAsync());
        }

        await using (var scope = _harness.In(world.A.Id))
        {
            Assert.Equal(0, await PartsOfAsync(Db(scope), gone));
            Assert.Equal(12, await PartsOfAsync(Db(scope), kept));
        }
    }

    /// <summary>How many of <paramref name="rows"/>' rows - the recipe and its eleven parts - are still there.</summary>
    internal static async Task<int> PartsOfAsync(PantryPressDbContext db, RecipeRowSet rows)
    {
        var recipe = rows.Recipe.Id;
        return await db.Set<Recipe>().CountAsync(r => r.Id == recipe)
            + await db.Set<RecipeIngredientGroup>().CountAsync(g => g.Id == rows.IngredientGroup.Id)
            + await db.Set<RecipeIngredient>().CountAsync(i => i.Id == rows.Ingredient.Id)
            + await db.Set<RecipeStepGroup>().CountAsync(g => g.Id == rows.StepGroup.Id)
            + await db.Set<RecipeStep>().CountAsync(s => s.Id == rows.Step.Id)
            + await db.Set<RecipeEquipment>().CountAsync(e => e.RecipeId == recipe)
            + await db.Set<RecipeNote>().CountAsync(n => n.RecipeId == recipe)
            + await db.Set<RecipeKeyword>().CountAsync(k => k.RecipeId == recipe)
            + await db.Set<RecipeCourse>().CountAsync(c => c.RecipeId == recipe)
            + await db.Set<RecipeCuisine>().CountAsync(c => c.RecipeId == recipe)
            + await db.Set<RecipeDiet>().CountAsync(d => d.RecipeId == recipe)
            + await db.Set<RecipeAllergen>().CountAsync(a => a.RecipeId == recipe);
    }

    private async Task<(SiteId Site, RecipeRowSet Rows)> SeedRecipeAsync(string prefix)
    {
        var world = await _harness.SeedAsync(prefix);
        RecipeRowSet rows = null!;
        await _harness.ChangeAsync(world.A.Id, db => rows = RecipeRows.Add(db));
        return (world.A.Id, rows);
    }

    // The change is raw SQL, past every .NET rule, as a bug or an operator's script would make it; the row is the site's own,
    // named by its key. Only the clause comes from the test's own table of cases.
    private async Task AssertRefusedAsync(string table, string keyColumn, SiteId site, Guid id, string set, string[] constraints)
    {
        await using var scope = _harness.Scope();
        var database = Db(scope).Database;

        // The table, key, and clause are the cases' own constants; the row's key goes as parameters.
        var statement = string.Concat("UPDATE [", table, "] SET ", set, " WHERE [SiteId] = @site AND [", keyColumn, "] = @id");
        var error = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteSqlRawAsync(
            statement,
            new SqlParameter("@site", site.Value),
            new SqlParameter("@id", id)));

        Assert.Equal(CheckOrReferenceViolation, error.Number);
        Assert.Contains(constraints, c => error.Message.Contains($"\"{c}\"", StringComparison.Ordinal));
    }

    private static PantryPressDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
}
