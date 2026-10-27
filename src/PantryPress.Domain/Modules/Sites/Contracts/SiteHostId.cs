namespace PantryPress.Domain.Modules.Sites.Contracts;

/// <summary>The id of one host a site answers on (SITE-005; backend.md: strongly typed ids).</summary>
public readonly record struct SiteHostId(Guid Value)
{
    public override string ToString() => Value.ToString("D");
}
