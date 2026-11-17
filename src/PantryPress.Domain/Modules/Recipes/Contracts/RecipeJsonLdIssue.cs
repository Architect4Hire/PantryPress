namespace PantryPress.Domain.Modules.Recipes.Contracts;

/// <summary>
/// What a recipe's schema.org JSON-LD lacks (RCP-008; PP4.10), as the missing-fields report names it. Each is a fact about
/// the recipe or its page, never a guess at a fix: the preview (PP4.15) and the publish readiness check (POST-006) turn it
/// into plain language. Written as kebab-case names (api-contract.md), so from the first wire that carries one, renaming a
/// member is a breaking change; a test pins them.
/// </summary>
public enum RecipeJsonLdIssue
{
    /// <summary>No image: Google requires one for a recipe rich result.</summary>
    ImageMissing,

    /// <summary>Nutrition per serving was entered but no yield: Google requires the yield beside it.</summary>
    YieldMissingForNutrition,

    /// <summary>The page names no author.</summary>
    AuthorMissing,

    /// <summary>The page has no publish date.</summary>
    DatePublishedMissing,

    /// <summary>The recipe has no summary to describe it.</summary>
    DescriptionMissing,

    /// <summary>No time was entered, so there is no total.</summary>
    TimesMissing,

    /// <summary>A cook time was entered without a prep time; Google reads them as a pair.</summary>
    PrepTimeMissing,

    /// <summary>A prep time was entered without a cook time; Google reads them as a pair.</summary>
    CookTimeMissing,

    /// <summary>No yield.</summary>
    YieldMissing,

    /// <summary>No ingredient lines.</summary>
    IngredientsMissing,

    /// <summary>No steps.</summary>
    InstructionsMissing,

    /// <summary>No course.</summary>
    CategoryMissing,

    /// <summary>No cuisine.</summary>
    CuisineMissing,

    /// <summary>No keyword, once those repeating the recipe's course or cuisine are left out.</summary>
    KeywordsMissing,

    /// <summary>No calories were entered. PantryPress never estimates them (RCP-006).</summary>
    CaloriesMissing,

    /// <summary>
    /// The recipe has a video address, but nothing yet says the video's title, thumbnail, and upload date, which Google
    /// requires of a video, so it is left out of the JSON-LD.
    /// </summary>
    VideoNotDescribed,
}

/// <summary>How much an issue matters to a recipe rich result.</summary>
public enum RecipeJsonLdSeverity
{
    /// <summary>Without it the recipe is not eligible for a rich result.</summary>
    Required,

    /// <summary>The rich result is weaker without it.</summary>
    Recommended,
}

/// <summary>Each issue's fixed severity, from Google's recipe structured data guidance as checked on 2026-11-14.</summary>
public static class RecipeJsonLdIssues
{
    public static RecipeJsonLdSeverity SeverityOf(RecipeJsonLdIssue issue) => issue switch
    {
        RecipeJsonLdIssue.ImageMissing or RecipeJsonLdIssue.YieldMissingForNutrition => RecipeJsonLdSeverity.Required,
        RecipeJsonLdIssue.AuthorMissing or RecipeJsonLdIssue.DatePublishedMissing or RecipeJsonLdIssue.DescriptionMissing
            or RecipeJsonLdIssue.TimesMissing or RecipeJsonLdIssue.PrepTimeMissing or RecipeJsonLdIssue.CookTimeMissing
            or RecipeJsonLdIssue.YieldMissing or RecipeJsonLdIssue.IngredientsMissing or RecipeJsonLdIssue.InstructionsMissing
            or RecipeJsonLdIssue.CategoryMissing or RecipeJsonLdIssue.CuisineMissing or RecipeJsonLdIssue.KeywordsMissing
            or RecipeJsonLdIssue.CaloriesMissing or RecipeJsonLdIssue.VideoNotDescribed => RecipeJsonLdSeverity.Recommended,
        _ => throw new ArgumentOutOfRangeException(nameof(issue), issue, "Not a JSON-LD issue."),
    };
}
