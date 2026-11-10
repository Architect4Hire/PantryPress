using PantryPress.Domain.Modules.Recipes.Business;
using K = PantryPress.Domain.Modules.Recipes.Business.IngredientTokenKind;

namespace PantryPress.Tests.Recipes;

// PP4.5: the tokenizer covers a line end to end, in UTF-16 code units, and classifies by Unicode category alone.
public sealed class IngredientLineTokenizerTests
{
    public static TheoryData<string> Lines { get; } =
    [
        string.Empty,
        "2½ cups (250 g) all-purpose flour, sifted",
        "▢ 1 1/2 tsp. kosher salt",
        "1\u20442 cup – 3/4 cup",
        "2\u00A0cups\u2009flour\t",
        "🍎 3 apples 👩\u200D🍳",
        "cre\u0300me frai\u0302che",
        "２ cups ¹\u2044₂ ↉",
        "350°F 200ºC",
        "\uD83C flour \uDC00 \u0007",
    ];

    [Theory]
    [MemberData(nameof(Lines))]
    public void Tokens_cover_the_line_exactly(string line)
    {
        var tokens = IngredientLineTokenizer.Tokenize(line);
        var at = 0;
        foreach (var token in tokens)
        {
            Assert.Equal(at, token.Start);
            Assert.True(token.Length > 0);
            at = token.End;
        }

        Assert.Equal(line.Length, at);
    }

    [Fact]
    public void A_line_is_split_by_kind()
    {
        const string line = "1½ cups (250 g) flour, sifted.";
        Assert.Equal(
            [
                (K.Digits, "1"), (K.FractionGlyph, "½"), (K.Space, " "), (K.Word, "cups"), (K.Space, " "), (K.Open, "("),
                (K.Digits, "250"), (K.Space, " "), (K.Word, "g"), (K.Close, ")"), (K.Space, " "), (K.Word, "flour"),
                (K.Comma, ","), (K.Space, " "), (K.Word, "sifted"), (K.Point, "."),
            ],
            Read(line));
    }

    [Theory]
    [InlineData("-", K.Dash)]
    [InlineData("\u2010", K.Dash)]
    [InlineData("\u2011", K.Dash)]
    [InlineData("\u2012", K.Dash)]
    [InlineData("–", K.Dash)]
    [InlineData("—", K.Dash)]
    [InlineData("/", K.Slash)]
    [InlineData("\u2044", K.Slash)]
    [InlineData("\u2212", K.Symbol)]
    [InlineData("°", K.Symbol)]
    [InlineData("º", K.Word)]
    [InlineData("%", K.Symbol)]
    [InlineData("▢", K.Symbol)]
    [InlineData("\u200B", K.Space)]
    [InlineData("\uFEFF", K.Space)]
    [InlineData("\u200E", K.Space)]
    [InlineData("\u00AD", K.Space)]
    [InlineData("\u0378", K.Invalid)]
    [InlineData("２", K.OtherNumber)]
    [InlineData("¹", K.OtherNumber)]
    [InlineData("↉", K.OtherNumber)]
    [InlineData("٣", K.OtherNumber)]
    [InlineData("\u00A0", K.Space)]
    [InlineData("\u2009", K.Space)]
    [InlineData("\u202F", K.Space)]
    [InlineData("\t", K.Space)]
    [InlineData("\n", K.Space)]
    [InlineData("\u0000", K.Invalid)]
    [InlineData("\u0007", K.Invalid)]
    [InlineData("\u007F", K.Invalid)]
    public void A_character_has_its_kind(string character, IngredientTokenKind kind) =>
        Assert.Equal([(kind, character)], Read(character));

    [Fact]
    public void A_surrogate_pair_is_one_token_by_its_category()
    {
        // An emoji is a symbol; a letter outside the basic plane (mathematical script A) joins a word.
        Assert.Equal([(K.Symbol, "🍎")], Read("🍎"));
        Assert.Equal([(K.Word, "a\U0001D49Cb")], Read("a\U0001D49Cb"));
        // Built in code: xUnit cannot carry half a surrogate pair through InlineData.
        Assert.Equal([(K.Invalid, "\uDF4E"), (K.Invalid, "\uD83C")], Read("\uDF4E\uD83C"));
        Assert.Equal([(K.Invalid, "\uD83C"), (K.Space, " ")], Read("\uD83C "));
    }

    [Fact]
    public void Punctuation_never_runs_together()
    {
        Assert.Equal([(K.Digits, "1"), (K.Dash, "-"), (K.Dash, "-"), (K.Digits, "2")], Read("1--2"));
        Assert.Equal([(K.Point, "."), (K.Point, "."), (K.Point, ".")], Read("..."));
        Assert.Equal([(K.OtherNumber, "\U0001D7D0")], Read("\U0001D7D0"));
        Assert.Equal([(K.Word, "a"), (K.Invalid, "\uD83C")], Read("a\uD83C"));
    }

    [Fact]
    public void A_format_character_joins_the_space_around_it()
    {
        Assert.Equal([(K.Space, "\uFEFF"), (K.Digits, "2")], Read("\uFEFF2"));
        Assert.Equal([(K.Digits, "2"), (K.Space, " \u200B "), (K.Word, "cups")], Read("2 \u200B cups"));
    }

    [Fact]
    public void A_combining_mark_stays_in_its_word()
    {
        Assert.Equal([(K.Word, "cre\u0300me")], Read("cre\u0300me"));
        Assert.Equal([(K.Space, " "), (K.Word, "\u0300x")], Read(" \u0300x"));
    }

    [Fact]
    public void Every_fraction_character_has_its_value()
    {
        var expected = new Dictionary<char, (long, long)>
        {
            ['½'] = (1, 2),
            ['⅓'] = (1, 3),
            ['⅔'] = (2, 3),
            ['¼'] = (1, 4),
            ['¾'] = (3, 4),
            ['⅕'] = (1, 5),
            ['⅖'] = (2, 5),
            ['⅗'] = (3, 5),
            ['⅘'] = (4, 5),
            ['⅙'] = (1, 6),
            ['⅚'] = (5, 6),
            ['⅐'] = (1, 7),
            ['⅛'] = (1, 8),
            ['⅜'] = (3, 8),
            ['⅝'] = (5, 8),
            ['⅞'] = (7, 8),
            ['⅑'] = (1, 9),
            ['⅒'] = (1, 10),
        };
        foreach (var (glyph, (numerator, denominator)) in expected)
        {
            Assert.Equal([(K.FractionGlyph, glyph.ToString())], Read(glyph.ToString()));
            Assert.Equal(Rational.Create(numerator, denominator), IngredientLineTokenizer.GlyphValue(glyph));
        }

        // Every other vulgar fraction Unicode has is an "other number", which no quantity reads: ↉ is zero thirds.
        for (var c = '⅐'; c <= '⅟'; c++)
        {
            Assert.Equal(expected.ContainsKey(c) ? K.FractionGlyph : K.OtherNumber, Read(c.ToString())[0].Kind);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => IngredientLineTokenizer.GlyphValue('2'));
    }

    private static List<(IngredientTokenKind Kind, string Text)> Read(string line) =>
        [.. IngredientLineTokenizer.Tokenize(line).Select(t => (t.Kind, line.Substring(t.Start, t.Length)))];
}
