namespace PantryPress.Domain.Modules.Recipes.Contracts;

/// <summary>
/// The sizes a recipe's fields are stored at (RCP-001, RCP-002, RCP-006; PP4.3), shared by the schema and the validators
/// that come with its routes (PP4.11). How many groups, lines, and steps a recipe may have is the validators' to say.
/// </summary>
public static class RecipeLimits
{
    public const int MaxTitleLength = 200;

    /// <summary>The summary is plain text; recipes are structured, never rich text (PP-D05).</summary>
    public const int MaxSummaryLength = 1000;

    /// <summary>An ingredient or step group's heading ("For the crust").</summary>
    public const int MaxGroupHeadingLength = 100;

    /// <summary>An ingredient line as the blogger wrote it, which is kept exactly (RCP-002).</summary>
    public const int MaxIngredientTextLength = 500;

    /// <summary>The item a parsed line names ("all-purpose flour").</summary>
    public const int MaxIngredientItemLength = 200;

    /// <summary>A parsed line's note ("sifted", "at room temperature").</summary>
    public const int MaxIngredientNoteLength = 200;

    public const int MaxStepTextLength = 2000;

    public const int MaxEquipmentLength = 200;

    public const int MaxNoteLength = 2000;

    public const int MaxKeywordLength = 60;

    /// <summary>The blogger's word for what the recipe makes ("servings", "cookies", "9-inch pie").</summary>
    public const int MaxYieldUnitLength = 40;

    /// <summary>The label of the additional time ("Chill", "Rise").</summary>
    public const int MaxAdditionalTimeLabelLength = 30;

    /// <summary>
    /// A time in whole minutes: 30 days, room for a long ferment. Total time is not stored; it is the sum (PP4.9).
    /// </summary>
    public const int MaxMinutes = 43_200;

    public const int MaxVideoUrlLength = 2048;

    /// <summary>The serving the nutrition is for, in the blogger's words ("1 slice").</summary>
    public const int MaxServingSizeLength = 60;

    /// <summary>
    /// The largest numerator or denominator a stored quantity may have, so that scaling (PP4.7) and conversion (PP4.8) stay
    /// far inside the 64 bits a <c>Rational</c> holds.
    /// </summary>
    public const long MaxQuantityTerm = 1_000_000;

    /// <summary>Nutrition values are entered per serving, to two decimal places (RCP-006).</summary>
    public const int NutritionPrecision = 9;

    public const int NutritionScale = 2;
}
