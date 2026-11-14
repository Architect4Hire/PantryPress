using System.Globalization;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// What a recipe's text may hold when it is saved (RCP-002; PP4.4, PP4.11). The blogger's words are stored exactly, never
/// trimmed or changed, so the checks only refuse; they never repair.
/// </summary>
public static class RecipeText
{
    /// <summary>
    /// Whether <paramref name="text"/> is well-formed UTF-16 with no control character but a tab - and, where
    /// <paramref name="multiline"/>, a line break. Half of a surrogate pair is refused, never stored: SqlClient would read it
    /// back as U+FFFD, and the words would no longer be the blogger's (PP4.4).
    /// </summary>
    public static bool IsWellFormed(string text, bool multiline)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                {
                    return false;
                }

                i++;
            }
            else if (char.IsLowSurrogate(c))
            {
                return false;
            }
            else if (char.IsControl(c) && c != '\t' && !(multiline && c is '\r' or '\n'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether <paramref name="text"/> holds nothing a reader could see: only white space - tabs and line breaks included,
    /// which the tables' checks let by, since they trim spaces only - and invisible format characters (a zero-width
    /// space, a byte-order mark), which the ingredient parser reads as white space too (PP4.5).
    /// </summary>
    public static bool IsBlank(string text)
    {
        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format)
            {
                return false;
            }
        }

        return true;
    }
}
