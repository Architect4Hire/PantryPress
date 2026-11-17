using System.Buffers;
using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// What a recipe's JSON-LD needs from the page it is published on (PP4.10), because a recipe holds none of it: the image of
/// the finished dish, the author, and the publish date are the post's (POST-004). The caller passes what it has; anything
/// missing is left out and reported, never made up.
/// </summary>
/// <param name="Url">The page's absolute <c>https</c> address, with no fragment. The recipe's node is <c>{Url}#recipe</c>.</param>
/// <param name="ImageUrls">The finished dish's images, absolute <c>https</c> addresses, in the order given.</param>
/// <param name="AuthorName">The author's name, as shown on the page.</param>
/// <param name="DatePublished">The day the page was published, in the site's time zone; the caller decides it.</param>
internal sealed record RecipeJsonLdPage(
    string? Url,
    IReadOnlyList<string> ImageUrls,
    string? AuthorName,
    DateOnly? DatePublished)
{
    /// <summary>A page that gives nothing: the recipe editor's case (PP4.15), where the recipe sits in no post yet.</summary>
    public static RecipeJsonLdPage None { get; } = new(null, [], null, null);
}

/// <summary>A recipe's JSON-LD and what it lacks, in <see cref="RecipeJsonLdGap"/>'s order.</summary>
internal sealed record RecipeJsonLdResult(string Json, IReadOnlyList<RecipeJsonLdGap> Gaps);

/// <summary>
/// The one schema.org <c>Recipe</c> JSON-LD generator (RCP-008; PP4.10), from a published revision's snapshot and its page.
/// It is pure and deterministic: the same snapshot and page give the same bytes on every machine, with no clock, culture, or
/// floating point, and every property comes in one fixed order. It writes only what the blogger entered:
/// <list type="bullet">
/// <item>The blogger's words exactly: the title, the summary, each ingredient line's original text (an unparsed line
/// included), each step, each heading, each keyword, the equipment, the serving size, and the yield's unit. A text with
/// nothing but white space in it is left out, as if empty.</item>
/// <item>Times as entered, a zero included (<c>PT0M</c>; owner decision, PP4.9), and the total whenever any time was
/// entered (<see cref="RecipeTimes"/>).</item>
/// <item>Nutrition only as entered, per serving (<see cref="NutritionFacts"/>), never estimated or derived (RCP-006).</item>
/// <item>A diet only when it is a schema.org <c>RestrictedDiet</c> member (<see cref="Diets"/>).</item>
/// </list>
/// Never written: a rating or review (<c>aggregateRating</c> comes from real ratings only, PP12), allergens (schema.org has
/// no property for them), the notes, a step's name or address, and the video, whose <c>VideoObject</c> needs a name, a
/// thumbnail, and an upload date that a recipe does not hold (owner decision, PP4.10). The output escapes every character
/// outside ASCII and every HTML-sensitive one, so it stays inert inside a <c>script</c> element (PP7.10).
/// </summary>
internal static class RecipeJsonLd
{
    public const string SchemaOrg = "https://schema.org";

    /// <summary>The fragment the recipe's node takes on its page, so a page's graph can point at it (PP7.10).</summary>
    public const string NodeFragment = "#recipe";

    /// <summary>What keywords, courses, and cuisines are joined with, as Google's examples write a list in one text.</summary>
    public const string ListSeparator = ", ";

    private static readonly JsonWriterOptions CompactOptions = new() { Indented = false };

    private static readonly FrozenDictionary<string, string> CourseNames =
        Courses.All.ToFrozenDictionary(e => e.Code, e => e.Name, StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> CuisineNames =
        Cuisines.All.ToFrozenDictionary(e => e.Code, e => e.Name, StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string?> DietMembers =
        Diets.All.ToFrozenDictionary(e => e.Code, e => e.SchemaOrgDiet, StringComparer.Ordinal);

    /// <summary>The recipe's JSON-LD document, with <c>@context</c>, written compactly, and what it lacks.</summary>
    public static RecipeJsonLdResult Write(RecipeSnapshot snapshot, RecipeJsonLdPage page)
    {
        var buffer = new ArrayBufferWriter<byte>();
        IReadOnlyList<RecipeJsonLdGap> gaps;
        using (var writer = new Utf8JsonWriter(buffer, CompactOptions))
        {
            gaps = WriteNode(writer, snapshot, page, withContext: true);
        }

        return new RecipeJsonLdResult(Encoding.UTF8.GetString(buffer.WrittenSpan), gaps);
    }

    /// <summary>
    /// Writes the recipe's node as one JSON object and returns what it lacks. Without <paramref name="withContext"/> it has no
    /// <c>@context</c>, for a page's <c>@graph</c> that declares it once (PP7.10). The writer must be compact and use the
    /// default encoder, which escapes every character outside ASCII and every HTML-sensitive one: with any other encoder, a
    /// blogger's <c>&lt;/script&gt;</c> could close the element the JSON-LD sits in, and indenting would make other bytes
    /// for the same recipe. Either is a bug, and throws before anything is written.
    /// </summary>
    public static IReadOnlyList<RecipeJsonLdGap> WriteNode(
        Utf8JsonWriter writer, RecipeSnapshot snapshot, RecipeJsonLdPage page, bool withContext)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(page);
        if (writer.Options.Indented
            || (writer.Options.Encoder is { } encoder && !ReferenceEquals(encoder, JavaScriptEncoder.Default)))
        {
            throw new ArgumentException(
                "JSON-LD is written compactly with the default encoder only, so its bytes are one form and inert inside a script element.",
                nameof(writer));
        }

        RequirePage(page);
        RequireSnapshot(snapshot);

        var gaps = new SortedSet<RecipeJsonLdGap>();
        void Lacks(RecipeJsonLdGap gap) => gaps.Add(gap);

        writer.WriteStartObject();
        if (withContext)
        {
            writer.WriteString("@context", SchemaOrg);
        }

        writer.WriteString("@type", "Recipe");
        if (page.Url is { } url)
        {
            writer.WriteString("@id", url + NodeFragment);
        }

        if (!OptionalText(writer, "name", snapshot.Title))
        {
            Lacks(RecipeJsonLdGap.Name);
        }

        if (!OptionalText(writer, "description", snapshot.Summary))
        {
            Lacks(RecipeJsonLdGap.Description);
        }

        if (page.ImageUrls.Count > 0)
        {
            writer.WriteStartArray("image");
            foreach (var image in page.ImageUrls)
            {
                writer.WriteStringValue(image);
            }

            writer.WriteEndArray();
        }
        else
        {
            Lacks(RecipeJsonLdGap.Image);
        }

        if (HasText(page.AuthorName))
        {
            writer.WriteStartObject("author");
            writer.WriteString("@type", "Person");
            WriteText(writer, "name", page.AuthorName!);
            writer.WriteEndObject();
        }
        else
        {
            Lacks(RecipeJsonLdGap.Author);
        }

        if (page.DatePublished is { } published)
        {
            writer.WriteString("datePublished", published.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        else
        {
            Lacks(RecipeJsonLdGap.DatePublished);
        }

        WriteTimes(writer, snapshot, Lacks);

        var nutrition = NutritionFacts.From(snapshot.Nutrition);
        if (!WriteYield(writer, snapshot))
        {
            Lacks(nutrition.HasNutrition ? RecipeJsonLdGap.YieldForNutrition : RecipeJsonLdGap.RecipeYield);
        }

        if (!JoinedNames(writer, "recipeCategory", snapshot.Courses, CourseNames))
        {
            Lacks(RecipeJsonLdGap.Courses);
        }

        if (!JoinedNames(writer, "recipeCuisine", snapshot.Cuisines, CuisineNames))
        {
            Lacks(RecipeJsonLdGap.Cuisines);
        }

        var keywords = Texts(snapshot.Keywords);
        if (keywords.Count > 0)
        {
            WriteText(writer, "keywords", string.Join(ListSeparator, keywords));
        }
        else
        {
            Lacks(RecipeJsonLdGap.Keywords);
        }

        WriteDiets(writer, snapshot.Diets);
        TextArray(writer, "tool", Texts(snapshot.Equipment));

        var ingredients = Texts(snapshot.IngredientGroups.SelectMany(g => g.Lines).Select(l => l.OriginalText));
        if (!TextArray(writer, "recipeIngredient", ingredients))
        {
            Lacks(RecipeJsonLdGap.Ingredients);
        }

        if (!WriteInstructions(writer, snapshot.StepGroups))
        {
            Lacks(RecipeJsonLdGap.Instructions);
        }

        WriteNutrition(writer, nutrition, Lacks);
        writer.WriteEndObject();
        writer.Flush();
        return [.. gaps];
    }

    private static void WriteTimes(Utf8JsonWriter writer, RecipeSnapshot snapshot, Action<RecipeJsonLdGap> lacks)
    {
        var times = RecipeTimes.From(snapshot.PrepMinutes, snapshot.CookMinutes, snapshot.AdditionalMinutes);
        if (times.Prep is { } prep)
        {
            writer.WriteString("prepTime", prep.IsoDuration);
        }

        if (times.Cook is { } cook)
        {
            writer.WriteString("cookTime", cook.IsoDuration);
        }

        if (times.Total is { } total)
        {
            writer.WriteString("totalTime", total.IsoDuration);
        }
        else
        {
            lacks(RecipeJsonLdGap.Times);
        }

        if ((times.Prep is null) != (times.Cook is null))
        {
            lacks(RecipeJsonLdGap.PrepAndCookTime);
        }
    }

    // The yield as a reader reads it: its amount, then the blogger's word for it when there is one ("4–6 servings",
    // "1.5 loaves", "12"). Decimals where they end within three places, ASCII fractions otherwise, so the text is plain.
    private static bool WriteYield(Utf8JsonWriter writer, RecipeSnapshot snapshot)
    {
        if (snapshot.Yield is not { HasAmount: true } yield)
        {
            return false;
        }

        var amount = QuantityText.Format(yield, QuantityStyle.Decimal, asciiFractions: true);
        WriteText(writer, "recipeYield", HasText(snapshot.YieldUnit) ? amount + " " + snapshot.YieldUnit : amount);
        return true;
    }

    private static bool JoinedNames(
        Utf8JsonWriter writer, string name, IReadOnlyList<string> codes, FrozenDictionary<string, string> names)
    {
        if (codes.Count == 0)
        {
            return false;
        }

        WriteText(writer, name, string.Join(ListSeparator, codes.Select(code => Lookup(names, code, name))));
        return true;
    }

    private static void WriteDiets(Utf8JsonWriter writer, IReadOnlyList<string> codes)
    {
        var members = codes.Select(code => Lookup(DietMembers, code, "suitableForDiet")).OfType<string>().ToList();
        if (members.Count == 0)
        {
            return;
        }

        writer.WriteStartArray("suitableForDiet");
        foreach (var member in members)
        {
            writer.WriteStringValue(SchemaOrg + "/" + member);
        }

        writer.WriteEndArray();
    }

    // A group with a heading is a HowToSection named by it; a group without one gives its steps in its place. A group with
    // no step is left out, since a section must hold one.
    private static bool WriteInstructions(Utf8JsonWriter writer, IReadOnlyList<RecipeSnapshotStepGroup> groups)
    {
        var written = groups
            .Select(g => (Heading: HasText(g.Heading) ? g.Heading : null, Steps: Texts(g.Steps.Select(s => s.Text))))
            .Where(g => g.Steps.Count > 0)
            .ToList();
        if (written.Count == 0)
        {
            return false;
        }

        writer.WriteStartArray("recipeInstructions");
        foreach (var (heading, steps) in written)
        {
            if (heading is not null)
            {
                writer.WriteStartObject();
                writer.WriteString("@type", "HowToSection");
                WriteText(writer, "name", heading);
                writer.WriteStartArray("itemListElement");
                WriteSteps(writer, steps);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            else
            {
                WriteSteps(writer, steps);
            }
        }

        writer.WriteEndArray();
        return true;
    }

    private static void WriteSteps(Utf8JsonWriter writer, IReadOnlyList<string> steps)
    {
        foreach (var step in steps)
        {
            writer.WriteStartObject();
            writer.WriteString("@type", "HowToStep");
            WriteText(writer, "text", step);
            writer.WriteEndObject();
        }
    }

    // Only with a value entered: a serving size alone is no nutrition, as on the card (PP4.9).
    private static void WriteNutrition(Utf8JsonWriter writer, NutritionFacts nutrition, Action<RecipeJsonLdGap> lacks)
    {
        if (!nutrition.Facts.Any(f => f.Nutrient == Nutrient.Calories))
        {
            lacks(RecipeJsonLdGap.Calories);
        }

        if (!nutrition.HasNutrition)
        {
            return;
        }

        writer.WriteStartObject("nutrition");
        writer.WriteString("@type", "NutritionInformation");
        OptionalText(writer, "servingSize", nutrition.ServingSize);
        foreach (var fact in nutrition.Facts)
        {
            WriteText(writer, PropertyOf(fact.Nutrient), fact.Nutrient == Nutrient.Calories ? fact.Amount + " calories" : fact.Text);
        }

        writer.WriteEndObject();
    }

    /// <summary>The schema.org <c>NutritionInformation</c> property each nutrient is written under.</summary>
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

    // The page is the caller's, so anything malformed in it is a bug, and throws: an address the reader's browser could not
    // follow, or one that would make the recipe's node point somewhere else.
    private static void RequirePage(RecipeJsonLdPage page)
    {
        if (page.Url is { } url)
        {
            RequireHttps(url, nameof(page.Url));
            if (url.Contains('#', StringComparison.Ordinal))
            {
                throw new ArgumentException("The page's address has a fragment; the recipe's node adds its own.", nameof(page));
            }
        }

        ArgumentNullException.ThrowIfNull(page.ImageUrls, nameof(page.ImageUrls));
        foreach (var image in page.ImageUrls)
        {
            RequireHttps(image, nameof(page.ImageUrls));
        }

        if (page.AuthorName is { } author)
        {
            RecipeSnapshotJson.RequireWellFormed(author, nameof(page.AuthorName), owner: "page's");
        }
    }

    // An absolute https address spelled as it will be written: lower-case scheme, a host, no user name or password, and
    // nothing outside printable ASCII or that markup would read (quotes, angle brackets, a backslash).
    private static void RequireHttps(string? address, string name)
    {
        if (address is null
            || !address.StartsWith("https://", StringComparison.Ordinal)
            || address.Any(c => c is <= ' ' or > '~' or '"' or '\'' or '<' or '>' or '\\' or '`')
            || !Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || uri.HostNameType == UriHostNameType.Unknown
            || uri.UserInfo.Length > 0)
        {
            throw new ArgumentException($"The page's {name} holds an address that is not an absolute https URL in ASCII.", name);
        }
    }

    // Everything that could refuse the snapshot is checked before the first byte is written, so a refused node never
    // leaves a caller's writer - a page's open @graph (PP7.10) - half-written.
    private static void RequireSnapshot(RecipeSnapshot snapshot)
    {
        string?[] texts = [snapshot.Title, snapshot.Summary, snapshot.YieldUnit, snapshot.Nutrition.ServingSize];
        var all = texts
            .Concat(snapshot.IngredientGroups.SelectMany(g => g.Lines.Select(l => l.OriginalText)))
            .Concat(snapshot.StepGroups.Select(g => g.Heading))
            .Concat(snapshot.StepGroups.SelectMany(g => g.Steps.Select(s => s.Text)))
            .Concat(snapshot.Equipment)
            .Concat(snapshot.Keywords);
        foreach (var text in all.OfType<string>())
        {
            RecipeSnapshotJson.RequireWellFormed(text, "text");
        }

        foreach (var code in snapshot.Courses)
        {
            Lookup(CourseNames, code, "recipeCategory");
        }

        foreach (var code in snapshot.Cuisines)
        {
            Lookup(CuisineNames, code, "recipeCuisine");
        }

        foreach (var code in snapshot.Diets)
        {
            Lookup(DietMembers, code, "suitableForDiet");
        }
    }

    // A stored code is never removed from its list (PP4.1), so one the list does not hold is a bug.
    private static TValue Lookup<TValue>(FrozenDictionary<string, TValue> map, string code, string property) =>
        map.TryGetValue(code, out var value)
            ? value
            : throw new ArgumentException($"'{code}' is not a code of the vocabulary {property} is written from.", nameof(code));

    private static bool TextArray(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return false;
        }

        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            RecipeSnapshotJson.RequireWellFormed(value, name);
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
        return true;
    }

    private static bool OptionalText(Utf8JsonWriter writer, string name, string? value)
    {
        if (!HasText(value))
        {
            return false;
        }

        WriteText(writer, name, value!);
        return true;
    }

    private static void WriteText(Utf8JsonWriter writer, string name, string value)
    {
        RecipeSnapshotJson.RequireWellFormed(value, name);
        writer.WriteString(name, value);
    }

    private static List<string> Texts(IEnumerable<string> values) => [.. values.Where(HasText)];

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);
}
