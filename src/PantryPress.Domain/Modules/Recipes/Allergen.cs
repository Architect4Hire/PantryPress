using PantryPress.Domain.Managers.Persistence;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// An allergen a blogger can label a recipe with (RCP-006).
/// Platform reference data (PP-D06, ADR 0006; PP4.1): no <c>SiteId</c>, the same rows in every environment, seeded by
/// migrations from <c>Business.Allergens</c> and never written by the app (<see cref="IReferenceData"/>).
/// </summary>
public sealed class Allergen : IReferenceData
{
    /// <summary>The stable code a recipe stores and the API sends; never changes once seeded.</summary>
    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>The version of the list that added it.</summary>
    public int Since { get; init; }
}
