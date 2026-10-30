namespace PantryPress.Domain.Modules.Recipes.Contracts;

/// <summary>
/// The measuring system a unit belongs to (RCP-005; PP4.1). Stored as these numbers, so they never change; a new system
/// takes the next one.
/// </summary>
public enum UnitSystem
{
    /// <summary>US customary: the teaspoon, the cup, the US pint, the ounce, the pound, degrees Fahrenheit.</summary>
    UsCustomary = 0,

    /// <summary>Metric: the milliliter, the liter, the gram, the kilogram, degrees Celsius.</summary>
    Metric = 1,

    /// <summary>Neither: a pinch, a clove, a can. These scale but never convert.</summary>
    Neutral = 2,
}
