using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>A recipe's JSON-LD, and what it lacks for a recipe rich result, in <see cref="RecipeJsonLdIssue"/>'s order.</summary>
internal sealed record RecipeJsonLdDocument(string Json, IReadOnlyList<RecipeJsonLdIssue> Issues);

/// <summary>
/// The one generator of a recipe's schema.org <c>Recipe</c> JSON-LD (RCP-008; PP4.10), from a published revision's snapshot
/// and what the page knows (<see cref="RecipeJsonLdContext"/>). It is pure and deterministic: the same input always gives the
/// same bytes, with no clock, culture, floating point, or I/O, so the recipe card's style never changes it (THM-009).
/// <list type="bullet">
/// <item>Every fact is the blogger's, as entered (RCP-002, RCP-006): names, summary, ingredient lines, and steps exactly as
/// stored; times as entered (zero included) and their sum; nutrition only as entered, never derived; diets only as set.
/// Nothing is inferred, and no rating or review is written: <c>aggregateRating</c> belongs inside the Recipe, where Google
/// reads it, and PP12.8 adds it here from approved ratings only, through a typed input on the context (RND-005).</item>
/// <item>Nutrition is written only beside a yield: Google requires the yield with per-serving nutrition, and without it the
/// whole recipe would lose its rich result. Until a yield is entered the nutrition is withheld and
/// <see cref="RecipeJsonLdIssue.YieldMissingForNutrition"/> asks for one; nothing is changed or estimated.</item>
/// <item>Properties come in one fixed order and an empty one is left out. Every character outside ASCII, and every
/// HTML-sensitive one (<c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, quotes), is escaped, so the bytes are ASCII and can sit
/// inside a <c>&lt;script type="application/ld+json"&gt;</c> element whatever the blogger wrote.</item>
/// <item>A video address alone cannot describe a video as Google requires (title, thumbnail, upload date), so it is left
/// out and reported; so are allergens, equipment, and notes, which a recipe rich result does not read.</item>
/// </list>
/// Vocabulary checked against schema.org V30.1 and Google's recipe guidance on 2026-11-14.
/// </summary>
internal static class RecipeJsonLd
{
    private const string SchemaOrg = "https://schema.org";
    private const string ListSeparator = ", ";

    // Named, never left to a default: this is what keeps the output ASCII and safe inside a script element, so a relaxed
    // encoder must never reach it.
    private static readonly JavaScriptEncoder Escaping = JavaScriptEncoder.Default;

    private static readonly Dictionary<string, string> CourseNames = Courses.All.ToDictionary(e => e.Code, e => e.Name, StringComparer.Ordinal);
    private static readonly Dictionary<string, string> CuisineNames = Cuisines.All.ToDictionary(e => e.Code, e => e.Name, StringComparer.Ordinal);
    private static readonly Dictionary<string, string?> DietMembers = Diets.All.ToDictionary(e => e.Code, e => e.SchemaOrgDiet, StringComparer.Ordinal);

    /// <summary>The page-data form: compact.</summary>
    public static RecipeJsonLdDocument Write(RecipeSnapshot snapshot, RecipeJsonLdContext context) => Write(snapshot, context, indented: false);

    /// <summary>The same JSON-LD, indented only when asked; the golden files are indented to be read (PP4.10).</summary>
    internal static RecipeJsonLdDocument Write(RecipeSnapshot snapshot, RecipeJsonLdContext context, bool indented)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);

        // Google requires a name. The table refuses a blank title (CK_Recipes_Title), so one here is a bug.
        if (string.IsNullOrWhiteSpace(snapshot.Title))
        {
            throw new ArgumentException("A recipe has a title.", nameof(snapshot));
        }

        var times = RecipeTimes.From(snapshot.PrepMinutes, snapshot.CookMinutes, snapshot.AdditionalMinutes);
        var nutrition = NutritionFacts.From(snapshot.Nutrition);
        var categories = Names(snapshot.Courses, CourseNames, "course");
        var cuisines = Names(snapshot.Cuisines, CuisineNames, "cuisine");
        var keywords = Keywords(snapshot.Keywords, [.. categories, .. cuisines]);
        var diets = snapshot.Diets.Select(code => DietMembers.TryGetValue(code, out var member)
                ? member
                : throw new ArgumentException($"'{code}' is not a diet.", nameof(snapshot)))
            .OfType<string>()
            .ToList();
        var ingredients = snapshot.IngredientGroups.SelectMany(g => g.Lines).Select(l => l.OriginalText).ToList();
        var stepGroups = snapshot.StepGroups.Where(g => g.Steps.Count > 0).ToList();
        var description = Present(snapshot.Summary);
        var yield = snapshot.Yield is null ? null : YieldText(snapshot.Yield, snapshot.YieldUnit);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented, Encoder = Escaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("@context", SchemaOrg);
            writer.WriteString("@type", "Recipe");
            writer.WriteString("name", snapshot.Title);
            Text(writer, "description", description);
            Text(writer, "url", context.Url);
            Texts(writer, "image", context.Images);

            if (context.Author is { } author)
            {
                writer.WriteStartObject("author");
                writer.WriteString("@type", "Person");
                writer.WriteString("name", author.Name);
                Text(writer, "url", author.Url);
                writer.WriteEndObject();
            }

            if (context.DatePublished is { } published)
            {
                writer.WriteString("datePublished", published.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            }

            Text(writer, "prepTime", times.Prep?.IsoDuration);
            Text(writer, "cookTime", times.Cook?.IsoDuration);
            Text(writer, "totalTime", times.Total?.IsoDuration);
            Text(writer, "recipeYield", yield);
            Text(writer, "recipeCategory", Joined(categories));
            Text(writer, "recipeCuisine", Joined(cuisines));
            Text(writer, "keywords", Joined(keywords));
            Texts(writer, "suitableForDiet", [.. diets.Select(member => $"{SchemaOrg}/{member}")]);
            Texts(writer, "recipeIngredient", ingredients);
            WriteInstructions(writer, stepGroups);
            if (yield is not null)
            {
                WriteNutrition(writer, nutrition);
            }
            writer.WriteEndObject();
        }

        var issues = new SortedSet<RecipeJsonLdIssue>();
        Add(issues, context.Images.Count == 0, RecipeJsonLdIssue.ImageMissing);
        Add(issues, nutrition.HasNutrition && yield is null, RecipeJsonLdIssue.YieldMissingForNutrition);
        Add(issues, context.Author is null, RecipeJsonLdIssue.AuthorMissing);
        Add(issues, context.DatePublished is null, RecipeJsonLdIssue.DatePublishedMissing);
        Add(issues, description is null, RecipeJsonLdIssue.DescriptionMissing);
        Add(issues, times.Total is null, RecipeJsonLdIssue.TimesMissing);
        Add(issues, times.Prep is null && times.Cook is not null, RecipeJsonLdIssue.PrepTimeMissing);
        Add(issues, times.Cook is null && times.Prep is not null, RecipeJsonLdIssue.CookTimeMissing);
        Add(issues, yield is null && !nutrition.HasNutrition, RecipeJsonLdIssue.YieldMissing);
        Add(issues, ingredients.Count == 0, RecipeJsonLdIssue.IngredientsMissing);
        Add(issues, stepGroups.Count == 0, RecipeJsonLdIssue.InstructionsMissing);
        Add(issues, categories.Count == 0, RecipeJsonLdIssue.CategoryMissing);
        Add(issues, cuisines.Count == 0, RecipeJsonLdIssue.CuisineMissing);
        Add(issues, keywords.Count == 0, RecipeJsonLdIssue.KeywordsMissing);
        Add(issues, nutrition.Facts.All(f => f.Nutrient != Nutrient.Calories), RecipeJsonLdIssue.CaloriesMissing);
        Add(issues, snapshot.VideoUrl is not null, RecipeJsonLdIssue.VideoNotDescribed);

        return new RecipeJsonLdDocument(Encoding.UTF8.GetString(buffer.WrittenSpan), [.. issues]);
    }

    /// <summary>schema.org's name for a nutrient's property.</summary>
    public static string PropertyOf(Nutrient nutrient) => nutrient switch
    {
        Nutrient.Calories => "calories",
        Nutrient.Fat => "fatContent",
        Nutrient.SaturatedFat => "saturatedFatContent",
        Nutrient.TransFat => "transFatContent",
        Nutrient.UnsaturatedFat => "unsaturatedFatContent",
        Nutrient.Cholesterol => "cholesterolContent",
        Nutrient.Sodium => "sodiumContent",
        Nutrient.Carbohydrate => "carbohydrateContent",
        Nutrient.Fiber => "fiberContent",
        Nutrient.Sugar => "sugarContent",
        Nutrient.Protein => "proteinContent",
        _ => throw new ArgumentOutOfRangeException(nameof(nutrient), nutrient, "Not a nutrient."),
    };

    /// <summary>
    /// A nutrient's value as schema.org's <c>Energy</c> and <c>Mass</c> take it: the amount exactly as entered, then the
    /// unit's word (<c>270 calories</c>, <c>14.5 grams</c>, <c>75 milligrams</c>). Not the recipe card's <c>kcal</c> text
    /// (PP4.9). The word is always plural: it is a unit for a reader of markup, not a sentence.
    /// </summary>
    public static string ValueOf(NutritionFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        var unit = fact.Unit switch
        {
            "kcal" => "calories",
            "g" => "grams",
            "mg" => "milligrams",
            _ => throw new ArgumentException($"'{fact.Unit}' is not a nutrition unit.", nameof(fact)),
        };
        return $"{fact.Amount} {unit}";
    }

    // The yield in the recipe card's words at 1x (PP4.7): the amount, then the blogger's own unit word if any.
    private static string YieldText(Quantity yield, string? unit)
    {
        var amount = QuantityText.Format(yield, QuantityStyle.Fraction);
        return Present(unit) is { } word ? $"{amount} {word}" : amount;
    }

    private static void WriteInstructions(Utf8JsonWriter writer, List<RecipeSnapshotStepGroup> groups)
    {
        if (groups.Count == 0)
        {
            return;
        }

        // Sections only where the blogger named a group: a headed group is a HowToSection, which needs a name, and a
        // group with no heading lists its steps in place.
        writer.WriteStartArray("recipeInstructions");
        foreach (var group in groups)
        {
            if (Present(group.Heading) is { } heading)
            {
                writer.WriteStartObject();
                writer.WriteString("@type", "HowToSection");
                writer.WriteString("name", heading);
                writer.WriteStartArray("itemListElement");
                WriteSteps(writer, group.Steps);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            else
            {
                WriteSteps(writer, group.Steps);
            }
        }

        writer.WriteEndArray();
    }

    private static void WriteSteps(Utf8JsonWriter writer, IReadOnlyList<RecipeSnapshotStep> steps)
    {
        foreach (var step in steps)
        {
            writer.WriteStartObject();
            writer.WriteString("@type", "HowToStep");
            writer.WriteString("text", step.Text);
            writer.WriteEndObject();
        }
    }

    private static void WriteNutrition(Utf8JsonWriter writer, NutritionFacts nutrition)
    {
        if (!nutrition.HasNutrition)
        {
            return;
        }

        writer.WriteStartObject("nutrition");
        writer.WriteString("@type", "NutritionInformation");
        Text(writer, "servingSize", Present(nutrition.ServingSize));
        foreach (var fact in nutrition.Facts)
        {
            writer.WriteString(PropertyOf(fact.Nutrient), ValueOf(fact));
        }

        writer.WriteEndObject();
    }

    // Each code's name, in the snapshot's order. A code the list does not hold cannot have been stored, so it is a bug.
    private static List<string> Names(IReadOnlyList<string> codes, Dictionary<string, string> names, string what) =>
        [.. codes.Select(code => names.TryGetValue(code, out var name) ? name : throw new ArgumentException($"'{code}' is not a {what}.", nameof(codes)))];

    // Google asks that no keyword repeat the recipe's own category or cuisine, so one that matches either - compared with
    // ASCII letters folded and white space (as .NET's Trim reads it) at its ends ignored, nothing else - is left out (owner
    // decision, PP4.10). The rest stay exactly as written, in order: a repeat is kept, and so is a comma inside one, which
    // a reader of the joined list takes as two keywords.
    private static List<string> Keywords(IReadOnlyList<string> keywords, IReadOnlyList<string> taken)
    {
        var folded = taken.Select(t => FoldAscii(t.Trim())).ToHashSet(StringComparer.Ordinal);
        return [.. keywords.Where(k => Present(k) is not null && !folded.Contains(FoldAscii(k.Trim())))];
    }

    private static string FoldAscii(string value) => string.Create(value.Length, value, static (span, text) =>
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            span[i] = c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;
        }
    });

    private static string? Present(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? Joined(IReadOnlyList<string> values) => values.Count == 0 ? null : string.Join(ListSeparator, values);

    private static void Text(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    private static void Texts(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static void Add(SortedSet<RecipeJsonLdIssue> issues, bool when, RecipeJsonLdIssue issue)
    {
        if (when)
        {
            issues.Add(issue);
        }
    }
}
