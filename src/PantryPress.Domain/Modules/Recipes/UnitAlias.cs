using PantryPress.Domain.Managers.Persistence;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// One spelling of a unit the ingredient parser recognizes (RCP-002; PP4.5). A case-insensitive alias is stored
/// lower-case; a case-sensitive one exactly as it must be written.
/// Platform reference data (PP-D06, ADR 0006; PP4.1): no <c>SiteId</c>, the same rows in every environment, seeded by
/// migrations from <c>Business.Units</c> and never written by the app (<see cref="IReferenceData"/>).
/// </summary>
public sealed class UnitAlias : IReferenceData
{
    public string Alias { get; init; } = string.Empty;

    public string UnitCode { get; init; } = string.Empty;

    public bool CaseSensitive { get; init; }

    /// <summary>The version of the list that added it.</summary>
    public int Since { get; init; }
}
