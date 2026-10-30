namespace PantryPress.Domain.Modules.Recipes.Contracts;

/// <summary>
/// What a unit measures (RCP-005; PP4.1). Conversion stays within a dimension, except volume to weight where a density is
/// known (PP4.8). Stored as these numbers, so they never change; a new dimension takes the next one.
/// </summary>
public enum UnitDimension
{
    Volume = 0,

    Weight = 1,

    Temperature = 2,

    /// <summary>A count of things or an inexact measure (a clove, a pinch): scaled, never converted.</summary>
    Count = 3,
}
