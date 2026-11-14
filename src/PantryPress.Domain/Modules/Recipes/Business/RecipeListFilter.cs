using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// Which of a site's recipes a list holds (PP4.11, PP4.12a): the library or the archived ones, narrowed by text in the title
/// and by one course, one cuisine, and one diet, all at once. The search is the caller's text with the white space at its
/// ends left off, and none when nothing is left; the codes are the vocabulary's, checked before a filter is made. How the
/// search is matched in the database is the repository's.
/// </summary>
public sealed record RecipeListFilter
{
    /// <summary>The longest search, in UTF-16 code units, after its ends are trimmed.</summary>
    public const int MaxSearchLength = 100;

    private RecipeListFilter(bool archived, string? search, string? course, string? cuisine, string? diet)
    {
        Archived = archived;
        Search = search;
        Course = course;
        Cuisine = cuisine;
        Diet = diet;
    }

    /// <summary>The library, unnarrowed.</summary>
    public static RecipeListFilter Library { get; } = new(false, null, null, null, null);

    public bool Archived { get; }

    /// <summary>Text the title contains, as typed, compared ignoring case and accents; none for any title.</summary>
    public string? Search { get; }

    public string? Course { get; }

    public string? Cuisine { get; }

    public string? Diet { get; }

    /// <summary>The filter a query asks for. Its search is trimmed; a blank one, or one of only white space, is none.</summary>
    public static RecipeListFilter From(ListRecipesQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new(query.Archived ?? false, NormalizeSearch(query.Search), Blank(query.CourseCode), Blank(query.CuisineCode), Blank(query.DietCode));
    }

    /// <summary>The search a list uses for <paramref name="search"/>: trimmed, and none when nothing is left.</summary>
    public static string? NormalizeSearch(string? search) => search?.Trim() is { Length: > 0 } trimmed ? trimmed : null;

    /// <summary>
    /// Eight bytes that tell this filter from any other, so a cursor made under one is never read under another and a page
    /// never continues a different list. Not a secret: it only has to differ. The search is compared as sent, so a cursor
    /// asks for the same spelling, case included.
    /// </summary>
    internal ulong Fingerprint()
    {
        var text = new StringBuilder()
            .Append(Archived ? '1' : '0')
            .Append('\0').Append(Search?.Length ?? -1).Append(':').Append(Search)
            .Append('\0').Append(Course)
            .Append('\0').Append(Cuisine)
            .Append('\0').Append(Diet)
            .ToString();
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(text), hash);
        return BinaryPrimitives.ReadUInt64BigEndian(hash);
    }

    private static string? Blank(string? code) => string.IsNullOrEmpty(code) ? null : code;
}
