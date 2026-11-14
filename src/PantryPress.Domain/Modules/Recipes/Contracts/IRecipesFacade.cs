using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Contracts;

/// <summary>
/// A site's recipes as its members edit them (RCP-001, RCP-007; PP4.11): create, read, list, replace the working copy,
/// archive, restore, and duplicate. Works on the request's site, which the route's site policy entered (tenancy.md rule
/// 4): a recipe of any other site is no recipe here, one answer with an unknown id.
/// <para>
/// The recipe tables are the working copy, the draft a blogger edits (PP4.4). Nothing here touches a revision: what readers
/// see changes only when a recipe is published (PP5.14), so saving a published recipe's working copy never changes it.
/// Every write takes the recipe's row lock first and moves the recipe's row, so its version guards the whole recipe and a
/// freeze never captures half of an edit.
/// </para>
/// <para>
/// Who may change a recipe (SITE-010): an Owner or Editor any of the site's recipes, an Author only one they created. Any
/// member may read, list, and duplicate every recipe of the site; a duplicate is the caller's own.
/// </para>
/// </summary>
public interface IRecipesFacade
{
    /// <summary>
    /// Creates a recipe from <paramref name="content"/>, made by <paramref name="account"/>. Each ingredient line is read
    /// again on the server (PP4.5); nothing parsed is taken from the caller. Otherwise a validation error.
    /// </summary>
    Task<Result<RecipeDetail>> CreateAsync(
        AccountId account,
        string siteSlug,
        RecipeContent content,
        CancellationToken cancellationToken);

    /// <summary>The recipe, archived or not, or <see cref="RecipeErrors.RecipeNotFound"/>.</summary>
    Task<Result<RecipeDetail>> GetAsync(AccountId account, string siteSlug, RecipeId recipe, CancellationToken cancellationToken);

    /// <summary>
    /// The site's recipes, most recently edited first, a page at a time: those in the library, or the archived ones, narrowed
    /// by words in the title and by a course, a cuisine, and a diet, all at once (PP4.12a).
    /// </summary>
    Task<Result<RecipeSummaryPage>> ListAsync(
        AccountId account,
        string siteSlug,
        ListRecipesQuery query,
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the recipe's working copy with <see cref="UpdateRecipeRequest.Content"/>, if the caller may change it, it is
    /// not archived, and it is still the version the caller read. When the content is exactly what the recipe already holds
    /// nothing is written, and the recipe comes back with its current version, so a retried save is safe and repeated
    /// autosaves do not move the version. Otherwise <see cref="RecipeErrors.RecipeNotFound"/>,
    /// <see cref="RecipeErrors.RecipeNotYours"/>, <see cref="RecipeErrors.RecipeArchived"/>,
    /// <see cref="RecipeErrors.RecipeChanged"/>, or a validation error - a part id that is not this recipe's included.
    /// Nothing is saved on any of them.
    /// </summary>
    Task<Result<RecipeDetail>> UpdateAsync(
        AccountId account,
        string siteSlug,
        RecipeId recipe,
        UpdateRecipeRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Takes the recipe out of the library. Archiving an archived recipe changes nothing. What readers see is unchanged.
    /// </summary>
    Task<Result> ArchiveAsync(AccountId account, string siteSlug, RecipeId recipe, CancellationToken cancellationToken);

    /// <summary>Puts an archived recipe back in the library. Restoring one already there changes nothing.</summary>
    Task<Result> RestoreAsync(AccountId account, string siteSlug, RecipeId recipe, CancellationToken cancellationToken);

    /// <summary>
    /// A new recipe with the working copy of <paramref name="recipe"/>, made by <paramref name="account"/>, in the
    /// library, titled "Copy of …" where that fits. Its parts get new ids; its revisions are not copied.
    /// </summary>
    Task<Result<RecipeDetail>> DuplicateAsync(
        AccountId account,
        string siteSlug,
        RecipeId recipe,
        CancellationToken cancellationToken);

    /// <summary>
    /// The words a recipe is filed under (PP4.1, PP4.12a): every course, cuisine, diet, and allergen, by code and name, in
    /// the vocabulary's own order. Platform reference data: the same for every site, and nothing of any site's.
    /// </summary>
    RecipeVocabulary GetVocabulary();
}

/// <summary>An exact amount, never floating point (PP-D05): a fraction with both terms from 1 to 1,000,000.</summary>
/// <param name="Numerator">The numerator, 1 to 1,000,000.</param>
/// <param name="Denominator">The denominator, 1 to 1,000,000. A fraction is kept in lowest terms.</param>
public sealed record RecipeFraction(long Numerator, long Denominator);

/// <summary>An amount or a range.</summary>
/// <param name="Kind">An amount or a range; a yield is never to taste.</param>
/// <param name="Low">The amount, or a range's low end.</param>
/// <param name="High">A range's high end, above its low end; none for an amount.</param>
public sealed record RecipeQuantity(QuantityKind Kind, RecipeFraction? Low, RecipeFraction? High);

/// <summary>Where a part is in a line, in UTF-16 code units, as both .NET and JavaScript index a string.</summary>
/// <param name="Start">The first code unit.</param>
/// <param name="Length">How many code units.</param>
public sealed record RecipeTextSpan(int Start, int Length);

/// <summary>Nutrition per serving, exactly as the blogger entered it (RCP-006): never estimated, derived, or rounded.</summary>
/// <param name="ServingSize">The serving it is for, in the blogger's words ("1 slice"), up to 60 characters.</param>
/// <param name="Calories">Kilocalories. Each amount is 0 to 9,999,999.99, with at most two decimal places.</param>
/// <param name="FatGrams">Total fat, in grams.</param>
/// <param name="SaturatedFatGrams">Saturated fat, in grams.</param>
/// <param name="TransFatGrams">Trans fat, in grams.</param>
/// <param name="UnsaturatedFatGrams">Unsaturated fat, in grams.</param>
/// <param name="CholesterolMilligrams">Cholesterol, in milligrams.</param>
/// <param name="SodiumMilligrams">Sodium, in milligrams.</param>
/// <param name="CarbohydrateGrams">Carbohydrate, in grams.</param>
/// <param name="FiberGrams">Fiber, in grams.</param>
/// <param name="SugarGrams">Sugar, in grams.</param>
/// <param name="ProteinGrams">Protein, in grams.</param>
public sealed record RecipeNutrition(
    string? ServingSize,
    decimal? Calories,
    decimal? FatGrams,
    decimal? SaturatedFatGrams,
    decimal? TransFatGrams,
    decimal? UnsaturatedFatGrams,
    decimal? CholesterolMilligrams,
    decimal? SodiumMilligrams,
    decimal? CarbohydrateGrams,
    decimal? FiberGrams,
    decimal? SugarGrams,
    decimal? ProteinGrams);

/// <summary>
/// A whole recipe as the editor saves it (RCP-001): every field and every part, in order. What is left out is cleared.
/// </summary>
/// <param name="Title">Required, up to 200 characters, on one line.</param>
/// <param name="Summary">Plain text, up to 1,000 characters.</param>
/// <param name="Yield">How much the recipe makes: an amount or a range.</param>
/// <param name="YieldUnit">The blogger's word for what the yield counts ("servings"), up to 40 characters; only with a yield.</param>
/// <param name="PrepMinutes">Whole minutes, 0 to 43,200. Zero is a time entered; none is no time.</param>
/// <param name="CookMinutes">Whole minutes, 0 to 43,200.</param>
/// <param name="AdditionalMinutes">Whole minutes, 0 to 43,200.</param>
/// <param name="AdditionalTimeLabel">What the additional time is for ("Chill"), up to 30 characters; only with that time.</param>
/// <param name="VideoUrl">The recipe's video on YouTube or Vimeo: an https:// address, up to 2,048 characters.</param>
/// <param name="Nutrition">Nutrition per serving; none for none.</param>
/// <param name="IngredientGroups">Up to 20 groups and 200 lines in all, in order.</param>
/// <param name="StepGroups">Up to 20 groups and 100 steps in all, in order.</param>
/// <param name="Equipment">Up to 30 items, each up to 200 characters, in order.</param>
/// <param name="Notes">Up to 20 notes, each up to 2,000 characters, in order.</param>
/// <param name="Keywords">Up to 20 keywords, each up to 60 characters, in order.</param>
/// <param name="Courses">Up to 5 course codes from the vocabulary, each once, in order.</param>
/// <param name="Cuisines">Up to 5 cuisine codes, each once, in order.</param>
/// <param name="Diets">Diet codes the blogger chose, each once; never inferred (RCP-006).</param>
/// <param name="Allergens">Allergen codes the blogger chose, each once; never inferred (RCP-006).</param>
public sealed record RecipeContent(
    string? Title,
    string? Summary,
    RecipeQuantity? Yield,
    string? YieldUnit,
    int? PrepMinutes,
    int? CookMinutes,
    int? AdditionalMinutes,
    string? AdditionalTimeLabel,
    string? VideoUrl,
    RecipeNutrition? Nutrition,
    IReadOnlyList<RecipeIngredientGroupInput?>? IngredientGroups,
    IReadOnlyList<RecipeStepGroupInput?>? StepGroups,
    IReadOnlyList<string?>? Equipment,
    IReadOnlyList<string?>? Notes,
    IReadOnlyList<string?>? Keywords,
    IReadOnlyList<string?>? Courses,
    IReadOnlyList<string?>? Cuisines,
    IReadOnlyList<string?>? Diets,
    IReadOnlyList<string?>? Allergens);

/// <summary>One ingredient group as the editor saves it.</summary>
/// <param name="Id">The group's id as the recipe gave it, to keep it; none for a new group.</param>
/// <param name="Heading">An optional heading ("For the crust"), up to 100 characters, on one line.</param>
/// <param name="Lines">The group's lines in order; a group may have none yet.</param>
public sealed record RecipeIngredientGroupInput(Guid? Id, string? Heading, IReadOnlyList<RecipeIngredientLineInput?>? Lines);

/// <summary>One ingredient line as the editor saves it: the blogger's words, and nothing parsed (RCP-002).</summary>
/// <param name="Id">The line's id as the recipe gave it, to keep it; none for a new line.</param>
/// <param name="OriginalText">
/// Required: the line exactly as written, up to 500 characters, on one line. It is stored as sent, never trimmed, and read
/// again on the server.
/// </param>
public sealed record RecipeIngredientLineInput(Guid? Id, string? OriginalText);

/// <summary>One step group as the editor saves it.</summary>
/// <param name="Id">The group's id as the recipe gave it, to keep it; none for a new group.</param>
/// <param name="Heading">An optional heading ("Make the filling"), up to 100 characters, on one line.</param>
/// <param name="Steps">The group's steps in order; a group may have none yet.</param>
public sealed record RecipeStepGroupInput(Guid? Id, string? Heading, IReadOnlyList<RecipeStepInput?>? Steps);

/// <summary>One step as the editor saves it.</summary>
/// <param name="Id">The step's id as the recipe gave it, to keep it; none for a new step.</param>
/// <param name="Text">Required: the step in the blogger's words, up to 2,000 characters, stored as sent.</param>
public sealed record RecipeStepInput(Guid? Id, string? Text);

/// <summary>Replace a recipe's working copy.</summary>
/// <param name="Version">Required: the recipe's version exactly as it was read.</param>
/// <param name="Content">Required: the whole recipe.</param>
public sealed record UpdateRecipeRequest(string? Version, RecipeContent? Content);

/// <summary>Which recipes to list, and where the page starts.</summary>
/// <param name="Limit">How many at most, 1 to 100; none means 50.</param>
/// <param name="Cursor">
/// The <c>nextCursor</c> of the page before, as given, with the same search and filters; none for the first page.
/// </param>
/// <param name="Archived">True for the archived recipes; none or false for the library.</param>
/// <param name="Search">
/// Text the title contains, as typed, ignoring case and accents, up to 100 characters on one line once the white space at its ends is
/// left off; none, or only white space, for any title.
/// </param>
/// <param name="CourseCode">Only recipes filed under this course code; none for any.</param>
/// <param name="CuisineCode">Only recipes filed under this cuisine code; none for any.</param>
/// <param name="DietCode">Only recipes labeled with this diet code; none for any.</param>
public sealed record ListRecipesQuery(
    int? Limit,
    string? Cursor,
    bool? Archived,
    string? Search = null,
    string? CourseCode = null,
    string? CuisineCode = null,
    string? DietCode = null);

/// <summary>A page of recipes.</summary>
/// <param name="Items">The recipes, most recently edited first.</param>
/// <param name="NextCursor">The cursor of the next page; none on the last.</param>
public sealed record RecipeSummaryPage(IReadOnlyList<RecipeSummary> Items, string? NextCursor);

/// <summary>One recipe in a list.</summary>
/// <param name="Id">The recipe's id.</param>
/// <param name="Title">Its title.</param>
/// <param name="UpdatedAt">When its content last changed (UTC).</param>
/// <param name="ArchivedAt">When it was archived (UTC); none while it is in the library.</param>
/// <param name="CanEdit">Whether the caller may change, archive, and restore it.</param>
public sealed record RecipeSummary(Guid Id, string Title, DateTimeOffset UpdatedAt, DateTimeOffset? ArchivedAt, bool CanEdit);

/// <summary>A recipe's working copy, whole.</summary>
/// <param name="Id">The recipe's id.</param>
/// <param name="Version">Its version, to send back unchanged with the next save.</param>
/// <param name="Title">Its title.</param>
/// <param name="Summary">Its summary.</param>
/// <param name="Yield">How much it makes, in lowest terms.</param>
/// <param name="YieldUnit">The blogger's word for what the yield counts.</param>
/// <param name="PrepMinutes">Prep time as entered.</param>
/// <param name="CookMinutes">Cook time as entered.</param>
/// <param name="AdditionalMinutes">Additional time as entered.</param>
/// <param name="AdditionalTimeLabel">What the additional time is for.</param>
/// <param name="VideoUrl">The video's address.</param>
/// <param name="Nutrition">Nutrition per serving as entered; none when nothing is.</param>
/// <param name="IngredientGroups">The ingredient groups and their lines, in order.</param>
/// <param name="StepGroups">The step groups and their steps, in order.</param>
/// <param name="Equipment">Equipment, in order.</param>
/// <param name="Notes">Notes, in order.</param>
/// <param name="Keywords">Keywords, in order.</param>
/// <param name="Courses">Course codes, in order.</param>
/// <param name="Cuisines">Cuisine codes, in order.</param>
/// <param name="Diets">Diet codes, in order.</param>
/// <param name="Allergens">Allergen codes, in order.</param>
/// <param name="CreatedAt">When it was created (UTC).</param>
/// <param name="UpdatedAt">When its content last changed (UTC).</param>
/// <param name="ArchivedAt">When it was archived (UTC); none while it is in the library.</param>
/// <param name="CanEdit">Whether the caller may change, archive, and restore it.</param>
public sealed record RecipeDetail(
    Guid Id,
    string Version,
    string Title,
    string? Summary,
    RecipeQuantity? Yield,
    string? YieldUnit,
    int? PrepMinutes,
    int? CookMinutes,
    int? AdditionalMinutes,
    string? AdditionalTimeLabel,
    string? VideoUrl,
    RecipeNutrition? Nutrition,
    IReadOnlyList<RecipeIngredientGroupDetail> IngredientGroups,
    IReadOnlyList<RecipeStepGroupDetail> StepGroups,
    IReadOnlyList<string> Equipment,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Courses,
    IReadOnlyList<string> Cuisines,
    IReadOnlyList<string> Diets,
    IReadOnlyList<string> Allergens,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt,
    bool CanEdit);

/// <summary>One ingredient group.</summary>
/// <param name="Id">The group's id, to send back to keep it.</param>
/// <param name="Heading">Its heading.</param>
/// <param name="Lines">Its lines, in order.</param>
public sealed record RecipeIngredientGroupDetail(Guid Id, string? Heading, IReadOnlyList<RecipeIngredientLineDetail> Lines);

/// <summary>One ingredient line: the blogger's words, and what the server read from them (RCP-002).</summary>
/// <param name="Id">The line's id, to send back to keep it.</param>
/// <param name="OriginalText">The line exactly as written, which is what a reader sees.</param>
/// <param name="Status">Parsed, or unparsed: kept as written and flagged.</param>
/// <param name="Problem">Why an unparsed line could not be read; none for a parsed one.</param>
/// <param name="Quantity">The amount, range, or to taste; none when the line has none or is unparsed.</param>
/// <param name="QuantitySpan">Where the quantity is written in <paramref name="OriginalText"/>.</param>
/// <param name="UnitCode">The unit's vocabulary code.</param>
/// <param name="UnitSpan">Where the unit is written in <paramref name="OriginalText"/>.</param>
/// <param name="Item">What the line names, cut from it as written.</param>
/// <param name="Note">The line's other pieces, each as written, joined by "; ".</param>
public sealed record RecipeIngredientLineDetail(
    Guid Id,
    string OriginalText,
    IngredientParseStatus Status,
    IngredientParseProblem? Problem,
    RecipeQuantity? Quantity,
    RecipeTextSpan? QuantitySpan,
    string? UnitCode,
    RecipeTextSpan? UnitSpan,
    string? Item,
    string? Note);

/// <summary>One step group.</summary>
/// <param name="Id">The group's id, to send back to keep it.</param>
/// <param name="Heading">Its heading.</param>
/// <param name="Steps">Its steps, in order.</param>
public sealed record RecipeStepGroupDetail(Guid Id, string? Heading, IReadOnlyList<RecipeStepDetail> Steps);

/// <summary>One step.</summary>
/// <param name="Id">The step's id, to send back to keep it.</param>
/// <param name="Text">The step in the blogger's words.</param>
public sealed record RecipeStepDetail(Guid Id, string Text);

/// <summary>One word of the recipe vocabulary.</summary>
/// <param name="Code">Its stable code, which a recipe stores and the API sends; it never changes.</param>
/// <param name="Name">What it is called, for display.</param>
public sealed record RecipeVocabularyWord(string Code, string Name);

/// <summary>The words a recipe is filed under, each list in the vocabulary's own order (PP4.1).</summary>
/// <param name="Courses">Every course.</param>
/// <param name="Cuisines">Every cuisine.</param>
/// <param name="Diets">Every diet, which a blogger sets and PantryPress never infers (RCP-006).</param>
/// <param name="Allergens">Every allergen, which a blogger sets and PantryPress never infers (RCP-006).</param>
public sealed record RecipeVocabulary(
    IReadOnlyList<RecipeVocabularyWord> Courses,
    IReadOnlyList<RecipeVocabularyWord> Cuisines,
    IReadOnlyList<RecipeVocabularyWord> Diets,
    IReadOnlyList<RecipeVocabularyWord> Allergens);
