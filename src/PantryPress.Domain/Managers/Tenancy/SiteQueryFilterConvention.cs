using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace PantryPress.Domain.Managers.Tenancy;

/// <summary>
/// The global site filter (tenancy.md rule 2; ARCH-015; PP2.5): every <see cref="ISiteOwned"/> entity type is read only
/// for the site its context works on. A convention rather than a call in each configuration, so it reaches every such
/// type however it joined the model, and it runs last, after every configuration has had its say.
/// </summary>
/// <remarks>
/// The filter is named <see cref="FilterName"/> so a later filter of another kind (soft delete) can be ignored without
/// this one. It compares with <paramref name="currentSite"/>, which reads the context the model was built with; EF Core
/// reads it from the context running each query instead, so the cached model serves every scope. With no site, reading
/// it throws: a site-owned query without a site is a bug, and an empty answer would hide one.
/// The same pass makes <see cref="ISiteOwned.SiteId"/> required and a concurrency token, so every update and delete
/// also matches the row's original site, behind the save's own check (<see cref="SiteStampInterceptor"/>). It also
/// refuses a model in which one site's row could point at another's: a reference between two site-owned types must
/// carry <see cref="ISiteOwned.SiteId"/> in its key, so the database refuses a link across sites; and a type that is not
/// site-owned may not depend on one that is, because nothing would filter or stamp it (an implicit join table included).
/// </remarks>
internal sealed class SiteQueryFilterConvention(Expression<Func<SiteId>> currentSite) : IModelFinalizingConvention
{
    public const string FilterName = "site";

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            if (!IsSiteOwned(entityType))
            {
                EnsureNoSiteOwnedPrincipal(entityType);
                continue;
            }

            // EF Core filters only a hierarchy's root, so a site-owned type below a root that is not would go unfiltered,
            // and an owned type cannot carry a filter at all.
            if (entityType.IsOwned())
            {
                throw new InvalidOperationException(
                    $"{entityType.DisplayName()} is site-owned but owned by another entity; a site-owned type must be an entity of its own.");
            }

            // A row split over several tables can be updated in one that holds no SiteId, which the concurrency token cannot
            // then guard, so a site-owned hierarchy is one table per type tree (TPH) and never split.
            if (entityType.GetRootType().GetMappingStrategy() is RelationalAnnotationNames.TptMappingStrategy or RelationalAnnotationNames.TpcMappingStrategy
                || entityType.GetMappingFragments().Any())
            {
                throw new InvalidOperationException(
                    $"{entityType.DisplayName()} is site-owned and split across tables; a site-owned type keeps one table per hierarchy.");
            }

            EnsureSiteKeyedReferences(entityType);

            if (entityType.BaseType is { } baseType)
            {
                if (IsSiteOwned(baseType.GetRootType()))
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"{entityType.DisplayName()} is site-owned but its base type is not; the root of the hierarchy must be site-owned.");
            }

            var siteId = entityType.FindProperty(nameof(ISiteOwned.SiteId));
            if (siteId is null || siteId.ClrType != typeof(SiteId) || siteId.PropertyInfo is null)
            {
                throw new InvalidOperationException(
                    $"{entityType.DisplayName()} is site-owned but does not map a public {nameof(SiteId)} property of type {nameof(SiteId)}.");
            }

            var entity = Expression.Parameter(entityType.ClrType, "entity");
            var filter = Expression.Lambda(
                Expression.Equal(Expression.Property(entity, siteId.PropertyInfo), currentSite.Body),
                entity);

            // A configuration that turned the requirement or the token off would win over a convention, so each answer is
            // checked: a site-owned type cannot opt out. A filter configured under the same name is replaced by this one.
            if (siteId.Builder.IsRequired(true) is null
                || siteId.Builder.IsConcurrencyToken(true) is null
                || entityType.Builder.HasQueryFilter(FilterName, filter) is null)
            {
                throw new InvalidOperationException(
                    $"{entityType.DisplayName()} configures its own {nameof(SiteId)} rules or '{FilterName}' filter; the site convention sets them.");
            }
        }
    }

    private static bool IsSiteOwned(IReadOnlyEntityType entityType) => typeof(ISiteOwned).IsAssignableFrom(entityType.ClrType);

    private static void EnsureNoSiteOwnedPrincipal(IConventionEntityType entityType)
    {
        if (entityType.GetForeignKeys().FirstOrDefault(fk => IsSiteOwned(fk.PrincipalEntityType)) is { } reference)
        {
            throw new InvalidOperationException(
                $"{entityType.DisplayName()} depends on the site-owned {reference.PrincipalEntityType.DisplayName()} but is not site-owned itself, " +
                "so nothing would filter or stamp it. Make it site-owned (a join table becomes an entity of its own).");
        }
    }

    private static void EnsureSiteKeyedReferences(IConventionEntityType entityType)
    {
        foreach (var reference in entityType.GetDeclaredForeignKeys().Where(fk => IsSiteOwned(fk.PrincipalEntityType)))
        {
            if (reference.Properties.All(p => p.Name != nameof(ISiteOwned.SiteId))
                || reference.PrincipalKey.Properties.All(p => p.Name != nameof(ISiteOwned.SiteId)))
            {
                throw new InvalidOperationException(
                    $"{entityType.DisplayName()} refers to the site-owned {reference.PrincipalEntityType.DisplayName()} without {nameof(SiteId)} in the key, " +
                    $"so a row could point at another site's. Key the reference by ({nameof(SiteId)}, id) on both sides.");
            }
        }
    }
}
