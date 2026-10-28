namespace PantryPress.Domain.Modules.Sites.Contracts;

// Request and response records for invitations (api-contract.md: records in Contracts/, never entities). Values arrive
// straight from JSON, so validators treat every property as possibly null. No record here carries a site id: the site an
// invitation is for is named by the route's slug when it is made, and sealed inside its link (tenancy.md rule 7).

/// <summary>Invite someone to the site by email, with the role they will have once they accept (SITE-010; PP2.11).</summary>
/// <param name="Email">Where the invitation goes. Whoever accepts must be signed in with this address, confirmed.</param>
/// <param name="Role">The role they join with: <c>owner</c>, <c>editor</c>, or <c>author</c>. Required.</param>
public sealed record InviteMemberRequest(string Email, SiteRole? Role);

/// <summary>
/// An invitation as the Owner who sent it sees it. Not the address: the Owner just typed it, and an idempotent answer is
/// stored to be replayed, where no one's address belongs (PSEC-005).
/// </summary>
/// <param name="ExpiresAt">When the link stops working if nobody has used it.</param>
public sealed record SiteInvitationDetail(Guid Id, SiteRole Role, DateTimeOffset ExpiresAt);

/// <summary>Accept an invitation, from the link in its email.</summary>
/// <param name="Token">
/// The token from the invitation link: the value of <c>token</c> in its fragment (<c>/dashboard/join#token=…</c>),
/// percent-decoded, and otherwise exactly as given.
/// </param>
public sealed record AcceptInvitationRequest(string Token);
