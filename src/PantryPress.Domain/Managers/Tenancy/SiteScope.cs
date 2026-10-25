namespace PantryPress.Domain.Managers.Tenancy;

/// <summary>
/// Which site a kernel writer (the outbox, the audit log) files its record under, said out loud at every call (PP2.4).
/// There is no way to name a site here: <see cref="Current"/> takes the one <see cref="ISiteContext"/> resolved, so a
/// record can never be filed under a site the request does not work on.
/// </summary>
public enum SiteScope
{
    /// <summary>The request's own site; throws when the request has none.</summary>
    Current = 1,

    /// <summary>No site: account and platform work, even inside a site request.</summary>
    None = 2,
}
