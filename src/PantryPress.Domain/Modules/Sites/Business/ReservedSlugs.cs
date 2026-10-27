using System.Collections.Frozen;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites.Business;

/// <summary>One reserved slug, the reason it is held back, and the version of the list that added it.</summary>
public sealed record ReservedSlugEntry(string Slug, ReservedSlugReason Reason, int Since);

/// <summary>
/// The slugs no site may take (SITE-002; PP2.8), as platform reference data (ADR 0006). This list is the one source: the
/// slug policy reads it, and the <c>ReservedSlugs</c> table is seeded from it, so a change here changes the model and needs
/// a reviewed migration - and that migration refuses to reserve a slug a site already holds. To change it, bump
/// <see cref="Version"/> and give each new entry that version as its <c>Since</c>; never remove an entry a site might
/// then take without deciding that on purpose.
/// </summary>
public static class ReservedSlugs
{
    /// <summary>The list's version: the highest <see cref="ReservedSlugEntry.Since"/> in it.</summary>
    public const int Version = 1;

    public static IReadOnlyList<ReservedSlugEntry> All { get; } =
    [
        // Hosts PantryPress answers on or holds DNS records under. "app" is the admin app (gateway.md); "dev" is the dev
        // environment's zone, delegated under the production one (docs/infrastructure); "mail" sends account email.
        .. Entries(
            ReservedSlugReason.PlatformHost,
            "app", "api", "www", "dev", "mail", "status", "admin", "staging", "preview", "prod", "sandbox", "beta"),
        // Labels the internet's plumbing looks for under a domain: mail discovery and policy (autodiscover, mta-sts,
        // openpgpkey), name servers, and proxy discovery (wpad, isatap).
        .. Entries(
            ReservedSlugReason.Infrastructure,
            "email", "smtp", "imap", "pop3", "webmail", "ns1", "ns2", "ns3", "ns4", "dns", "autodiscover", "autoconfig",
            "cdn", "static", "assets", "media", "img", "images", "files", "uploads", "ftp", "sftp", "localhost", "wpad",
            "isatap", "vpn", "proxy", "origin", "edge", "cache", "health", "mta-sts", "openpgpkey"),
        .. Entries(
            ReservedSlugReason.Impersonation,
            "pantrypress", "pantry-press", "creatorpantry", "creator-pantry", "support", "help", "helpdesk", "billing",
            "payments", "checkout", "security", "login", "signin", "sign-in", "signup", "sign-up", "register", "logout",
            "account", "accounts", "auth", "oauth", "sso", "password", "verify", "abuse", "postmaster", "hostmaster",
            "webmaster", "root", "administrator", "noreply", "no-reply", "official", "team", "staff", "moderator",
            "operator", "system", "platform"),
        .. Entries(
            ReservedSlugReason.Product,
            "blog", "docs", "pricing", "about", "contact", "legal", "terms", "privacy", "cookies", "press", "careers",
            "jobs", "news", "community", "forum", "shop", "store", "developer", "developers", "publish", "import",
            "dashboard", "studio", "settings"),
    ];

    private static readonly FrozenSet<string> Slugs = All.Select(e => e.Slug).ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// True when <paramref name="slug"/>, in its stored form (ASCII lower-case), is reserved. Internal, because any other
    /// form reads as not reserved: callers ask <see cref="SlugPolicy.Check"/>, which normalizes first.
    /// </summary>
    internal static bool IsReserved(string slug) => Slugs.Contains(slug);

    private static IEnumerable<ReservedSlugEntry> Entries(ReservedSlugReason reason, params string[] slugs) =>
        slugs.Select(slug => new ReservedSlugEntry(slug, reason, Since: 1));
}
