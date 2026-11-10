using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using PantryPress.Domain.Modules.Recipes.Business;

namespace PantryPress.Tests.Recipes;

// PP4.6: whole pastes as bloggers bring them - a copied recipe card, plain text with tabs and bullets, numbered steps, steps
// as paragraphs - read by PastedListReader and compared with what a person read and approved, in Corpus/paste. Each paste
// is <name>.ingredients.txt or <name>.steps.txt, and its reading is <name>.<kind>.approved.txt. There is no mode that writes
// an approved file: a change to the reader shows every line it moves, and each is approved by editing the file by hand
// (CLAUDE.md: golden files are never re-approved in bulk). Line endings in a paste file do not matter, since every break
// splits alike and no line keeps its break.
public sealed class PastedListCorpusTests
{
    private static readonly string Directory = Path.Combine(ThisDirectory(), "Corpus", "paste");

    public static TheoryData<string> Pastes() => new(PasteFiles());

    [Theory]
    [MemberData(nameof(Pastes))]
    public void Every_paste_reads_as_approved(string file)
    {
        var paste = File.ReadAllText(Path.Combine(Directory, file), Encoding.UTF8);
        var actual = file.EndsWith(".ingredients.txt", StringComparison.Ordinal)
            ? Describe(PastedListReader.ReadIngredients(paste), l => $"\"{IngredientCorpus.Escape(l.OriginalText)}\" => {IngredientCorpus.Describe(l.Parse)}")
            : file.EndsWith(".steps.txt", StringComparison.Ordinal)
                ? Describe(PastedListReader.ReadSteps(paste), Describe)
                : throw new InvalidDataException($"{file} is neither a .ingredients.txt nor a .steps.txt paste.");

        var approvedPath = Path.Combine(Directory, Path.ChangeExtension(file, ".approved.txt"));
        Assert.True(File.Exists(approvedPath), $"{file} has no approved reading. Read this one and approve it by hand:\n{actual}");
        var approved = File.ReadAllText(approvedPath, Encoding.UTF8).ReplaceLineEndings("\n");
        Assert.True(approved == actual, $"{file} reads differently from {Path.GetFileName(approvedPath)}:\n{Diff(approved, actual)}");
    }

    // The reader reads no culture: every paste reads as approved under cultures whose casing and digits differ most.
    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    [InlineData("")]
    public void Every_paste_reads_alike_in_every_culture(string culture)
    {
        var (current, ui) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            foreach (var file in PasteFiles())
            {
                Every_paste_reads_as_approved(file);
            }
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (current, ui);
        }
    }

    [Fact]
    public void The_pastes_cover_both_lists()
    {
        var pastes = PasteFiles();

        Assert.Contains(pastes, p => p.EndsWith(".ingredients.txt", StringComparison.Ordinal));
        Assert.Contains(pastes, p => p.EndsWith(".steps.txt", StringComparison.Ordinal));
    }

    private static List<string> PasteFiles() =>
        [.. System.IO.Directory.GetFiles(Directory, "*.txt")
            .Select(path => Path.GetFileName(path))
            .Where(name => !name.EndsWith(".approved.txt", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];

    // One group per line, "group -" or group "heading", then each of its lines indented.
    private static string Describe<TLine>(IReadOnlyList<PasteGroup<TLine>> groups, Func<TLine, string> describe)
    {
        var builder = new StringBuilder();
        foreach (var group in groups)
        {
            builder.Append(group.Heading is null ? "group -" : $"group \"{IngredientCorpus.Escape(group.Heading)}\"").Append('\n');
            foreach (var line in group.Lines)
            {
                builder.Append("  ").Append(describe(line)).Append('\n');
            }
        }

        return builder.ToString();
    }

    private static string Describe(PasteStepLine step)
    {
        var problem = step.Problem is { } p ? " ! " + Kebab(p.ToString()) : string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"\"{IngredientCorpus.Escape(step.OriginalText)}\" => \"{IngredientCorpus.Escape(step.Text)}\" @{step.TextSpan.Start}+{step.TextSpan.Length}{problem}");
    }

    private static string Kebab(string name) =>
        string.Concat(name.Select((c, i) => char.IsUpper(c) && i > 0 ? "-" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    private static string Diff(string approved, string actual)
    {
        var expectedLines = approved.Split('\n');
        var actualLines = actual.Split('\n');
        var lines = new List<string>();
        for (var i = 0; i < Math.Max(expectedLines.Length, actualLines.Length); i++)
        {
            var expected = i < expectedLines.Length ? expectedLines[i] : "(none)";
            var got = i < actualLines.Length ? actualLines[i] : "(none)";
            if (expected != got)
            {
                lines.Add($"line {i + 1}\n  approved {expected}\n  actual   {got}");
            }
        }

        return string.Join('\n', lines);
    }

    private static string ThisDirectory([CallerFilePath] string thisFile = "") => Path.GetDirectoryName(thisFile)!;
}
