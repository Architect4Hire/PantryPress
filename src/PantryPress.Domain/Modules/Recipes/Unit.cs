using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// A unit a recipe measures in (RCP-001, RCP-005).
/// Platform reference data (PP-D06, ADR 0006; PP4.1): no <c>SiteId</c>, the same rows in every environment, seeded by
/// migrations from <c>Business.Units</c> and never written by the app (<see cref="IReferenceData"/>).
/// </summary>
public sealed class Unit : IReferenceData
{
    /// <summary>The stable code an ingredient line stores and the API sends; never changes once seeded.</summary>
    public string Code { get; init; } = string.Empty;

    public UnitSystem System { get; init; }

    public UnitDimension Dimension { get; init; }

    public string Singular { get; init; } = string.Empty;

    public string Plural { get; init; } = string.Empty;

    public string? Abbreviation { get; init; }

    /// <summary>The version of the list that added it.</summary>
    public int Since { get; init; }
}
