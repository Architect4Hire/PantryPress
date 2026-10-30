using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// A whole recipe as one value (RCP-001, RCP-007; PP4.4): what a revision freezes, and what a published revision gives the
/// page-data build, JSON-LD, and a post's recipe card (PP4.10, PP7.11, PP-D04). Every text is the blogger's own, exactly as
/// stored. Parts come in the order a reader sees them, so no position is kept: the order is the list's. Ids are kept, so a
/// later compare can line parts up across revisions. Written and read only by <see cref="RecipeSnapshotJson"/>.
/// </summary>
internal sealed record RecipeSnapshot(
    RecipeId Id,
    string Title,
    string? Summary,
    Quantity? Yield,
    string? YieldUnit,
    int? PrepMinutes,
    int? CookMinutes,
    int? AdditionalMinutes,
    string? AdditionalTimeLabel,
    string? VideoUrl,
    RecipeSnapshotNutrition Nutrition,
    IReadOnlyList<RecipeSnapshotIngredientGroup> IngredientGroups,
    IReadOnlyList<RecipeSnapshotStepGroup> StepGroups,
    IReadOnlyList<string> Equipment,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Courses,
    IReadOnlyList<string> Cuisines,
    IReadOnlyList<string> Diets,
    IReadOnlyList<string> Allergens);

/// <summary>
/// Nutrition per serving as the blogger entered it (RCP-006), never estimated. Each value is a whole number of hundredths of
/// its unit - kilocalories, grams, or milligrams, as the recipe's columns name them - which is exactly what a two-place
/// column holds, so the snapshot carries it with no rounding and no floating point (PP-D05).
/// </summary>
internal sealed record RecipeSnapshotNutrition(
    string? ServingSize,
    long? Calories,
    long? FatGrams,
    long? SaturatedFatGrams,
    long? TransFatGrams,
    long? UnsaturatedFatGrams,
    long? CholesterolMilligrams,
    long? SodiumMilligrams,
    long? CarbohydrateGrams,
    long? FiberGrams,
    long? SugarGrams,
    long? ProteinGrams);

internal sealed record RecipeSnapshotIngredientGroup(
    RecipeIngredientGroupId Id,
    string? Heading,
    IReadOnlyList<RecipeSnapshotIngredient> Lines);

/// <summary>One ingredient line: the blogger's wording, and the parsed parts that only enrich it (RCP-002).</summary>
internal sealed record RecipeSnapshotIngredient(
    RecipeIngredientId Id,
    string OriginalText,
    IngredientParseStatus ParseStatus,
    Quantity? Quantity,
    RecipeSnapshotSpan? QuantitySpan,
    string? UnitCode,
    RecipeSnapshotSpan? UnitSpan,
    string? Item,
    string? Note);

/// <summary>Where a part is in a line's original text, in UTF-16 code units.</summary>
internal readonly record struct RecipeSnapshotSpan(int Start, int Length);

internal sealed record RecipeSnapshotStepGroup(
    RecipeStepGroupId Id,
    string? Heading,
    IReadOnlyList<RecipeSnapshotStep> Steps);

internal sealed record RecipeSnapshotStep(RecipeStepId Id, string Text);
