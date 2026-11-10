using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.6: the paste reader's rules one at a time - how a paste splits into lines, which lines are headings, what is cut from
// a step, and that every line comes back exactly as pasted. The golden pastes (PastedListCorpusTests) show the rules
// together on real lists; the properties (PastedListReaderPropertyTests) show nothing is ever lost or rewritten.
public sealed class PastedListReaderTests
{
    [Theory]
    [InlineData("a@LF@b")]
    [InlineData("a@CR@b")]
    [InlineData("a@CRLF@b")]
    [InlineData("a@NEL@b")]
    [InlineData("a@LS@b")]
    [InlineData("a@PS@b")]
    [InlineData("a@CRLF@@CRLF@b@LF@")]
    [InlineData("@LF@@LF@a@CR@@CR@  @TAB@ @NBSP@@ZWSP@@BOM@@LF@b@CRLF@")]
    public void A_paste_splits_at_every_line_break_and_skips_blank_lines(string paste) =>
        Assert.Equal(["a", "b"], PastedListReader.Lines(Text(paste)));

    // A carriage return and a line feed together are one break; two carriage returns are two, and the blank between them is
    // skipped.
    [Fact]
    public void Lines_are_kept_exactly_as_pasted()
    {
        var paste = Text("  2 cups flour @TAB@@CRLF@@NBSP@1 tsp salt,@CR@@CR@• 1 egg  ");
        Assert.Equal([Text("  2 cups flour @TAB@"), Text("@NBSP@1 tsp salt,"), "• 1 egg  "], PastedListReader.Lines(paste));
    }

    [Fact]
    public void A_vertical_tab_or_form_feed_is_whitespace_inside_a_line_not_a_break() =>
        Assert.Equal([Text("a@VT@b@FF@c")], PastedListReader.Lines(Text("a@VT@b@FF@c")));

    [Fact]
    public void Nothing_but_blank_lines_is_no_group() =>
        Assert.Empty(PastedListReader.ReadIngredients(Text("@LF@  @CRLF@@ZWSP@")));

    [Fact]
    public void Lines_before_any_heading_are_one_group_with_none()
    {
        var groups = PastedListReader.ReadIngredients("2 cups flour\n1 tsp salt");

        var group = Assert.Single(groups);
        Assert.Null(group.Heading);
        Assert.Equal(["2 cups flour", "1 tsp salt"], group.Lines.Select(l => l.OriginalText));
    }

    [Fact]
    public void Each_heading_starts_a_group_and_two_together_leave_an_empty_one()
    {
        var groups = PastedListReader.ReadIngredients("1 egg\nFor the crust:\n\nFor the filling:\n2 apples\n\n3 tbsp sugar");

        Assert.Equal(
            [(null, new[] { "1 egg" }), ("For the crust", []), ("For the filling", ["2 apples", "3 tbsp sugar"])],
            groups.Select(g => (g.Heading, g.Lines.Select(l => l.OriginalText).ToArray())));
    }

    [Fact]
    public void A_paste_that_starts_with_a_heading_has_no_group_without_one()
    {
        var groups = PastedListReader.ReadSteps("## Crust\nMix.");

        Assert.Equal([("Crust", new[] { "Mix." })], groups.Select(g => (g.Heading, g.Lines.Select(l => l.Text).ToArray())));
    }

    [Fact]
    public void Every_ingredient_line_is_the_parsers_reading_of_it()
    {
        string[] lines = ["1 ½ cups flour, sifted", "2 3 cups", "salt to taste", "1. 2 cups milk", "Ingredients"];
        var group = Assert.Single(PastedListReader.ReadIngredients(string.Join('\n', lines)));

        Assert.Equal(lines, group.Lines.Select(l => l.OriginalText));
        Assert.Equal(lines.Select(IngredientLineParser.Parse), group.Lines.Select(l => l.Parse));
    }

    [Theory]
    [InlineData("For the crust:", "For the crust")]
    [InlineData("  For the crust :  ", "For the crust")]
    [InlineData("Crust:", "Crust")]
    [InlineData("Make the glaze:", "Make the glaze")]
    [InlineData("## Filling", "Filling")]
    [InlineData("# Filling:", "Filling")]
    [InlineData("###### Six", "Six")]
    [InlineData("For the crème fraîche topping:", "For the crème fraîche topping")]
    public void A_heading_is_cut_from_its_line_in_either_list(string line, string heading)
    {
        Assert.Equal(heading, PastedListReader.ReadHeading(line, ingredientList: true));
        Assert.Equal(heading, PastedListReader.ReadHeading(line, ingredientList: false));
    }

    [Theory]
    [InlineData("For serving", "For serving")]
    [InlineData("FOR THE FILLING", "FOR THE FILLING")]
    [InlineData("for the glaze", "for the glaze")]
    [InlineData("• For the crust:", "For the crust")]
    [InlineData("- For the crust", "For the crust")]
    [InlineData("▢ Topping:", "Topping")]
    public void An_ingredient_list_also_reads_a_for_line_and_a_bulleted_heading(string line, string heading)
    {
        Assert.Equal(heading, PastedListReader.ReadHeading(line, ingredientList: true));
        Assert.Null(PastedListReader.ReadHeading(line, ingredientList: false));
    }

    [Theory]
    [InlineData("2 cups flour")]
    [InlineData("Fresh mint, for garnish")]
    [InlineData("For garnish: parsley")]
    [InlineData("For the 9-inch crust")]
    [InlineData("For the crust (optional)")]
    [InlineData("For ½ batch")]
    [InlineData("For")]
    [InlineData("Forks")]
    [InlineData("2 cups:")]
    [InlineData("½ cup:")]
    [InlineData("１ cup:")]
    [InlineData(":")]
    [InlineData("##")]
    [InlineData("## ")]
    [InlineData("#Filling")]
    [InlineData("#1 favorite cookies")]
    [InlineData("####### Seven")]
    [InlineData("## :")]
    [InlineData("salt:@BELL@")]
    [InlineData("Crust@LONE@:")]
    public void Other_lines_are_never_headings(string line)
    {
        Assert.Null(PastedListReader.ReadHeading(Text(line), ingredientList: true));
        Assert.Null(PastedListReader.ReadHeading(Text(line), ingredientList: false));
    }

    [Theory]
    [InlineData("Step 2: make the glaze:")]
    [InlineData("1. Make the crust:")]
    [InlineData("• Whisk together:")]
    [InlineData("For best results, use cold butter")]
    public void A_marked_step_or_a_for_sentence_is_a_step(string line) =>
        Assert.Null(PastedListReader.ReadHeading(line, ingredientList: false));

    // Case folds ASCII letters only, as unit spellings do: a long s (U+017F), which the runtime's case mapping turns into S,
    // never becomes the s of "step".
    [Fact]
    public void Only_ASCII_letters_fold_into_step()
    {
        Assert.Equal(Text("@LONGS@tep 1 Mix"), PastedListReader.ReadStep(Text("@LONGS@tep 1 Mix")).Text);
        Assert.Equal("Mix", PastedListReader.ReadStep("sTeP 1 Mix").Text);
    }

    [Fact]
    public void A_heading_keeps_its_line_and_where_it_is_in_it()
    {
        var group = Assert.Single(PastedListReader.ReadIngredients("  • For the crust :  \n1 egg"));

        Assert.Equal("For the crust", group.Heading);
        Assert.Equal("  • For the crust :  ", group.HeadingOriginalText);
        Assert.Equal(new TextSpan(4, 13), group.HeadingSpan);
        Assert.Equal(new TextSpan(4, 13), PastedListReader.ReadHeadingSpan("  • For the crust :  ", ingredientList: true));
    }

    [Fact]
    public void A_heading_fits_the_stored_heading_length()
    {
        var longest = new string('a', RecipeLimits.MaxGroupHeadingLength);

        Assert.Equal(longest, PastedListReader.ReadHeading(longest + ":", ingredientList: true));
        Assert.Null(PastedListReader.ReadHeading(longest + "a:", ingredientList: true));
        Assert.Null(PastedListReader.ReadHeading("For " + new string('a', RecipeLimits.MaxGroupHeadingLength), ingredientList: true));
    }

    [Theory]
    [InlineData("1. Preheat the oven.", "Preheat the oven.")]
    [InlineData("12) Bake", "Bake")]
    [InlineData("3: Cool", "Cool")]
    [InlineData("Step 4: Serve", "Serve")]
    [InlineData("STEP 5 - Serve", "Serve")]
    [InlineData("step 6 Serve", "Serve")]
    [InlineData("Step 7. Serve", "Serve")]
    [InlineData("Step 8) Serve", "Serve")]
    [InlineData("Step 9 – Serve", "Serve")]
    [InlineData("• Stir", "Stir")]
    [InlineData("•Stir", "Stir")]
    [InlineData("▢ Stir", "Stir")]
    [InlineData("- Stir", "Stir")]
    [InlineData("– Stir", "Stir")]
    [InlineData("* Stir", "Stir")]
    [InlineData("  1.@TAB@Stir  ", "Stir")]
    [InlineData("1. 2. Stir", "2. Stir")]
    [InlineData("Preheat the oven.", "Preheat the oven.")]
    [InlineData("  Preheat the oven.  ", "Preheat the oven.")]
    [InlineData("1.5 hours later, check it.", "1.5 hours later, check it.")]
    [InlineData("1:30 later, check it.", "1:30 later, check it.")]
    [InlineData("2 hours later, check it.", "2 hours later, check it.")]
    [InlineData("2024. A good year.", "2024. A good year.")]
    [InlineData("Step 1000: Serve", "Step 1000: Serve")]
    [InlineData("Step5 Serve", "Step5 Serve")]
    [InlineData("Stepping back, let it rest.", "Stepping back, let it rest.")]
    [InlineData("-5 degrees outside, chill it.", "-5 degrees outside, chill it.")]
    [InlineData("*Optional* sprinkle salt.", "*Optional* sprinkle salt.")]
    [InlineData("1.Stir", "1.Stir")]
    [InlineData("@LONGS@tep 1 Mix", "@LONGS@tep 1 Mix")]
    public void A_step_is_its_line_with_a_leading_marker_cut_off(string line, string text)
    {
        var step = PastedListReader.ReadStep(Text(line));

        Assert.Equal(Text(line), step.OriginalText);
        Assert.Equal(Text(text), step.Text);
        Assert.Equal(Text(text), step.OriginalText.Substring(step.TextSpan.Start, step.TextSpan.Length));
        Assert.Null(step.Problem);
    }

    [Theory]
    [InlineData("Step 1")]
    [InlineData("STEP 2:")]
    [InlineData("3.")]
    [InlineData("  4)  ")]
    [InlineData("•")]
    [InlineData("-")]
    public void A_line_that_is_only_a_marker_is_flagged_not_dropped(string line)
    {
        var group = Assert.Single(PastedListReader.ReadSteps("Preheat.\n" + line + "\nBake."));

        Assert.Equal(["Preheat.", line, "Bake."], group.Lines.Select(s => s.OriginalText));
        var step = group.Lines[1];
        Assert.Equal(StepPasteProblem.NoText, step.Problem);
        Assert.Equal(string.Empty, step.Text);
        Assert.Equal(line.TrimEnd().Length, step.TextSpan.Start);
    }

    [Theory]
    [InlineData("1. Stir@LONE@ well")]
    [InlineData("Stir@BELL@")]
    [InlineData("@LOW@")]
    public void A_step_with_a_broken_or_control_character_is_flagged(string line)
    {
        var step = PastedListReader.ReadStep(Text(line));

        Assert.Equal(StepPasteProblem.IllFormedText, step.Problem);
        Assert.Equal(Text(line), step.OriginalText);
    }

    [Fact]
    public void A_step_fits_the_stored_step_length_after_its_marker()
    {
        var longest = new string('a', RecipeLimits.MaxStepTextLength);

        Assert.Null(PastedListReader.ReadStep(longest).Problem);
        Assert.Null(PastedListReader.ReadStep("Step 12: " + longest).Problem);
        Assert.Equal(StepPasteProblem.TooLong, PastedListReader.ReadStep(longest + "a").Problem);
    }

    [Fact]
    public void An_ingredient_line_with_a_broken_character_is_flagged_by_the_parser()
    {
        var line = Assert.Single(Assert.Single(PastedListReader.ReadIngredients(Text("Crust@LONE@:"))).Lines);

        Assert.Equal(IngredientParseProblem.IllFormedText, line.Parse.Problem);
    }

    // The tests name invisible characters rather than holding them, so the source shows what each case is.
    private static string Text(string spelled)
    {
        var map = new (string Name, string Value)[]
        {
            ("@CRLF@", "\r\n"), ("@CR@", "\r"), ("@LF@", "\n"), ("@NEL@", ((char)0x85).ToString()),
            ("@LS@", ((char)0x2028).ToString()), ("@PS@", ((char)0x2029).ToString()), ("@TAB@", "\t"),
            ("@VT@", ((char)0x0B).ToString()), ("@FF@", ((char)0x0C).ToString()), ("@NBSP@", ((char)0xA0).ToString()),
            ("@ZWSP@", ((char)0x200B).ToString()), ("@BOM@", ((char)0xFEFF).ToString()), ("@BELL@", ((char)0x07).ToString()),
            ("@LONE@", ((char)0xD83C).ToString()), ("@LOW@", ((char)0xDF4E).ToString()), ("@LONGS@", ((char)0x017F).ToString()),
        };
        foreach (var (name, value) in map)
        {
            spelled = spelled.Replace(name, value, StringComparison.Ordinal);
        }

        return spelled;
    }
}
