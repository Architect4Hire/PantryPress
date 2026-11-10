using System.Text;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// One group read from a paste: its heading, cut from its line, or none; that line exactly as pasted and where the heading is
/// in it, so a line misread as a heading can be turned back into a line with the blogger's own words; and its lines in the
/// order pasted.
/// </summary>
public sealed record PasteGroup<TLine>(string? Heading, string? HeadingOriginalText, TextSpan? HeadingSpan, IReadOnlyList<TLine> Lines);

/// <summary>One ingredient line of a paste: the line exactly as pasted, and what the ingredient parser read from it.</summary>
public sealed record PasteIngredientLine(string OriginalText, IngredientLineParse Parse);

/// <summary>
/// One step line of a paste: the line exactly as pasted, where the step's own text is in it, and why it cannot be kept as it
/// is, if it cannot.
/// </summary>
public sealed record PasteStepLine(string OriginalText, TextSpan TextSpan, StepPasteProblem? Problem)
{
    /// <summary>The step's text: the line with a leading number or bullet and the spaces at its edges cut off.</summary>
    public string Text => OriginalText.Substring(TextSpan.Start, TextSpan.Length);
}

/// <summary>
/// Reads a pasted ingredient or step list into groups of lines for review before saving (RCP-003; PP4.6). Pure and
/// deterministic: no model, no culture, no regular expression, the same answer for the same paste everywhere. It never
/// rewrites a line: each one is kept exactly as pasted, and a heading or a step's text is a cut of its line, with only the
/// marks around it left off.
/// <list type="bullet">
/// <item><b>Lines</b>: the paste splits at <c>\r\n</c>, <c>\n</c>, <c>\r</c>, U+0085, U+2028, and U+2029. A line of only
/// whitespace and invisible format characters is blank: it is skipped, and never starts a group, since pasted lists are
/// often double-spaced.</item>
/// <item><b>Headings</b> (<see cref="ReadHeading"/>) start a group; lines before the first heading form a group with none,
/// and a heading followed by another heading is an empty group.</item>
/// <item><b>Ingredients</b>: every other line is read by <see cref="IngredientLineParser"/>, unchanged.</item>
/// <item><b>Steps</b>: every other line is one step (<see cref="ReadStep"/>). A step written across several lines is several
/// steps; the blogger joins them in review.</item>
/// </list>
/// </summary>
public static class PastedListReader
{
    // The longest step number read as a marker: "Step 120." is a number, "2024." is not.
    private const int MaxStepNumberDigits = 3;

    // ATX headings have one to six marks.
    private const int MaxHeadingMarks = 6;

    public static IReadOnlyList<PasteGroup<PasteIngredientLine>> ReadIngredients(string text) =>
        Read(text, ingredientList: true, line => new PasteIngredientLine(line, IngredientLineParser.Parse(line)));

    public static IReadOnlyList<PasteGroup<PasteStepLine>> ReadSteps(string text) =>
        Read(text, ingredientList: false, ReadStep);

    /// <summary>The paste's non-blank lines, each exactly as pasted, in order.</summary>
    public static IReadOnlyList<string> Lines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is not ('\n' or '\r' or '\u0085' or '\u2028' or '\u2029'))
            {
                continue;
            }

            Add(text[start..i]);
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
            }

            start = i + 1;
        }

        Add(text[start..]);
        return lines;

        void Add(string line)
        {
            if (!IsBlank(line))
            {
                lines.Add(line);
            }
        }
    }

    /// <summary>
    /// The group heading a line is, cut from it, or null when it is a line of the list. A heading is at most
    /// <see cref="RecipeLimits.MaxGroupHeadingLength"/> characters once cut, holds a letter, and is one of:
    /// <list type="bullet">
    /// <item>a line ending in a colon that does not start with a number: <c>For the crust:</c> is <c>For the crust</c>;</item>
    /// <item>a Markdown heading, one to six <c>#</c> and a space: <c>## Filling</c> is <c>Filling</c>;</item>
    /// <item>in an ingredient list only, a line starting with the word <c>For</c> that holds no number, comma, colon, or
    /// parenthesis: <c>For serving</c>. A step list leaves it out, because steps start "For best results, …".</item>
    /// </list>
    /// Only spaces, format characters, the <c>#</c> marks, the final colon, and in an ingredient list a leading bullet
    /// (<c>• For the crust:</c>) are left off. In a step list a line with a step marker is a step, whatever it ends with
    /// (<c>Step 2: make the glaze:</c>). A line with a broken or control character is never a heading, so it is flagged as
    /// a line.
    /// </summary>
    public static string? ReadHeading(string line, bool ingredientList) =>
        ReadHeadingSpan(line, ingredientList) is { } span ? line.Substring(span.Start, span.Length) : null;

    /// <summary>Where the heading <see cref="ReadHeading"/> reads is in its line, or null when the line is not one.</summary>
    public static TextSpan? ReadHeadingSpan(string line, bool ingredientList)
    {
        ArgumentNullException.ThrowIfNull(line);
        var tokens = IngredientLineTokenizer.Tokenize(line);
        if (tokens.Any(t => t.Kind == IngredientTokenKind.Invalid) || !TryContent(tokens, out var first, out var last))
        {
            return null;
        }

        // A numbered or bulleted step is a step, whatever it ends with ("Step 2: make the glaze:"). An ingredient heading may
        // carry a bullet ("• For the crust:"), which is not part of it.
        if (!ingredientList && SkipMarker(line, tokens, first) != first)
        {
            return null;
        }

        first = SkipSpaces(tokens, SkipBullet(line, tokens, first), last);
        if (first > last)
        {
            return null;
        }

        // A Markdown heading: the marks, then a space.
        var marks = first;
        while (marks <= last && IsChar(line, tokens[marks], '#'))
        {
            marks++;
        }

        if (marks > first)
        {
            return marks - first <= MaxHeadingMarks && marks <= last && tokens[marks].Kind == IngredientTokenKind.Space
                ? Cut(line, tokens, marks + 1, last)
                : null;
        }

        if (IsChar(line, tokens[last], ':'))
        {
            return IsNumber(tokens[first].Kind) ? null : Cut(line, tokens, first, last);
        }

        var forHeading = ingredientList
            && first + 1 <= last
            && tokens[first].Kind == IngredientTokenKind.Word
            && Ascii.EqualsIgnoreCase(Text(line, tokens[first]), "for")
            && tokens[first + 1].Kind == IngredientTokenKind.Space
            && !tokens.Any(t => IsNumber(t.Kind)
                || t.Kind is IngredientTokenKind.Comma or IngredientTokenKind.Open or IngredientTokenKind.Close
                || IsChar(line, t, ':'));
        return forHeading ? Cut(line, tokens, first, last) : null;
    }

    /// <summary>
    /// Reads one line as a step. A leading marker is cut off, with the spaces after it:
    /// <list type="bullet">
    /// <item>a bullet the ingredient parser skips: one of <c>• · ▪ ◦ ‣ ▢ ☐ □</c>, or a dash or <c>*</c> before a space;</item>
    /// <item>a number of up to three digits followed by <c>.</c>, <c>)</c>, or <c>:</c> and then a space or the line's end
    /// (<c>1.</c>, <c>2)</c>); <c>1.5 cups</c> and <c>1:30</c> are not markers;</item>
    /// <item><c>Step</c> in any case, a space, and such a number, optionally followed - straight away or after one space -
    /// by <c>.</c>, <c>:</c>, <c>)</c>, or a dash, then a space or the line's end (<c>Step 3:</c>, <c>STEP 3 - </c>).</item>
    /// </list>
    /// The step is flagged <see cref="StepPasteProblem.IllFormedText"/> for a broken or control character,
    /// <see cref="StepPasteProblem.NoText"/> when nothing is left after the marker, and
    /// <see cref="StepPasteProblem.TooLong"/> past <see cref="RecipeLimits.MaxStepTextLength"/>.
    /// </summary>
    public static PasteStepLine ReadStep(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var tokens = IngredientLineTokenizer.Tokenize(line);
        if (!TryContent(tokens, out var first, out var last))
        {
            return new PasteStepLine(line, new TextSpan(line.Length, 0), StepPasteProblem.NoText);
        }

        var start = SkipSpaces(tokens, SkipMarker(line, tokens, first), last);
        var span = start > last
            ? new TextSpan(tokens[last].End, 0)
            : new TextSpan(tokens[start].Start, tokens[last].End - tokens[start].Start);
        StepPasteProblem? problem =
            tokens.Any(t => t.Kind == IngredientTokenKind.Invalid) ? StepPasteProblem.IllFormedText
            : span.Length == 0 ? StepPasteProblem.NoText
            : span.Length > RecipeLimits.MaxStepTextLength ? StepPasteProblem.TooLong
            : null;
        return new PasteStepLine(line, span, problem);
    }

    private static List<PasteGroup<TLine>> Read<TLine>(string text, bool ingredientList, Func<string, TLine> readLine)
    {
        var groups = new List<PasteGroup<TLine>>();
        string? headingLine = null;
        TextSpan? headingSpan = null;
        var lines = new List<TLine>();
        var open = false;
        foreach (var line in Lines(text))
        {
            if (ReadHeadingSpan(line, ingredientList) is { } next)
            {
                if (open)
                {
                    groups.Add(Group(headingLine, headingSpan, lines));
                }

                (headingLine, headingSpan, lines, open) = (line, next, [], true);
                continue;
            }

            lines.Add(readLine(line));
            open = true;
        }

        if (open)
        {
            groups.Add(Group(headingLine, headingSpan, lines));
        }

        return groups;

        static PasteGroup<TLine> Group(string? line, TextSpan? span, List<TLine> lines) =>
            new(span is { } s ? line!.Substring(s.Start, s.Length) : null, line, span, lines);
    }

    // The index just past a leading step marker at first, or first when there is none.
    private static int SkipMarker(string line, IReadOnlyList<IngredientToken> tokens, int first)
    {
        var token = tokens[first];
        if (SkipBullet(line, tokens, first) != first)
        {
            return first + 1;
        }

        if (IsStepNumber(tokens, first)
            && first + 1 < tokens.Count
            && (tokens[first + 1].Kind is IngredientTokenKind.Point or IngredientTokenKind.Close || IsChar(line, tokens[first + 1], ':'))
            && EndsMarker(tokens, first + 2))
        {
            return first + 2;
        }

        if (token.Kind == IngredientTokenKind.Word
            && Ascii.EqualsIgnoreCase(Text(line, token), "step")
            && first + 2 < tokens.Count
            && tokens[first + 1].Kind == IngredientTokenKind.Space
            && IsStepNumber(tokens, first + 2))
        {
            // The number's punctuation follows it straight away ("Step 3:") or after one space ("Step 3 - ").
            var after = first + 3;
            if (IsStepPunctuation(line, tokens, after))
            {
                after++;
            }
            else if (after < tokens.Count && tokens[after].Kind == IngredientTokenKind.Space
                && IsStepPunctuation(line, tokens, after + 1) && EndsMarker(tokens, after + 2))
            {
                after += 2;
            }

            if (EndsMarker(tokens, after))
            {
                return after;
            }
        }

        return first;
    }

    // The index just past a leading bullet at first, as the ingredient parser skips one, or first when there is none.
    private static int SkipBullet(string line, IReadOnlyList<IngredientToken> tokens, int first)
    {
        var token = tokens[first];
        var boxed = token.Kind == IngredientTokenKind.Symbol && line[token.Start] is '•' or '·' or '▪' or '◦' or '‣' or '▢' or '☐' or '□';
        var spacedMark = (token.Kind == IngredientTokenKind.Dash || IsChar(line, token, '*')) && EndsMarker(tokens, first + 1);
        return boxed || spacedMark ? first + 1 : first;
    }

    private static bool IsStepPunctuation(string line, IReadOnlyList<IngredientToken> tokens, int index) =>
        index < tokens.Count
        && (tokens[index].Kind is IngredientTokenKind.Point or IngredientTokenKind.Close or IngredientTokenKind.Dash
            || IsChar(line, tokens[index], ':'));

    private static bool IsStepNumber(IReadOnlyList<IngredientToken> tokens, int index) =>
        index < tokens.Count && tokens[index] is { Kind: IngredientTokenKind.Digits, Length: <= MaxStepNumberDigits };

    // A marker ends at the line's end or before a space.
    private static bool EndsMarker(IReadOnlyList<IngredientToken> tokens, int index) =>
        index >= tokens.Count || tokens[index].Kind == IngredientTokenKind.Space;

    private static bool IsNumber(IngredientTokenKind kind) =>
        kind is IngredientTokenKind.Digits or IngredientTokenKind.FractionGlyph or IngredientTokenKind.OtherNumber;

    // The heading from first to last, with spaces at its edges and a final colon left off; null unless it holds a letter
    // and fits a group heading.
    private static TextSpan? Cut(string line, IReadOnlyList<IngredientToken> tokens, int first, int last)
    {
        if (last >= first && IsChar(line, tokens[last], ':'))
        {
            last--;
        }

        first = SkipSpaces(tokens, first, last);
        while (last >= first && tokens[last].Kind == IngredientTokenKind.Space)
        {
            last--;
        }

        if (last < first || !tokens.Skip(first).Take(last - first + 1).Any(t => t.Kind == IngredientTokenKind.Word))
        {
            return null;
        }

        var span = new TextSpan(tokens[first].Start, tokens[last].End - tokens[first].Start);
        return span.Length <= RecipeLimits.MaxGroupHeadingLength ? span : null;
    }

    private static int SkipSpaces(IReadOnlyList<IngredientToken> tokens, int index, int last)
    {
        while (index <= last && tokens[index].Kind == IngredientTokenKind.Space)
        {
            index++;
        }

        return index;
    }

    // The first and last tokens that are not spaces; false for a blank line.
    private static bool TryContent(IReadOnlyList<IngredientToken> tokens, out int first, out int last)
    {
        first = 0;
        last = tokens.Count - 1;
        while (first <= last && tokens[first].Kind == IngredientTokenKind.Space)
        {
            first++;
        }

        while (last >= first && tokens[last].Kind == IngredientTokenKind.Space)
        {
            last--;
        }

        return first <= last;
    }

    private static bool IsBlank(string line) =>
        IngredientLineTokenizer.Tokenize(line).All(t => t.Kind == IngredientTokenKind.Space);

    private static bool IsChar(string line, IngredientToken token, char c) => token.Length == 1 && line[token.Start] == c;

    private static string Text(string line, IngredientToken token) => line.Substring(token.Start, token.Length);
}
