using System.Text.Json;
using CsCheck;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;
using static PantryPress.Tests.Recipes.RecipeJsonLdCases;

namespace PantryPress.Tests.Recipes;

// PP4.10: the laws the JSON-LD keeps over any recipe a revision can hold (RCP-008, RCP-002, RCP-006): the same recipe is
// the same bytes; the bytes parse, are ASCII, and are safe in a script element; the blogger's words come back exactly; and
// nothing appears that was not entered.
public sealed class RecipeJsonLdPropertyTests
{
    private const long Iterations = 5_000;

    // The line and paragraph separators, an astral character, and a combining accent, built from code points so no literal
    // separator sits in this file and ends a C# string.
    private static readonly string[] Separators =
        [char.ConvertFromUtf32(0x2028), char.ConvertFromUtf32(0x2029), char.ConvertFromUtf32(0x1F36E), char.ConvertFromUtf32(0x0301)];

    // Any text: ASCII, HTML-sensitive characters, non-ASCII, separators, and surrogate pairs, but never a lone surrogate,
    // which a snapshot refuses before it is stored.
    private static readonly Gen<string> Text = Gen.Frequency(
        (3, Gen.String),
        (2, Gen.OneOfConst("<", ">", "&", "\"", "'", "</script>", Separators[0], Separators[1], Separators[2], "e" + Separators[3], "\\", " ", "")
            .Array[0, 6].Select(parts => string.Concat(parts))))
        .Where(s => !HasLoneSurrogate(s));

    private static readonly Gen<string?> OptionalText = Gen.Frequency((1, Gen.Const((string?)null)), (3, Text.Select(s => (string?)s)));

    private static readonly Gen<int?> Minutes = Gen.Frequency(
        (2, Gen.Const((int?)null)),
        (1, Gen.Const((int?)0)),
        (3, Gen.Int[0, RecipeLimits.MaxMinutes].Select(m => (int?)m)));

    private static readonly Gen<long?> Hundredths = Gen.Frequency(
        (3, Gen.Const((long?)null)),
        (2, Gen.Long[0, NutritionFacts.MaxHundredths].Select(v => (long?)v)));

    private static readonly Gen<RecipeSnapshotNutrition> NutritionGen = Gen.Select(
        OptionalText, Hundredths.Array[11],
        (serving, v) => new RecipeSnapshotNutrition(serving, v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10]));

    private static readonly Gen<(string? Heading, string[] Items)[]> Groups =
        Gen.Select(OptionalText, Text.Where(t => t.Length > 0).Array[0, 5], (h, items) => (h, items)).Array[0, 4];

    private static readonly Gen<Quantity?> Yield = Gen.Frequency(
        (1, Gen.Const((Quantity?)null)),
        (3, Gen.Long[1, 1000].Select(n => (Quantity?)Quantity.Amount(n))),
        (1, Gen.Select(Gen.Long[1, 50], Gen.Long[1, 50], (a, b) => (Quantity?)Quantity.Range(a, a + b))));

    private static readonly Gen<RecipeSnapshot> Snapshot =
        Gen.Select(
            Gen.Select(Text.Where(t => !string.IsNullOrWhiteSpace(t)), OptionalText, Yield, OptionalText),
            Gen.Select(Minutes, Minutes, Minutes, OptionalText),
            Gen.Select(Groups, Groups, NutritionGen),
            Gen.Select(
                Gen.OneOfConst([.. Courses.All.Select(e => e.Code)]).Array[0, 3],
                Gen.OneOfConst([.. Cuisines.All.Select(e => e.Code)]).Array[0, 3],
                Gen.OneOfConst([.. Diets.All.Select(e => e.Code)]).Array[0, 4],
                Text.Array[0, 5]),
            (a, b, c, d) => Recipe(
                a.Item1,
                summary: a.Item2,
                yield: a.Item3,
                yieldUnit: a.Item4,
                prep: b.Item1,
                cook: b.Item2,
                additional: b.Item3,
                videoUrl: b.Item4 is null ? null : "https://video.example.com/v",
                ingredients: c.Item1,
                steps: c.Item2,
                nutrition: c.Item3,
                courses: [.. d.Item1.Distinct()],
                cuisines: [.. d.Item2.Distinct()],
                diets: [.. d.Item3.Distinct()],
                keywords: d.Item4));

    // Any context the page could give: an author named in any words, and each part present or not.
    private static readonly Gen<RecipeJsonLdContext> Context = Gen.Select(
        Gen.Bool,
        Gen.Int[0, 3],
        Gen.Frequency((1, Gen.Const((RecipeJsonLdAuthor?)null)), (3, Gen.Select(Text.Where(t => !string.IsNullOrWhiteSpace(t)), Gen.Bool, (name, hasUrl) => (RecipeJsonLdAuthor?)new RecipeJsonLdAuthor(name, hasUrl ? "https://a.example/about" : null)))),
        Gen.Frequency((1, Gen.Const((DateTimeOffset?)null)), (3, Gen.Long[0, 4_000_000_000].Select(s => (DateTimeOffset?)DateTimeOffset.FromUnixTimeSeconds(s)))),
        (hasUrl, images, author, date) => new RecipeJsonLdContext(
            hasUrl ? "https://a.example/posts/p" : null,
            [.. Enumerable.Range(0, images).Select(i => $"https://a.example/media/{i}.jpg")],
            author,
            date));

    // The same recipe, read back from a stored revision with fresh lists, writes the same bytes: what page data builds
    // from a revision is what these tests see (PP-D04).
    [Fact]
    public void A_stored_revision_writes_the_same_bytes() =>
        Gen.Select(Snapshot, Context).Sample(iter: Iterations, assert: input =>
        {
            var stored = RecipeSnapshotJson.Read(RecipeSnapshotJson.CurrentVersion, RecipeSnapshotJson.Write(input.Item1).Json);
            var first = RecipeJsonLd.Write(input.Item1, input.Item2);
            var second = RecipeJsonLd.Write(stored, input.Item2);
            Assert.Equal(first.Json, second.Json);
            Assert.Equal(first.Issues, second.Issues);
        });

    [Fact]
    public void No_internal_id_is_ever_written() =>
        Gen.Select(Snapshot, Context).Sample(iter: Iterations, assert: input =>
        {
            var json = RecipeJsonLd.Write(input.Item1, input.Item2).Json;
            var snapshot = input.Item1;
            IEnumerable<Guid> ids =
            [
                snapshot.Id.Value,
                .. snapshot.IngredientGroups.Select(g => g.Id.Value),
                .. snapshot.IngredientGroups.SelectMany(g => g.Lines).Select(l => l.Id.Value),
                .. snapshot.StepGroups.Select(g => g.Id.Value),
                .. snapshot.StepGroups.SelectMany(g => g.Steps).Select(s => s.Id.Value),
            ];
            Assert.All(ids, id => Assert.DoesNotContain(id.ToString("D"), json, StringComparison.OrdinalIgnoreCase));
        });

    [Fact]
    public void Issues_come_in_the_enum_order_without_repeats() =>
        Gen.Select(Snapshot, Context).Sample(iter: Iterations, assert: input =>
        {
            var issues = RecipeJsonLd.Write(input.Item1, input.Item2).Issues;
            Assert.Equal(issues.Distinct().Order(), issues);
        });

    [Fact]
    public void The_output_parses_is_ASCII_and_is_safe_in_a_script_element() =>
        Gen.Select(Snapshot, Context).Sample(iter: Iterations, assert: input =>
        {
            var json = RecipeJsonLd.Write(input.Item1, input.Item2).Json;
            Assert.All(json, c => Assert.InRange(c, ' ', '~'));
            Assert.DoesNotContain('<', json);
            Assert.DoesNotContain('>', json);
            Assert.DoesNotContain('&', json);
            using var parsed = JsonDocument.Parse(json);
            Assert.Equal(JsonValueKind.Object, parsed.RootElement.ValueKind);
        });

    [Fact]
    public void The_blogger_s_words_come_back_exactly() =>
        Snapshot.Sample(iter: Iterations, assert: snapshot =>
        {
            using var parsed = JsonDocument.Parse(RecipeJsonLd.Write(snapshot, FullContext).Json);
            var root = parsed.RootElement;

            Assert.Equal(snapshot.Title, root.GetProperty("name").GetString());
            var lines = snapshot.IngredientGroups.SelectMany(g => g.Lines).Select(l => l.OriginalText).ToList();
            Assert.Equal(lines, root.TryGetProperty("recipeIngredient", out var ingredients) ? ingredients.EnumerateArray().Select(e => e.GetString()!) : []);

            var steps = snapshot.StepGroups.SelectMany(g => g.Steps).Select(s => s.Text).ToList();
            Assert.Equal(steps, root.TryGetProperty("recipeInstructions", out var instructions) ? Steps(instructions) : []);
        });

    [Fact]
    public void Nothing_appears_that_was_not_entered() =>
        Snapshot.Sample(iter: Iterations, assert: snapshot =>
        {
            using var parsed = JsonDocument.Parse(RecipeJsonLd.Write(snapshot, FullContext).Json);
            var root = parsed.RootElement;
            var facts = NutritionFacts.From(snapshot.Nutrition);

            // Nutrition only as entered, and only beside a yield.
            var written = root.TryGetProperty("nutrition", out var nutrition)
                ? nutrition.EnumerateObject().Select(p => p.Name).Where(n => n is not "@type" and not "servingSize").ToList()
                : [];
            Assert.Equal(snapshot.Yield is null ? [] : facts.Facts.Select(f => RecipeJsonLd.PropertyOf(f.Nutrient)), written);

            Assert.Equal(snapshot.PrepMinutes is not null, root.TryGetProperty("prepTime", out _));
            Assert.Equal(snapshot.CookMinutes is not null, root.TryGetProperty("cookTime", out _));
            Assert.Equal(
                snapshot.PrepMinutes is not null || snapshot.CookMinutes is not null || snapshot.AdditionalMinutes is not null,
                root.TryGetProperty("totalTime", out _));
            Assert.Equal(snapshot.Yield is not null, root.TryGetProperty("recipeYield", out _));
            Assert.False(root.TryGetProperty("video", out _));
            Assert.False(root.TryGetProperty("aggregateRating", out _));
            Assert.False(root.TryGetProperty("review", out _));

            var diets = root.TryGetProperty("suitableForDiet", out var d) ? d.EnumerateArray().Select(e => e.GetString()).ToList() : [];
            Assert.Equal(
                snapshot.Diets.Select(code => Diets.All.Single(e => e.Code == code).SchemaOrgDiet).OfType<string>().Select(m => "https://schema.org/" + m),
                diets);
        });

    [Fact]
    public void Each_issue_matches_what_is_missing() =>
        Gen.Select(Snapshot, Context).Sample(iter: Iterations, assert: input =>
        {
            var (snapshot, context) = input;
            var issues = RecipeJsonLd.Write(snapshot, context).Issues;
            var facts = NutritionFacts.From(snapshot.Nutrition);

            Assert.Equal(context.Images.Count == 0, issues.Contains(RecipeJsonLdIssue.ImageMissing));
            Assert.Equal(snapshot.Yield is null && facts.HasNutrition, issues.Contains(RecipeJsonLdIssue.YieldMissingForNutrition));
            Assert.Equal(snapshot.Yield is null && !facts.HasNutrition, issues.Contains(RecipeJsonLdIssue.YieldMissing));
            Assert.Equal(snapshot.VideoUrl is not null, issues.Contains(RecipeJsonLdIssue.VideoNotDescribed));
            Assert.Equal(facts.Facts.All(f => f.Nutrient != Nutrient.Calories), issues.Contains(RecipeJsonLdIssue.CaloriesMissing));
            Assert.Equal(snapshot.Courses.Count == 0, issues.Contains(RecipeJsonLdIssue.CategoryMissing));
            Assert.Equal(snapshot.Cuisines.Count == 0, issues.Contains(RecipeJsonLdIssue.CuisineMissing));
            Assert.Equal(context.Author is null, issues.Contains(RecipeJsonLdIssue.AuthorMissing));
            Assert.Equal(context.DatePublished is null, issues.Contains(RecipeJsonLdIssue.DatePublishedMissing));
            Assert.Equal(string.IsNullOrWhiteSpace(snapshot.Summary), issues.Contains(RecipeJsonLdIssue.DescriptionMissing));

            var (prep, cook, additional) = (snapshot.PrepMinutes, snapshot.CookMinutes, snapshot.AdditionalMinutes);
            Assert.Equal(prep is null && cook is null && additional is null, issues.Contains(RecipeJsonLdIssue.TimesMissing));
            Assert.Equal(prep is null && cook is not null, issues.Contains(RecipeJsonLdIssue.PrepTimeMissing));
            Assert.Equal(cook is null && prep is not null, issues.Contains(RecipeJsonLdIssue.CookTimeMissing));

            Assert.Equal(!snapshot.IngredientGroups.Any(g => g.Lines.Count > 0), issues.Contains(RecipeJsonLdIssue.IngredientsMissing));
            Assert.Equal(!snapshot.StepGroups.Any(g => g.Steps.Count > 0), issues.Contains(RecipeJsonLdIssue.InstructionsMissing));

            using var parsed = JsonDocument.Parse(RecipeJsonLd.Write(snapshot, context).Json);
            Assert.Equal(!parsed.RootElement.TryGetProperty("keywords", out _), issues.Contains(RecipeJsonLdIssue.KeywordsMissing));
        });

    private static List<string> Steps(JsonElement instructions)
    {
        var texts = new List<string>();
        foreach (var item in instructions.EnumerateArray())
        {
            if (item.GetProperty("@type").GetString() == "HowToSection")
            {
                texts.AddRange(item.GetProperty("itemListElement").EnumerateArray().Select(s => s.GetProperty("text").GetString()!));
            }
            else
            {
                texts.Add(item.GetProperty("text").GetString()!);
            }
        }

        return texts;
    }

    private static bool HasLoneSurrogate(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(s[i]))
            {
                return true;
            }
        }

        return false;
    }
}
