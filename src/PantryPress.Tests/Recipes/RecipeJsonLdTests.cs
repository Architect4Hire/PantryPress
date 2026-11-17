using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.10 (RCP-008, RCP-006, RCP-002): the rules of the JSON-LD generator one at a time - what it writes, what it reports
// missing, what it never writes, and what it refuses. The corpus (RecipeJsonLdCorpusTests) shows whole recipes.
public sealed class RecipeJsonLdTests
{
    private static readonly RecipeSnapshotNutrition NoNutrition = new(null, null, null, null, null, null, null, null, null, null, null, null);

    private static readonly RecipeJsonLdPage FullPage = new(
        "https://apple-pie.pantrypress.app/apple-pie",
        ["https://apple-pie.pantrypress.app/media/pie.jpg"],
        "Maya Brooks",
        new DateOnly(2026, 10, 1));

    internal static RecipeSnapshot Snapshot(
        string title = "Apple pie",
        string? summary = null,
        Quantity? yield = null,
        string? yieldUnit = null,
        int? prep = null,
        int? cook = null,
        int? additional = null,
        RecipeSnapshotNutrition? nutrition = null,
        IReadOnlyList<RecipeSnapshotIngredientGroup>? ingredients = null,
        IReadOnlyList<RecipeSnapshotStepGroup>? steps = null,
        IReadOnlyList<string>? equipment = null,
        IReadOnlyList<string>? notes = null,
        IReadOnlyList<string>? keywords = null,
        IReadOnlyList<string>? courses = null,
        IReadOnlyList<string>? cuisines = null,
        IReadOnlyList<string>? diets = null,
        IReadOnlyList<string>? allergens = null,
        string? videoUrl = null,
        string? additionalLabel = null) =>
        new(
            new RecipeId(Guid.Parse("0f8a5d2e-6b1c-4e7a-9d3f-1a2b3c4d5e6f")),
            title, summary, yield, yieldUnit, prep, cook, additional, additionalLabel, videoUrl,
            nutrition ?? NoNutrition, ingredients ?? [], steps ?? [], equipment ?? [], notes ?? [], keywords ?? [],
            courses ?? [], cuisines ?? [], diets ?? [], allergens ?? []);

    internal static RecipeSnapshotIngredientGroup Lines(string? heading, params string[] texts) =>
        new(new RecipeIngredientGroupId(Guid.NewGuid()), heading,
            [.. texts.Select(t => new RecipeSnapshotIngredient(
                new RecipeIngredientId(Guid.NewGuid()), t, IngredientParseStatus.Unparsed, null, null, null, null, null, null))]);

    internal static RecipeSnapshotStepGroup Steps(string? heading, params string[] texts) =>
        new(new RecipeStepGroupId(Guid.NewGuid()), heading,
            [.. texts.Select(t => new RecipeSnapshotStep(new RecipeStepId(Guid.NewGuid()), t))]);

    private static JsonObject Json(RecipeSnapshot snapshot, RecipeJsonLdPage? page = null) =>
        JsonNode.Parse(RecipeJsonLd.Write(snapshot, page ?? FullPage).Json)!.AsObject();

    private static IReadOnlyList<RecipeJsonLdGap> Gaps(RecipeSnapshot snapshot, RecipeJsonLdPage? page = null) =>
        RecipeJsonLd.Write(snapshot, page ?? FullPage).Gaps;

    // The codes become a wire contract when PP4.15 carries them; pinned now, so a rename is seen.
    [Fact]
    public void The_gap_codes_and_their_needs_are_pinned()
    {
        (RecipeJsonLdGap Gap, string Wire, RecipeJsonLdNeed Need)[] expected =
        [
            (RecipeJsonLdGap.Name, "name", RecipeJsonLdNeed.Required),
            (RecipeJsonLdGap.Image, "image", RecipeJsonLdNeed.Required),
            (RecipeJsonLdGap.YieldForNutrition, "yield-for-nutrition", RecipeJsonLdNeed.Required),
            (RecipeJsonLdGap.Author, "author", RecipeJsonLdNeed.Recommended),
            (RecipeJsonLdGap.DatePublished, "date-published", RecipeJsonLdNeed.Recommended),
            (RecipeJsonLdGap.Description, "description", RecipeJsonLdNeed.Recommended),
            (RecipeJsonLdGap.Times, "times", RecipeJsonLdNeed.Recommended),
            (RecipeJsonLdGap.PrepAndCookTime, "prep-and-cook-time", RecipeJsonLdNeed.Recommended),
            (RecipeJsonLdGap.RecipeYield, "recipe-yield", RecipeJsonLdNeed.Recommended),
            (RecipeJsonLdGap.Ingredients, "ingredients", RecipeJsonLdNeed.Recommended),
            (RecipeJsonLdGap.Instructions, "instructions", RecipeJsonLdNeed.Recommended),
            (RecipeJsonLdGap.Courses, "courses", RecipeJsonLdNeed.Recommended),
            (RecipeJsonLdGap.Cuisines, "cuisines", RecipeJsonLdNeed.Recommended),
            (RecipeJsonLdGap.Keywords, "keywords", RecipeJsonLdNeed.Recommended),
            (RecipeJsonLdGap.Calories, "calories", RecipeJsonLdNeed.Recommended),
        ];

        Assert.Equal(expected.Select(e => e.Gap), Enum.GetValues<RecipeJsonLdGap>());
        Assert.Equal(expected.Select(e => (e.Wire, e.Need)), expected.Select(e => (RecipeJsonLdCorpus.WireName(e.Gap), RecipeJsonLdGaps.NeedOf(e.Gap))));
        Assert.Equal(["required", "recommended"], Enum.GetValues<RecipeJsonLdNeed>().Select(n => JsonNamingPolicy.KebabCaseLower.ConvertName(n.ToString())));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecipeJsonLdGaps.NeedOf((RecipeJsonLdGap)expected.Length));
    }

    // RESTRICTION: no inferred facts, ratings, or nutrition. A recipe with every field filled writes exactly these
    // properties, and never a rating, a review, an allergen, the notes, the additional time's label, or the video.
    [Fact]
    public void Only_the_listed_properties_are_ever_written()
    {
        var full = Snapshot(
            summary: "A pie.", yield: Quantity.Amount(8), yieldUnit: "slices", prep: 30, cook: 60, additional: 120,
            nutrition: NoNutrition with { ServingSize = "1 slice", Calories = 30_000 },
            ingredients: [Lines(null, "6 apples")], steps: [Steps(null, "Bake it.")], equipment: ["Pie plate"],
            notes: ["Keeps two days."], keywords: ["fall"], courses: ["dessert"], cuisines: ["american"],
            diets: ["vegetarian"], allergens: ["milk", "wheat"], videoUrl: "https://www.youtube.com/watch?v=abc123",
            additionalLabel: "Cool");

        var json = Json(full);

        Assert.Equal(
            [
                "@context", "@type", "@id", "name", "description", "image", "author", "datePublished", "prepTime", "cookTime",
                "totalTime", "recipeYield", "recipeCategory", "recipeCuisine", "keywords", "suitableForDiet", "tool",
                "recipeIngredient", "recipeInstructions", "nutrition",
            ],
            json.Select(p => p.Key));
        Assert.Equal(["@type", "servingSize", "calories"], json["nutrition"]!.AsObject().Select(p => p.Key));
        Assert.Empty(Gaps(full));
    }

    [Fact]
    public void The_node_for_a_graph_has_no_context()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            RecipeJsonLd.WriteNode(writer, Snapshot(), RecipeJsonLdPage.None, withContext: false);
        }

        var node = JsonNode.Parse(buffer.WrittenSpan)!.AsObject();
        Assert.Equal(["@type", "name"], node.Select(p => p.Key));
    }

    // A page's graph (PP7.10) brings its own writer. Any encoder but the default could let a blogger's </script> close the
    // element the JSON-LD sits in, and indenting would make other bytes for the same recipe, so both are refused before
    // anything is written. A compact writer with the default encoder - stated or left unset - writes the hostile title inert.
    public static TheoryData<bool, bool> RefusedWriters { get; } = new() { { true, false }, { false, true }, { true, true } };

    [Theory]
    [MemberData(nameof(RefusedWriters))]
    public void A_writer_that_would_change_the_bytes_or_leave_html_unescaped_is_refused(bool relaxed, bool indented)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var options = new JsonWriterOptions
        {
            Indented = indented,
            Encoder = relaxed ? System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping : null,
        };
        using (var writer = new Utf8JsonWriter(buffer, options))
        {
            Assert.Throws<ArgumentException>(() => RecipeJsonLd.WriteNode(writer, Snapshot(), RecipeJsonLdPage.None, withContext: true));
        }

        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void A_compact_default_writer_writes_the_same_bytes_and_html_inert()
    {
        var hostile = Snapshot(title: "Pie </script><script>alert(1)</script>");
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default }))
        {
            RecipeJsonLd.WriteNode(writer, hostile, RecipeJsonLdPage.None, withContext: true);
        }

        var written = System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
        Assert.Equal(RecipeJsonLd.Write(hostile, RecipeJsonLdPage.None).Json, written);
        Assert.DoesNotContain("<", written, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blank_title_is_no_name()
    {
        var json = Json(Snapshot(title: " \t "));

        Assert.False(json.ContainsKey("name"));
        Assert.Contains(RecipeJsonLdGap.Name, Gaps(Snapshot(title: " \t ")));
    }

    [Fact]
    public void A_text_is_written_exactly_never_trimmed()
    {
        var json = Json(Snapshot(title: "  Apple pie ", summary: " A pie.\n", ingredients: [Lines(null, " 2 cups flour ")]));

        Assert.Equal("  Apple pie ", (string)json["name"]!);
        Assert.Equal(" A pie.\n", (string)json["description"]!);
        Assert.Equal(" 2 cups flour ", (string)json["recipeIngredient"]![0]!);
    }

    [Fact]
    public void Blank_list_items_are_left_out_and_an_all_blank_list_is_a_gap()
    {
        var snapshot = Snapshot(
            ingredients: [Lines(null, " ", "flour", "")], steps: [Steps("  ", "  ")], keywords: [" ", "fall"], equipment: [""]);
        var json = Json(snapshot);

        Assert.Equal(["flour"], json["recipeIngredient"]!.AsArray().Select(n => (string)n!));
        Assert.Equal("fall", (string)json["keywords"]!);
        Assert.False(json.ContainsKey("tool"));
        Assert.False(json.ContainsKey("recipeInstructions"));
        Assert.Contains(RecipeJsonLdGap.Instructions, Gaps(snapshot));
    }

    // Only an ingredient line's own words reach JSON-LD: how it parsed changes nothing.
    [Fact]
    public void An_ingredient_line_is_its_original_text_however_it_parsed()
    {
        var parsed = new RecipeSnapshotIngredient(
            new RecipeIngredientId(Guid.NewGuid()), "2½ cups flour", IngredientParseStatus.Parsed,
            Quantity.Amount(Rational.Create(5, 2)), new RecipeSnapshotSpan(0, 2), "cup", new RecipeSnapshotSpan(3, 4), "flour", null);
        var asParsed = Snapshot(ingredients: [new(new RecipeIngredientGroupId(Guid.NewGuid()), "Dry", [parsed])]);
        var asUnparsed = Snapshot(ingredients: [Lines(null, "2½ cups flour")]);

        Assert.Equal(RecipeJsonLd.Write(asUnparsed, FullPage).Json, RecipeJsonLd.Write(asParsed, FullPage).Json);
    }

    [Theory]
    [InlineData(null, null, null, null, null, true, false)]
    [InlineData(10, null, null, "PT10M", null, false, true)]
    [InlineData(null, 10, null, null, "PT10M", false, true)]
    [InlineData(null, null, 60, null, null, false, false)]
    [InlineData(0, 0, null, "PT0M", "PT0M", false, false)]
    [InlineData(20, 0, 240, "PT20M", "PT0M", false, false)]
    public void Times_are_written_as_entered_with_their_total(
        int? prep, int? cook, int? additional, string? prepTime, string? cookTime, bool timesGap, bool pairGap)
    {
        var snapshot = Snapshot(prep: prep, cook: cook, additional: additional);
        var json = Json(snapshot);
        var gaps = Gaps(snapshot);

        Assert.Equal(prepTime, (string?)json["prepTime"]);
        Assert.Equal(cookTime, (string?)json["cookTime"]);
        Assert.Equal(
            timesGap ? null : RecipeTimes.From(prep, cook, additional).Total!.Value.IsoDuration,
            (string?)json["totalTime"]);
        Assert.Equal(timesGap, gaps.Contains(RecipeJsonLdGap.Times));
        Assert.Equal(pairGap, gaps.Contains(RecipeJsonLdGap.PrepAndCookTime));
    }

    public static TheoryData<Quantity, string?, string> Yields { get; } = new()
    {
        { Quantity.Amount(4), "servings", "4 servings" },
        { Quantity.Amount(4), null, "4" },
        { Quantity.Amount(4), "   ", "4" },
        { Quantity.Amount(Rational.Create(3, 2)), "loaves", "1.5 loaves" },
        { Quantity.Amount(Rational.Create(4, 3)), "loaves", "1 1/3 loaves" },
        { Quantity.Range(4, 6), "servings", "4–6 servings" },
        { Quantity.Range(Rational.Create(1, 2), Rational.Create(2, 3)), "cups", "1/2–2/3 cups" },
        { Quantity.Amount(1), " 9-inch pie ", "1  9-inch pie " },
    };

    [Theory]
    [MemberData(nameof(Yields))]
    public void The_yield_is_its_amount_and_the_bloggers_word(Quantity yield, string? unit, string expected) =>
        Assert.Equal(expected, (string)Json(Snapshot(yield: yield, yieldUnit: unit))["recipeYield"]!);

    // A yield is never "to taste" (PP4.3); one that somehow were has no amount, and is no yield.
    [Fact]
    public void A_yield_with_no_amount_is_no_yield()
    {
        var snapshot = Snapshot(yield: Quantity.ToTaste, yieldUnit: "servings");

        Assert.False(Json(snapshot).ContainsKey("recipeYield"));
        Assert.Contains(RecipeJsonLdGap.RecipeYield, Gaps(snapshot));
    }

    [Fact]
    public void Nutrition_with_no_yield_makes_the_yield_required()
    {
        var withNutrition = Snapshot(nutrition: NoNutrition with { ProteinGrams = 300 });
        var without = Snapshot();

        Assert.Contains(RecipeJsonLdGap.YieldForNutrition, Gaps(withNutrition));
        Assert.DoesNotContain(RecipeJsonLdGap.RecipeYield, Gaps(withNutrition));
        Assert.Contains(RecipeJsonLdGap.RecipeYield, Gaps(without));
        Assert.DoesNotContain(RecipeJsonLdGap.YieldForNutrition, Gaps(without));
    }

    // RCP-006: only what was entered. A serving size alone is no nutrition, as on the card (PP4.9).
    [Fact]
    public void A_serving_size_alone_writes_no_nutrition()
    {
        var snapshot = Snapshot(nutrition: NoNutrition with { ServingSize = "1 slice" });

        Assert.False(Json(snapshot).ContainsKey("nutrition"));
        Assert.Contains(RecipeJsonLdGap.Calories, Gaps(snapshot));
    }

    [Fact]
    public void Every_nutrient_is_written_under_its_schema_org_property_in_its_unit()
    {
        var nutrition = new RecipeSnapshotNutrition("1 cup", 31_250, 100, 200, 0, 400, 500, 230_000, 700, 800, 900, 1_000);
        var written = Json(Snapshot(yield: Quantity.Amount(4), nutrition: nutrition))["nutrition"]!.AsObject();

        Assert.Equal(
            [
                ("@type", "NutritionInformation"), ("servingSize", "1 cup"), ("calories", "312.5 calories"), ("fatContent", "1 g"),
                ("saturatedFatContent", "2 g"), ("transFatContent", "0 g"), ("unsaturatedFatContent", "4 g"),
                ("cholesterolContent", "5 mg"), ("sodiumContent", "2300 mg"), ("carbohydrateContent", "7 g"),
                ("fiberContent", "8 g"), ("sugarContent", "9 g"), ("proteinContent", "10 g"),
            ],
            written.Select(p => (p.Key, (string)p.Value!)));
        Assert.Equal(Enum.GetValues<Nutrient>().Length, Enum.GetValues<Nutrient>().Select(RecipeJsonLd.PropertyOf).Distinct().Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => RecipeJsonLd.PropertyOf((Nutrient)Enum.GetValues<Nutrient>().Length));
    }

    [Fact]
    public void Only_diets_schema_org_names_are_written_in_their_order()
    {
        // Out of code order on purpose: the snapshot's order is kept, never re-sorted by member name.
        var json = Json(Snapshot(diets: ["vegan", "keto", "gluten-free"]));

        Assert.Equal(
            ["https://schema.org/VeganDiet", "https://schema.org/GlutenFreeDiet"],
            json["suitableForDiet"]!.AsArray().Select(n => (string)n!));
        Assert.False(Json(Snapshot(diets: ["keto", "paleo"])).ContainsKey("suitableForDiet"));
    }

    // Every diet the list maps is a RestrictedDiet member schema.org defines (verified at release 30.1, PP4.10).
    [Fact]
    public void Every_mapped_diet_is_a_schema_org_member() =>
        Assert.All(Diets.All.Where(d => d.SchemaOrgDiet is not null), d => Assert.True(Diets.SchemaOrgMembers.Contains(d.SchemaOrgDiet!), d.Code));

    [Fact]
    public void Courses_and_cuisines_are_their_names_joined()
    {
        var json = Json(Snapshot(courses: ["dessert", "snack"], cuisines: ["cajun-creole"], keywords: ["fall baking", "pie"]));

        Assert.Equal("Dessert, Snack", (string)json["recipeCategory"]!);
        Assert.Equal("Cajun and Creole", (string)json["recipeCuisine"]!);
        Assert.Equal("fall baking, pie", (string)json["keywords"]!);
    }

    [Fact]
    public void A_code_no_list_holds_is_a_bug()
    {
        Assert.Throws<ArgumentException>(() => RecipeJsonLd.Write(Snapshot(courses: ["entree"]), FullPage));
        Assert.Throws<ArgumentException>(() => RecipeJsonLd.Write(Snapshot(cuisines: ["martian"]), FullPage));
        Assert.Throws<ArgumentException>(() => RecipeJsonLd.Write(Snapshot(diets: ["carnivore"]), FullPage));
    }

    [Fact]
    public void Each_part_of_the_page_it_lacks_is_a_gap_and_written_when_given()
    {
        Assert.Equal(
            [RecipeJsonLdGap.Image, RecipeJsonLdGap.Author, RecipeJsonLdGap.DatePublished],
            Gaps(Snapshot(), RecipeJsonLdPage.None).Where(g => g is RecipeJsonLdGap.Image or RecipeJsonLdGap.Author or RecipeJsonLdGap.DatePublished));
        Assert.Contains(RecipeJsonLdGap.Author, Gaps(Snapshot(), FullPage with { AuthorName = "  " }));

        var json = Json(Snapshot());
        Assert.Equal("https://apple-pie.pantrypress.app/apple-pie#recipe", (string)json["@id"]!);
        Assert.Equal("2026-10-01", (string)json["datePublished"]!);
        Assert.Equal("Maya Brooks", (string)json["author"]!["name"]!);
        Assert.Equal("Person", (string)json["author"]!["@type"]!);
        Assert.False(Json(Snapshot(), RecipeJsonLdPage.None).ContainsKey("@id"));
    }

    [Fact]
    public void Gaps_come_once_each_in_their_order()
    {
        var gaps = Gaps(Snapshot(title: " ", nutrition: NoNutrition with { FatGrams = 1 }, prep: 5), RecipeJsonLdPage.None);

        Assert.Equal(gaps.Order(), gaps);
        Assert.Equal(gaps.Distinct(), gaps);
        Assert.Equal(
            [
                RecipeJsonLdGap.Name, RecipeJsonLdGap.Image, RecipeJsonLdGap.YieldForNutrition, RecipeJsonLdGap.Author,
                RecipeJsonLdGap.DatePublished, RecipeJsonLdGap.Description, RecipeJsonLdGap.PrepAndCookTime,
                RecipeJsonLdGap.Ingredients, RecipeJsonLdGap.Instructions, RecipeJsonLdGap.Courses, RecipeJsonLdGap.Cuisines,
                RecipeJsonLdGap.Keywords, RecipeJsonLdGap.Calories,
            ],
            gaps);
    }

    private static readonly RecipeJsonLdPage[] MalformedPages =
    [
        FullPage with { Url = "http://apple-pie.pantrypress.app/apple-pie" },
        FullPage with { Url = "/apple-pie" },
        FullPage with { Url = "" },
        FullPage with { Url = "https://" },
        FullPage with { Url = "HTTPS://apple-pie.pantrypress.app/apple-pie" },
        FullPage with { Url = "https://apple-pie.pantrypress.app/apple-pie#top" },
        FullPage with { Url = "https://apple-pie.pantrypress.app/apple pie" },
        FullPage with { Url = "https://user:secret@apple-pie.pantrypress.app/apple-pie" },
        FullPage with { ImageUrls = ["https://apple-pie.pantrypress.app/media/tarte-tatin-" + (char)0xE9 + ".jpg"] },
        FullPage with { ImageUrls = ["javascript:alert(1)"] },
        FullPage with { ImageUrls = ["data:image/png;base64,AAAA"] },
        FullPage with { ImageUrls = ["file:///etc/passwd"] },
        FullPage with { ImageUrls = ["//cdn.example.com/pie.jpg"] },
        FullPage with { ImageUrls = ["https://apple-pie.pantrypress.app/media/pie.jpg\"onerror=\"x"] },
        FullPage with { ImageUrls = ["https://apple-pie.pantrypress.app/media/<pie>.jpg"] },
        FullPage with { ImageUrls = [null!] },
        FullPage with { ImageUrls = null! },
        FullPage with { AuthorName = "Maya \ud800 Brooks" },
    ];

    // The page is the caller's: an address a browser could not follow, one that would carry credentials or markup into a
    // public page, or one that would move the recipe's node, is a bug.
    public static TheoryData<int> MalformedPageIndexes { get; } = [.. Enumerable.Range(0, MalformedPages.Length)];

    [Theory]
    [MemberData(nameof(MalformedPageIndexes))]
    public void A_malformed_page_is_refused(int index) =>
        Assert.ThrowsAny<ArgumentException>(() => RecipeJsonLd.Write(Snapshot(), MalformedPages[index]));

    // What is accepted is written exactly as given: a port, a query, and percent-encoding are the caller's to choose.
    [Theory]
    [InlineData("https://apple-pie.pantrypress.app/apple-pie")]
    [InlineData("https://apple-pie.localhost:7443/apple-pie")]
    [InlineData("https://www.example.com/recipes/apple-pie?utm=1")]
    [InlineData("https://apple-pie.pantrypress.app/tarte-tatin-%C3%A9")]
    public void A_well_formed_address_is_written_exactly(string address)
    {
        var json = Json(Snapshot(), FullPage with { Url = address, ImageUrls = [address + "/pie.jpg"] });

        Assert.Equal(address + "#recipe", (string)json["@id"]!);
        Assert.Equal(address + "/pie.jpg", (string)json["image"]![0]!);
    }

    // A lone surrogate cannot be written exactly, so it is not written at all (RCP-002), as in the snapshot - in every field
    // that is written, at its end too, where joining a list or a yield could hide it.
    public static TheoryData<string> TextFields { get; } =
    [
        "title", "summary", "yield-unit", "serving-size", "ingredient", "step-heading", "step", "equipment", "keyword",
        "keyword-end", "yield-unit-end",
    ];

    [Theory]
    [MemberData(nameof(TextFields))]
    public void Ill_formed_text_in_any_written_field_is_refused(string field)
    {
        const string Bad = "Pie \udc00 crust";
        const string BadEnd = "Pie crust \ud800";
        var snapshot = field switch
        {
            "title" => Snapshot(title: Bad),
            "summary" => Snapshot(summary: Bad),
            "yield-unit" => Snapshot(yield: Quantity.Amount(4), yieldUnit: Bad),
            "yield-unit-end" => Snapshot(yield: Quantity.Amount(4), yieldUnit: BadEnd),
            "serving-size" => Snapshot(nutrition: NoNutrition with { ServingSize = Bad, Calories = 100 }),
            "ingredient" => Snapshot(ingredients: [Lines(null, "flour", Bad)]),
            "step-heading" => Snapshot(steps: [Steps(Bad, "Bake.")]),
            "step" => Snapshot(steps: [Steps(null, "Mix.", Bad)]),
            "equipment" => Snapshot(equipment: [Bad]),
            "keyword" => Snapshot(keywords: ["fall", Bad]),
            "keyword-end" => Snapshot(keywords: [BadEnd, "fall"]),
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        Assert.Throws<ArgumentException>(() => RecipeJsonLd.Write(snapshot, FullPage));
    }

    // PP7.10 writes the node into its page's open @graph array. It fits there, and a refused node - an unknown code or
    // broken text, found anywhere in the recipe - leaves the caller's writer exactly as it was.
    [Fact]
    public void A_node_writes_into_an_open_graph_array_and_a_refused_one_writes_nothing()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("@context", RecipeJsonLd.SchemaOrg);
            writer.WriteStartArray("@graph");
            writer.Flush();
            var before = buffer.WrittenCount;

            Assert.Throws<ArgumentException>(() => RecipeJsonLd.WriteNode(writer, Snapshot(cuisines: ["american", "martian"]), FullPage, withContext: false));
            Assert.Throws<ArgumentException>(() => RecipeJsonLd.WriteNode(writer, Snapshot(steps: [Steps(null, "Mix.", "Bake \udc00")]), FullPage, withContext: false));
            writer.Flush();
            Assert.Equal(before, buffer.WrittenCount);
            Assert.Equal(0, writer.BytesPending);

            RecipeJsonLd.WriteNode(writer, Snapshot(), FullPage, withContext: false);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        var graph = JsonNode.Parse(buffer.WrittenSpan)!["@graph"]!.AsArray();
        var recipe = Assert.Single(graph)!.AsObject();
        Assert.Equal("Recipe", (string)recipe["@type"]!);
        Assert.False(recipe.ContainsKey("@context"));
    }
}
