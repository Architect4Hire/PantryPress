using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// A recipe's working copy as its routes answer it (PP4.11): every part in the order a reader sees it, every line exactly
/// as written beside what was read from it. Nutrition comes in already mapped from its columns, since this module's Business
/// names no fractional type (PP-D05).
/// </summary>
internal static class RecipeDetails
{
    /// <summary>
    /// The recipe whole. An unparsed line is read again for its reason, which is not stored (PP4.5): the parser is
    /// deterministic, so the reason is the one it had when the line was saved, unless the parser has changed since.
    /// </summary>
    public static RecipeDetail From(RecipeWorkingCopy copy, RecipeNutrition? nutrition, bool canEdit)
    {
        var recipe = copy.Recipe;
        var snapshot = copy.ToSnapshot();
        var lines = copy.Ingredients.ToDictionary(l => l.Id);
        return new RecipeDetail(
            recipe.Id.Value,
            RecipeVersion.Write(recipe.Version),
            recipe.Title,
            recipe.Summary,
            QuantityOf(recipe.Yield),
            recipe.YieldUnit,
            recipe.PrepMinutes,
            recipe.CookMinutes,
            recipe.AdditionalMinutes,
            recipe.AdditionalTimeLabel,
            recipe.VideoUrl,
            nutrition,
            [.. snapshot.IngredientGroups.Select(g => new RecipeIngredientGroupDetail(
                g.Id.Value,
                g.Heading,
                [.. g.Lines.Select(l => Line(lines[l.Id]))]))],
            [.. snapshot.StepGroups.Select(g => new RecipeStepGroupDetail(
                g.Id.Value,
                g.Heading,
                [.. g.Steps.Select(s => new RecipeStepDetail(s.Id.Value, s.Text))]))],
            snapshot.Equipment,
            snapshot.Notes,
            snapshot.Keywords,
            snapshot.Courses,
            snapshot.Cuisines,
            snapshot.Diets,
            snapshot.Allergens,
            recipe.CreatedAt,
            recipe.UpdatedAt,
            recipe.ArchivedAt,
            canEdit);
    }

    /// <summary>The recipe as one row of a list.</summary>
    public static RecipeSummary Summary(Recipe recipe, bool canEdit) =>
        new(recipe.Id.Value, recipe.Title, recipe.UpdatedAt, recipe.ArchivedAt, canEdit);

    private static RecipeIngredientLineDetail Line(RecipeIngredient line) => new(
        line.Id.Value,
        line.OriginalText,
        line.ParseStatus,
        line.ParseStatus == IngredientParseStatus.Unparsed ? IngredientLineParser.Parse(line.OriginalText).Problem : null,
        QuantityOf(line.Quantity),
        Span(line.QuantityStart, line.QuantityLength),
        line.UnitCode,
        Span(line.UnitStart, line.UnitLength),
        line.Item,
        line.Note);

    private static RecipeQuantity? QuantityOf(Quantity? quantity) => quantity is null
        ? null
        : new RecipeQuantity(quantity.Kind, Fraction(quantity.Low), Fraction(quantity.High));

    private static RecipeFraction? Fraction(Rational? value) =>
        value is { } v ? new RecipeFraction(v.Numerator, v.Denominator) : null;

    private static RecipeTextSpan? Span(int? start, int? length) =>
        start is { } s && length is { } l ? new RecipeTextSpan(s, l) : null;
}
