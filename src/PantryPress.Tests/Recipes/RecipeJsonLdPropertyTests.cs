using System.Text.Json.Nodes;
using CsCheck;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.10: the laws the JSON-LD generator keeps over any recipe (RCP-008, RCP-002, RCP-006): it always writes JSON; the
// blogger's ingredient lines and steps come out exactly, in order, none added; nutrition only as entered; the same recipe
// always makes the same bytes; and the gaps say exactly what is missing.
public sealed class RecipeJsonLdPropertyTests
{
    private const long Iterations = 2_000;

    // Any well-formed text, from anywhere in Unicode, white space alone included.
    private static readonly Gen<string> Text = Gen.Frequency(
        (1, Gen.OneOfConst(
            "", " ", "\t\n", "</script>", "</SCRIPT >", "<!--", "-->", "]]>", "a & b", "\"quoted\"", "it's", "a+b", "back\\slash",
            "nul" + (char)0x0000, "del" + (char)0x007F, "line" + (char)0x2028, "para" + (char)0x2029, "rtl" + (char)0x202E)),
        (3, Gen.Char['\0', '\x7F'].Array[1, 20].Select(c => new string(c))),
        (3, Gen.Int[0, 0x10FFFF].Where(c => c is < 0xD800 or > 0xDFFF).Select(char.ConvertFromUtf32).Array[1, 12]
            .Select(parts => string.Concat(parts))));

    private static readonly Gen<long?> Hundredths = Gen.Frequency(
        (2, Gen.Const((long?)null)), (1, Gen.Long[0, NutritionFacts.MaxHundredths].Select(v => (long?)v)));

    private static readonly Gen<int?> Minutes = Gen.Frequency(
        (1, Gen.Const((int?)null)), (1, Gen.Int[0, RecipeLimits.MaxMinutes].Select(v => (int?)v)));

    private static readonly Gen<Quantity?> Yield = Gen.Frequency(
        (1, Gen.Const((Quantity?)null)),
        (2, Gen.Long[1, 100].Select(Gen.Long[1, 12], (n, d) => (Quantity?)Quantity.Amount(Rational.Create(n, d)))),
        (1, Gen.Long[1, 50].Select(Gen.Long[1, 50], (a, b) => (Quantity?)Quantity.Range(a, a + b))));

    private static Gen<string[]> Codes(IReadOnlyList<string> codes) =>
        Gen.Int[0, codes.Count].SelectMany(n => Gen.Shuffle(codes.ToArray(), n)).Select(c => c.Order(StringComparer.Ordinal).ToArray());

    private static readonly Gen<RecipeSnapshot> Recipe =
        Gen.Select(
            Gen.Select(Text, Gen.Frequency((1, Gen.Const((string?)null)), (2, Text.Select(t => (string?)t))), Yield, Text, Minutes.Array[3]),
            Gen.Select(Gen.Frequency((1, Gen.Const((string?)null)), (1, Text.Select(t => (string?)t))), Hundredths.Array[11]),
            Gen.Select(Text.Array[0, 6].Array[0, 3], Gen.Frequency((1, Gen.Const((string?)null)), (1, Text.Select(t => (string?)t))).Array[3]),
            Gen.Select(Text.Array[0, 6].Array[0, 3], Text.Array[0, 4], Text.Array[0, 4]),
            Gen.Select(
                Codes([.. Courses.All.Select(c => c.Code)]), Codes([.. Cuisines.All.Select(c => c.Code)]),
                Codes([.. Diets.All.Select(c => c.Code)])),
            (head, nutrition, ingredients, rest, vocab) =>
            {
                var (title, summary, yield, yieldUnit, minutes) = head;
                var (serving, values) = nutrition;
                var (lineGroups, headings) = ingredients;
                var (stepGroups, equipment, keywords) = rest;
                var (courses, cuisines, diets) = vocab;
                return RecipeJsonLdTests.Snapshot(
                    title, summary, yield, yieldUnit, minutes[0], minutes[1], minutes[2],
                    new RecipeSnapshotNutrition(serving, values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8], values[9], values[10]),
                    ingredients: [.. lineGroups.Select((lines, i) => RecipeJsonLdTests.Lines(headings[i], lines))],
                    steps: [.. stepGroups.Select((steps, i) => RecipeJsonLdTests.Steps(headings[i], steps))],
                    equipment: equipment, keywords: keywords, courses: courses, cuisines: cuisines, diets: diets,
                    allergens: ["milk"], notes: ["A note."]);
            });

    private static readonly Gen<RecipeJsonLdPage> Page = Gen.Frequency(
        (1, Gen.Const(RecipeJsonLdPage.None)),
        (2, Gen.Select(
            Gen.Bool, Gen.Int[0, 3], Gen.Frequency((1, Gen.Const((string?)null)), (2, Text.Select(t => (string?)t))),
            Gen.Frequency((1, Gen.Const((DateOnly?)null)), (2, Gen.Int[0, 20_000].Select(d => (DateOnly?)new DateOnly(2000, 1, 1).AddDays(d)))),
            (hasUrl, images, author, date) => new RecipeJsonLdPage(
                hasUrl ? "https://a.pantrypress.app/p" : null,
                [.. Enumerable.Range(0, images).Select(i => $"https://a.pantrypress.app/media/{i}.jpg")],
                author,
                date))));

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    [Fact]
    public void The_output_is_json_with_the_bloggers_lines_and_steps_exactly() =>
        Gen.Select(Recipe, Page).Sample(iter: Iterations, assert: (snapshot, page) =>
        {
            var json = JsonNode.Parse(RecipeJsonLd.Write(snapshot, page).Json)!.AsObject();

            var lines = snapshot.IngredientGroups.SelectMany(g => g.Lines).Select(l => l.OriginalText).Where(HasText).ToList();
            Assert.Equal(lines, json["recipeIngredient"]?.AsArray().Select(n => (string)n!) ?? []);

            var steps = snapshot.StepGroups.SelectMany(g => g.Steps).Select(s => s.Text).Where(HasText).ToList();
            var written = json["recipeInstructions"]?.AsArray()
                .Select(n => n!)
                .SelectMany(n => (string)n["@type"]! == "HowToSection" ? n["itemListElement"]!.AsArray().Select(s => s!) : [n])
                .Select(n => (string)n!["text"]!)
                .ToList() ?? [];
            Assert.Equal(steps, written);

            Assert.Equal(HasText(snapshot.Title) ? snapshot.Title : null, (string?)json["name"]);
            Assert.False(json.ContainsKey("aggregateRating") || json.ContainsKey("review") || json.ContainsKey("video"));
        });

    // RCP-006: a nutrient appears exactly when it was entered, and nutrition at all only when one was.
    [Fact]
    public void Nutrition_is_exactly_what_was_entered() =>
        Recipe.Sample(iter: Iterations, assert: snapshot =>
        {
            var json = JsonNode.Parse(RecipeJsonLd.Write(snapshot, RecipeJsonLdPage.None).Json)!.AsObject();
            var facts = NutritionFacts.From(snapshot.Nutrition);

            if (!facts.HasNutrition)
            {
                Assert.False(json.ContainsKey("nutrition"));
                return;
            }

            var nutrition = json["nutrition"]!.AsObject();
            var expected = facts.Facts
                .Select(f => (RecipeJsonLd.PropertyOf(f.Nutrient), f.Nutrient == Nutrient.Calories ? f.Amount + " calories" : f.Text))
                .ToList();
            Assert.Equal(expected, nutrition.Where(p => p.Key is not ("@type" or "servingSize")).Select(p => (p.Key, (string)p.Value!)));
            Assert.Equal(HasText(snapshot.Nutrition.ServingSize) ? snapshot.Nutrition.ServingSize : null, (string?)nutrition["servingSize"]);
        });

    // Equipment and keywords are the blogger's own entries, blank ones left out, in order; keywords joined by ", ".
    [Fact]
    public void Tools_and_keywords_are_the_bloggers_entries() =>
        Recipe.Sample(iter: Iterations, assert: snapshot =>
        {
            var json = JsonNode.Parse(RecipeJsonLd.Write(snapshot, RecipeJsonLdPage.None).Json)!.AsObject();
            var tools = snapshot.Equipment.Where(HasText).ToList();
            var keywords = snapshot.Keywords.Where(HasText).ToList();

            Assert.Equal(tools, json["tool"]?.AsArray().Select(n => (string)n!) ?? []);
            Assert.Equal(keywords.Count == 0 ? null : string.Join(RecipeJsonLd.ListSeparator, keywords), (string?)json["keywords"]);
        });

    // Whatever the blogger wrote, anywhere, the output holds nothing an HTML parser would act on inside a script element,
    // and nothing outside printable ASCII (PP7.10).
    [Fact]
    public void Production_output_is_inert_in_html_for_any_recipe() =>
        Gen.Select(Recipe, Page).Sample(iter: Iterations, assert: (snapshot, page) =>
        {
            var output = RecipeJsonLd.Write(snapshot, page).Json;
            Assert.DoesNotContain('<', output);
            Assert.DoesNotContain('>', output);
            Assert.DoesNotContain('&', output);
            Assert.All(output, c => Assert.True(c is >= ' ' and <= '~', $"U+{(int)c:X4} is not printable ASCII"));
        });

    [Fact]
    public void The_same_recipe_always_makes_the_same_bytes() =>
        Gen.Select(Recipe, Page).Sample(iter: Iterations, assert: (snapshot, page) =>
        {
            var first = RecipeJsonLd.Write(snapshot, page);
            var again = RecipeJsonLd.Write(snapshot with { }, page with { });
            Assert.Equal(first.Json, again.Json);
            Assert.Equal(first.Gaps, again.Gaps);
            Assert.All(first.Json, c => Assert.True(c is >= ' ' and <= '~'));
        });

    // Each gap is reported exactly when its field is absent from the output.
    [Fact]
    public void A_gap_is_reported_exactly_when_its_property_is_missing() =>
        Gen.Select(Recipe, Page).Sample(iter: Iterations, assert: (snapshot, page) =>
        {
            var result = RecipeJsonLd.Write(snapshot, page);
            var json = JsonNode.Parse(result.Json)!.AsObject();
            var hasNutrition = NutritionFacts.From(snapshot.Nutrition).HasNutrition;
            bool Lacks(RecipeJsonLdGap gap) => result.Gaps.Contains(gap);

            Assert.Equal(!json.ContainsKey("name"), Lacks(RecipeJsonLdGap.Name));
            Assert.Equal(!json.ContainsKey("image"), Lacks(RecipeJsonLdGap.Image));
            Assert.Equal(!json.ContainsKey("recipeYield") && hasNutrition, Lacks(RecipeJsonLdGap.YieldForNutrition));
            Assert.Equal(!json.ContainsKey("recipeYield") && !hasNutrition, Lacks(RecipeJsonLdGap.RecipeYield));
            Assert.Equal(!json.ContainsKey("author"), Lacks(RecipeJsonLdGap.Author));
            Assert.Equal(!json.ContainsKey("datePublished"), Lacks(RecipeJsonLdGap.DatePublished));
            Assert.Equal(!json.ContainsKey("description"), Lacks(RecipeJsonLdGap.Description));
            Assert.Equal(!json.ContainsKey("totalTime"), Lacks(RecipeJsonLdGap.Times));
            Assert.Equal(json.ContainsKey("prepTime") != json.ContainsKey("cookTime"), Lacks(RecipeJsonLdGap.PrepAndCookTime));
            Assert.Equal(!json.ContainsKey("recipeIngredient"), Lacks(RecipeJsonLdGap.Ingredients));
            Assert.Equal(!json.ContainsKey("recipeInstructions"), Lacks(RecipeJsonLdGap.Instructions));
            Assert.Equal(!json.ContainsKey("recipeCategory"), Lacks(RecipeJsonLdGap.Courses));
            Assert.Equal(!json.ContainsKey("recipeCuisine"), Lacks(RecipeJsonLdGap.Cuisines));
            Assert.Equal(!json.ContainsKey("keywords"), Lacks(RecipeJsonLdGap.Keywords));
            Assert.Equal(json["nutrition"]?["calories"] is null, Lacks(RecipeJsonLdGap.Calories));
            Assert.Equal(result.Gaps.Order().Distinct(), result.Gaps);
        });
}
