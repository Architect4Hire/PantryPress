namespace PantryPress.Domain.Modules.Recipes.Contracts;

/// <summary>
/// Something a recipe's schema.org <c>Recipe</c> JSON-LD lacks (RCP-008; PP4.10), measured against Google's Recipe rich
/// result requirements, which name what a search result needs. The generator reports each one the recipe or its page lacks,
/// in this order, and emits nothing in its place: PantryPress never fills a gap by inferring a fact (RCP-006). What the
/// blogger is told about each - in the recipe editor (PP4.15) and the publish readiness check (POST-006) - is theirs to
/// word. Never stored; enums are written as kebab-case names (api-contract.md), so once a contract carries it, renaming a
/// member is a breaking change.
/// </summary>
public enum RecipeJsonLdGap
{
    /// <summary>The recipe has no title with any text in it. Required (<c>name</c>).</summary>
    Name,

    /// <summary>
    /// The page gives no image of the finished dish. Required (<c>image</c>). A recipe has no image of its own: it is the
    /// post's (POST-004).
    /// </summary>
    Image,

    /// <summary>
    /// Nutrition was entered but no yield was, and Google needs the number of servings whenever nutrition is per serving.
    /// Required (<c>recipeYield</c>), and reported instead of <see cref="RecipeYield"/>.
    /// </summary>
    YieldForNutrition,

    /// <summary>The page names no author. Recommended (<c>author</c>).</summary>
    Author,

    /// <summary>The page gives no publish date. Recommended (<c>datePublished</c>).</summary>
    DatePublished,

    /// <summary>The recipe has no summary. Recommended (<c>description</c>).</summary>
    Description,

    /// <summary>No time was entered, so there is no total. Recommended (<c>totalTime</c>).</summary>
    Times,

    /// <summary>
    /// Prep time was entered without cook time, or cook time without prep time. Google asks for the two together. The one
    /// entered is still emitted, with the total. Recommended (<c>prepTime</c>, <c>cookTime</c>).
    /// </summary>
    PrepAndCookTime,

    /// <summary>No yield was entered, and no nutrition either. Recommended (<c>recipeYield</c>).</summary>
    RecipeYield,

    /// <summary>The recipe has no ingredient line. Recommended (<c>recipeIngredient</c>).</summary>
    Ingredients,

    /// <summary>The recipe has no step. Recommended (<c>recipeInstructions</c>).</summary>
    Instructions,

    /// <summary>No course was chosen. Recommended (<c>recipeCategory</c>).</summary>
    Courses,

    /// <summary>No cuisine was chosen. Recommended (<c>recipeCuisine</c>).</summary>
    Cuisines,

    /// <summary>No keyword was entered. Recommended (<c>keywords</c>).</summary>
    Keywords,

    /// <summary>
    /// No calories were entered. Recommended (<c>nutrition.calories</c>). Only the blogger can close it: PantryPress never
    /// estimates nutrition (RCP-006).
    /// </summary>
    Calories,
}

/// <summary>How much a <see cref="RecipeJsonLdGap"/> matters to a search result (PP4.10).</summary>
public enum RecipeJsonLdNeed
{
    /// <summary>Without it the recipe is not eligible for a recipe rich result.</summary>
    Required,

    /// <summary>The result is eligible without it, but richer with it.</summary>
    Recommended,
}

/// <summary>Which gaps are required and which recommended (PP4.10), as Google's Recipe documentation lists them.</summary>
public static class RecipeJsonLdGaps
{
    public static RecipeJsonLdNeed NeedOf(RecipeJsonLdGap gap) => gap switch
    {
        RecipeJsonLdGap.Name or RecipeJsonLdGap.Image or RecipeJsonLdGap.YieldForNutrition => RecipeJsonLdNeed.Required,
        RecipeJsonLdGap.Author or RecipeJsonLdGap.DatePublished or RecipeJsonLdGap.Description or RecipeJsonLdGap.Times
            or RecipeJsonLdGap.PrepAndCookTime or RecipeJsonLdGap.RecipeYield or RecipeJsonLdGap.Ingredients
            or RecipeJsonLdGap.Instructions or RecipeJsonLdGap.Courses or RecipeJsonLdGap.Cuisines or RecipeJsonLdGap.Keywords
            or RecipeJsonLdGap.Calories => RecipeJsonLdNeed.Recommended,
        _ => throw new ArgumentOutOfRangeException(nameof(gap), gap, "Not a JSON-LD gap."),
    };
}
