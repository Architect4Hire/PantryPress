namespace PantryPress.Domain.Managers.Persistence;

/// <summary>
/// Platform reference data (PP-D06, ADR 0006): no <c>SiteId</c>, the same rows in every environment, seeded by migrations
/// from a versioned list in code, and read-only to the app (PP4.1). <see cref="ReferenceDataGuard"/> refuses a save that
/// adds, changes, or removes one, and a bulk update or delete of one, so its rows change only through a reviewed
/// migration.
/// </summary>
public interface IReferenceData
{
}
