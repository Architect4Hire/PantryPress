using System.Globalization;
using FluentValidation;
using PantryPress.Domain.Managers.Validation;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Recipes;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.Domain.Modules.Recipes.Validation;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.11: the pure rules of saving a recipe - what a save may hold, who may make it, and which rows it touches - with no
// database. The facade and the routes are RecipesFacadeTests' and RecipesApiTwoSiteTests'.
public sealed class RecipeEditorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A small valid recipe as the editor sends it.</summary>
    internal static RecipeContent Content(
        string title = "Apple pie",
        IReadOnlyList<RecipeIngredientGroupInput?>? ingredients = null,
        IReadOnlyList<RecipeStepGroupInput?>? steps = null) => new(
        title,
        "A deep-dish pie.",
        new RecipeQuantity(QuantityKind.Range, new RecipeFraction(6, 1), new RecipeFraction(8, 1)),
        "servings",
        45,
        60,
        0,
        "Cool",
        "https://www.youtube.com/watch?v=abc123",
        new RecipeNutrition("1 slice", 410.5m, 19.25m, null, null, null, null, 230m, null, null, null, 0m),
        ingredients ?? [new RecipeIngredientGroupInput(null, "For the crust", [new(null, "2½ cups all-purpose flour, sifted"), new(null, "a pinch of salt")])],
        steps ?? [new RecipeStepGroupInput(null, null, [new(null, "Whisk the flour and salt."), new(null, "Bake.")])],
        ["9-inch pie plate"],
        ["Keeps two days."],
        ["fall baking"],
        ["dessert"],
        ["american"],
        ["vegetarian"],
        ["milk", "wheat"]);

    // ---- Who may change a recipe (SITE-010) ----

    [Theory]
    [InlineData(SiteRole.Owner, false, true)]
    [InlineData(SiteRole.Editor, false, true)]
    [InlineData(SiteRole.Author, true, true)]
    [InlineData(SiteRole.Author, false, false)]
    public void An_author_changes_only_their_own_recipes(SiteRole role, bool made, bool allowed)
    {
        var caller = new AccountId(Guid.NewGuid());
        var maker = made ? caller : new AccountId(Guid.NewGuid());

        Assert.Equal(allowed, RecipeEditor.CanChange(role, caller, maker));
    }

    // A recipe no person made (the publishing API, an import, or an erased maker) is no Author's.
    [Fact]
    public void A_recipe_with_no_maker_is_only_for_editors_and_owners()
    {
        var caller = new AccountId(Guid.NewGuid());

        Assert.False(RecipeEditor.CanChange(SiteRole.Author, caller, null));
        Assert.True(RecipeEditor.CanChange(SiteRole.Editor, caller, null));
        Assert.False(RecipeEditor.CanChange((SiteRole)99, caller, caller));
    }

    [Fact]
    public void A_copys_title_says_so_when_it_fits()
    {
        Assert.Equal("Copy of Apple pie", RecipeEditor.CopyTitle("Apple pie"));
        var longest = new string('a', RecipeLimits.MaxTitleLength - "Copy of ".Length);
        Assert.Equal("Copy of " + longest, RecipeEditor.CopyTitle(longest));
        Assert.Equal(longest + "a", RecipeEditor.CopyTitle(longest + "a"));
    }

    // ---- Applying a save ----

    [Fact]
    public void A_new_recipe_gets_every_part_numbered_in_order_with_its_lines_read_on_the_server()
    {
        var recipe = NewRecipe();
        var plan = RecipeEditor.Apply(RecipeWorkingCopy.New(recipe, RecipeRows.Nutrition), Edit(Content()), RecipeRows.Nutrition, Now);

        Assert.Empty(plan.Updated);
        Assert.Empty(plan.Removed);
        Assert.Equal(2 + 2 + 2 + 1 + 1 + 1 + 1 + 1 + 1 + 2, plan.Added.Count);
        var lines = plan.After.Ingredients;
        Assert.Equal([0, 1], lines.Select(l => l.Position));
        Assert.Equal("2½ cups all-purpose flour, sifted", lines[0].OriginalText);
        Assert.Equal((IngredientParseStatus.Parsed, "cup", "all-purpose flour", "sifted"), (lines[0].ParseStatus, lines[0].UnitCode, lines[0].Item, lines[0].Note));
        Assert.Equal(Quantity.Amount(Rational.Create(5, 2)), lines[0].Quantity);
        Assert.Equal([0, 1], plan.After.Allergens.Select(a => a.Position));
        Assert.Equal(Now, recipe.UpdatedAt);
        Assert.Equal(Quantity.Range(6, 8), recipe.Yield);
    }

    // An unchanged save touches no part, so an autosave writes only the recipe's row - which the facade skips too when the
    // snapshot is the same.
    [Fact]
    public void Saving_the_recipe_as_it_is_changes_no_part()
    {
        var current = Saved(Content());

        var plan = RecipeEditor.Apply(current, Edit(Echo(current)), RecipeRows.Nutrition, Now);

        Assert.Empty(plan.Added);
        Assert.Empty(plan.Updated);
        Assert.Empty(plan.Removed);
    }

    [Fact]
    public void Editing_one_line_writes_that_line_alone()
    {
        var current = Saved(Content());
        var echo = Echo(current);
        var group = echo.IngredientGroups![0]!;
        var edited = echo with
        {
            IngredientGroups = [group with { Lines = [group.Lines![0], group.Lines[1]! with { OriginalText = "1 tsp salt" }] }],
        };

        var plan = RecipeEditor.Apply(current, Edit(edited), RecipeRows.Nutrition, Now);

        var line = Assert.IsType<RecipeIngredient>(Assert.Single(plan.Updated));
        Assert.Equal(("1 tsp salt", "teaspoon", "salt"), (line.OriginalText, line.UnitCode, line.Item));
        Assert.Empty(plan.Added);
        Assert.Empty(plan.Removed);
    }

    // A line keeps its id when it moves to another group of the recipe, even when its old group is removed: the line is
    // updated and only the group removed, so the database's cascade from the group never reaches it.
    [Fact]
    public void A_line_moved_out_of_a_removed_group_keeps_its_id()
    {
        var current = Saved(Content(ingredients:
        [
            new RecipeIngredientGroupInput(null, "Crust", [new(null, "2 cups flour")]),
            new RecipeIngredientGroupInput(null, "Filling", [new(null, "6 apples")]),
        ]));
        var flour = current.Ingredients[0];
        var filling = current.IngredientGroups[1];
        var edited = Echo(current) with
        {
            IngredientGroups = [new RecipeIngredientGroupInput(filling.Id.Value, "Filling", [new(null, "6 apples"), new(flour.Id.Value, "2 cups flour")])],
        };

        var plan = RecipeEditor.Apply(current, Edit(edited), RecipeRows.Nutrition, Now);

        Assert.Same(flour, plan.After.Ingredients[1]);
        Assert.Equal((filling.Id, 1), (flour.GroupId, flour.Position));
        Assert.Contains(flour, plan.Updated);
        Assert.Contains(current.IngredientGroups[0], plan.Removed);
        Assert.DoesNotContain(flour, plan.Removed);
        Assert.Contains(current.Ingredients[1], plan.Removed);
        Assert.IsType<RecipeIngredient>(Assert.Single(plan.Added));
    }

    [Fact]
    public void Lists_are_rewritten_by_position_and_codes_by_code()
    {
        var current = Saved(Content());
        var edited = Echo(current) with
        {
            Equipment = ["Rolling pin", "9-inch pie plate"],
            Keywords = [],
            Allergens = ["wheat", "eggs"],
        };

        var plan = RecipeEditor.Apply(current, Edit(edited), RecipeRows.Nutrition, Now);

        Assert.Equal(["Rolling pin", "9-inch pie plate"], plan.After.Equipment.Select(e => e.Text));
        Assert.Contains(current.Equipment[0], plan.Updated);
        Assert.Contains(current.Keywords[0], plan.Removed);
        var wheat = current.Allergens.First(a => a.AllergenCode == "wheat");
        Assert.Contains(wheat, plan.Updated);
        Assert.Equal(0, wheat.Position);
        Assert.Contains(current.Allergens.First(a => a.AllergenCode == "milk"), plan.Removed);
        Assert.Contains(plan.Added, a => a is RecipeAllergen { AllergenCode: "eggs", Position: 1 });
    }

    [Fact]
    public void Only_ids_of_this_recipes_own_parts_may_be_named()
    {
        var current = Saved(Content());
        var stranger = Guid.NewGuid();
        var step = current.Steps[0].Id.Value;
        var edited = Echo(current) with
        {
            IngredientGroups =
            [
                new RecipeIngredientGroupInput(stranger, null, [new(step, "1 egg")]),
                new RecipeIngredientGroupInput(current.IngredientGroups[0].Id.Value, null, [new(current.Ingredients[0].Id.Value, "1 egg")]),
            ],
        };

        var unknown = RecipeEditor.UnknownParts(current, Edit(edited), "content.");

        Assert.Equal(
            ["content.ingredientGroups[0].id", "content.ingredientGroups[0].lines[0].id"],
            unknown.Keys.Order(StringComparer.Ordinal));
        Assert.All(unknown.Values, v => Assert.Equal([RecipeErrors.PartNotFoundMessage], v));
        Assert.Empty(RecipeEditor.UnknownParts(current, Edit(Echo(current)), string.Empty));
    }

    // A parser that now reads a line differently rewrites the line on its next save, though its words are unchanged.
    [Fact]
    public void A_line_whose_stored_reading_is_stale_is_read_again()
    {
        var current = Saved(Content());
        current.Ingredients[0].Item = "flour";

        var plan = RecipeEditor.Apply(current, Edit(Echo(current)), RecipeRows.Nutrition, Now);

        Assert.Equal("all-purpose flour", Assert.IsType<RecipeIngredient>(Assert.Single(plan.Updated)).Item);
    }

    [Fact]
    public void A_copy_has_the_same_words_and_new_ids()
    {
        var source = Saved(Content());
        var minted = new List<Guid>();

        var copy = RecipeEdit.Copy(source, "Copy of Apple pie", () =>
        {
            minted.Add(Guid.NewGuid());
            return minted[^1];
        });

        Assert.Equal(6, minted.Count);
        Assert.All(copy.IngredientGroups.SelectMany(g => g.Parts.Append(new RecipeEditPart(g.Id, g.Claimed, string.Empty))), p => Assert.False(p.Claimed));
        Assert.Empty(minted.Intersect(source.Ingredients.Select(i => i.Id.Value).Concat(source.IngredientGroups.Select(g => g.Id.Value))));
        Assert.Equal(source.Ingredients.Select(i => i.OriginalText), copy.IngredientGroups.SelectMany(g => g.Parts).Select(p => p.Text));
        Assert.Equal(("Copy of Apple pie", source.Recipe.Summary, source.Recipe.Yield), (copy.Title, copy.Summary, copy.Yield));
        Assert.Equal(["milk", "wheat"], copy.Allergens);
    }

    // ---- Text ----

    // Built from code units: a lone surrogate does not survive an attribute's serialization.
    public static TheoryData<string, bool, bool> Texts => new()
    {
        { "2\u00BD cups flour", false, true },
        { "a\tb", false, true },
        { "a\nb", false, false },
        { "a\r\nb", true, true },
        { "a\u0000b", true, false },
        { "a\u007Fb", true, false },
        { string.Concat(char.ConvertFromUtf32(0x1F34E), " apples"), false, true },
        { string.Concat((char)0xD83C, " apples"), true, false },
        { string.Concat("apples ", (char)0xDF4E), true, false },
        { string.Concat("apples ", (char)0xD83C), true, false },
        { string.Concat((char)0xDF4E, (char)0xD83C), true, false },
    };

    [Theory]
    [MemberData(nameof(Texts), DisableDiscoveryEnumeration = true)]
    public void Text_is_well_formed_utf16_with_no_control_character(string text, bool multiline, bool wellFormed) =>
        Assert.Equal(wellFormed, RecipeText.IsWellFormed(text, multiline));

    [Theory]
    [InlineData("", true)]
    [InlineData(" \t\r\n", true)]
    [InlineData("​﻿ ", true)]
    [InlineData(" . ", false)]
    [InlineData("­", true)]
    public void Blank_text_holds_nothing_a_reader_could_see(string text, bool blank) =>
        Assert.Equal(blank, RecipeText.IsBlank(text));

    // ---- Video hosts ----

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc123", true)]
    [InlineData("https://youtu.be/abc123", true)]
    [InlineData("https://player.vimeo.com/video/1", true)]
    [InlineData("https://WWW.YouTube.com/watch?v=abc", true)]
    [InlineData("https://vimeo.com", true)]
    [InlineData("http://www.youtube.com/watch?v=abc", false)]
    [InlineData("HTTPS://www.youtube.com/watch?v=abc", false)]
    [InlineData("https://", false)]
    [InlineData("https://evil.example/www.youtube.com", false)]
    [InlineData("https://www.youtube.com.evil.example/", false)]
    [InlineData("https://www.youtube.com@evil.example/", false)]
    [InlineData("https://user:pw@www.youtube.com/", false)]
    [InlineData("https://www.youtube.com:8443/watch", false)]
    [InlineData("https://www.youtube.com:443/watch", false)]
    [InlineData("https://music.youtube.com/watch", false)]
    [InlineData("https://www.youtube.com\\@evil.example/", false)]
    [InlineData("https://www.youtube.com/watch?v=a b", false)]
    [InlineData("https://xn--youtube-ofa.com/", false)]
    [InlineData("https://127.0.0.1/", false)]
    [InlineData("https://www.youtube.com./watch", false)]
    [InlineData("javascript:alert(1)", false)]
    public void A_video_is_on_youtube_or_vimeo_spelled_plainly(string url, bool allowed) =>
        Assert.Equal(allowed, RecipeVideoHosts.IsAllowed(url));

    // ---- The list cursor ----

    [Fact]
    public void A_list_cursor_reads_back_only_in_its_own_spelling()
    {
        var position = new RecipeListPosition(new DateTimeOffset(2026, 11, 14, 12, 0, 0, 1, TimeSpan.Zero).AddTicks(7), new RecipeId(Guid.NewGuid()));
        var cursor = RecipeListCursor.After(position);

        Assert.True(RecipeListCursor.TryRead(cursor, out var read));
        Assert.Equal(position, read);
        Assert.False(RecipeListCursor.TryRead(cursor + "=", out _));
        Assert.False(RecipeListCursor.TryRead(cursor[..^1], out _));
        Assert.False(RecipeListCursor.TryRead(string.Empty, out _));
        Assert.False(RecipeListCursor.TryRead(null, out _));
        Assert.False(RecipeListCursor.TryRead(new string('A', 65), out _));
    }

    // ---- The validators ----

    [Fact]
    public void A_whole_recipe_passes()
    {
        Assert.Empty(Failures(Content()));
        Assert.Empty(Failures(new RecipeContent("Toast", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null)));
    }

    public static TheoryData<string, Func<RecipeContent, RecipeContent>> Refusals => new()
    {
        { "title", c => c with { Title = null } },
        { "title", c => c with { Title = "\t\r\n" } },
        { "title", c => c with { Title = "Apple\npie" } },
        { "title", c => c with { Title = new string('a', RecipeLimits.MaxTitleLength + 1) } },
        { "title", c => c with { Title = "Apple \uD83C pie" } },
        { "summary", c => c with { Summary = "​" } },
        { "summary", c => c with { Summary = "Tasty\u0007" } },
        { "yield.kind", c => c with { Yield = new RecipeQuantity(QuantityKind.ToTaste, null, null) } },
        { "yield.kind", c => c with { Yield = new RecipeQuantity((QuantityKind)9, new(1, 1), null) } },
        { "yield.low", c => c with { Yield = new RecipeQuantity(QuantityKind.Amount, new(0, 1), null) } },
        { "yield.low", c => c with { Yield = new RecipeQuantity(QuantityKind.Amount, new(1, RecipeLimits.MaxQuantityTerm + 1), null) } },
        { "yield.low", c => c with { Yield = new RecipeQuantity(QuantityKind.Amount, null, null) } },
        { "yield.high", c => c with { Yield = new RecipeQuantity(QuantityKind.Amount, new(1, 1), new(2, 1)) } },
        { "yield.high", c => c with { Yield = new RecipeQuantity(QuantityKind.Range, new(4, 2), new(2, 1)) } },
        { "yield.high", c => c with { Yield = new RecipeQuantity(QuantityKind.Range, new(1, 1), null) } },
        { "yieldUnit", c => c with { Yield = null } },
        { "prepMinutes", c => c with { PrepMinutes = -1 } },
        { "cookMinutes", c => c with { CookMinutes = RecipeLimits.MaxMinutes + 1 } },
        { "additionalTimeLabel", c => c with { AdditionalMinutes = null } },
        { "videoUrl", c => c with { VideoUrl = "https://evil.example/v" } },
        { "videoUrl", c => c with { VideoUrl = "https://youtu.be/" + new string('a', RecipeLimits.MaxVideoUrlLength) } },
        { "nutrition.calories", c => c with { Nutrition = c.Nutrition! with { Calories = 410.505m } } },
        { "nutrition.calories", c => c with { Nutrition = c.Nutrition! with { Calories = -0.01m } } },
        { "nutrition.sodiumMilligrams", c => c with { Nutrition = c.Nutrition! with { SodiumMilligrams = 10_000_000m } } },
        { "nutrition.servingSize", c => c with { Nutrition = c.Nutrition! with { ServingSize = " " } } },
        { "ingredientGroups", c => c with { IngredientGroups = [.. Enumerable.Repeat(new RecipeIngredientGroupInput(null, null, []), RecipeLimits.MaxIngredientGroups + 1)] } },
        { "ingredientGroups", c => c with { IngredientGroups = [new RecipeIngredientGroupInput(null, null, [.. Enumerable.Range(0, RecipeLimits.MaxIngredientLines + 1).Select(_ => new RecipeIngredientLineInput(null, "1 egg"))])] } },
        { "ingredientGroups[0]", c => c with { IngredientGroups = [null] } },
        { "ingredientGroups[0].heading", c => c with { IngredientGroups = [new RecipeIngredientGroupInput(null, "\t", [])] } },
        { "ingredientGroups[0].id", c => c with { IngredientGroups = [new RecipeIngredientGroupInput(Guid.Empty, null, [])] } },
        { "ingredientGroups[1].id", c => c with { IngredientGroups = [new RecipeIngredientGroupInput(Twice, null, []), new RecipeIngredientGroupInput(Twice, null, [])] } },
        { "ingredientGroups[0].lines[0]", c => c with { IngredientGroups = [new RecipeIngredientGroupInput(null, null, [null])] } },
        { "ingredientGroups[0].lines[0].originalText", c => c with { IngredientGroups = [new RecipeIngredientGroupInput(null, null, [new(null, null)])] } },
        { "ingredientGroups[0].lines[0].originalText", c => c with { IngredientGroups = [new RecipeIngredientGroupInput(null, null, [new(null, "\t\t")])] } },
        { "ingredientGroups[0].lines[0].originalText", c => c with { IngredientGroups = [new RecipeIngredientGroupInput(null, null, [new(null, "1 egg\n2 eggs")])] } },
        { "ingredientGroups[0].lines[0].originalText", c => c with { IngredientGroups = [new RecipeIngredientGroupInput(null, null, [new(null, new string('a', RecipeLimits.MaxIngredientTextLength + 1))])] } },
        { "ingredientGroups[0].lines[1].id", c => c with { IngredientGroups = [new RecipeIngredientGroupInput(null, null, [new(Twice, "1 egg"), new(Twice, "2 eggs")])] } },
        { "stepGroups", c => c with { StepGroups = [new RecipeStepGroupInput(null, null, [.. Enumerable.Range(0, RecipeLimits.MaxSteps + 1).Select(_ => new RecipeStepInput(null, "Stir."))])] } },
        { "stepGroups[0].steps[0].text", c => c with { StepGroups = [new RecipeStepGroupInput(null, null, [new(null, "\r\n")])] } },
        { "equipment", c => c with { Equipment = [.. Enumerable.Repeat("Bowl", RecipeLimits.MaxEquipment + 1)] } },
        { "equipment[0]", c => c with { Equipment = [null] } },
        { "notes[0]", c => c with { Notes = [new string('a', RecipeLimits.MaxNoteLength + 1)] } },
        { "keywords[0]", c => c with { Keywords = ["fall\nbaking"] } },
        { "courses[0]", c => c with { Courses = ["brunchy"] } },
        { "courses", c => c with { Courses = ["dessert", "breakfast", "lunch", "dinner", "snack", "side-dish"] } },
        { "cuisines[1]", c => c with { Cuisines = ["american", "american"] } },
        { "diets[0]", c => c with { Diets = [null] } },
        { "allergens[0]", c => c with { Allergens = ["Milk"] } },
        { "allergens[1]", c => c with { Allergens = ["milk", "milk"] } },
        { "diets[1]", c => c with { Diets = ["vegan", "vegan"] } },
        { "cuisines", c => c with { Cuisines = ["american", "british", "chinese", "french", "greek", "indian"] } },
        { "stepGroups", c => c with { StepGroups = [.. Enumerable.Repeat(new RecipeStepGroupInput(null, null, []), RecipeLimits.MaxStepGroups + 1)] } },
        { "notes", c => c with { Notes = [.. Enumerable.Repeat("Note.", RecipeLimits.MaxNotes + 1)] } },
        { "keywords", c => c with { Keywords = [.. Enumerable.Repeat("pie", RecipeLimits.MaxKeywords + 1)] } },
        { "ingredientGroups", c => c with { IngredientGroups = [Lines(100), Lines(101)] } },
        { "nutrition.calories", c => c with { Nutrition = c.Nutrition! with { Calories = decimal.MaxValue } } },
        { "nutrition.fatGrams", c => c with { Nutrition = c.Nutrition! with { FatGrams = decimal.MinValue } } },
        { "nutrition.proteinGrams", c => c with { Nutrition = c.Nutrition! with { ProteinGrams = 10_000_000.00m } } },
    };

    private static RecipeIngredientGroupInput Lines(int count) =>
        new(null, null, [.. Enumerable.Range(0, count).Select(_ => new RecipeIngredientLineInput(null, "1 egg"))]);

    // Import and the publishing API call the same facade without the JSON edge's guard, so half a surrogate pair and a
    // control character are refused at every text field, each at its own path (the PP4.4 hand-off).
    [Fact]
    public void Broken_text_is_refused_at_every_text_field()
    {
        var lone = string.Concat("a", (char)0xD83C, "b");
        var bell = "a\u0007b";
        foreach (var broken in new[] { lone, bell })
        {
            var content = Content(
                ingredients: [new RecipeIngredientGroupInput(null, broken, [new(null, broken)])],
                steps: [new RecipeStepGroupInput(null, broken, [new(null, broken)])]) with
            {
                Title = broken,
                Summary = broken,
                YieldUnit = broken,
                AdditionalTimeLabel = broken,
                Nutrition = Content().Nutrition! with { ServingSize = broken },
                Equipment = [broken],
                Notes = [broken],
                Keywords = [broken],
            };

            Assert.Equal(
                [
                    "additionalTimeLabel", "equipment[0]", "ingredientGroups[0].heading", "ingredientGroups[0].lines[0].originalText",
                    "keywords[0]", "notes[0]", "nutrition.servingSize", "stepGroups[0].heading", "stepGroups[0].steps[0].text",
                    "summary", "title", "yieldUnit",
                ],
                Failures(content).Keys.Order(StringComparer.Ordinal));
        }
    }

    // Every limit is a limit, not one short of it.
    [Fact]
    public void A_recipe_at_every_limit_passes()
    {
        var content = Content(
            ingredients: [.. Enumerable.Range(0, RecipeLimits.MaxIngredientGroups).Select(i => i == 0 ? Lines(RecipeLimits.MaxIngredientLines) : new RecipeIngredientGroupInput(null, null, []))],
            steps: [new RecipeStepGroupInput(null, new string('h', RecipeLimits.MaxGroupHeadingLength), [.. Enumerable.Range(0, RecipeLimits.MaxSteps).Select(_ => new RecipeStepInput(null, new string('s', RecipeLimits.MaxStepTextLength)))])]) with
        {
            Title = new string('t', RecipeLimits.MaxTitleLength),
            Yield = new RecipeQuantity(QuantityKind.Range, new(1, RecipeLimits.MaxQuantityTerm), new(RecipeLimits.MaxQuantityTerm, 1)),
            PrepMinutes = RecipeLimits.MaxMinutes,
            CookMinutes = 0,
            Nutrition = Content().Nutrition! with { Calories = 9_999_999.99m, FatGrams = 0m, SodiumMilligrams = 1.50m },
            Equipment = [.. Enumerable.Repeat("Bowl", RecipeLimits.MaxEquipment)],
            Notes = [.. Enumerable.Repeat("Note.", RecipeLimits.MaxNotes)],
            Keywords = [.. Enumerable.Repeat(new string('k', RecipeLimits.MaxKeywordLength), RecipeLimits.MaxKeywords)],
            Courses = [.. Courses.All.Take(RecipeLimits.MaxCourses).Select(c => c.Code)],
            Diets = [.. Diets.All.Select(d => d.Code)],
            Allergens = [.. Allergens.All.Select(a => a.Code)],
        };

        Assert.Empty(Failures(content));
    }

    // A create names no part: every id it sends is refused at its path, as a save's unknown parts are.
    [Fact]
    public void A_create_claims_no_part()
    {
        var edit = Edit(Content(
            ingredients: [new RecipeIngredientGroupInput(Guid.NewGuid(), null, [new(null, "1 egg"), new(Guid.NewGuid(), "2 eggs")])],
            steps: [new RecipeStepGroupInput(null, null, [new(Guid.NewGuid(), "Stir.")])]));

        Assert.Equal(
            ["ingredientGroups[0].id", "ingredientGroups[0].lines[1].id", "stepGroups[0].steps[0].id"],
            RecipeEditor.ClaimedParts(edit).Keys.Order(StringComparer.Ordinal));
        Assert.Empty(RecipeEditor.ClaimedParts(Edit(Content())));
    }

    [Fact]
    public void A_version_is_written_and_read_in_one_spelling()
    {
        byte[] version = [0, 0, 0, 0, 0, 0, 0x07, 0xD1];
        var text = RecipeVersion.Write(version);

        Assert.True(RecipeVersion.TryRead(text, out var read));
        Assert.Equal(version, read);
        Assert.False(RecipeVersion.TryRead(text.ToLowerInvariant(), out _));
        Assert.False(RecipeVersion.TryRead(null, out _));
    }

    private static readonly Guid Twice = Guid.NewGuid();

    [Theory]
    [MemberData(nameof(Refusals))]
    public void A_recipe_that_breaks_a_rule_is_refused_at_that_field(string field, Func<RecipeContent, RecipeContent> change)
    {
        var failures = Failures(change(Content()));

        Assert.Contains(field, failures.Keys);
    }

    // Tabs and line breaks the tables would let by (their checks trim spaces only) are refused here, as is a third decimal
    // place a two-place column would round (the PP4.3 hand-off).
    [Fact]
    public void Steps_and_notes_may_span_lines_but_ingredient_lines_may_not()
    {
        var content = Content(steps: [new RecipeStepGroupInput(null, null, [new(null, "Stir.\r\nThen bake.")])]) with { Notes = ["One.\nTwo."] };

        Assert.Empty(Failures(content));
    }

    // As the facade's edge names them on the wire: camel-cased, the recipe's fields under content.
    [Fact]
    public async Task A_save_is_refused_without_its_version_or_its_recipe_and_failures_sit_under_content()
    {
        var validator = new UpdateRecipeRequestValidator();

        Assert.Equal(["content", "version"], await KeysAsync(validator, new UpdateRecipeRequest(null, null)));
        Assert.Equal(["version"], await KeysAsync(validator, new UpdateRecipeRequest("AAAAAAAAB9A", Content())));
        Assert.Equal(["version"], await KeysAsync(validator, new UpdateRecipeRequest("AAAAAAAAB9B=", Content())));
        Assert.Equal(["version"], await KeysAsync(validator, new UpdateRecipeRequest("AAAAAAAAAAAAAA==", Content())));
        Assert.Empty(await KeysAsync(validator, new UpdateRecipeRequest("AAAAAAAAB9E=", Content())));
        Assert.Equal(["content.title"], await KeysAsync(validator, new UpdateRecipeRequest("AAAAAAAAB9E=", Content() with { Title = null })));
    }

    // A message is the same under every culture: its numbers are grouped with commas.
    [Fact]
    public void Messages_read_alike_under_any_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var failures = Failures(Content() with { Summary = new string('a', RecipeLimits.MaxSummaryLength + 1) });
            Assert.Equal(["Keep this to 1,000 characters."], failures["summary"]);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(0, null, false)]
    [InlineData(101, null, false)]
    [InlineData(100, "nonsense", false)]
    public void A_page_asks_for_1_to_100_from_a_cursor_the_list_gave(int? limit, string? cursor, bool valid) =>
        Assert.Equal(valid, new ListRecipesQueryValidator().Validate(new ListRecipesQuery(limit, cursor, null)).IsValid);

    private static Dictionary<string, string[]> Failures(RecipeContent content) =>
        new RecipeContentValidator().Validate(content).Errors
            .GroupBy(e => e.PropertyName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray(), StringComparer.Ordinal);

    private static async Task<string[]> KeysAsync<T>(IValidator<T> validator, T request) =>
        await validator.CheckAsync(request, CancellationToken.None) is { } error
            ? [.. error.Fields.Keys.Order(StringComparer.Ordinal)]
            : [];

    private static Recipe NewRecipe() => new() { Id = new RecipeId(Guid.NewGuid()), CreatedAt = Now, UpdatedAt = Now };

    private static RecipeEdit Edit(RecipeContent content) => RecipeEdit.From(content, Guid.NewGuid);

    // The working copy a create of content would leave.
    private static RecipeWorkingCopy Saved(RecipeContent content) =>
        RecipeEditor.Apply(RecipeWorkingCopy.New(NewRecipe(), RecipeRows.Nutrition), Edit(content), RecipeRows.Nutrition, Now).After;

    // What the editor would send back for current: every part by its id, in order.
    private static RecipeContent Echo(RecipeWorkingCopy current)
    {
        var detail = RecipeDetails.From(current, null, canEdit: true);
        return new RecipeContent(
            detail.Title,
            detail.Summary,
            detail.Yield,
            detail.YieldUnit,
            detail.PrepMinutes,
            detail.CookMinutes,
            detail.AdditionalMinutes,
            detail.AdditionalTimeLabel,
            detail.VideoUrl,
            null,
            [.. detail.IngredientGroups.Select(g => new RecipeIngredientGroupInput(g.Id, g.Heading, [.. g.Lines.Select(l => new RecipeIngredientLineInput(l.Id, l.OriginalText))]))],
            [.. detail.StepGroups.Select(g => new RecipeStepGroupInput(g.Id, g.Heading, [.. g.Steps.Select(s => new RecipeStepInput(s.Id, s.Text))]))],
            detail.Equipment,
            detail.Notes,
            detail.Keywords,
            detail.Courses,
            detail.Cuisines,
            detail.Diets,
            detail.Allergens);
    }
}
