using System.Globalization;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// Reads an ingredient line the way the blogger wrote it into its quantity, unit, item, and note (RCP-002; PP-D05; PP4.5).
/// It is deterministic - no model, no culture, no clock, the same answer for the same line on every machine - and it never
/// rewrites the line: the quantity and unit are spans of it, and the item and the note's pieces are cut from it verbatim,
/// with only the spaces and separating punctuation at their edges left off. A line it cannot read with confidence is
/// flagged <see cref="IngredientParseStatus.Unparsed"/> with a reason rather than guessed at; it is still kept and shown
/// exactly as written, and is never scaled or converted. The line reads, left to right:
/// <code>[bullet] [quantity] [size] [unit] ["of"] item [, note | (note)]...</code>
/// <list type="bullet">
/// <item><b>Bullet</b>: one of <c>• · ▪ ◦ ‣ ▢ ☐ □</c>, or <c>-</c>, a dash, or <c>*</c> before a space, is skipped and
/// part of no span.</item>
/// <item><b>Quantity</b>: an exact <see cref="Rational"/>, never floating point. Whole numbers, decimals (<c>0.5</c>,
/// <c>.5</c>), fractions (<c>1/2</c>, with a slash or the fraction slash), mixed numbers (<c>1 1/2</c>, <c>1-1/2</c>,
/// <c>1½</c>, <c>1 ½</c>), every vulgar-fraction character, and the words <c>one</c> to <c>twelve</c>. A range is two of
/// those joined by a dash, or by <c>to</c> between spaces (<c>2-3</c>, <c>1/2–1</c>, <c>1 to 1½</c>). A hyphen between a
/// whole number and a fraction below one (<c>1-1/2</c>, <c>1-½</c>) makes a mixed number, not a range. <c>a</c> or <c>an</c> is one, but only when a
/// unit follows (<c>a pinch</c>, <c>an 8-ounce package</c>).</item>
/// <item><b>Size</b>: a parenthesis or a hyphenated size (<c>14-ounce</c>, <c>9-inch</c>) straight after the quantity goes
/// to the note (<c>1 (14 oz) can</c>).</item>
/// <item><b>Unit</b>: a spelling from the vocabulary (<see cref="UnitSpellings"/>), straight after the quantity or the size,
/// with or without a space (<c>250g</c>). Only a line with an amount or a range has one; <c>of</c> after it is skipped.</item>
/// <item><b>To taste</b>: on a line with no quantity, the words <c>to taste</c> outside parentheses, or as a parenthesis'
/// whole text, are its quantity; they leave the item and the note.</item>
/// <item><b>Item and note</b>: the item is the first text that remains, up to the first comma or parenthesis outside a
/// parenthesis, and it starts before that comma (<c>1 cup, sifted</c> names nothing); every other piece - the size, each
/// parenthesis' text, what follows that comma - is the note, in the order written, joined by
/// <see cref="IngredientLineParse.NoteSeparator"/>. Spaces, commas, semicolons, colons, and periods at a piece's edges are
/// left off; a period that starts a decimal (<c>.25 oz</c>) stays.</item>
/// </list>
/// It does not guess at two measures together (<c>1 cup + 2 tbsp</c>, <c>1 cup to 1½ cups</c>, <c>2 x 400g</c>,
/// <c>2 3 cups</c>), a comma inside a number (<c>1,5</c>), or a number joined to a word that is not a unit; each is
/// <see cref="IngredientParseProblem.AmbiguousQuantity"/>. A number later in the item (<c>Juice of 2 lemons</c>) is part
/// of the item; whether such a line can be scaled is the scaling service's question (PP4.7).
/// </summary>
public static class IngredientLineParser
{
    // A whole number or the digits after a decimal point, at most this many: past it no term fits RecipeLimits.
    private const int MaxDigits = 7;
    private const int MaxDecimalPlaces = 6;

    private static readonly string[] NumberWords =
        ["one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve"];

    private static readonly string[] ConnectorWords = ["to", "or", "and", "plus", "x"];

    /// <summary>The words read as an amount, lower-case: <c>one</c> to <c>twelve</c> (the scaler looks for them, PP4.7).</summary>
    internal static IReadOnlyList<string> AmountWords { get; } = Array.AsReadOnly(NumberWords);

    public static IngredientLineParse Parse(string originalText)
    {
        ArgumentNullException.ThrowIfNull(originalText);
        return originalText.Length > RecipeLimits.MaxIngredientTextLength
            ? IngredientLineParse.Unparsed(IngredientParseProblem.TooLong)
            : ParseAnyLength(originalText);
    }

    /// <summary>
    /// <see cref="Parse"/> without the stored length cap, for the scaler's read-back (PP4.7): a stored line can grow past
    /// the cap once a scaled amount or a plural is written into it, and what the reader sees must still be read.
    /// </summary>
    internal static IngredientLineParse ParseAnyLength(string originalText)
    {
        ArgumentNullException.ThrowIfNull(originalText);
        var tokens = IngredientLineTokenizer.Tokenize(originalText);
        if (tokens.Any(t => t.Kind == IngredientTokenKind.Invalid))
        {
            return IngredientLineParse.Unparsed(IngredientParseProblem.IllFormedText);
        }

        var line = new Line(originalText, tokens);
        if (line.Count == 0)
        {
            return IngredientLineParse.Unparsed(IngredientParseProblem.Empty);
        }

        return line.ParenthesesBalance()
            ? line.Parse()
            : IngredientLineParse.Unparsed(IngredientParseProblem.UnbalancedParentheses);
    }

    /// <summary>An amount read from the line, nothing, or why it cannot be read.</summary>
    private readonly record struct AmountRead(Rational? Value, int End, IngredientParseProblem? Problem)
    {
        public static AmountRead None { get; } = new(null, 0, null);

        public static AmountRead Fail(IngredientParseProblem problem) => new(null, 0, problem);
    }

    /// <summary>The line's tokens without its whitespace, each marked with whether whitespace came before it.</summary>
    private sealed class Line
    {
        private readonly string _text;
        private readonly IngredientToken[] _tokens;
        private readonly bool[] _spaced;
        private readonly HashSet<int> _boundaries;

        public Line(string text, IReadOnlyList<IngredientToken> all)
        {
            _text = text;
            var tokens = new List<IngredientToken>();
            var spaced = new List<bool>();
            var space = false;
            foreach (var token in all)
            {
                if (token.Kind == IngredientTokenKind.Space)
                {
                    space = true;
                    continue;
                }

                tokens.Add(token);
                spaced.Add(space);
                space = false;
            }

            _tokens = [.. tokens];
            _spaced = [.. spaced];
            _boundaries = [.. all.Select(t => t.Start), text.Length];
        }

        public int Count => _tokens.Length;

        public bool ParenthesesBalance()
        {
            var depth = 0;
            for (var i = 0; i < Count; i++)
            {
                depth += _tokens[i].Kind switch
                {
                    IngredientTokenKind.Open => 1,
                    IngredientTokenKind.Close => -1,
                    _ => 0,
                };
                if (depth < 0)
                {
                    return false;
                }
            }

            return depth == 0;
        }

        public IngredientLineParse Parse()
        {
            var p = SkipBullet();
            Quantity? quantity = null;
            TextSpan? quantitySpan = null;
            QuantityStyle? style = null;
            UnitMatch? unit = null;
            TextSpan? unitSpan = null;
            int? cut = null;
            var pieces = new List<(int Start, int End)>();

            var amount = ReadAmount(p);
            if (amount.Problem is { } amountProblem)
            {
                return IngredientLineParse.Unparsed(amountProblem);
            }

            if (amount.Value is { } low)
            {
                var (read, end, problem) = ReadRange(low, amount.End);
                if (problem is { } rangeProblem)
                {
                    return IngredientLineParse.Unparsed(rangeProblem);
                }

                quantity = read;
                quantitySpan = Span(p, end);
                style = WrittenWithPoint(p, end) ? QuantityStyle.Decimal : QuantityStyle.Fraction;
                p = end;
                if (p < Count && !_spaced[p] && UnitAt(p) is null)
                {
                    return IngredientLineParse.Unparsed(IngredientParseProblem.AmbiguousQuantity);
                }
            }
            else if (IsArticleBeforeUnit(p))
            {
                quantity = Quantity.Amount(Rational.One);
                quantitySpan = Span(p, p + 1);
                style = QuantityStyle.Fraction;
                p++;
            }

            if (quantity is not null)
            {
                p = TakeSizes(p, pieces);
                unit = UnitAt(p);
                if (unit is { } found)
                {
                    unitSpan = new TextSpan(_tokens[p].Start, found.Length);
                    p = FirstTokenFrom(unitSpan.Value.End);
                }

                if (IsWord(p, "of") && _spaced[p])
                {
                    p++;
                }

                if (StartsAnotherMeasure(p))
                {
                    return IngredientLineParse.Unparsed(IngredientParseProblem.AmbiguousQuantity);
                }
            }
            else if (FindToTaste(p) is { } at)
            {
                quantity = Quantity.ToTaste;
                quantitySpan = Span(at, at + 2);
                cut = at;
            }

            if (CollectFragments(p, cut, pieces) is not { } item)
            {
                return IngredientLineParse.Unparsed(IngredientParseProblem.NoItem);
            }

            var itemText = _text[item.Start..item.End];
            if (itemText.Length > RecipeLimits.MaxIngredientItemLength)
            {
                return IngredientLineParse.Unparsed(IngredientParseProblem.ItemTooLong);
            }

            var note = pieces.Count == 0 ? null : string.Join(IngredientLineParse.NoteSeparator, pieces.Select(n => _text[n.Start..n.End]));
            if (note?.Length > RecipeLimits.MaxIngredientNoteLength)
            {
                return IngredientLineParse.Unparsed(IngredientParseProblem.NoteTooLong);
            }

            return IngredientLineParse.Parsed(quantity, quantitySpan, style, unit?.Code, unitSpan, itemText, note);
        }

        private int SkipBullet()
        {
            var first = _text[_tokens[0].Start];
            var boxed = _tokens[0].Kind == IngredientTokenKind.Symbol && first is '•' or '·' or '▪' or '◦' or '‣' or '▢' or '☐' or '□';
            var spacedMark = (_tokens[0].Kind == IngredientTokenKind.Dash || first == '*') && (Count == 1 || _spaced[1]);
            return boxed || spacedMark ? 1 : 0;
        }

        private (Quantity? Quantity, int End, IngredientParseProblem? Problem) ReadRange(Rational low, int end)
        {
            var dash = Is(end, IngredientTokenKind.Dash);
            var to = IsWord(end, "to") && _spaced[end] && Spaced(end + 1);
            if (!dash && !to)
            {
                return (Quantity.Amount(low), end, null);
            }

            var high = ReadAmount(end + 1);
            if (high.Problem is { } problem)
            {
                return (null, 0, problem);
            }

            if (high.Value is not { } value)
            {
                // "2 to" with no second amount is an amount followed by words; a dash with none is "2-inch" or "2 - x".
                return dash ? (null, 0, IngredientParseProblem.AmbiguousQuantity) : (Quantity.Amount(low), end, null);
            }

            return value <= low
                ? (null, 0, IngredientParseProblem.ReversedRange)
                : (Quantity.Range(low, value), high.End, null);
        }

        private AmountRead ReadAmount(int i)
        {
            if (i >= Count)
            {
                return AmountRead.None;
            }

            switch (_tokens[i].Kind)
            {
                case IngredientTokenKind.OtherNumber:
                    return AmountRead.Fail(IngredientParseProblem.UnreadableQuantity);
                case IngredientTokenKind.Word:
                    var word = Array.FindIndex(NumberWords, w => IsWord(i, w));
                    return word < 0 ? AmountRead.None : new AmountRead(word + 1, i + 1, null);
                case IngredientTokenKind.FractionGlyph:
                    return Checked(Glyph(i), i + 1);
                case IngredientTokenKind.Point when Glued(i + 1) && Is(i + 1, IngredientTokenKind.Digits):
                    return ReadDecimal(0, i + 1);
                case IngredientTokenKind.Digits:
                    break;
                default:
                    return AmountRead.None;
            }

            if (_tokens[i].Length > MaxDigits)
            {
                return AmountRead.Fail(IngredientParseProblem.QuantityOutOfRange);
            }

            var whole = Number(i);
            var k = i + 1;
            if (Glued(k))
            {
                switch (_tokens[k].Kind)
                {
                    case IngredientTokenKind.Point:
                        return Glued(k + 1) && Is(k + 1, IngredientTokenKind.Digits)
                            ? ReadDecimal(whole, k + 1)
                            : AmountRead.Fail(IngredientParseProblem.UnreadableQuantity);
                    case IngredientTokenKind.Comma when Glued(k + 1) && Is(k + 1, IngredientTokenKind.Digits):
                        return AmountRead.Fail(IngredientParseProblem.AmbiguousQuantity);
                    case IngredientTokenKind.Slash:
                        return ReadFraction(i);
                    case IngredientTokenKind.FractionGlyph:
                        return Checked(whole + Glyph(k), k + 1);
                    case IngredientTokenKind.Dash when IsHyphen(k) && Glued(k + 1) && Is(k + 1, IngredientTokenKind.FractionGlyph):
                        return Checked(whole + Glyph(k + 1), k + 2);
                    case IngredientTokenKind.Dash when IsHyphen(k) && Glued(k + 1) && FractionAt(k + 1) is { TooLarge: true }:
                        return AmountRead.Fail(IngredientParseProblem.QuantityOutOfRange);
                    case IngredientTokenKind.Dash when IsHyphen(k) && Glued(k + 1) && FractionAt(k + 1) is { Proper: true } hyphenated:
                        return Checked(whole + hyphenated.Value, hyphenated.End);
                }
            }
            else if (Spaced(k))
            {
                if (Is(k, IngredientTokenKind.FractionGlyph))
                {
                    return Checked(whole + Glyph(k), k + 1);
                }

                if (FractionAt(k) is { } fraction)
                {
                    return fraction switch
                    {
                        { TooLarge: true } => AmountRead.Fail(IngredientParseProblem.QuantityOutOfRange),
                        { Proper: true } => Checked(whole + fraction.Value, fraction.End),
                        _ => AmountRead.Fail(IngredientParseProblem.UnreadableQuantity),
                    };
                }
            }

            return Checked(whole, k);
        }

        // Digits, a slash, digits, all joined: the denominator above zero and no second slash after.
        private AmountRead ReadFraction(int i)
        {
            if (!(Glued(i + 2) && Is(i + 2, IngredientTokenKind.Digits))
                || (Glued(i + 3) && Is(i + 3, IngredientTokenKind.Slash)))
            {
                return AmountRead.Fail(IngredientParseProblem.UnreadableQuantity);
            }

            if (_tokens[i + 2].Length > MaxDigits)
            {
                return AmountRead.Fail(IngredientParseProblem.QuantityOutOfRange);
            }

            return Number(i + 2) == 0
                ? AmountRead.Fail(IngredientParseProblem.UnreadableQuantity)
                : Checked(Rational.Create(Number(i), Number(i + 2)), i + 3);
        }

        // A plain fraction at i for a mixed number's second part, if one is written there: proper when it is above zero and
        // below one with no second slash, too large when a term has more digits than any stored term.
        private (Rational Value, bool Proper, bool TooLarge, int End)? FractionAt(int i)
        {
            if (!(Is(i, IngredientTokenKind.Digits) && Glued(i + 1) && Is(i + 1, IngredientTokenKind.Slash)
                && Glued(i + 2) && Is(i + 2, IngredientTokenKind.Digits)))
            {
                return null;
            }

            if (_tokens[i].Length > MaxDigits || _tokens[i + 2].Length > MaxDigits)
            {
                return (Rational.Zero, false, true, i + 3);
            }

            var (numerator, denominator) = (Number(i), Number(i + 2));
            var secondSlash = Glued(i + 3) && Is(i + 3, IngredientTokenKind.Slash);
            var proper = denominator > 0 && numerator > 0 && numerator < denominator && !secondSlash;
            return (proper ? Rational.Create(numerator, denominator) : Rational.Zero, proper, false, i + 3);
        }

        private AmountRead ReadDecimal(long whole, int places)
        {
            var digits = _tokens[places].Length;
            if (digits > MaxDecimalPlaces)
            {
                return AmountRead.Fail(IngredientParseProblem.QuantityOutOfRange);
            }

            var end = places + 1;
            if (Glued(end) && (Is(end, IngredientTokenKind.Point) || Is(end, IngredientTokenKind.Comma) || Is(end, IngredientTokenKind.Slash))
                && Glued(end + 1) && Is(end + 1, IngredientTokenKind.Digits))
            {
                return AmountRead.Fail(IngredientParseProblem.UnreadableQuantity);
            }

            var scale = 1L;
            for (var d = 0; d < digits; d++)
            {
                scale *= 10;
            }

            return Checked(Rational.Create((whole * scale) + Number(places), scale), end);
        }

        private static AmountRead Checked(Rational value, int end)
        {
            if (value.Sign <= 0)
            {
                return AmountRead.Fail(IngredientParseProblem.UnreadableQuantity);
            }

            return value.Numerator > RecipeLimits.MaxQuantityTerm || value.Denominator > RecipeLimits.MaxQuantityTerm
                ? AmountRead.Fail(IngredientParseProblem.QuantityOutOfRange)
                : new AmountRead(value, end, null);
        }

        private bool IsArticleBeforeUnit(int p)
        {
            if (!(IsWord(p, "a") || IsWord(p, "an")) || !Spaced(p + 1))
            {
                return false;
            }

            var k = TakeSizes(p + 1, []);
            return Spaced(k) && UnitAt(k) is not null;
        }

        // Parentheses and hyphenated sizes straight after the quantity, each added to the note as written.
        private int TakeSizes(int p, List<(int Start, int End)> pieces)
        {
            while (p < Count)
            {
                if (Is(p, IngredientTokenKind.Open))
                {
                    var close = MatchingClose(p);
                    AddPiece(pieces, _tokens[p].End, _tokens[close].Start);
                    p = close + 1;
                }
                else if (SizeWordEnd(p) is { } end)
                {
                    AddPiece(pieces, _tokens[p].Start, _tokens[end - 1].End);
                    p = end;
                }
                else
                {
                    break;
                }
            }

            return p;
        }

        // "14-ounce", "8.5-oz", "1/2-inch", "½-inch": a number joined by a hyphen to a word.
        private int? SizeWordEnd(int i)
        {
            int k;
            if (Is(i, IngredientTokenKind.FractionGlyph))
            {
                k = i + 1;
            }
            else if (Is(i, IngredientTokenKind.Digits))
            {
                k = i + 1;
                if (Glued(k) && (Is(k, IngredientTokenKind.Point) || Is(k, IngredientTokenKind.Slash))
                    && Glued(k + 1) && Is(k + 1, IngredientTokenKind.Digits))
                {
                    k += 2;
                }
            }
            else
            {
                return null;
            }

            return Glued(k) && IsHyphen(k) && Glued(k + 1) && Is(k + 1, IngredientTokenKind.Word) ? k + 2 : null;
        }

        // After the quantity and unit: a second number, or a join to one - a dash, a slash after a space ("1 / 2"), a word,
        // or a sign - means two measures or a split fraction the parser will not guess at. A percentage ("2% milk") is part
        // of the item.
        private bool StartsAnotherMeasure(int p)
        {
            if (Is(p, IngredientTokenKind.Digits))
            {
                return !(Glued(p + 1) && Is(p + 1, IngredientTokenKind.Symbol) && _text[_tokens[p + 1].Start] == '%');
            }

            if (Is(p, IngredientTokenKind.FractionGlyph))
            {
                return true;
            }

            var connector = Is(p, IngredientTokenKind.Dash) || Is(p, IngredientTokenKind.Slash)
                || ConnectorWords.Any(w => IsWord(p, w))
                || (Is(p, IngredientTokenKind.Symbol) && _text[_tokens[p].Start] is '+' or '×' or '&');
            return connector && StartsNumber(p + 1);
        }

        private bool StartsNumber(int i) =>
            Is(i, IngredientTokenKind.Digits) || Is(i, IngredientTokenKind.FractionGlyph)
            || (Is(i, IngredientTokenKind.Point) && Glued(i + 1) && Is(i + 1, IngredientTokenKind.Digits));

        // "to taste" outside parentheses, or as a parenthesis' whole text; the index of its "to".
        private int? FindToTaste(int p)
        {
            for (var i = p; i < Count; i++)
            {
                if (Is(i, IngredientTokenKind.Open))
                {
                    if (IsToTaste(i + 1) && Is(i + 3, IngredientTokenKind.Close))
                    {
                        return i + 1;
                    }

                    i = MatchingClose(i);
                }
                else if (IsToTaste(i))
                {
                    return i;
                }
            }

            return null;
        }

        private bool IsToTaste(int i) => IsWord(i, "to") && IsWord(i + 1, "taste") && _spaced[i + 1];

        // Splits what follows the quantity and unit into the item and the note's pieces: text runs end at a parenthesis,
        // at the first comma outside one, and at "to taste"; the item is the first run left once its edges are trimmed.
        private (int Start, int End)? CollectFragments(int p, int? cut, List<(int Start, int End)> pieces)
        {
            int? runStart = null;
            var runEnd = 0;
            var commaTaken = false;
            (int Start, int End)? found = null;

            void CloseRun()
            {
                if (runStart is { } start)
                {
                    var (s, e) = Trim(start, runEnd);
                    if (s < e)
                    {
                        // The item comes before the first comma: "1 cup, sifted" names nothing, and "sifted" is no item.
                        if (found is null && !commaTaken)
                        {
                            found = (s, e);
                        }
                        else
                        {
                            pieces.Add((s, e));
                        }
                    }
                }

                runStart = null;
            }

            for (var i = p; i < Count; i++)
            {
                if (i == cut)
                {
                    CloseRun();
                    i++;
                }
                else if (Is(i, IngredientTokenKind.Open))
                {
                    CloseRun();
                    if (cut == i + 1)
                    {
                        i += 3;
                        continue;
                    }

                    var close = MatchingClose(i);
                    AddPiece(pieces, _tokens[i].End, _tokens[close].Start);
                    i = close;
                }
                else if (Is(i, IngredientTokenKind.Comma) && !commaTaken)
                {
                    CloseRun();
                    commaTaken = true;
                }
                else
                {
                    runStart ??= _tokens[i].Start;
                    runEnd = _tokens[i].End;
                }
            }

            CloseRun();
            return found;
        }

        private void AddPiece(List<(int Start, int End)> pieces, int start, int end)
        {
            var (s, e) = Trim(start, end);
            if (s < e)
            {
                pieces.Add((s, e));
            }
        }

        // Leaves off the spaces and separating punctuation at a piece's edges: a closing period ("2 eggs."), and an opening
        // one left by an abbreviation the vocabulary spells without it ("1 T. honey"), unless it is a decimal's ("(.25 oz)").
        private (int Start, int End) Trim(int start, int end)
        {
            while (start < end && IsEdge(_text[start]) && !(_text[start] == '.' && start + 1 < end && char.IsAsciiDigit(_text[start + 1])))
            {
                start++;
            }

            while (end > start && IsEdge(_text[end - 1]))
            {
                end--;
            }

            return (start, end);
        }

        private static bool IsEdge(char c) =>
            char.IsWhiteSpace(c) || IngredientLineTokenizer.IsFormat(c) || c is ',' or ';' or ':' or '.';

        private bool WrittenWithPoint(int first, int end)
        {
            for (var i = first; i < end; i++)
            {
                if (Is(i, IngredientTokenKind.Point))
                {
                    return true;
                }
            }

            return false;
        }

        private int MatchingClose(int open)
        {
            var depth = 0;
            for (var i = open; i < Count; i++)
            {
                depth += _tokens[i].Kind switch
                {
                    IngredientTokenKind.Open => 1,
                    IngredientTokenKind.Close => -1,
                    _ => 0,
                };
                if (depth == 0)
                {
                    return i;
                }
            }

            throw new InvalidOperationException("The line's parentheses were checked to balance.");
        }

        // A unit starting at token p and ending on a token's edge.
        private UnitMatch? UnitAt(int p)
        {
            if (p >= Count)
            {
                return null;
            }

            var match = UnitSpellings.Match(_text, _tokens[p].Start);
            return match is { } m && _boundaries.Contains(_tokens[p].Start + m.Length) ? match : null;
        }

        private int FirstTokenFrom(int index)
        {
            var i = 0;
            while (i < Count && _tokens[i].Start < index)
            {
                i++;
            }

            return i;
        }

        private TextSpan Span(int first, int end) =>
            new(_tokens[first].Start, _tokens[end - 1].End - _tokens[first].Start);

        private bool Is(int i, IngredientTokenKind kind) => i < Count && _tokens[i].Kind == kind;

        private bool Glued(int i) => i < Count && !_spaced[i];

        private bool Spaced(int i) => i < Count && _spaced[i];

        private bool IsHyphen(int i) => Is(i, IngredientTokenKind.Dash) && _text[_tokens[i].Start] is '-' or '\u2010' or '\u2011';

        // The word at i in any ASCII case; only ASCII letters fold, so no look-alike reads as one of these words.
        private bool IsWord(int i, string lower)
        {
            if (!Is(i, IngredientTokenKind.Word) || _tokens[i].Length != lower.Length)
            {
                return false;
            }

            var start = _tokens[i].Start;
            for (var c = 0; c < lower.Length; c++)
            {
                var actual = _text[start + c];
                if ((char.IsAsciiLetterUpper(actual) ? (char)(actual + ('a' - 'A')) : actual) != lower[c])
                {
                    return false;
                }
            }

            return true;
        }

        private Rational Glyph(int i) => IngredientLineTokenizer.GlyphValue(_text[_tokens[i].Start]);

        private long Number(int i) =>
            long.Parse(_text.AsSpan(_tokens[i].Start, _tokens[i].Length), NumberStyles.None, CultureInfo.InvariantCulture);
    }
}
