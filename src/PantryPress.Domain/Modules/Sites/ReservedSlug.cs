using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites;

/// <summary>
/// A slug no site may take (SITE-002; PP2.8). Platform reference data (ADR 0006): no <c>SiteId</c>, the same rows in every
/// environment, seeded by migrations from <c>Business.ReservedSlugs</c> and never written by the app: only the Sites module
/// and the migrations name it, which a boundary test holds. The slug policy reads that list in .NET; the table is what the
/// database checks a site's slug against, beneath the policy.
/// </summary>
public sealed class ReservedSlug
{
    /// <summary>The stored form: ASCII lower-case.</summary>
    public string Slug { get; init; } = string.Empty;

    public ReservedSlugReason Reason { get; init; }

    /// <summary>The version of the reserved list that added it.</summary>
    public int Since { get; init; }
}
