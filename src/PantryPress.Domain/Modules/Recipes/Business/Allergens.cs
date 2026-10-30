namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// The allergens a blogger can label a recipe with (RCP-006; PP4.1), as platform reference data (PP-D06, ADR 0006): the
/// US FDA's nine major allergens and the EU's fourteen, together. A label appears only when the blogger sets it;
/// PantryPress never infers one from the ingredients. This list is the one source: the <c>Allergens</c> table is seeded
/// from it, so a change here changes the model and needs a reviewed migration. To change it, bump <see cref="Version"/>
/// and give each new entry that version; never remove an entry or change a code, because recipes store the code (PP4.3).
/// </summary>
public static class Allergens
{
    /// <summary>The list's version: the highest <see cref="VocabularyEntry.Since"/> in it.</summary>
    public const int Version = 1;

    public static IReadOnlyList<VocabularyEntry> All { get; } = VocabularyCodes.Entries(
        since: 1,
        ("milk", "Milk"),
        ("eggs", "Eggs"),
        ("fish", "Fish"),
        ("crustacean-shellfish", "Crustacean shellfish"),
        ("mollusks", "Mollusks"),
        ("tree-nuts", "Tree nuts"),
        ("peanuts", "Peanuts"),
        ("wheat", "Wheat"),
        ("gluten", "Gluten"),
        ("soy", "Soy"),
        ("sesame", "Sesame"),
        ("celery", "Celery"),
        ("mustard", "Mustard"),
        ("sulfites", "Sulfites"),
        ("lupin", "Lupin"));
}
