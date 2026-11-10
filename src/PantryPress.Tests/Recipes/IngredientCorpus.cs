using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

/// <summary>One case of the ingredient-line corpus: the line as written and what the parser is expected to read.</summary>
internal sealed record IngredientCorpusCase(int LineNumber, string Input, string Expected);

/// <summary>
/// The ingredient-line corpus (PP4.5), <c>Corpus/ingredient-lines.txt</c>, read from the source tree. Each case is a
/// <c>&gt;</c> line - the ingredient exactly as written, where <c>\uXXXX</c> and <c>\\</c> are the only escapes, for
/// characters an editor would hide or trim - followed by a <c>=</c> line, the parse as <see cref="Describe"/> writes it.
/// <c>#</c> lines and blank lines are comments. There is no mode that rewrites the file: each expectation is approved by
/// reading it, and a change to one is made by hand (CLAUDE.md: golden files are never re-approved in bulk).
/// </summary>
internal static class IngredientCorpus
{
    public static string Path { get; } = System.IO.Path.Combine(ThisDirectory(), "Corpus", "ingredient-lines.txt");

    public static IReadOnlyList<IngredientCorpusCase> Load()
    {
        var lines = File.ReadAllLines(Path, Encoding.UTF8);
        var cases = new List<IngredientCorpusCase>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (!line.StartsWith('>'))
            {
                throw new InvalidDataException($"Line {i + 1}: a case starts with '>'.");
            }

            var input = line == ">" ? string.Empty : line.StartsWith("> ", StringComparison.Ordinal)
                ? Unescape(line[2..])
                : throw new InvalidDataException($"Line {i + 1}: '>' is followed by one space, then the ingredient.");
            if (i + 1 >= lines.Length || !lines[i + 1].StartsWith("= ", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Line {i + 2}: a case's second line starts with '= '.");
            }

            cases.Add(new IngredientCorpusCase(i + 1, input, lines[i + 1][2..]));
            i++;
        }

        return cases;
    }

    /// <summary>
    /// A parse as one line: <c>unparsed &lt;problem&gt;</c>, or <c>parsed | &lt;quantity&gt; | &lt;unit&gt; | item "…" |
    /// note "…"</c>, where the quantity is <c>-</c>, <c>n/d @start+length</c>, <c>low..high @start+length</c>, or
    /// <c>to-taste @start+length</c>, the unit is <c>-</c> or <c>code @start+length</c>, and an absent note is <c>note -</c>.
    /// </summary>
    public static string Describe(IngredientLineParse parse)
    {
        ArgumentNullException.ThrowIfNull(parse);
        if (parse.Status == IngredientParseStatus.Unparsed)
        {
            return "unparsed " + Kebab(parse.Problem!.Value.ToString());
        }

        var quantity = parse.Quantity switch
        {
            null => "-",
            { Kind: QuantityKind.ToTaste } => "to-taste" + At(parse.QuantitySpan),
            { Kind: QuantityKind.Range } q => $"{q.Low}..{q.High}" + At(parse.QuantitySpan),
            { } q => q.Low!.Value + At(parse.QuantitySpan),
        };
        var unit = parse.UnitCode is null ? "-" : parse.UnitCode + At(parse.UnitSpan);
        var note = parse.Note is null ? "note -" : $"note \"{Escape(parse.Note)}\"";
        return $"parsed | {quantity} | {unit} | item \"{Escape(parse.Item!)}\" | {note}";
    }

    /// <summary>
    /// Text with <c>\</c>, <c>"</c>, and anything unprinted or invisible - whitespace other than a space, a control or format
    /// character, a combining mark, half a surrogate pair - written as an escape. A whole pair (an emoji) is written as is.
    /// </summary>
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append(c).Append(text[++i]);
            }
            else if (c is '\\' or '"')
            {
                builder.Append('\\').Append(c);
            }
            else if (c != ' ' && (char.IsWhiteSpace(c) || char.IsControl(c) || char.IsSurrogate(c)
                || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.NonSpacingMark))
            {
                builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}");
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    public static string Unescape(string text)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\')
            {
                builder.Append(text[i]);
            }
            else if (i + 1 < text.Length && text[i + 1] == '\\')
            {
                builder.Append('\\');
                i++;
            }
            else if (i + 5 < text.Length && text[i + 1] == 'u')
            {
                builder.Append((char)int.Parse(text.AsSpan(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                i += 5;
            }
            else
            {
                throw new InvalidDataException($"An unknown escape in \"{text}\".");
            }
        }

        return builder.ToString();
    }

    private static string At(TextSpan? span) =>
        span is { } s ? string.Create(CultureInfo.InvariantCulture, $" @{s.Start}+{s.Length}") : " @?";

    private static string Kebab(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && builder.Length > 0)
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    private static string ThisDirectory([CallerFilePath] string thisFile = "") =>
        System.IO.Path.GetDirectoryName(thisFile)!;
}
