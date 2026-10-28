using PantryPress.Domain.Managers.Results;

namespace PantryPress.Domain.Modules.Sites.Contracts;

/// <summary>The Sites module's expected failures. Codes are stable (api-contract.md); titles are what a blogger reads.</summary>
public static class SiteErrors
{
    /// <summary>
    /// No such site, or one the caller is not a member of: the two are one answer (tenancy.md rule 4), so a slug never
    /// tells a stranger whether a site exists.
    /// </summary>
    public static readonly Error SiteNotFound = new(
        ErrorKind.NotFound,
        "site-not-found",
        "We couldn't find that site.");

    /// <summary>A member whose role on the site does not reach what the route needs.</summary>
    public static readonly Error SiteRoleRequired = new(
        ErrorKind.Forbidden,
        "site-role-required",
        "Your role on this site doesn't include this. Ask the site's owner if you need it.");

    /// <summary>
    /// A slug no site may take: reserved (PP2.8), or held by another site (PP2.10). The two are one answer, so availability
    /// never says which, let alone who holds it (SITE-002).
    /// </summary>
    public static readonly Error SlugUnavailable = new(
        ErrorKind.Conflict,
        "slug-unavailable",
        "That address isn't available. Try another.");

    /// <summary>The change would leave the site with no active owner (SITE-010; PP2.3).</summary>
    public static readonly Error LastOwner = new(
        ErrorKind.Conflict,
        "last-owner",
        "A site needs at least one owner. Make someone else an owner first.");

    /// <summary>
    /// No member of this site has that id (PP2.11b): unknown, not an id at all, or a member of another site only - one
    /// answer, so an id says nothing about anyone outside the site.
    /// </summary>
    public static readonly Error MemberNotFound = new(
        ErrorKind.NotFound,
        "member-not-found",
        "We couldn't find that member on this site. They may already have been removed.");

    /// <summary>Someone else changed the same member at the same moment; nothing was changed by this request.</summary>
    public static readonly Error MemberChanged = new(
        ErrorKind.Conflict,
        "member-changed",
        "Someone else changed this member just now. Check the member list and try again.");

    /// <summary>
    /// An invitation link that cannot be used: not one of ours, expired, already used, replaced by a newer invitation, or
    /// for a site that was deleted. One answer for all of them (PP2.11), so a link says nothing about the site it names.
    /// </summary>
    public static readonly Error InvitationInvalid = new(
        ErrorKind.Rejected,
        "invitation-invalid",
        "This invitation has expired or was already used. Ask the site's owner to invite you again.");

    /// <summary>
    /// The invitation went to another address than the caller's. It never says which: the invited person reads that inbox.
    /// </summary>
    public static readonly Error InvitationForOtherAddress = new(
        ErrorKind.Forbidden,
        "invitation-other-address",
        "This invitation was sent to a different email address. Sign in with that address to accept it.");

    /// <summary>The caller already belongs to the invitation's site; an invitation never changes a membership.</summary>
    public static readonly Error AlreadyMember = new(
        ErrorKind.Conflict,
        "already-member",
        "You're already a member of this site.");

    /// <summary>The site already has as many open invitations as it may (<c>Sites:MaxOpenInvitations</c>).</summary>
    public static readonly Error TooManyInvitations = new(
        ErrorKind.Conflict,
        "too-many-invitations",
        "This site has as many open invitations as it can. Wait for some to be accepted or to expire, then try again.");

    // The public answers for a site that is not open to readers (SITE-008, SEO-007; PP2.13). Each is one of the neutral
    // problem documents the public tier writes, never the site's name or anything else about it, and a status that is a
    // private matter - an operator's suspension, a failed payment - shares its answer with another, so a reader cannot
    // tell which it was.

    /// <summary>A site being set up: there is nothing to read yet, and soon there will be.</summary>
    public static readonly Error SiteComingSoon = new(
        ErrorKind.Unavailable,
        "site-coming-soon",
        "This site is getting ready. Please check back soon.")
    {
        RetryAfter = TimeSpan.FromSeconds(30),
    };

    /// <summary>
    /// A site closed for now: suspended by an operator, or offline after a failed payment. One answer for both, since why a
    /// site is closed is between its owner and the platform.
    /// </summary>
    public static readonly Error SiteUnavailable = new(
        ErrorKind.Unavailable,
        "site-unavailable",
        "This site isn't available right now. Please check back later.")
    {
        RetryAfter = TimeSpan.FromHours(1),
    };

    /// <summary>A site its owner deleted. Its address is never given to another site (SITE-002).</summary>
    public static readonly Error SiteGone = new(
        ErrorKind.Gone,
        "site-gone",
        "This site is no longer here.");
}
