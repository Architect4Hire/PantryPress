namespace PantryPress.Domain.Managers.Tenancy;

/// <summary>
/// A context that knows its scope's site (PP2.5): the query filter reads it on every query, and the save stamps and
/// checks <see cref="ISiteOwned.SiteId"/> against it. One context per scope, never pooled, so it is always this
/// request's or this job's site.
/// </summary>
internal interface ISiteScopedDbContext
{
    ISiteContext Site { get; }
}
