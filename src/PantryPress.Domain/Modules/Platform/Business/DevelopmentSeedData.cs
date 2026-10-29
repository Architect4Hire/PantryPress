using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Platform.Business;

/// <summary>
/// What the local development seed makes (PP3.9a): fixed, deterministic values, so a developer's machine always starts
/// the same way and a second run has nothing left to do. Approved at PP3.9a.
/// <para>
/// The addresses sit under <c>.test</c>, the reserved TLD that resolves nowhere (RFC 2606), so neither can ever be a
/// real mailbox: no person's address appears here or anywhere else in the seed. Both are accepted by sign-up's own
/// validator and the disposable-domain check, which a test pins.
/// </para>
/// </summary>
internal static class DevelopmentSeedData
{
    /// <summary>The account that owns the seeded sites, and the one a developer signs in as.</summary>
    public const string OwnerEmail = "owner@pantrypress.test";

    /// <summary>A second account, a member of no site, for trying an invitation from the owner's side and the other.</summary>
    public const string EditorEmail = "editor@pantrypress.test";

    /// <summary>Both accounts, in the order the seed makes them.</summary>
    public static IReadOnlyList<string> Emails { get; } = [OwnerEmail, EditorEmail];

    /// <summary>
    /// The sites the owner gets. Locally the zone is <c>localhost</c>, so they answer at <c>apple-pie.localhost</c> and
    /// <c>plum-tart.localhost</c> (SITE-009).
    /// </summary>
    /// <remarks>
    /// Not named <c>Sites</c>: a member access after it would read as this module reaching a type at the Sites module's
    /// root, which <c>ModuleBoundaryTests</c> scans the source for.
    /// </remarks>
    public static IReadOnlyList<CreateSiteRequest> SiteRequests { get; } =
    [
        new("apple-pie", "Apple Pie", "Baking notes from a very small kitchen", TimeZone, Locale),
        new("plum-tart", "Plum Tart", "Second site, for trying two of them", TimeZone, Locale),
    ];

    private const string TimeZone = "America/Chicago";

    private const string Locale = "en-US";
}
