namespace PantryPress.Domain.Modules.Recipes.Contracts;

/// <summary>How a quantity is written for a reader (PP4.2). Stored as these numbers, so they never change.</summary>
public enum QuantityStyle
{
    /// <summary>Mixed numbers with kitchen fractions: <c>1½</c>, <c>⅓</c>, <c>1 5/16</c>.</summary>
    Fraction = 0,

    /// <summary>Decimals where they are exact: <c>0.5</c>, <c>1.25</c>, <c>250</c>; anything else as a fraction.</summary>
    Decimal = 1,
}
