using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PantryPress.Domain.Managers.Tenancy;

/// <summary>
/// Stamps and guards <see cref="ISiteOwned.SiteId"/> on every save (tenancy.md rule 3; PP2.5). A new row gets its
/// context's site; one already carrying that site is let through. Every other change must be to a row of the context's
/// site and leave its site alone. Anything else - another site's id on a new row, a site changed in place, another site's
/// row attached and updated, a stub removed by key, or any site-owned write with no site - fails the whole save. Each is
/// a bug, never a person's doing. Stateless: the site comes from the context being saved.
/// </summary>
/// <remarks>
/// Bulk updates and deletes never pass through a save. The filter keeps them to the context's site, and
/// <see cref="SiteIdUpdateGuard"/> refuses one that sets a site.
/// </remarks>
internal sealed class SiteStampInterceptor : SaveChangesInterceptor
{
    public static SiteStampInterceptor Instance { get; } = new();

    private SiteStampInterceptor()
    {
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // Interceptors run before the save detects changes, and a site edited in place must be seen.
        if (context.ChangeTracker.AutoDetectChangesEnabled)
        {
            context.ChangeTracker.DetectChanges();
        }

        foreach (var entry in context.ChangeTracker.Entries<ISiteOwned>())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached)
            {
                continue;
            }

            var site = context is ISiteScopedDbContext scoped
                ? scoped.Site.Required
                : throw new InvalidOperationException($"{context.GetType().Name} does not know its site, so it cannot save site-owned rows.");
            var property = entry.Property(nameof(ISiteOwned.SiteId));
            var current = (SiteId)property.CurrentValue!;

            if (entry.State == EntityState.Added)
            {
                if (current == default)
                {
                    property.CurrentValue = site;
                }
                else if (current != site)
                {
                    throw new InvalidOperationException(
                        $"A new {entry.Metadata.DisplayName()} names another site than the one this work is on.");
                }

                continue;
            }

            if ((SiteId)property.OriginalValue! != site)
            {
                throw new InvalidOperationException(
                    $"A {entry.Metadata.DisplayName()} that is not this site's row cannot be changed or removed here.");
            }

            if (current != site)
            {
                throw new InvalidOperationException(
                    $"A {entry.Metadata.DisplayName()}'s site never changes; a row never moves between sites.");
            }
        }
    }
}
