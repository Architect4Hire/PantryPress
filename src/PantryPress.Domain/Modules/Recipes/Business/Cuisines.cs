namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// The cuisines a recipe can be filed under (RCP-001; PP4.1), as platform reference data (PP-D06, ADR 0006). This list is
/// the one source: the <c>Cuisines</c> table is seeded from it, so a change here changes the model and needs a reviewed
/// migration. JSON-LD's <c>recipeCuisine</c> is the name (PP4.10). To change it, bump <see cref="Version"/> and give each
/// new entry that version; never remove an entry or change a code, because recipes store the code (PP4.3).
/// </summary>
public static class Cuisines
{
    /// <summary>The list's version: the highest <see cref="VocabularyEntry.Since"/> in it.</summary>
    public const int Version = 1;

    public static IReadOnlyList<VocabularyEntry> All { get; } = VocabularyCodes.Entries(
        since: 1,
        ("american", "American"),
        ("southern", "Southern"),
        ("cajun-creole", "Cajun and Creole"),
        ("tex-mex", "Tex-Mex"),
        ("mexican", "Mexican"),
        ("caribbean", "Caribbean"),
        ("latin-american", "Latin American"),
        ("british", "British"),
        ("irish", "Irish"),
        ("french", "French"),
        ("italian", "Italian"),
        ("spanish", "Spanish"),
        ("portuguese", "Portuguese"),
        ("german", "German"),
        ("greek", "Greek"),
        ("mediterranean", "Mediterranean"),
        ("middle-eastern", "Middle Eastern"),
        ("north-african", "North African"),
        ("indian", "Indian"),
        ("chinese", "Chinese"),
        ("japanese", "Japanese"),
        ("korean", "Korean"),
        ("thai", "Thai"),
        ("vietnamese", "Vietnamese"),
        ("filipino", "Filipino"));
}
