using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

/// <summary>One case of the JSON-LD corpus: a stored snapshot, its page, and the files that approve its output.</summary>
internal sealed record RecipeJsonLdCase(string Name, RecipeSnapshot Snapshot, RecipeJsonLdPage Page);

/// <summary>
/// The JSON-LD corpus (PP4.10), <c>Corpus/json-ld/</c>, read from the source tree; its README says how it is written. There
/// is no mode that rewrites it: each expectation is approved by reading it (CLAUDE.md).
/// </summary>
internal static class RecipeJsonLdCorpus
{
    public static string Directory { get; } = Path.Combine(ThisDirectory(), "Corpus", "json-ld");

    // The production bytes written again indented and with non-ASCII as itself, so a person can approve them. Only the test
    // re-writes them: the generator writes with the default encoder alone, so this view is never production output.
    private static readonly JsonSerializerOptions ReadableOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The production output, written readably: the same properties in the same order.</summary>
    public static string Readable(string productionJson) => JsonNode.Parse(productionJson)!.ToJsonString(ReadableOptions);

    public static IReadOnlyList<RecipeJsonLdCase> Load() =>
        [.. System.IO.Directory.GetFiles(Directory, "*.case.json")
            .Order(StringComparer.Ordinal)
            .Select(path => Read(Path.GetFileName(path)[..^".case.json".Length], File.ReadAllText(path, Encoding.UTF8)))];

    public static RecipeJsonLdCase Read(string name, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var snapshot = RecipeSnapshotJson.Read(RecipeSnapshotJson.CurrentVersion, root.GetProperty("snapshot").GetRawText());
        var page = root.GetProperty("page") is { ValueKind: JsonValueKind.Object } p
            ? new RecipeJsonLdPage(
                p.GetProperty("url").GetString(),
                [.. p.GetProperty("imageUrls").EnumerateArray().Select(i => i.GetString()!)],
                p.GetProperty("authorName").GetString(),
                p.GetProperty("datePublished").GetString() is { } date
                    ? DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : null)
            : RecipeJsonLdPage.None;
        return new RecipeJsonLdCase(name, snapshot, page);
    }

    /// <summary>A result as an approved file writes it: the gaps by need, <c>---</c>, then the JSON-LD, readably.</summary>
    public static string Describe(RecipeJsonLdResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        string Gaps(RecipeJsonLdNeed need) =>
            result.Gaps.Where(g => RecipeJsonLdGaps.NeedOf(g) == need).Select(WireName).DefaultIfEmpty("none")
                .Aggregate((a, b) => a + ", " + b);
        return $"required: {Gaps(RecipeJsonLdNeed.Required)}\nrecommended: {Gaps(RecipeJsonLdNeed.Recommended)}\n---\n{Readable(result.Json)}\n";
    }

    /// <summary>A gap's name on the wire: kebab-case, as every host's enum converter writes it (api-contract.md).</summary>
    public static string WireName(RecipeJsonLdGap gap) => JsonNamingPolicy.KebabCaseLower.ConvertName(gap.ToString());

    public static string Approved(string name, string extension) =>
        File.ReadAllText(Path.Combine(Directory, name + extension), Encoding.UTF8).ReplaceLineEndings("\n");

    private static string ThisDirectory([CallerFilePath] string thisFile = "") => Path.GetDirectoryName(thisFile)!;
}

// PP4.10 (RCP-008): the generator against the golden corpus, Corpus/json-ld/ - real recipes on real pages, each with its JSON-LD
// and its gaps, written and approved by hand. Every mismatch is listed at once.
public sealed class RecipeJsonLdCorpusTests
{
    private static readonly IReadOnlyList<RecipeJsonLdCase> Cases = RecipeJsonLdCorpus.Load();

    // The byte-exact cases: the escaping of every character outside ASCII and every HTML-sensitive one is pinned here.
    public static TheoryData<string> ExactCases { get; } = ["complete", "unparsed-and-hostile"];

    [Fact]
    public void The_corpus_holds_its_cases()
    {
        Assert.True(Cases.Count >= 14, $"Expected the corpus's cases; found {Cases.Count}.");
        var names = Cases.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var approved = Directory.GetFiles(RecipeJsonLdCorpus.Directory, "*.approved.*")
            .Select(p => Path.GetFileName(p).Split(".approved.")[0]);
        Assert.All(approved, name => Assert.Contains(name, names));
        Assert.All(names, name => Assert.True(
            File.Exists(Path.Combine(RecipeJsonLdCorpus.Directory, name + ".approved.txt")), $"{name} has no approved.txt"));
    }

    [Fact]
    public void Every_case_writes_as_approved()
    {
        var mismatches = Cases
            .Select(c => (c.Name, Expected: RecipeJsonLdCorpus.Approved(c.Name, ".approved.txt"),
                Actual: RecipeJsonLdCorpus.Describe(RecipeJsonLd.Write(c.Snapshot, c.Page))))
            .Where(m => m.Expected != m.Actual)
            .Select(m => $"--- {m.Name}: expected\n{m.Expected}--- written\n{m.Actual}")
            .ToList();
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} cases differ:\n" + string.Join('\n', mismatches));
    }

    [Theory]
    [MemberData(nameof(ExactCases))]
    public void The_production_bytes_are_as_approved(string name)
    {
        var @case = Cases.Single(c => c.Name == name);
        Assert.Equal(RecipeJsonLdCorpus.Approved(name, ".approved.jsonld").TrimEnd('\n'), RecipeJsonLd.Write(@case.Snapshot, @case.Page).Json);
    }

    // Production output is ASCII with nothing an HTML parser would act on, so it is inert inside a script element (PP7.10).
    [Fact]
    public void Production_output_is_ascii_and_inert_in_html()
    {
        foreach (var @case in Cases)
        {
            var json = RecipeJsonLd.Write(@case.Snapshot, @case.Page).Json;
            Assert.All(json, c => Assert.True(c is >= ' ' and <= '~', $"{@case.Name}: U+{(int)c:X4} is not printable ASCII"));
            Assert.DoesNotContain('<', json);
            Assert.DoesNotContain('>', json);
            Assert.DoesNotContain('&', json);
        }
    }

    // Nothing reads the culture: a comma decimal point, other digits, or a different case rule would all show here.
    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    [InlineData("de-DE")]
    [InlineData("")]
    public void Every_case_writes_alike_in_every_culture(string culture)
    {
        var invariant = Cases.Select(c => RecipeJsonLd.Write(c.Snapshot, c.Page).Json).ToList();
        var before = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal(invariant, Cases.Select(c => RecipeJsonLd.Write(c.Snapshot, c.Page).Json));
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = before;
        }
    }
}
