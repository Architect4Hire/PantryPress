using System.Text.Json;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;
using static PantryPress.Tests.Recipes.RecipeJsonLdCases;

namespace PantryPress.Tests.Recipes;

// PP4.10: each rule of the recipe JSON-LD generator on its own (RCP-008). The golden files show whole recipes; these say
// which rule makes each part, and what the generator refuses.
public sealed class RecipeJsonLdTests
{
    [Fact]
    public void A_recipe_is_a_schema_org_Recipe_named_by_its_title()
    {
        var json = Parse(Recipe("Toast"));
        Assert.Equal("https://schema.org", json.GetProperty("@context").GetString());
        Assert.Equal("Recipe", json.GetProperty("@type").GetString());
        Assert.Equal("Toast", json.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_summary_is_no_description(string? summary)
    {
        var document = RecipeJsonLd.Write(Recipe("Toast", summary: summary), FullContext);
        Assert.False(Parse(document).TryGetProperty("description", out _));
        Assert.Contains(RecipeJsonLdIssue.DescriptionMissing, document.Issues);
    }

    [Fact]
    public void The_summary_is_written_exactly_as_stored()
    {
        const string summary = "  Two spaces lead,\nand a line break follows.  ";
        Assert.Equal(summary, Parse(Recipe("Toast", summary: summary)).GetProperty("description").GetString());
    }

    [Theory]
    [InlineData(5, 10, null, "PT5M", "PT10M", "PT15M")]
    [InlineData(0, 0, null, "PT0M", "PT0M", "PT0M")]
    [InlineData(null, null, 1620, null, null, "PT27H")]
    [InlineData(90, null, null, "PT1H30M", null, "PT1H30M")]
    public void Times_are_as_entered_and_the_total_is_their_sum(int? prep, int? cook, int? additional, string? prepIso, string? cookIso, string? totalIso)
    {
        var json = Parse(Recipe("Toast", prep: prep, cook: cook, additional: additional));
        Assert.Equal(prepIso, Optional(json, "prepTime"));
        Assert.Equal(cookIso, Optional(json, "cookTime"));
        Assert.Equal(totalIso, Optional(json, "totalTime"));
    }

    [Theory]
    [InlineData(null, null, null, RecipeJsonLdIssue.TimesMissing)]
    [InlineData(null, 10, null, RecipeJsonLdIssue.PrepTimeMissing)]
    [InlineData(10, null, null, RecipeJsonLdIssue.CookTimeMissing)]
    [InlineData(null, 0, null, RecipeJsonLdIssue.PrepTimeMissing)]
    public void The_report_names_the_missing_time(int? prep, int? cook, int? additional, RecipeJsonLdIssue expected)
    {
        var issues = RecipeJsonLd.Write(Recipe("Toast", prep: prep, cook: cook, additional: additional), FullContext).Issues;
        RecipeJsonLdIssue[] timeIssues = [RecipeJsonLdIssue.TimesMissing, RecipeJsonLdIssue.PrepTimeMissing, RecipeJsonLdIssue.CookTimeMissing];
        Assert.Equal([expected], issues.Intersect(timeIssues));
    }

    [Theory]
    [InlineData(null, null, 30)]
    [InlineData(10, 20, null)]
    [InlineData(10, 20, 30)]
    [InlineData(0, 0, 0)]
    public void Times_that_satisfy_Google_report_nothing(int? prep, int? cook, int? additional)
    {
        var issues = RecipeJsonLd.Write(Recipe("Toast", prep: prep, cook: cook, additional: additional), FullContext).Issues;
        Assert.DoesNotContain(issues, i => i is RecipeJsonLdIssue.TimesMissing or RecipeJsonLdIssue.PrepTimeMissing or RecipeJsonLdIssue.CookTimeMissing);
    }

    [Fact]
    public void A_total_from_the_additional_time_alone_asks_for_nothing_more() =>
        Assert.DoesNotContain(
            RecipeJsonLd.Write(Recipe("Pickles", additional: 60), FullContext).Issues,
            i => i is RecipeJsonLdIssue.TimesMissing or RecipeJsonLdIssue.PrepTimeMissing or RecipeJsonLdIssue.CookTimeMissing);

    [Theory]
    [InlineData(8L, 1L, "servings", "8 servings")]
    [InlineData(3L, 2L, null, "1½")]
    [InlineData(6L, 1L, " ", "6")]
    [InlineData(12L, 1L, "small cookies (about 2 in)", "12 small cookies (about 2 in)")]
    public void The_yield_is_written_as_the_recipe_card_writes_it(long numerator, long denominator, string? unit, string expected) =>
        Assert.Equal(expected, Parse(Recipe("Toast", yield: Quantity.Amount(Rational.Create(numerator, denominator)), yieldUnit: unit)).GetProperty("recipeYield").GetString());

    [Fact]
    public void A_range_yield_keeps_its_en_dash() =>
        Assert.Equal("2–3 loaves", Parse(Recipe("Bread", yield: Quantity.Range(2, 3), yieldUnit: "loaves")).GetProperty("recipeYield").GetString());

    [Fact]
    public void Nutrition_without_a_yield_is_required_to_have_one_and_is_not_also_reported_as_no_yield()
    {
        var issues = RecipeJsonLd.Write(Recipe("Bar", nutrition: Nutrition(protein: 100)), FullContext).Issues;
        Assert.Contains(RecipeJsonLdIssue.YieldMissingForNutrition, issues);
        Assert.DoesNotContain(RecipeJsonLdIssue.YieldMissing, issues);
        Assert.Equal(RecipeJsonLdSeverity.Required, RecipeJsonLdIssues.SeverityOf(RecipeJsonLdIssue.YieldMissingForNutrition));
    }

    [Fact]
    public void Every_ingredient_line_is_its_original_text_in_order_with_no_group_heading()
    {
        var json = Parse(Recipe("Pie", ingredients: [("Crust", ["2 cups flour", "  1 stick   butter  "]), (null, ["???", "salt"])]));
        Assert.Equal(["2 cups flour", "  1 stick   butter  ", "???", "salt"], json.GetProperty("recipeIngredient").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void Steps_with_no_heading_are_a_plain_list()
    {
        var json = Parse(Recipe("Pie", steps: [(null, ["Mix."]), (null, ["Bake."])]));
        var items = json.GetProperty("recipeInstructions").EnumerateArray().ToList();
        Assert.All(items, i => Assert.Equal("HowToStep", i.GetProperty("@type").GetString()));
        Assert.Equal(["Mix.", "Bake."], items.Select(i => i.GetProperty("text").GetString()));
        Assert.All(items, i => Assert.Equal(["@type", "text"], i.EnumerateObject().Select(p => p.Name)));
    }

    [Fact]
    public void A_headed_step_group_is_a_section_and_an_empty_one_is_left_out()
    {
        var json = Parse(Recipe("Pie", steps: [("Crust", ["Rub in the butter."]), ("Nothing", []), ("   ", ["Bake."])]));
        var items = json.GetProperty("recipeInstructions").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal("HowToSection", items[0].GetProperty("@type").GetString());
        Assert.Equal("Crust", items[0].GetProperty("name").GetString());
        Assert.Equal("Rub in the butter.", items[0].GetProperty("itemListElement")[0].GetProperty("text").GetString());
        Assert.Equal("HowToStep", items[1].GetProperty("@type").GetString());
    }

    [Fact]
    public void Only_empty_step_groups_are_no_instructions()
    {
        var document = RecipeJsonLd.Write(Recipe("Pie", steps: [("Empty", [])]), FullContext);
        Assert.False(Parse(document).TryGetProperty("recipeInstructions", out _));
        Assert.Contains(RecipeJsonLdIssue.InstructionsMissing, document.Issues);
    }

    [Fact]
    public void Courses_and_cuisines_are_their_names_in_order()
    {
        var json = Parse(Recipe("Pie", courses: ["dessert", "snack"], cuisines: ["american", "southern"]));
        Assert.Equal("Dessert, Snack", json.GetProperty("recipeCategory").GetString());
        Assert.Equal("American, Southern", json.GetProperty("recipeCuisine").GetString());
    }

    [Fact]
    public void A_keyword_repeating_a_course_or_cuisine_is_left_out_and_the_rest_stay_as_written()
    {
        var json = Parse(Recipe("Pie", courses: ["dessert"], cuisines: ["american"], keywords: ["Dessert", " AMERICAN ", "Desserts", "Brown Butter "]));
        Assert.Equal("Desserts, Brown Butter ", json.GetProperty("keywords").GetString());
    }

    [Fact]
    public void Keyword_folding_is_ASCII_only() =>
        Assert.Equal(
            "DESSERTİ",
            Parse(Recipe("Pie", courses: ["dessert"], keywords: ["DESSERTİ"])).GetProperty("keywords").GetString());

    [Fact]
    public void A_diet_is_its_schema_org_member_and_one_without_is_left_out()
    {
        foreach (var diet in Diets.All)
        {
            var json = Parse(Recipe("Soup", diets: [diet.Code]));
            if (diet.SchemaOrgDiet is { } member)
            {
                Assert.Equal(["https://schema.org/" + member], json.GetProperty("suitableForDiet").EnumerateArray().Select(e => e.GetString()));
            }
            else
            {
                Assert.False(json.TryGetProperty("suitableForDiet", out _), diet.Code);
            }
        }
    }

    [Fact]
    public void Allergens_equipment_and_notes_are_never_written()
    {
        var json = RecipeJsonLd.Write(Full(), FullContext).Json;
        Assert.DoesNotContain("allergen", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pie plate", json, StringComparison.Ordinal);
        Assert.DoesNotContain("overnight", json, StringComparison.Ordinal);
        Assert.DoesNotContain("tool", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Nutrient.Calories, 27_000, "calories", "270 calories")]
    [InlineData(Nutrient.Fat, 1_450, "fatContent", "14.5 grams")]
    [InlineData(Nutrient.SaturatedFat, 0, "saturatedFatContent", "0 grams")]
    [InlineData(Nutrient.TransFat, 25, "transFatContent", "0.25 grams")]
    [InlineData(Nutrient.UnsaturatedFat, 100, "unsaturatedFatContent", "1 grams")]
    [InlineData(Nutrient.Cholesterol, 7_500, "cholesterolContent", "75 milligrams")]
    [InlineData(Nutrient.Sodium, 230_000, "sodiumContent", "2300 milligrams")]
    [InlineData(Nutrient.Carbohydrate, 3_000, "carbohydrateContent", "30 grams")]
    [InlineData(Nutrient.Fiber, 310, "fiberContent", "3.1 grams")]
    [InlineData(Nutrient.Sugar, 1_200, "sugarContent", "12 grams")]
    [InlineData(Nutrient.Protein, 999_999_999, "proteinContent", "9999999.99 grams")]
    public void Each_nutrient_is_its_schema_org_property_and_value(Nutrient nutrient, long hundredths, string property, string value)
    {
        Assert.Equal(property, RecipeJsonLd.PropertyOf(nutrient));
        Assert.Equal(value, RecipeJsonLd.ValueOf(new NutritionFact(nutrient, hundredths)));
    }

    [Fact]
    public void Every_nutrient_has_a_NutritionInformation_property() =>
        Assert.Equal(
            ["calories", "fatContent", "saturatedFatContent", "transFatContent", "unsaturatedFatContent", "cholesterolContent",
                "sodiumContent", "carbohydrateContent", "fiberContent", "sugarContent", "proteinContent"],
            Enum.GetValues<Nutrient>().Select(RecipeJsonLd.PropertyOf));

    [Fact]
    public void Nutrition_lists_only_what_was_entered_and_a_serving_size_alone_is_none()
    {
        var json = Parse(Recipe("Tea", yield: Quantity.Amount(1), nutrition: Nutrition(servingSize: "1 cup", sodium: 500)));
        Assert.Equal(["@type", "servingSize", "sodiumContent"], json.GetProperty("nutrition").EnumerateObject().Select(p => p.Name));

        Assert.False(Parse(Recipe("Tea", nutrition: Nutrition(servingSize: "1 cup"))).TryGetProperty("nutrition", out _));
    }

    [Fact]
    public void No_calories_entered_is_reported_and_never_estimated()
    {
        var document = RecipeJsonLd.Write(Recipe("Tea", yield: Quantity.Amount(1), nutrition: Nutrition(fat: 1_000, carbohydrate: 2_000, protein: 500)), FullContext);
        Assert.False(Parse(document).GetProperty("nutrition").TryGetProperty("calories", out _));
        Assert.Contains(RecipeJsonLdIssue.CaloriesMissing, document.Issues);
    }

    [Fact]
    public void A_video_address_is_left_out_and_reported()
    {
        var document = RecipeJsonLd.Write(Recipe("Pancakes", videoUrl: "https://video.example.com/v/1"), FullContext);
        Assert.DoesNotContain("video", document.Json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(RecipeJsonLdIssue.VideoNotDescribed, document.Issues);
        Assert.DoesNotContain(RecipeJsonLdIssue.VideoNotDescribed, RecipeJsonLd.Write(Recipe("Pancakes"), FullContext).Issues);
    }

    [Fact]
    public void No_rating_or_review_is_ever_written()
    {
        foreach (var (snapshot, context) in All.Values)
        {
            var json = RecipeJsonLd.Write(snapshot, context).Json;
            Assert.DoesNotContain("rating", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("review", json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_context_is_written_as_given()
    {
        var json = Parse(Recipe("Pie"), FullContext);
        Assert.Equal(FullContext.Url, json.GetProperty("url").GetString());
        Assert.Equal(FullContext.Images, json.GetProperty("image").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("June Baker", json.GetProperty("author").GetProperty("name").GetString());
        Assert.Equal("2026-11-02T14:30:05Z", json.GetProperty("datePublished").GetString());
    }

    [Fact]
    public void A_publish_date_is_written_to_the_second()
    {
        var context = new RecipeJsonLdContext(null, [], null, new DateTimeOffset(2026, 1, 2, 3, 4, 5, 678, TimeSpan.Zero));
        Assert.Equal("2026-01-02T03:04:05Z", Parse(Recipe("Pie"), context).GetProperty("datePublished").GetString());
    }

    [Fact]
    public void No_context_reports_what_only_the_page_can_give()
    {
        var issues = RecipeJsonLd.Write(Recipe("Pie"), RecipeJsonLdContext.None).Issues;
        Assert.Contains(RecipeJsonLdIssue.ImageMissing, issues);
        Assert.Contains(RecipeJsonLdIssue.AuthorMissing, issues);
        Assert.Contains(RecipeJsonLdIssue.DatePublishedMissing, issues);
        Assert.Equal(RecipeJsonLdSeverity.Required, RecipeJsonLdIssues.SeverityOf(RecipeJsonLdIssue.ImageMissing));
    }

    [Theory]
    [InlineData("http://example.com/a.jpg")]
    [InlineData("/media/a.jpg")]
    [InlineData("example.com/a.jpg")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://example.com/a b.jpg")]
    [InlineData("https://example.com/café.jpg")]
    [InlineData("")]
    [InlineData("HTTPS://example.com/a.jpg")]
    [InlineData("https:\\\\example.com\\a.jpg")]
    [InlineData("https:/example.com/a.jpg")]
    [InlineData("https://user:pw@example.com/a.jpg")]
    [InlineData("https://example.com/\"a\".jpg")]
    [InlineData("https://example.com/<a>.jpg")]
    [InlineData("https://example.com/{a}.jpg")]
    [InlineData("https://")]
    public void A_context_address_must_be_absolute_https(string address)
    {
        Assert.ThrowsAny<ArgumentException>(() => new RecipeJsonLdContext(address, [], null, null));
        Assert.ThrowsAny<ArgumentException>(() => new RecipeJsonLdContext(null, [address], null, null));
        Assert.ThrowsAny<ArgumentException>(() => new RecipeJsonLdAuthor("Sam", address));
    }

    [Theory]
    [InlineData("https://example.com/a.jpg")]
    [InlineData("https://cdn.example.com:8443/media/pie-16x9.jpg?v=3&w=1200#top")]
    [InlineData("https://example.com/caf%C3%A9.jpg")]
    [InlineData("https://[2001:db8::1]/a.jpg")]
    public void An_absolute_https_address_is_written_as_given(string address) =>
        Assert.Equal(address, Parse(Recipe("Pie"), new RecipeJsonLdContext(address, [address], null, null)).GetProperty("url").GetString());

    [Fact]
    public void A_missing_image_address_is_a_bug() =>
        Assert.Throws<ArgumentNullException>(() => new RecipeJsonLdContext(null, [null!], null, null));

    [Fact]
    public void The_context_keeps_its_own_copy_of_the_images()
    {
        var images = new List<string> { "https://example.com/a.jpg" };
        var context = new RecipeJsonLdContext(null, images, null, null);
        images.Add("https://example.com/b.jpg");
        Assert.Equal(["https://example.com/a.jpg"], Parse(Recipe("Pie"), context).GetProperty("image").EnumerateArray().Select(e => e.GetString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_title_is_a_bug(string title) =>
        Assert.Throws<ArgumentException>(() => RecipeJsonLd.Write(Recipe(title), FullContext));

    [Fact]
    public void Nothing_is_a_bug_too()
    {
        Assert.Throws<ArgumentNullException>(() => RecipeJsonLd.Write(null!, FullContext));
        Assert.Throws<ArgumentNullException>(() => RecipeJsonLd.Write(Recipe("Pie"), null!));
    }

    [Fact]
    public void An_unknown_nutrient_or_issue_is_a_bug()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecipeJsonLd.PropertyOf((Nutrient)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecipeJsonLdIssues.SeverityOf((RecipeJsonLdIssue)99));
    }

    [Fact]
    public void Nutrition_waits_for_a_yield()
    {
        var withoutYield = RecipeJsonLd.Write(Recipe("Bar", nutrition: Nutrition(calories: 20_000)), FullContext);
        Assert.False(Parse(withoutYield).TryGetProperty("nutrition", out _));
        Assert.Contains(RecipeJsonLdIssue.YieldMissingForNutrition, withoutYield.Issues);
        Assert.DoesNotContain(RecipeJsonLdIssue.CaloriesMissing, withoutYield.Issues);

        var withYield = Parse(Recipe("Bar", yield: Quantity.Amount(12), nutrition: Nutrition(calories: 20_000)));
        Assert.Equal("200 calories", withYield.GetProperty("nutrition").GetProperty("calories").GetString());
    }

    [Fact]
    public void Zero_calories_entered_is_written_and_not_reported()
    {
        var document = RecipeJsonLd.Write(Recipe("Water", yield: Quantity.Amount(1), nutrition: Nutrition(calories: 0)), FullContext);
        Assert.Equal("0 calories", Parse(document).GetProperty("nutrition").GetProperty("calories").GetString());
        Assert.DoesNotContain(RecipeJsonLdIssue.CaloriesMissing, document.Issues);
    }

    [Fact]
    public void Ingredient_groups_with_no_lines_are_no_ingredients()
    {
        var document = RecipeJsonLd.Write(Recipe("Pie", ingredients: [("Crust", []), (null, [])]), FullContext);
        Assert.False(Parse(document).TryGetProperty("recipeIngredient", out _));
        Assert.Contains(RecipeJsonLdIssue.IngredientsMissing, document.Issues);
        Assert.DoesNotContain("Crust", document.Json, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ingredient_group_heading_appears_nowhere()
    {
        var json = RecipeJsonLd.Write(All["sections"].Snapshot, FullContext).Json;
        Assert.DoesNotContain("For the cake", json, StringComparison.Ordinal);
        Assert.DoesNotContain("For the frosting", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_keyword_with_a_comma_or_a_repeat_is_written_as_is() =>
        Assert.Equal(
            "salt, pepper, coffee, Coffee",
            Parse(Recipe("Pie", keywords: ["salt, pepper", "coffee", "Coffee"])).GetProperty("keywords").GetString());

    [Fact]
    public void Keyword_matching_ignores_any_white_space_at_its_ends() =>
        Assert.False(Parse(Recipe("Pie", courses: ["dessert"], keywords: [" Dessert\t"])).TryGetProperty("keywords", out _));

    [Fact]
    public void Each_diet_member_is_named_by_one_diet_only() =>
        Assert.Equal(
            Diets.All.Count(d => d.SchemaOrgDiet is not null),
            Diets.All.Select(d => d.SchemaOrgDiet).OfType<string>().Distinct(StringComparer.Ordinal).Count());

    [Fact]
    public void A_publish_date_must_be_UTC() =>
        Assert.Throws<ArgumentException>(() => new RecipeJsonLdContext(null, [], null, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(-5))));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void An_author_has_a_name(string name) =>
        Assert.ThrowsAny<ArgumentException>(() => new RecipeJsonLdAuthor(name, null));

    [Fact]
    public void A_code_no_list_holds_is_a_bug()
    {
        Assert.Throws<ArgumentException>(() => RecipeJsonLd.Write(Recipe("Pie", courses: ["elevenses"]), FullContext));
        Assert.Throws<ArgumentException>(() => RecipeJsonLd.Write(Recipe("Pie", cuisines: ["martian"]), FullContext));
        Assert.Throws<ArgumentException>(() => RecipeJsonLd.Write(Recipe("Pie", diets: ["carnivore"]), FullContext));
    }

    // The output can sit in a <script> element whatever the blogger wrote: it is ASCII, and no character HTML reads
    // inside one appears unescaped.
    [Fact]
    public void The_output_is_ASCII_and_safe_inside_a_script_element()
    {
        var json = RecipeJsonLd.Write(All["escaping"].Snapshot, FullContext).Json;
        Assert.All(json, c => Assert.InRange(c, ' ', '~'));
        Assert.DoesNotContain('<', json);
        Assert.DoesNotContain('>', json);
        Assert.DoesNotContain('&', json);
        Assert.Equal(All["escaping"].Snapshot.Title, Parse(All["escaping"].Snapshot).GetProperty("name").GetString());
    }

    [Fact]
    public void Every_issue_has_a_severity_and_its_wire_name_is_pinned()
    {
        Assert.All(Enum.GetValues<RecipeJsonLdIssue>(), i => RecipeJsonLdIssues.SeverityOf(i));
        Assert.Equal(
            ["image-missing", "yield-missing-for-nutrition", "author-missing", "date-published-missing", "description-missing",
                "times-missing", "prep-time-missing", "cook-time-missing", "yield-missing", "ingredients-missing",
                "instructions-missing", "category-missing", "cuisine-missing", "keywords-missing", "calories-missing",
                "video-not-described"],
            Enum.GetValues<RecipeJsonLdIssue>().Select(i => RecipeJsonLdCorpusTests.Kebab(i.ToString())));
        Assert.Equal(["required", "recommended"], Enum.GetValues<RecipeJsonLdSeverity>().Select(s => RecipeJsonLdCorpusTests.Kebab(s.ToString())));
    }

    [Fact]
    public void Issues_come_in_the_enum_order_without_repeats()
    {
        foreach (var (snapshot, context) in All.Values)
        {
            var issues = RecipeJsonLd.Write(snapshot, context).Issues;
            Assert.Equal(issues.Distinct().Order(), issues);
        }
    }

    private static JsonElement Parse(RecipeSnapshot snapshot) => Parse(snapshot, FullContext);

    private static JsonElement Parse(RecipeSnapshot snapshot, RecipeJsonLdContext context) => Parse(RecipeJsonLd.Write(snapshot, context));

    private static JsonElement Parse(RecipeJsonLdDocument document)
    {
        using var parsed = JsonDocument.Parse(document.Json);
        return parsed.RootElement.Clone();
    }

    private static string? Optional(JsonElement json, string name) => json.TryGetProperty(name, out var value) ? value.GetString() : null;
}
