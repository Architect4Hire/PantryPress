namespace PantryPress.Domain.Modules.Sites.Contracts;

/// <summary>The sizes a site's fields are stored at, shared by the schema and the validators that come with its routes.</summary>
public static class SiteLimits
{
    /// <summary>SITE-002: 3 to 40 lower-case letters, digits, and hyphens.</summary>
    public const int MinSlugLength = 3;

    public const int MaxSlugLength = 40;

    /// <summary>Customization spec, <c>brand.siteName</c>: 1 to 60 characters.</summary>
    public const int MaxNameLength = 60;

    /// <summary>Customization spec, <c>brand.tagline</c>: up to 120 characters; empty hides it.</summary>
    public const int MaxTaglineLength = 120;

    /// <summary>An IANA time zone id ("America/Chicago"); the longest in the database is 32 characters.</summary>
    public const int MaxTimeZoneLength = 64;

    /// <summary>A BCP 47 language tag ("en-US"); RFC 5646 asks that 35 characters be supported.</summary>
    public const int MaxLocaleLength = 35;
}
