using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>A snapshot as a revision stores it: the canonical JSON, and the SHA-256 of its UTF-8 bytes.</summary>
internal sealed record RecipeSnapshotDocument(string Json, byte[] Hash);

/// <summary>
/// The one writer and reader of a revision's snapshot (PP4.4). Writing is canonical, so the same recipe always makes the
/// same bytes and the same hash:
/// <list type="bullet">
/// <item>every property is written, null or not, in one fixed order, with no white space;</item>
/// <item>parts come in the snapshot's own order (<see cref="RecipeWorkingCopy"/> makes it);</item>
/// <item>text is the blogger's exactly, with every character outside ASCII escaped, so the bytes are ASCII;</item>
/// <item>numbers are whole: minutes, spans, a quantity's numerator and denominator, and nutrition in hundredths.</item>
/// </list>
/// Text that is not well-formed UTF-16 - a lone surrogate - is refused, never written as a replacement character, since
/// the blogger's wording is kept exactly or not at all (RCP-002). A revision is never rewritten, so every
/// <see cref="CurrentVersion"/> ever written stays readable here for good.
/// </summary>
internal static class RecipeSnapshotJson
{
    /// <summary>The format this writes. A new format is a new number, and the reader keeps every old one.</summary>
    public const int CurrentVersion = 1;

    private static readonly string[] RecipeProperties =
    [
        "id", "title", "summary", "yield", "yieldUnit", "prepMinutes", "cookMinutes", "additionalMinutes", "additionalTimeLabel",
        "videoUrl", "nutrition", "ingredientGroups", "stepGroups", "equipment", "notes", "keywords", "courses", "cuisines",
        "diets", "allergens",
    ];

    private static readonly string[] NutritionProperties =
    [
        "servingSize", "caloriesHundredths", "fatGramsHundredths", "saturatedFatGramsHundredths", "transFatGramsHundredths",
        "unsaturatedFatGramsHundredths", "cholesterolMilligramsHundredths", "sodiumMilligramsHundredths",
        "carbohydrateGramsHundredths", "fiberGramsHundredths", "sugarGramsHundredths", "proteinGramsHundredths",
    ];

    private static readonly string[] GroupProperties = ["id", "heading", "lines"];
    private static readonly string[] StepGroupProperties = ["id", "heading", "steps"];
    private static readonly string[] StepProperties = ["id", "text"];
    private static readonly string[] SpanProperties = ["start", "length"];
    private static readonly string[] QuantityProperties = ["kind", "low", "high"];
    private static readonly string[] RationalProperties = ["numerator", "denominator"];

    private static readonly string[] LineProperties =
        ["id", "originalText", "parseStatus", "quantity", "quantitySpan", "unitCode", "unitSpan", "item", "note"];

    // The default encoder escapes everything outside ASCII, and the HTML-sensitive characters, the same way every time.
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = false };

    private static readonly JsonDocumentOptions ReaderOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
    };

    public static RecipeSnapshotDocument Write(RecipeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("id", snapshot.Id.Value.ToString("D"));
            Text(writer, "title", snapshot.Title);
            Text(writer, "summary", snapshot.Summary);
            WriteQuantity(writer, "yield", snapshot.Yield);
            Text(writer, "yieldUnit", snapshot.YieldUnit);
            Number(writer, "prepMinutes", snapshot.PrepMinutes);
            Number(writer, "cookMinutes", snapshot.CookMinutes);
            Number(writer, "additionalMinutes", snapshot.AdditionalMinutes);
            Text(writer, "additionalTimeLabel", snapshot.AdditionalTimeLabel);
            Text(writer, "videoUrl", snapshot.VideoUrl);
            WriteNutrition(writer, snapshot.Nutrition);

            writer.WriteStartArray("ingredientGroups");
            foreach (var group in snapshot.IngredientGroups)
            {
                writer.WriteStartObject();
                writer.WriteString("id", group.Id.Value.ToString("D"));
                Text(writer, "heading", group.Heading);
                writer.WriteStartArray("lines");
                foreach (var line in group.Lines)
                {
                    WriteLine(writer, line);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("stepGroups");
            foreach (var group in snapshot.StepGroups)
            {
                writer.WriteStartObject();
                writer.WriteString("id", group.Id.Value.ToString("D"));
                Text(writer, "heading", group.Heading);
                writer.WriteStartArray("steps");
                foreach (var step in group.Steps)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", step.Id.Value.ToString("D"));
                    Text(writer, "text", step.Text);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            Texts(writer, "equipment", snapshot.Equipment);
            Texts(writer, "notes", snapshot.Notes);
            Texts(writer, "keywords", snapshot.Keywords);
            Texts(writer, "courses", snapshot.Courses);
            Texts(writer, "cuisines", snapshot.Cuisines);
            Texts(writer, "diets", snapshot.Diets);
            Texts(writer, "allergens", snapshot.Allergens);
            writer.WriteEndObject();
        }

        var bytes = buffer.WrittenSpan;
        return new RecipeSnapshotDocument(Encoding.UTF8.GetString(bytes), SHA256.HashData(bytes));
    }

    /// <summary>
    /// The snapshot <paramref name="json"/> holds, written in format <paramref name="version"/>. It is read as strictly as
    /// it was written - every property, in its order, and nothing else - because a snapshot this writer did not make is a
    /// bug, and throws.
    /// </summary>
    public static RecipeSnapshot Read(int version, string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (version != CurrentVersion)
        {
            throw new NotSupportedException($"Recipe snapshot format {version} is not one this build has ever written.");
        }

        using var document = JsonDocument.Parse(json, ReaderOptions);
        var recipe = Properties(document.RootElement, RecipeProperties);
        return new RecipeSnapshot(
            new RecipeId(Id(recipe[0])),
            RequiredText(recipe[1]),
            OptionalText(recipe[2]),
            ReadQuantity(recipe[3]),
            OptionalText(recipe[4]),
            OptionalInt(recipe[5]),
            OptionalInt(recipe[6]),
            OptionalInt(recipe[7]),
            OptionalText(recipe[8]),
            OptionalText(recipe[9]),
            ReadNutrition(recipe[10]),
            [.. Items(recipe[11]).Select(ReadIngredientGroup)],
            [.. Items(recipe[12]).Select(ReadStepGroup)],
            [.. Items(recipe[13]).Select(RequiredText)],
            [.. Items(recipe[14]).Select(RequiredText)],
            [.. Items(recipe[15]).Select(RequiredText)],
            [.. Items(recipe[16]).Select(RequiredText)],
            [.. Items(recipe[17]).Select(RequiredText)],
            [.. Items(recipe[18]).Select(RequiredText)],
            [.. Items(recipe[19]).Select(RequiredText)]);
    }

    private static void WriteNutrition(Utf8JsonWriter writer, RecipeSnapshotNutrition nutrition)
    {
        ArgumentNullException.ThrowIfNull(nutrition);
        writer.WriteStartObject("nutrition");
        Text(writer, NutritionProperties[0], nutrition.ServingSize);
        long?[] values =
        [
            nutrition.Calories, nutrition.FatGrams, nutrition.SaturatedFatGrams, nutrition.TransFatGrams, nutrition.UnsaturatedFatGrams,
            nutrition.CholesterolMilligrams, nutrition.SodiumMilligrams, nutrition.CarbohydrateGrams, nutrition.FiberGrams,
            nutrition.SugarGrams, nutrition.ProteinGrams,
        ];
        for (var i = 0; i < values.Length; i++)
        {
            Number(writer, NutritionProperties[i + 1], values[i]);
        }

        writer.WriteEndObject();
    }

    private static void WriteLine(Utf8JsonWriter writer, RecipeSnapshotIngredient line)
    {
        writer.WriteStartObject();
        writer.WriteString("id", line.Id.Value.ToString("D"));
        Text(writer, "originalText", line.OriginalText);
        writer.WriteString("parseStatus", ParseStatusName(line.ParseStatus));
        WriteQuantity(writer, "quantity", line.Quantity);
        WriteSpan(writer, "quantitySpan", line.QuantitySpan);
        Text(writer, "unitCode", line.UnitCode);
        WriteSpan(writer, "unitSpan", line.UnitSpan);
        Text(writer, "item", line.Item);
        Text(writer, "note", line.Note);
        writer.WriteEndObject();
    }

    private static void WriteQuantity(Utf8JsonWriter writer, string name, Quantity? quantity)
    {
        if (quantity is null)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteStartObject(name);
        writer.WriteString("kind", KindName(quantity.Kind));
        WriteRational(writer, "low", quantity.Low);
        WriteRational(writer, "high", quantity.High);
        writer.WriteEndObject();
    }

    private static void WriteRational(Utf8JsonWriter writer, string name, Rational? value)
    {
        if (value is not { } v)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteStartObject(name);
        writer.WriteNumber("numerator", v.Numerator);
        writer.WriteNumber("denominator", v.Denominator);
        writer.WriteEndObject();
    }

    private static void WriteSpan(Utf8JsonWriter writer, string name, RecipeSnapshotSpan? span)
    {
        if (span is not { } s)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteStartObject(name);
        writer.WriteNumber("start", s.Start);
        writer.WriteNumber("length", s.Length);
        writer.WriteEndObject();
    }

    private static void Texts(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            RequireWellFormed(value, name);
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static void Text(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
            return;
        }

        RequireWellFormed(value, name);
        writer.WriteString(name, value);
    }

    private static void Number(Utf8JsonWriter writer, string name, long? value)
    {
        if (value is { } v)
        {
            writer.WriteNumber(name, v);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    // A lone surrogate would be written as U+FFFD by the encoder and read back as something the blogger never wrote.
    private static void RequireWellFormed(string value, string name)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(value[i]))
            {
                throw new ArgumentException(
                    $"The recipe's {name} holds a lone surrogate at {i}, which is not text; it cannot be kept exactly, so it is not kept at all.",
                    nameof(value));
            }
        }
    }

    private static string KindName(QuantityKind kind) => kind switch
    {
        QuantityKind.Amount => "amount",
        QuantityKind.Range => "range",
        QuantityKind.ToTaste => "to-taste",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a quantity kind."),
    };

    private static string ParseStatusName(IngredientParseStatus status) => status switch
    {
        IngredientParseStatus.Parsed => "parsed",
        IngredientParseStatus.Unparsed => "unparsed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Not a parse status."),
    };

    private static RecipeSnapshotNutrition ReadNutrition(JsonElement element)
    {
        var n = Properties(element, NutritionProperties);
        return new RecipeSnapshotNutrition(
            OptionalText(n[0]),
            OptionalLong(n[1]),
            OptionalLong(n[2]),
            OptionalLong(n[3]),
            OptionalLong(n[4]),
            OptionalLong(n[5]),
            OptionalLong(n[6]),
            OptionalLong(n[7]),
            OptionalLong(n[8]),
            OptionalLong(n[9]),
            OptionalLong(n[10]),
            OptionalLong(n[11]));
    }

    private static RecipeSnapshotIngredientGroup ReadIngredientGroup(JsonElement element)
    {
        var g = Properties(element, GroupProperties);
        return new RecipeSnapshotIngredientGroup(
            new RecipeIngredientGroupId(Id(g[0])),
            OptionalText(g[1]),
            [.. Items(g[2]).Select(ReadLine)]);
    }

    private static RecipeSnapshotIngredient ReadLine(JsonElement element)
    {
        var l = Properties(element, LineProperties);
        return new RecipeSnapshotIngredient(
            new RecipeIngredientId(Id(l[0])),
            RequiredText(l[1]),
            RequiredText(l[2]) switch
            {
                "parsed" => IngredientParseStatus.Parsed,
                "unparsed" => IngredientParseStatus.Unparsed,
                var other => throw Malformed($"'{other}' is not a parse status"),
            },
            ReadQuantity(l[3]),
            ReadSpan(l[4]),
            OptionalText(l[5]),
            ReadSpan(l[6]),
            OptionalText(l[7]),
            OptionalText(l[8]));
    }

    private static RecipeSnapshotStepGroup ReadStepGroup(JsonElement element)
    {
        var g = Properties(element, StepGroupProperties);
        return new RecipeSnapshotStepGroup(
            new RecipeStepGroupId(Id(g[0])),
            OptionalText(g[1]),
            [.. Items(g[2]).Select(s =>
            {
                var step = Properties(s, StepProperties);
                return new RecipeSnapshotStep(new RecipeStepId(Id(step[0])), RequiredText(step[1]));
            })]);
    }

    // Through Quantity's own constructors, so a value read back meets every rule a value written did.
    private static Quantity? ReadQuantity(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var q = Properties(element, QuantityProperties);
        var (low, high) = (ReadRational(q[1]), ReadRational(q[2]));
        return (RequiredText(q[0]), low, high) switch
        {
            ("amount", { } l, null) => Quantity.Amount(l),
            ("range", { } l, { } h) => Quantity.Range(l, h),
            ("to-taste", null, null) => Quantity.ToTaste,
            var (kind, _, _) => throw Malformed($"a '{kind}' quantity has the wrong ends"),
        };
    }

    private static Rational? ReadRational(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var r = Properties(element, RationalProperties);
        return Rational.Create(Long(r[0]), Long(r[1]));
    }

    private static RecipeSnapshotSpan? ReadSpan(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var s = Properties(element, SpanProperties);
        return new RecipeSnapshotSpan(Int(s[0]), Int(s[1]));
    }

    // The object's values, which must be exactly these properties in exactly this order.
    private static JsonElement[] Properties(JsonElement element, string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Malformed($"expected an object with {string.Join(", ", names)}");
        }

        var properties = element.EnumerateObject().ToList();
        if (!properties.Select(p => p.Name).SequenceEqual(names, StringComparer.Ordinal))
        {
            throw Malformed($"expected exactly {string.Join(", ", names)}, in that order");
        }

        return [.. properties.Select(p => p.Value)];
    }

    private static IEnumerable<JsonElement> Items(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : throw Malformed("expected a list");

    private static Guid Id(JsonElement element) =>
        Guid.TryParseExact(RequiredText(element), "D", out var id) ? id : throw Malformed("expected an id");

    private static string RequiredText(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString()! : throw Malformed("expected text");

    private static string? OptionalText(JsonElement element) =>
        element.ValueKind == JsonValueKind.Null ? null : RequiredText(element);

    private static long Long(JsonElement element) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var value) ? value : throw Malformed("expected a whole number");

    private static int Int(JsonElement element) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value) ? value : throw Malformed("expected a whole number");

    private static long? OptionalLong(JsonElement element) => element.ValueKind == JsonValueKind.Null ? null : Long(element);

    private static int? OptionalInt(JsonElement element) => element.ValueKind == JsonValueKind.Null ? null : Int(element);

    private static FormatException Malformed(string what) => new($"A recipe snapshot is malformed: {what}.");
}
