namespace PantryPress.Domain.Modules.Sites.Contracts;

/// <summary>The id of one invitation to join a site (SITE-010; backend.md: strongly typed ids).</summary>
public readonly record struct SiteInvitationId(Guid Value)
{
    public override string ToString() => Value.ToString("D");
}
