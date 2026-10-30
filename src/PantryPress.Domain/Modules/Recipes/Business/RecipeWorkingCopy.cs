namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// A recipe's rows as the working copy holds them (PP4.3), read together under the recipe's lock (PP4.4), and the one way
/// they become a <see cref="RecipeSnapshot"/>. Nutrition comes already in hundredths, read from its two-place columns
/// where the numbers are, so nothing here names a fractional number type (PP-D05).
/// </summary>
internal sealed record RecipeWorkingCopy(
    Recipe Recipe,
    RecipeSnapshotNutrition Nutrition,
    IReadOnlyList<RecipeIngredientGroup> IngredientGroups,
    IReadOnlyList<RecipeIngredient> Ingredients,
    IReadOnlyList<RecipeStepGroup> StepGroups,
    IReadOnlyList<RecipeStep> Steps,
    IReadOnlyList<RecipeEquipment> Equipment,
    IReadOnlyList<RecipeNote> Notes,
    IReadOnlyList<RecipeKeyword> Keywords,
    IReadOnlyList<RecipeCourse> Courses,
    IReadOnlyList<RecipeCuisine> Cuisines,
    IReadOnlyList<RecipeDiet> Diets,
    IReadOnlyList<RecipeAllergen> Allergens)
{
    /// <summary>
    /// The recipe as one snapshot, its parts in the order a reader sees them: by position, and - since a position may be
    /// shared while parts are being reordered - by id after it, or by code for a vocabulary choice, so the same rows always
    /// make the same snapshot. A part that is not this recipe's is a bug, and throws.
    /// </summary>
    public RecipeSnapshot ToSnapshot()
    {
        var recipe = Recipe.Id;
        Require(IngredientGroups.All(g => g.RecipeId == recipe) && StepGroups.All(g => g.RecipeId == recipe), "a group");
        Require(
            Equipment.All(e => e.RecipeId == recipe) && Notes.All(n => n.RecipeId == recipe) && Keywords.All(k => k.RecipeId == recipe)
                && Courses.All(c => c.RecipeId == recipe) && Cuisines.All(c => c.RecipeId == recipe)
                && Diets.All(d => d.RecipeId == recipe) && Allergens.All(a => a.RecipeId == recipe),
            "a list item");

        var ingredientGroups = IngredientGroups.Select(g => g.Id).ToHashSet();
        var stepGroups = StepGroups.Select(g => g.Id).ToHashSet();
        Require(Ingredients.All(i => ingredientGroups.Contains(i.GroupId)), "an ingredient line");
        Require(Steps.All(s => stepGroups.Contains(s.GroupId)), "a step");

        return new RecipeSnapshot(
            recipe,
            Recipe.Title,
            Recipe.Summary,
            Recipe.Yield,
            Recipe.YieldUnit,
            Recipe.PrepMinutes,
            Recipe.CookMinutes,
            Recipe.AdditionalMinutes,
            Recipe.AdditionalTimeLabel,
            Recipe.VideoUrl,
            Nutrition,
            [.. IngredientGroups
                .OrderBy(g => g.Position).ThenBy(g => g.Id.Value)
                .Select(g => new RecipeSnapshotIngredientGroup(
                    g.Id,
                    g.Heading,
                    [.. Ingredients.Where(i => i.GroupId == g.Id).OrderBy(i => i.Position).ThenBy(i => i.Id.Value).Select(Line)]))],
            [.. StepGroups
                .OrderBy(g => g.Position).ThenBy(g => g.Id.Value)
                .Select(g => new RecipeSnapshotStepGroup(
                    g.Id,
                    g.Heading,
                    [.. Steps
                        .Where(s => s.GroupId == g.Id)
                        .OrderBy(s => s.Position).ThenBy(s => s.Id.Value)
                        .Select(s => new RecipeSnapshotStep(s.Id, s.Text))]))],
            [.. Equipment.OrderBy(e => e.Position).Select(e => e.Text)],
            [.. Notes.OrderBy(n => n.Position).Select(n => n.Text)],
            [.. Keywords.OrderBy(k => k.Position).Select(k => k.Text)],
            [.. Courses.OrderBy(c => c.Position).ThenBy(c => c.CourseCode, StringComparer.Ordinal).Select(c => c.CourseCode)],
            [.. Cuisines.OrderBy(c => c.Position).ThenBy(c => c.CuisineCode, StringComparer.Ordinal).Select(c => c.CuisineCode)],
            [.. Diets.OrderBy(d => d.Position).ThenBy(d => d.DietCode, StringComparer.Ordinal).Select(d => d.DietCode)],
            [.. Allergens.OrderBy(a => a.Position).ThenBy(a => a.AllergenCode, StringComparer.Ordinal).Select(a => a.AllergenCode)]);
    }

    private static RecipeSnapshotIngredient Line(RecipeIngredient line) => new(
        line.Id,
        line.OriginalText,
        line.ParseStatus,
        line.Quantity,
        Span(line.QuantityStart, line.QuantityLength),
        line.UnitCode,
        Span(line.UnitStart, line.UnitLength),
        line.Item,
        line.Note);

    private static RecipeSnapshotSpan? Span(int? start, int? length) => (start, length) switch
    {
        (null, null) => null,
        ({ } s, { } l) => new RecipeSnapshotSpan(s, l),
        _ => throw new InvalidOperationException("A span has a start and a length or neither; the table's checks should have refused it."),
    };

    private static void Require(bool holds, string what)
    {
        if (!holds)
        {
            throw new InvalidOperationException($"The working copy holds {what} that is not this recipe's.");
        }
    }
}
