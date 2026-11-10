using FluentValidation;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Managers.Validation;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Facades;

/// <inheritdoc cref="IRecipePasteFacade"/>
/// <remarks>
/// No data layer: the reader is pure and the vocabulary it reads units from is the versioned list in <c>Business/</c>, so
/// nothing here touches a store. The pasted text is never logged (backend.md).
/// </remarks>
internal sealed class RecipePasteFacade(IValidator<PasteListRequest> validator) : IRecipePasteFacade
{
    public async Task<Result<PastedIngredientList>> ReadIngredientsAsync(PasteListRequest request, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var groups = PastedListReader.ReadIngredients(request.Text!)
            .Select(g => new PastedIngredientGroup(g.Heading, g.HeadingOriginalText, ToContract(g.HeadingSpan), [.. g.Lines.Select(ToContract)]))
            .ToList();
        return new PastedIngredientList(groups);
    }

    public async Task<Result<PastedStepList>> ReadStepsAsync(PasteListRequest request, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var groups = PastedListReader.ReadSteps(request.Text!)
            .Select(g => new PastedStepGroup(
                g.Heading,
                g.HeadingOriginalText,
                ToContract(g.HeadingSpan),
                [.. g.Lines.Select(s => new PastedStep(s.OriginalText, s.Text, ToContract(s.TextSpan), s.Problem))]))
            .ToList();
        return new PastedStepList(groups);
    }

    private static PastedIngredientLine ToContract(PasteIngredientLine line)
    {
        var parse = line.Parse;
        return new PastedIngredientLine(
            line.OriginalText,
            parse.Status,
            parse.Problem,
            parse.Quantity is { } quantity
                ? new PastedQuantity(quantity.Kind, ToContract(quantity.Low), ToContract(quantity.High))
                : null,
            ToContract(parse.QuantitySpan),
            parse.Style,
            parse.UnitCode,
            ToContract(parse.UnitSpan),
            parse.Item,
            parse.Note);
    }

    private static PastedFraction? ToContract(Rational? value) =>
        value is { } v ? new PastedFraction(v.Numerator, v.Denominator) : null;

    private static PastedSpan ToContract(TextSpan span) => new(span.Start, span.Length);

    private static PastedSpan? ToContract(TextSpan? span) => span is { } s ? ToContract(s) : null;
}
