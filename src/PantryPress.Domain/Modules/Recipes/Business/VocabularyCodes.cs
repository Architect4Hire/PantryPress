namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>One word of a recipe vocabulary list: its stable code, its display name, and the list version that added it.</summary>
public sealed record VocabularyEntry(string Code, string Name, int Since);

/// <summary>
/// The shape every recipe vocabulary code has (PP4.1): 2 to 40 ASCII lower-case letters, digits, and single hyphens, none
/// at either end - the kebab-case the API speaks. A code is what a recipe stores and what the API sends, so it never
/// changes once seeded; the name is display text and may be corrected by a migration. The tables check the same shape.
/// </summary>
public static class VocabularyCodes
{
    public const int MinLength = 2;
    public const int MaxLength = 40;
    public const int NameMaxLength = 60;

    public static bool IsWellFormed(string code) =>
        code.Length is >= MinLength and <= MaxLength
        && code.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')
        && !code.StartsWith('-')
        && !code.EndsWith('-')
        && !code.Contains("--", StringComparison.Ordinal);

    internal static IReadOnlyList<VocabularyEntry> Entries(int since, params (string Code, string Name)[] words) =>
        [.. words.Select(w => new VocabularyEntry(w.Code, w.Name, since))];
}
