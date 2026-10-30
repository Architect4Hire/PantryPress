using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CsCheck;
using PantryPress.Domain.Modules.Recipes;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.Tests.Persistence;

namespace PantryPress.Tests.Recipes;

// PP4.4 (RCP-007, RCP-002, PP-D05): a revision freezes the recipe as one canonical snapshot. The same recipe always makes
// the same bytes - whatever order its rows were read in, whatever the culture - so the hash says whether two revisions hold
// the same recipe; the blogger's words come back exactly; and nothing is written that could not be read back as it was.
public sealed class RecipeSnapshotTests
{
    private static readonly RecipeId Recipe = new(Guid.Parse("0f8a5d2e-6b1c-4e7a-9d3f-1a2b3c4d5e6f"));
    private static readonly RecipeIngredientGroupId IngredientGroup = new(Guid.Parse("11111111-2222-4333-8444-555555555555"));
    private static readonly RecipeIngredientId Ingredient = new(Guid.Parse("22222222-3333-4444-8555-666666666666"));
    private static readonly RecipeStepGroupId StepGroup = new(Guid.Parse("33333333-4444-4555-8666-777777777777"));
    private static readonly RecipeStepId Step = new(Guid.Parse("44444444-5555-4666-8777-888888888888"));

    // The golden snapshots, approved by reading them: every property in order, nulls written, text escaped to ASCII. A
    // change here changes the bytes, and with them every revision's hash, so it is made only on purpose.
    private const string FullGolden =
        """{"id":"0f8a5d2e-6b1c-4e7a-9d3f-1a2b3c4d5e6f","title":"Apple pie","summary":"A deep-dish pie with a flaky crust.","yield":{"kind":"range","low":{"numerator":6,"denominator":1},"high":{"numerator":8,"denominator":1}},"yieldUnit":"servings","prepMinutes":45,"cookMinutes":60,"additionalMinutes":120,"additionalTimeLabel":"Cool","videoUrl":"https://www.youtube.com/watch?v=abc123","nutrition":{"servingSize":"1 slice","caloriesHundredths":41050,"fatGramsHundredths":1925,"saturatedFatGramsHundredths":null,"transFatGramsHundredths":null,"unsaturatedFatGramsHundredths":null,"cholesterolMilligramsHundredths":null,"sodiumMilligramsHundredths":23000,"carbohydrateGramsHundredths":null,"fiberGramsHundredths":null,"sugarGramsHundredths":null,"proteinGramsHundredths":0},"ingredientGroups":[{"id":"11111111-2222-4333-8444-555555555555","heading":"For the crust","lines":[{"id":"22222222-3333-4444-8555-666666666666","originalText":"2\u00BD cups all-purpose flour, sifted","parseStatus":"parsed","quantity":{"kind":"amount","low":{"numerator":5,"denominator":2},"high":null},"quantitySpan":{"start":0,"length":2},"unitCode":"cup","unitSpan":{"start":3,"length":4},"item":"all-purpose flour","note":"sifted"}]}],"stepGroups":[{"id":"33333333-4444-4555-8666-777777777777","heading":"Make the crust","steps":[{"id":"44444444-5555-4666-8777-888888888888","text":"Whisk the flour and salt, then cut in the cold butter."}]}],"equipment":["9-inch deep-dish pie plate"],"notes":["Keeps two days at room temperature."],"keywords":["fall baking"],"courses":["dessert"],"cuisines":["american"],"diets":["vegetarian"],"allergens":["milk"]}""";

    private const string BarestGolden =
        """{"id":"0f8a5d2e-6b1c-4e7a-9d3f-1a2b3c4d5e6f","title":"Toast","summary":null,"yield":null,"yieldUnit":null,"prepMinutes":null,"cookMinutes":null,"additionalMinutes":null,"additionalTimeLabel":null,"videoUrl":null,"nutrition":{"servingSize":null,"caloriesHundredths":null,"fatGramsHundredths":null,"saturatedFatGramsHundredths":null,"transFatGramsHundredths":null,"unsaturatedFatGramsHundredths":null,"cholesterolMilligramsHundredths":null,"sodiumMilligramsHundredths":null,"carbohydrateGramsHundredths":null,"fiberGramsHundredths":null,"sugarGramsHundredths":null,"proteinGramsHundredths":null},"ingredientGroups":[],"stepGroups":[],"equipment":[],"notes":[],"keywords":[],"courses":[],"cuisines":[],"diets":[],"allergens":[]}""";

    [Fact]
    public void A_whole_recipe_is_written_as_its_golden_snapshot()
    {
        var document = RecipeSnapshotJson.Write(RecipeRows.WorkingCopy(FullRows()).ToSnapshot());

        Assert.Equal(FullGolden, document.Json);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(FullGolden)), document.Hash);
    }

    [Fact]
    public void The_barest_recipe_is_written_as_its_golden_snapshot()
    {
        var recipe = new Recipe { Id = Recipe, Title = "Toast", CreatedAt = RecipeRows.Created, UpdatedAt = RecipeRows.Created };
        var copy = new RecipeWorkingCopy(
            recipe, new RecipeSnapshotNutrition(null, null, null, null, null, null, null, null, null, null, null, null),
            [], [], [], [], [], [], [], [], [], [], []);

        Assert.Equal(BarestGolden, RecipeSnapshotJson.Write(copy.ToSnapshot()).Json);
    }

    [Fact]
    public void The_bytes_are_ascii_and_the_hash_is_theirs()
    {
        var document = RecipeSnapshotJson.Write(Snapshot(lines: RecipeSchemaTests.Wording));
        var bytes = Encoding.UTF8.GetBytes(document.Json);

        Assert.All(bytes, b => Assert.True(b < 0x80, $"byte {b:x2} is outside ASCII"));
        Assert.Equal(SHA256.HashData(bytes), document.Hash);
        Assert.Equal(32, document.Hash.Length);
    }

    // RCP-002: the PP4.3 wording corpus - Unicode fractions and dashes, composed and decomposed accents, emoji and a joined
    // sequence, full-width digits, markup, tabs, line breaks, and edge spaces - comes back character for character, in
    // every place a recipe keeps text.
    [Fact]
    public void The_bloggers_wording_comes_back_exactly()
    {
        var words = RecipeSchemaTests.Wording;
        var read = RoundTrip(Snapshot(lines: words, steps: words, lists: words, title: words[0]));

        Assert.Equal(words, read.IngredientGroups.Single().Lines.Select(l => l.OriginalText));
        Assert.Equal(words, read.StepGroups.Single().Steps.Select(s => s.Text));
        Assert.Equal(words, read.Equipment);
        Assert.Equal(words, read.Notes);
        Assert.Equal(words, read.Keywords);
        Assert.Equal(words[0], read.Title);
    }

    // Any well-formed text, from anywhere in Unicode, comes back exactly, and is written again as the very same bytes.
    [Fact]
    public void Any_well_formed_text_round_trips_and_rewrites_identically()
    {
        var scalar = Gen.Int[0, 0x10FFFF].Where(c => c is < 0xD800 or > 0xDFFF).Select(char.ConvertFromUtf32);
        var text = scalar.Array[1, 40].Select(parts => string.Concat(parts));

        text.Array[1, 6].Sample(texts =>
        {
            var snapshot = Snapshot(lines: texts, steps: texts, lists: texts);
            var document = RecipeSnapshotJson.Write(snapshot);
            var read = RecipeSnapshotJson.Read(RecipeSnapshotJson.CurrentVersion, document.Json);

            Assert.Equal(texts, read.IngredientGroups.Single().Lines.Select(l => l.OriginalText));
            Assert.Equal(texts, read.StepGroups.Single().Steps.Select(s => s.Text));
            Assert.Equal(document.Json, RecipeSnapshotJson.Write(read).Json);
        });
    }

    // The order a reader sees is by position, and where reordering has left two parts on one position, by id - so rows
    // read in any order make one snapshot.
    [Fact]
    public void Rows_read_in_any_order_make_the_same_bytes()
    {
        var recipe = RecipeRows.NewRecipe(Recipe);
        var first = new RecipeIngredientGroup { Id = new(Guid.Parse("00000000-0000-4000-8000-000000000001")), RecipeId = Recipe, Position = 0 };
        var second = new RecipeIngredientGroup { Id = new(Guid.Parse("00000000-0000-4000-8000-000000000002")), RecipeId = Recipe, Position = 0 };
        var third = new RecipeIngredientGroup { Id = new(Guid.Parse("00000000-0000-4000-8000-000000000003")), RecipeId = Recipe, Position = 1 };
        RecipeIngredient Line(RecipeIngredientGroup group, int position, string text, int n) => new()
        {
            Id = new(Guid.Parse($"00000000-0000-4000-8000-0000000001{n:d2}")),
            GroupId = group.Id,
            Position = position,
            OriginalText = text,
            ParseStatus = IngredientParseStatus.Unparsed,
        };
        RecipeIngredient[] lines =
        [
            Line(first, 1, "b", 1), Line(first, 0, "a", 2), Line(first, 0, "a again", 3),
            Line(second, 0, "c", 4), Line(third, 0, "d", 5),
        ];
        RecipeCourse[] courses =
        [
            new() { RecipeId = Recipe, CourseCode = "main-course", Position = 1 },
            new() { RecipeId = Recipe, CourseCode = "side-dish", Position = 0 },
            new() { RecipeId = Recipe, CourseCode = "dessert", Position = 0 },
        ];
        RecipeKeyword[] keywords =
        [
            new() { RecipeId = Recipe, Position = 2, Text = "z" },
            new() { RecipeId = Recipe, Position = 0, Text = "x" },
            new() { RecipeId = Recipe, Position = 1, Text = "y" },
        ];

        string Written(Func<IEnumerable<object>, IEnumerable<object>> order) => RecipeSnapshotJson.Write(new RecipeWorkingCopy(
            recipe,
            RecipeRows.Nutrition,
            [.. order(new[] { first, second, third }).Cast<RecipeIngredientGroup>()],
            [.. order(lines).Cast<RecipeIngredient>()],
            [], [], [], [],
            [.. order(keywords).Cast<RecipeKeyword>()],
            [.. order(courses).Cast<RecipeCourse>()],
            [], [], []).ToSnapshot()).Json;

        var asRead = Written(rows => rows);
        Assert.Equal(asRead, Written(rows => rows.Reverse()));
        Assert.Equal(asRead, Written(rows => rows.OrderBy(r => r.GetHashCode())));

        var snapshot = RecipeSnapshotJson.Read(RecipeSnapshotJson.CurrentVersion, asRead);
        Assert.Equal([first.Id, second.Id, third.Id], snapshot.IngredientGroups.Select(g => g.Id));
        Assert.Equal(["a", "a again", "b"], snapshot.IngredientGroups[0].Lines.Select(l => l.OriginalText));
        Assert.Equal(["dessert", "side-dish", "main-course"], snapshot.Courses);
        Assert.Equal(["x", "y", "z"], snapshot.Keywords);
    }

    [Fact]
    public void The_bytes_are_the_same_in_every_culture()
    {
        var expected = RecipeSnapshotJson.Write(RecipeRows.WorkingCopy(FullRows()).ToSnapshot()).Json;
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "de-DE", "fr-FR", "ar-SA", "tr-TR", "hi-IN" })
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(name);
                Assert.Equal(expected, RecipeSnapshotJson.Write(RecipeRows.WorkingCopy(FullRows()).ToSnapshot()).Json);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = original;
        }
    }

    public static TheoryData<Quantity?> Quantities => new()
    {
        null,
        Quantity.ToTaste,
        Quantity.Amount(Rational.Create(5, 2)),
        Quantity.Amount(RecipeLimits.MaxQuantityTerm),
        Quantity.Amount(Rational.Create(1, RecipeLimits.MaxQuantityTerm)),
        Quantity.Range(2, 3),
        Quantity.Range(Rational.Create(1, 3), Rational.Create(1, 2)),
    };

    [Theory]
    [MemberData(nameof(Quantities))]
    public void Every_quantity_comes_back_as_the_exact_value_it_was(Quantity? quantity)
    {
        var line = RecipeRows.NewIngredient(IngredientGroup, Ingredient);
        line.Quantity = quantity;
        var rows = FullRows() with { Ingredient = line };
        if (quantity is { HasAmount: true })
        {
            rows.Recipe.Yield = quantity;
        }

        var read = RoundTrip(RecipeRows.WorkingCopy(rows).ToSnapshot());

        Assert.Equal(quantity, read.IngredientGroups.Single().Lines.Single().Quantity);
        Assert.Equal(quantity is { HasAmount: true } ? quantity : Quantity.Range(6, 8), read.Yield);
    }

    [Fact]
    public void An_unparsed_line_comes_back_with_none_of_its_parts()
    {
        var line = new RecipeIngredient
        {
            Id = Ingredient,
            GroupId = IngredientGroup,
            OriginalText = "a good glug of olive oil",
            ParseStatus = IngredientParseStatus.Unparsed,
        };

        var read = RoundTrip(RecipeRows.WorkingCopy(FullRows() with { Ingredient = line }).ToSnapshot()).IngredientGroups.Single().Lines.Single();

        Assert.Equal(
            new RecipeSnapshotIngredient(Ingredient, "a good glug of olive oil", IngredientParseStatus.Unparsed, null, null, null, null, null, null),
            read);
    }

    [Fact]
    public void Nutrition_comes_back_in_hundredths()
    {
        var read = RoundTrip(RecipeRows.WorkingCopy(FullRows()).ToSnapshot());

        Assert.Equal(RecipeRows.Nutrition, read.Nutrition);
    }

    // A lone surrogate is not text. Written, it would come back as U+FFFD - a character the blogger never typed - so it is
    // refused wherever a recipe keeps text, never replaced. Every text the writer writes is a case.
    [Theory]
    [InlineData("title")]
    [InlineData("summary")]
    [InlineData("yieldUnit")]
    [InlineData("additionalTimeLabel")]
    [InlineData("videoUrl")]
    [InlineData("servingSize")]
    [InlineData("heading")]
    [InlineData("originalText")]
    [InlineData("unitCode")]
    [InlineData("item")]
    [InlineData("note")]
    [InlineData("text")]
    [InlineData("equipment")]
    [InlineData("notes")]
    [InlineData("keywords")]
    [InlineData("courses")]
    [InlineData("cuisines")]
    [InlineData("diets")]
    [InlineData("allergens")]
    public void A_lone_surrogate_is_refused_wherever_it_is(string field)
    {
        var lone = string.Concat("half ", (char)0xD83C, " a moon");
        var full = RecipeRows.WorkingCopy(FullRows()).ToSnapshot();
        var group = full.IngredientGroups.Single();
        var line = group.Lines.Single();
        var snapshot = field switch
        {
            "title" => full with { Title = lone },
            "summary" => full with { Summary = lone },
            "yieldUnit" => full with { YieldUnit = lone },
            "additionalTimeLabel" => full with { AdditionalTimeLabel = lone },
            "videoUrl" => full with { VideoUrl = lone },
            "servingSize" => full with { Nutrition = full.Nutrition with { ServingSize = lone } },
            "heading" => full with { IngredientGroups = [group with { Heading = lone }] },
            "originalText" => full with { IngredientGroups = [group with { Lines = [line with { OriginalText = lone }] }] },
            "unitCode" => full with { IngredientGroups = [group with { Lines = [line with { UnitCode = lone }] }] },
            "item" => full with { IngredientGroups = [group with { Lines = [line with { Item = lone }] }] },
            "note" => full with { IngredientGroups = [group with { Lines = [line with { Note = lone }] }] },
            "text" => full with { StepGroups = [full.StepGroups.Single() with { Steps = [new RecipeSnapshotStep(Step, lone)] }] },
            "equipment" => full with { Equipment = [lone] },
            "notes" => full with { Notes = [lone] },
            "keywords" => full with { Keywords = [lone] },
            "courses" => full with { Courses = [lone] },
            "cuisines" => full with { Cuisines = [lone] },
            "diets" => full with { Diets = [lone] },
            _ => full with { Allergens = [lone] },
        };

        var error = Assert.Throws<ArgumentException>(() => RecipeSnapshotJson.Write(snapshot));
        Assert.Contains("lone surrogate", error.Message, StringComparison.Ordinal);
        Assert.Contains($"recipe's {field} ", error.Message, StringComparison.Ordinal);
    }

    // From review (test-gap-analyzer): every nutrient is written under its own name. Each holds a different value, so two
    // swapped in the writer or its list of names would show; the round trip proves the reader takes each from its own.
    [Fact]
    public void Every_nutrition_value_is_written_under_its_own_name()
    {
        var nutrition = new RecipeSnapshotNutrition("1 wedge", 101, 202, 303, 404, 505, 606, 707, 808, 909, 1_010, 1_111);
        var snapshot = RecipeRows.WorkingCopy(FullRows()).ToSnapshot() with { Nutrition = nutrition };
        var json = RecipeSnapshotJson.Write(snapshot).Json;

        using var document = System.Text.Json.JsonDocument.Parse(json);
        var written = document.RootElement.GetProperty("nutrition");
        Assert.Equal("1 wedge", written.GetProperty("servingSize").GetString());
        Assert.Equal(
            new Dictionary<string, long>
            {
                ["caloriesHundredths"] = 101,
                ["fatGramsHundredths"] = 202,
                ["saturatedFatGramsHundredths"] = 303,
                ["transFatGramsHundredths"] = 404,
                ["unsaturatedFatGramsHundredths"] = 505,
                ["cholesterolMilligramsHundredths"] = 606,
                ["sodiumMilligramsHundredths"] = 707,
                ["carbohydrateGramsHundredths"] = 808,
                ["fiberGramsHundredths"] = 909,
                ["sugarGramsHundredths"] = 1_010,
                ["proteinGramsHundredths"] = 1_111,
            },
            written.EnumerateObject().Skip(1).ToDictionary(p => p.Name, p => p.Value.GetInt64()));
        Assert.Equal(nutrition, RoundTrip(snapshot).Nutrition);
    }

    // From review: how each kind of character is escaped is part of the bytes, and so of the hash a repeat is decided by.
    // HTML-sensitive characters, quotes and backslashes, controls, the line and paragraph separators, characters outside
    // ASCII, and a surrogate pair are each pinned, so a runtime that escaped one differently shows here rather than as a
    // revision nobody changed. If this moves, the format's version moves with it; it is never simply re-approved.
    public static string EveryCharacterClass { get; } = string.Concat(
        "<>&'+`\"\\",
        "\t\n\r",
        string.Concat((char)0x00, (char)0x1F, (char)0x7F, (char)0x2028, (char)0x2029, (char)0xA0, (char)0xFEFF),
        string.Concat((char)0xBD, (char)0xE9, '2', (char)0x300),
        char.ConvertFromUtf32(0x1F34E),
        string.Concat(char.ConvertFromUtf32(0x1F469), (char)0x200D, char.ConvertFromUtf32(0x1F373)),
        string.Concat((char)0xFF12, " cups"));

    private const string EscapedGolden =
        """
        "\u003C\u003E\u0026\u0027\u002B\u0060\u0022\\\t\n\r\u0000\u001F\u007F\u2028\u2029\u00A0\uFEFF\u00BD\u00E92\u0300\uD83C\uDF4E\uD83D\uDC69\u200D\uD83C\uDF73\uFF12 cups"
        """;

    [Fact]
    public void The_escaping_of_every_character_class_is_golden()
    {
        var snapshot = RecipeRows.WorkingCopy(FullRows()).ToSnapshot() with { Title = EveryCharacterClass };

        var json = RecipeSnapshotJson.Write(snapshot).Json;

        Assert.Equal(EscapedGolden, TitleOf(json));
        Assert.Equal(EveryCharacterClass, RecipeSnapshotJson.Read(RecipeSnapshotJson.CurrentVersion, json).Title);
    }

    /// <summary>The title's JSON string as written, quotes included.</summary>
    internal static string TitleOf(string json)
    {
        var start = json.IndexOf("\"title\":", StringComparison.Ordinal) + "\"title\":".Length;
        return json[start..json.IndexOf(",\"summary\":", start, StringComparison.Ordinal)];
    }

    [Fact]
    public void A_trailing_or_reversed_surrogate_is_refused_too()
    {
        Assert.Throws<ArgumentException>(() => RecipeSnapshotJson.Write(Snapshot(title: string.Concat("end ", (char)0xD83D))));
        Assert.Throws<ArgumentException>(() => RecipeSnapshotJson.Write(Snapshot(title: string.Concat((char)0xDE00, (char)0xD83D))));
        Assert.Equal("🍎", RoundTrip(Snapshot(title: "🍎")).Title);
    }

    // A part filed under another recipe or group is a bug in whatever read them, never something to write.
    [Fact]
    public void A_part_that_is_not_this_recipes_is_a_bug()
    {
        var rows = FullRows();
        var other = new RecipeId(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => RecipeRows.WorkingCopy(rows with
        {
            Keyword = new RecipeKeyword { RecipeId = other, Position = 0, Text = "x" },
        }).ToSnapshot());
        Assert.Throws<InvalidOperationException>(() => RecipeRows.WorkingCopy(rows with
        {
            StepGroup = RecipeRows.NewStepGroup(other, StepGroup),
        }).ToSnapshot());
        Assert.Throws<InvalidOperationException>(() => RecipeRows.WorkingCopy(rows with
        {
            Ingredient = RecipeRows.NewIngredient(new RecipeIngredientGroupId(Guid.NewGuid())),
        }).ToSnapshot());
    }

    [Fact]
    public void A_snapshot_read_back_is_written_again_as_the_same_bytes()
    {
        var json = RecipeSnapshotJson.Write(RecipeRows.WorkingCopy(FullRows()).ToSnapshot()).Json;

        Assert.Equal(json, RecipeSnapshotJson.Write(RecipeSnapshotJson.Read(RecipeSnapshotJson.CurrentVersion, json)).Json);
    }

    [Fact]
    public void A_format_never_written_is_not_read()
    {
        var json = RecipeSnapshotJson.Write(RecipeRows.WorkingCopy(FullRows()).ToSnapshot()).Json;

        Assert.Throws<NotSupportedException>(() => RecipeSnapshotJson.Read(0, json));
        Assert.Throws<NotSupportedException>(() => RecipeSnapshotJson.Read(RecipeSnapshotJson.CurrentVersion + 1, json));
    }

    // The reader is as strict as the writer: what the writer never makes is a bug, and throws.
    public static TheoryData<string, string> Malformations => new()
    {
        { "\"summary\":", "\"summery\":" },
        { ",\"summary\":\"A deep-dish pie with a flaky crust.\"", string.Empty },
        { "\"title\":\"Apple pie\",\"summary\":\"A deep-dish pie with a flaky crust.\"", "\"summary\":\"A deep-dish pie with a flaky crust.\",\"title\":\"Apple pie\"" },
        { "\"allergens\":[\"milk\"]", "\"allergens\":[\"milk\"],\"extra\":1" },
        { "\"prepMinutes\":45", "\"prepMinutes\":45.0" },
        { "\"prepMinutes\":45", "\"prepMinutes\":\"45\"" },
        { "\"numerator\":5", "\"numerator\":2.5" },
        { "\"kind\":\"amount\"", "\"kind\":\"range\"" },
        { "\"kind\":\"amount\"", "\"kind\":\"to-taste\"" },
        { "\"parseStatus\":\"parsed\"", "\"parseStatus\":\"Parsed\"" },
        { "\"courses\":[\"dessert\"]", "\"courses\":[null]" },
        { "\"courses\":[\"dessert\"]", "\"courses\":\"dessert\"" },
        { "\"title\":\"Apple pie\"", "\"title\":null" },
        { "\"id\":\"0f8a5d2e-6b1c-4e7a-9d3f-1a2b3c4d5e6f\"", "\"id\":\"0f8a5d2e6b1c4e7a9d3f1a2b3c4d5e6f\"" },
    };

    [Theory]
    [MemberData(nameof(Malformations))]
    public void Reading_refuses_what_the_writer_never_makes(string written, string malformed)
    {
        var json = RecipeSnapshotJson.Write(RecipeRows.WorkingCopy(FullRows()).ToSnapshot()).Json;
        Assert.Contains(written, json, StringComparison.Ordinal);

        Assert.ThrowsAny<Exception>(() => RecipeSnapshotJson.Read(RecipeSnapshotJson.CurrentVersion, json.Replace(written, malformed, StringComparison.Ordinal)));
    }

    [Fact]
    public void A_version_is_current_only_when_it_is_the_eight_bytes_read()
    {
        byte[] version = [0, 0, 0, 0, 0, 0, 7, 209];

        Assert.True(RecipeRevisionRules.IsCurrent(version, [0, 0, 0, 0, 0, 0, 7, 209]));
        Assert.False(RecipeRevisionRules.IsCurrent(version, [0, 0, 0, 0, 0, 0, 7, 210]));
        Assert.False(RecipeRevisionRules.IsCurrent([], []));
        Assert.False(RecipeRevisionRules.IsCurrent([7, 209], [7, 209]));
    }

    [Fact]
    public void A_repeat_is_the_same_hash_as_the_latest_of_its_kind()
    {
        var hash = SHA256.HashData("a"u8);

        Assert.True(RecipeRevisionRules.IsRepeat(hash, (byte[])hash.Clone()));
        Assert.False(RecipeRevisionRules.IsRepeat(SHA256.HashData("b"u8), hash));
        Assert.False(RecipeRevisionRules.IsRepeat([], hash));
    }

    [Fact]
    public void Numbers_count_up_from_one_and_never_wrap()
    {
        Assert.Equal(1, RecipeRevisionRules.NextNumber(null));
        Assert.Equal(4, RecipeRevisionRules.NextNumber(3));
        Assert.Throws<OverflowException>(() => RecipeRevisionRules.NextNumber(int.MaxValue));
    }

    private static RecipeRowSet FullRows() => RecipeRows.New(Recipe, IngredientGroup, Ingredient, StepGroup, Step);

    private static RecipeSnapshot RoundTrip(RecipeSnapshot snapshot) =>
        RecipeSnapshotJson.Read(RecipeSnapshotJson.CurrentVersion, RecipeSnapshotJson.Write(snapshot).Json);

    // The full recipe, with its lines, steps, and free-text lists replaced where given, each item its own row.
    private static RecipeSnapshot Snapshot(
        IReadOnlyList<string>? lines = null,
        IReadOnlyList<string>? steps = null,
        IReadOnlyList<string>? lists = null,
        string? title = null)
    {
        var rows = FullRows();
        if (title is not null)
        {
            rows.Recipe.Title = title;
        }

        RecipeIngredient[] ingredients = lines is null
            ? [rows.Ingredient]
            : [.. lines.Select((text, i) => new RecipeIngredient
            {
                Id = new RecipeIngredientId(Guid.NewGuid()),
                GroupId = IngredientGroup,
                Position = i,
                OriginalText = text,
                ParseStatus = IngredientParseStatus.Unparsed,
            })];
        RecipeStep[] stepRows = steps is null
            ? [rows.Step]
            : [.. steps.Select((text, i) => new RecipeStep { Id = new RecipeStepId(Guid.NewGuid()), GroupId = StepGroup, Position = i, Text = text })];
        var items = lists ?? [];

        return new RecipeWorkingCopy(
            rows.Recipe,
            RecipeRows.Nutrition,
            [rows.IngredientGroup],
            ingredients,
            [rows.StepGroup],
            stepRows,
            lists is null ? [rows.Equipment] : [.. items.Select((t, i) => new RecipeEquipment { RecipeId = Recipe, Position = i, Text = t })],
            lists is null ? [rows.Note] : [.. items.Select((t, i) => new RecipeNote { RecipeId = Recipe, Position = i, Text = t })],
            lists is null ? [rows.Keyword] : [.. items.Select((t, i) => new RecipeKeyword { RecipeId = Recipe, Position = i, Text = t })],
            [rows.Course],
            [rows.Cuisine],
            [rows.Diet],
            [rows.Allergen]).ToSnapshot();
    }
}
