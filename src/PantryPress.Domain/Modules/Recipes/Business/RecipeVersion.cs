namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// A recipe's version as the API carries it (PP4.11): its row version, eight bytes, in standard Base64, in its one
/// spelling. The one writer and reader, so what an answer gives is exactly what a save is read by.
/// </summary>
public static class RecipeVersion
{
    private const int TextLength = 12;

    public static string Write(byte[] version) => Convert.ToBase64String(version);

    public static bool TryRead(string? version, out byte[] bytes)
    {
        bytes = [];
        if (version is not { Length: TextLength })
        {
            return false;
        }

        var buffer = new byte[RecipeRevisionRules.VersionLength];
        if (!Convert.TryFromBase64String(version, buffer, out var written) || written != RecipeRevisionRules.VersionLength
            || !string.Equals(Write(buffer), version, StringComparison.Ordinal))
        {
            return false;
        }

        bytes = buffer;
        return true;
    }
}
