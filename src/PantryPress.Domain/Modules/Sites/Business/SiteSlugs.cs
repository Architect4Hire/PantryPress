using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites.Business;

/// <summary>
/// The shape of a slug, for looking one up (SITE-002). Only the shape: what a site may take - a host name's rules and the
/// reserved list - is <see cref="SlugPolicy"/>'s. A slug the policy refuses simply holds no site.
/// </summary>
public static class SiteSlugs
{
    /// <summary>
    /// The stored form of <paramref name="value"/> - ASCII lower-case - when it has a slug's shape. Checked here, in
    /// .NET, before any query: the column is <c>varchar</c>, and the client's code page conversion could turn a look-alike
    /// letter into a valid one (tenancy.md, "Tenant root").
    /// </summary>
    public static bool TryNormalize(string? value, out string slug)
    {
        slug = string.Empty;
        if (value is null || value.Length is < SiteLimits.MinSlugLength or > SiteLimits.MaxSlugLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-'))
            {
                return false;
            }
        }

        slug = value.ToLowerInvariant();
        return true;
    }
}
