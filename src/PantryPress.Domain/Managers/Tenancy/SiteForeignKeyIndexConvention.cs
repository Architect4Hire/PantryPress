using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using PantryPress.Domain.Managers.Persistence;

namespace PantryPress.Domain.Managers.Tenancy;

/// <summary>
/// EF Core's foreign-key index convention, kept from indexing a site-owned table's reference to platform reference data
/// by the code alone (backend.md, "Indexes on site-owned tables lead with SiteId"; PP4.3): a recipe line's unit, a recipe's
/// course. Such an index would not lead with the site, no site's read starts from it, and nothing is ever deleted from
/// reference data (<see cref="IReferenceData"/>), so the database never needs it to check a delete. A read by code declares
/// its own index, led by the site.
/// <para>
/// Nothing else is left out: a unique index enforces a rule, a reference between site-owned types already carries the site
/// first (<see cref="SiteQueryFilterConvention"/>), and a reference to anything else - an account, which erasure deletes -
/// keeps its index, so a site-owned table's index that does not lead with the site fails the model tests rather than
/// vanishing unremarked. The base convention would put back an index removed in configuration, which is why this
/// replaces it.
/// </para>
/// </summary>
internal sealed class SiteForeignKeyIndexConvention(ProviderConventionSetBuilderDependencies dependencies)
    : ForeignKeyIndexConvention(dependencies)
{
    protected override IConventionIndex? CreateIndex(
        IReadOnlyList<IConventionProperty> properties,
        bool unique,
        IConventionEntityTypeBuilder entityTypeBuilder) =>
        !unique && RefersToReferenceDataBySiteOwnedRow(properties, entityTypeBuilder.Metadata)
            ? null
            : base.CreateIndex(properties, unique, entityTypeBuilder);

    private static bool RefersToReferenceDataBySiteOwnedRow(IReadOnlyList<IConventionProperty> properties, IConventionEntityType entityType) =>
        typeof(ISiteOwned).IsAssignableFrom(entityType.ClrType)
        && properties[0].Name != nameof(ISiteOwned.SiteId)
        && entityType.GetForeignKeys().Any(fk =>
            fk.Properties.SequenceEqual(properties)
            && typeof(IReferenceData).IsAssignableFrom(fk.PrincipalEntityType.ClrType));
}
