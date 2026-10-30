using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// The parsed quantity of an ingredient line (RCP-002, RCP-004; PP4.2): one exact amount, an exact range, or "to taste".
/// Amounts are <see cref="Rational"/>s and above zero, and a range's low end is below its high end, so every quantity that
/// exists is one a cook can measure. The blogger's own wording is kept beside it on the line (PP4.3) and is never made
/// from it. <see cref="MultiplyBy"/> is exact; rounding to friendly fractions and flagging what cannot be scaled are the
/// scaling service's (PP4.7), and text is <see cref="QuantityText"/>'s.
/// </summary>
public sealed record Quantity
{
    private Quantity(QuantityKind kind, Rational? low, Rational? high)
    {
        Kind = kind;
        Low = low;
        High = high;
    }

    /// <summary>A quantity with no amount, for "salt, to taste".</summary>
    public static Quantity ToTaste { get; } = new(QuantityKind.ToTaste, null, null);

    public QuantityKind Kind { get; }

    /// <summary>The amount, or a range's low end; absent for <see cref="QuantityKind.ToTaste"/>.</summary>
    public Rational? Low { get; }

    /// <summary>A range's high end; absent for an amount and for <see cref="QuantityKind.ToTaste"/>.</summary>
    public Rational? High { get; }

    /// <summary>
    /// Whether it has an amount to multiply; "to taste" has none. Whether a whole line scales - one egg at half - is the
    /// scaling service's question (PP4.7), not this one.
    /// </summary>
    public bool HasAmount => Kind != QuantityKind.ToTaste;

    public static Quantity Amount(Rational value)
    {
        RequirePositive(value, nameof(value));
        return new(QuantityKind.Amount, value, null);
    }

    public static Quantity Range(Rational low, Rational high)
    {
        RequirePositive(low, nameof(low));
        if (high <= low)
        {
            throw new ArgumentOutOfRangeException(nameof(high), high, "A range's high end must be above its low end.");
        }

        return new(QuantityKind.Range, low, high);
    }

    /// <summary>
    /// This quantity times <paramref name="factor"/>, exactly: an amount's value, or both ends of a range. "To taste" comes
    /// back as it is. Scaling by a factor and then by its reciprocal returns the original.
    /// </summary>
    public Quantity MultiplyBy(Rational factor)
    {
        RequirePositive(factor, nameof(factor));
        return Kind switch
        {
            QuantityKind.Amount => Amount(Low!.Value * factor),
            QuantityKind.Range => Range(Low!.Value * factor, High!.Value * factor),
            _ => this,
        };
    }

    private static void RequirePositive(Rational value, string name)
    {
        if (value.Sign <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "A quantity is above zero.");
        }
    }
}
