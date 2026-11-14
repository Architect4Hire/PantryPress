using System.Globalization;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// What a recipe's JSON-LD needs that a published revision does not hold (PP4.10): the page it is on, its images, its
/// author, and when it was published. The page-data build supplies them from the post, its media, and the site (PP7.11);
/// none is ever guessed. Anything absent is left out of the JSON-LD and named in its report. Every address is absolute
/// HTTPS, and the date is UTC: anything else is a caller's bug, and throws.
/// </summary>
internal sealed record RecipeJsonLdContext
{
    public RecipeJsonLdContext(string? url, IReadOnlyList<string> images, RecipeJsonLdAuthor? author, DateTimeOffset? datePublished)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (url is not null)
        {
            RequireHttps(url, nameof(url));
        }

        foreach (var image in images)
        {
            RequireHttps(image, nameof(images));
        }

        if (datePublished is { Offset: var offset } && offset != TimeSpan.Zero)
        {
            throw new ArgumentException("A publish date is UTC.", nameof(datePublished));
        }

        Url = url;
        Images = [.. images];
        Author = author;
        DatePublished = datePublished;
    }

    /// <summary>A context with nothing in it: the JSON-LD a revision alone can make, as a preview would show it.</summary>
    public static RecipeJsonLdContext None { get; } = new(null, [], null, null);

    /// <summary>The page's canonical address.</summary>
    public string? Url { get; }

    /// <summary>The recipe's images, in the order they are given; Google asks for 16x9, 4x3, and 1x1 crops.</summary>
    public IReadOnlyList<string> Images { get; }

    public RecipeJsonLdAuthor? Author { get; }

    /// <summary>When the post was first published, UTC. Written to the second.</summary>
    public DateTimeOffset? DatePublished { get; }

    internal static void RequireHttps(string value, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);

        // Spelled as the page would link it: lower-case "https://", only the characters RFC 3986 allows (so no space,
        // quote, angle bracket, backslash, or brace), and no user name or password.
        if (!value.StartsWith("https://", StringComparison.Ordinal)
            || !value.All(IsUriCharacter)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.UserInfo.Length > 0
            || uri.Host.Length == 0)
        {
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"'{value}' is not an absolute HTTPS address."), name);
        }
    }

    // RFC 3986's unreserved and reserved characters, and "%" for an escape.
    private static bool IsUriCharacter(char c) =>
        c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')
            or '-' or '.' or '_' or '~' or ':' or '/' or '?' or '#' or '[' or ']' or '@' or '!' or '$' or '&' or '\''
            or '(' or ')' or '*' or '+' or ',' or ';' or '=' or '%';
}

/// <summary>The person a recipe is by, as the page names them, and the page about them.</summary>
internal sealed record RecipeJsonLdAuthor
{
    public RecipeJsonLdAuthor(string name, string? url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (url is not null)
        {
            RecipeJsonLdContext.RequireHttps(url, nameof(url));
        }

        Name = name;
        Url = url;
    }

    public string Name { get; }

    public string? Url { get; }
}
