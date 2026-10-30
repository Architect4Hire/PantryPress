namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// The courses a recipe can be filed under (RCP-001; PP4.1), as platform reference data (PP-D06, ADR 0006). This list is
/// the one source: the <c>Courses</c> table is seeded from it, so a change here changes the model and needs a reviewed
/// migration. JSON-LD's <c>recipeCategory</c> is the name (PP4.10). To change it, bump <see cref="Version"/> and give each
/// new entry that version; never remove an entry or change a code, because recipes store the code (PP4.3).
/// </summary>
public static class Courses
{
    /// <summary>The list's version: the highest <see cref="VocabularyEntry.Since"/> in it.</summary>
    public const int Version = 1;

    public static IReadOnlyList<VocabularyEntry> All { get; } = VocabularyCodes.Entries(
        since: 1,
        ("breakfast", "Breakfast"),
        ("brunch", "Brunch"),
        ("appetizer", "Appetizer"),
        ("snack", "Snack"),
        ("soup", "Soup"),
        ("salad", "Salad"),
        ("side-dish", "Side dish"),
        ("main-course", "Main course"),
        ("dessert", "Dessert"),
        ("bread", "Bread"),
        ("drink", "Drink"),
        ("sauce", "Sauce"));
}
