using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.Domain.Modules.Recipes.Facades;
using PantryPress.Domain.Modules.Recipes.Validation;

namespace PantryPress.Tests.Recipes;

// PP4.6: the paste facade at its edge - what it refuses before reading, and how a reading becomes the contract: exact
// fractions, spans, and every part of a line, with nothing of the Business types showing. It needs no store, so it is built
// here directly; the routes and the two-site world are RecipePasteTwoSiteTests'.
public sealed class RecipePasteFacadeTests
{
    private readonly RecipePasteFacade _facade = new(new PasteListRequestValidator());

    public static TheoryData<string?, string> Refused => new()
    {
        { null, "Paste your list first." },
        { string.Empty, "Paste your list first." },
        { " \n\t\r\n ", "Paste your list first." },
        { new string('a', RecipeLimits.MaxPastedTextLength + 1), "That's more than we can read at once. Paste a shorter list." },
        { string.Join('\n', Enumerable.Repeat("1 egg", RecipeLimits.MaxPastedLines + 1)), "That's more than we can read at once. Paste up to 200 lines." },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task A_paste_with_nothing_to_read_or_too_much_is_refused_for_either_list(string? text, string message)
    {
        foreach (var error in new[]
        {
            (await _facade.ReadIngredientsAsync(new PasteListRequest(text), CancellationToken.None)).Error!,
            (await _facade.ReadStepsAsync(new PasteListRequest(text), CancellationToken.None)).Error!,
        })
        {
            Assert.Equal(ErrorKind.Validation, error.Kind);
            Assert.Equal([message], Assert.Single(error.Fields).Value);
            Assert.Equal("text", Assert.Single(error.Fields).Key);
        }
    }

    [Fact]
    public async Task The_limits_themselves_are_read()
    {
        // Blank lines do not count toward the line limit, and a paste of exactly the longest length is read.
        var most = string.Join("\n\n", Enumerable.Repeat("1 egg", RecipeLimits.MaxPastedLines));
        var longest = "a" + new string(' ', RecipeLimits.MaxPastedTextLength - 1);

        Assert.True((await _facade.ReadIngredientsAsync(new PasteListRequest(most), CancellationToken.None)).IsSuccess);
        Assert.True((await _facade.ReadStepsAsync(new PasteListRequest(longest), CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task An_ingredient_reading_is_written_into_the_contract_exactly()
    {
        var result = await _facade.ReadIngredientsAsync(
            new PasteListRequest("For the crust:\n1 ½ cups flour, sifted\n2–3 tbsp water\nsalt to taste\n2 3 cups\nbutter"),
            CancellationToken.None);

        var group = Assert.Single(result.Value.Groups);
        Assert.Equal(("For the crust", "For the crust:", new PastedSpan(0, 13)), (group.Heading, group.HeadingOriginalText, group.HeadingSpan));
        Assert.Equal(
            [
                new PastedIngredientLine(
                    "1 ½ cups flour, sifted", IngredientParseStatus.Parsed, null,
                    new PastedQuantity(QuantityKind.Amount, new PastedFraction(3, 2), null), new PastedSpan(0, 3), QuantityStyle.Fraction,
                    "cup", new PastedSpan(4, 4), "flour", "sifted"),
                new PastedIngredientLine(
                    "2–3 tbsp water", IngredientParseStatus.Parsed, null,
                    new PastedQuantity(QuantityKind.Range, new PastedFraction(2, 1), new PastedFraction(3, 1)), new PastedSpan(0, 3),
                    QuantityStyle.Fraction, "tablespoon", new PastedSpan(4, 4), "water", null),
                new PastedIngredientLine(
                    "salt to taste", IngredientParseStatus.Parsed, null,
                    new PastedQuantity(QuantityKind.ToTaste, null, null), new PastedSpan(5, 8), null, null, null, "salt", null),
                new PastedIngredientLine(
                    "2 3 cups", IngredientParseStatus.Unparsed, IngredientParseProblem.AmbiguousQuantity,
                    null, null, null, null, null, null, null),
                new PastedIngredientLine(
                    "butter", IngredientParseStatus.Parsed, null, null, null, null, null, null, "butter", null),
            ],
            group.Lines);
    }

    [Fact]
    public async Task A_step_reading_is_written_into_the_contract_exactly()
    {
        var result = await _facade.ReadStepsAsync(new PasteListRequest("Step 1: Preheat.\n## Bake\n2.\n3) Bake it."), CancellationToken.None);

        Assert.Equal(
            [
                new PastedStepGroup(null, null, null, [new PastedStep("Step 1: Preheat.", "Preheat.", new PastedSpan(8, 8), null)]),
                new PastedStepGroup("Bake", "## Bake", new PastedSpan(3, 4), [
                    new PastedStep("2.", string.Empty, new PastedSpan(2, 0), StepPasteProblem.NoText),
                    new PastedStep("3) Bake it.", "Bake it.", new PastedSpan(3, 8), null),
                ]),
            ],
            result.Value.Groups,
            new StepGroupComparer());
    }

    private sealed class StepGroupComparer : IEqualityComparer<PastedStepGroup>
    {
        public bool Equals(PastedStepGroup? x, PastedStepGroup? y) =>
            x is not null && y is not null && x.Heading == y.Heading && x.HeadingOriginalText == y.HeadingOriginalText
            && x.HeadingSpan == y.HeadingSpan && x.Steps.SequenceEqual(y.Steps);

        public int GetHashCode(PastedStepGroup obj) => 0;
    }
}
