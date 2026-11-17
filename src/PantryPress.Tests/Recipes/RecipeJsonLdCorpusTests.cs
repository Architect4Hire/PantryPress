using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.10: the golden JSON-LD. Each case in RecipeJsonLdCases is compared with Corpus/json-ld/<case>.approved.txt, which
// holds the missing-fields report and then the JSON-LD, indented to be read. There is no mode that writes an approved file:
// a change to the generator shows every case it moves, and each is approved by editing the file by hand (CLAUDE.md: golden
// files are never re-approved in bulk). The compact bytes page data carries are the approved JSON with its layout removed.
public sealed class RecipeJsonLdCorpusTests
{
    private static readonly string Directory = Path.Combine(ThisDirectory(), "Corpus", "json-ld");

    public static TheoryData<string> Cases() => new(RecipeJsonLdCases.All.Keys.Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_case_writes_as_approved(string name)
    {
        var (snapshot, context) = RecipeJsonLdCases.All[name];
        var actual = Describe(RecipeJsonLd.Write(snapshot, context, indented: true));

        var approvedPath = Path.Combine(Directory, name + ".approved.txt");
        Assert.True(File.Exists(approvedPath), $"{name} has no approved JSON-LD. Read this one and approve it by hand:\n{actual}");
        var approved = File.ReadAllText(approvedPath, Encoding.UTF8).ReplaceLineEndings("\n");
        Assert.True(approved == actual, $"{name} writes differently from {Path.GetFileName(approvedPath)}:\n{actual}");
    }

    // What page data carries is exactly the approved JSON-LD without its layout, and reports the same issues.
    [Theory]
    [MemberData(nameof(Cases))]
    public void The_compact_form_is_the_approved_form_without_layout(string name)
    {
        var (snapshot, context) = RecipeJsonLdCases.All[name];
        var compact = RecipeJsonLd.Write(snapshot, context);
        var indented = RecipeJsonLd.Write(snapshot, context, indented: true);

        Assert.Equal(WithoutLayout(indented.Json), compact.Json);
        Assert.Equal(indented.Issues, compact.Issues);
    }

    // The generator reads no culture: every case writes as approved under cultures whose casing and digits differ most.
    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    [InlineData("de-DE")]
    [InlineData("hi-IN")]
    public void Every_case_writes_as_approved_in_any_culture(string culture)
    {
        var (current, currentUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            foreach (var name in RecipeJsonLdCases.All.Keys)
            {
                Every_case_writes_as_approved(name);
            }
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (current, currentUi);
        }
    }

    [Fact]
    public void Every_approved_file_has_its_case()
    {
        var files = System.IO.Directory.GetFiles(Directory).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(RecipeJsonLdCases.All.Keys.Select(name => name + ".approved.txt").Order(StringComparer.Ordinal), files);
    }

    [Fact]
    public void Every_issue_is_shown_by_some_case()
    {
        var shown = RecipeJsonLdCases.All.Values.SelectMany(c => RecipeJsonLd.Write(c.Snapshot, c.Context).Issues).ToHashSet();
        Assert.Equal(Enum.GetValues<RecipeJsonLdIssue>(), shown.Order());
    }

    // The report, one issue a line as "<severity> <code>", then the JSON-LD.
    internal static string Describe(RecipeJsonLdDocument document)
    {
        var text = new StringBuilder("# issues\n");
        foreach (var issue in document.Issues)
        {
            text.Append(Kebab(RecipeJsonLdIssues.SeverityOf(issue).ToString())).Append(' ').Append(Kebab(issue.ToString())).Append('\n');
        }

        text.Append("# json-ld\n").Append(document.Json.ReplaceLineEndings("\n")).Append('\n');
        return text.ToString();
    }

    internal static string Kebab(string name) =>
        string.Concat(name.Select((c, i) => char.IsUpper(c) ? (i == 0 ? "" : "-") + char.ToLowerInvariant(c) : c.ToString()));

    // White space outside strings removed. Every string is ASCII with its quotes escaped, so a quote always opens or
    // closes one unless a backslash escapes it.
    internal static string WithoutLayout(string json)
    {
        var text = new StringBuilder(json.Length);
        var inString = false;
        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (inString)
            {
                text.Append(c);
                if (c == '\\')
                {
                    text.Append(json[++i]);
                }
                else if (c == '"')
                {
                    inString = false;
                }
            }
            else if (c == '"')
            {
                inString = true;
                text.Append(c);
            }
            else if (!char.IsWhiteSpace(c))
            {
                text.Append(c);
            }
        }

        return text.ToString();
    }

    private static string ThisDirectory([CallerFilePath] string thisFile = "") => Path.GetDirectoryName(thisFile)!;
}
