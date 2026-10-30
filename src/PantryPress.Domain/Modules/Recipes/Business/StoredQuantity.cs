using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// A <see cref="Quantity"/> as its columns hold it (PP4.3): its kind and the numerator and denominator of each end, or
/// nothing at all for a line or yield with no quantity. It is the one way a stored quantity is made and read: written from
/// a <see cref="Quantity"/>, whose ends are already in lowest terms, and read back through <see cref="Rational.Create"/>,
/// <see cref="Quantity.Amount"/>, and <see cref="Quantity.Range"/>, so a value never skips their rules. The tables check the
/// same shape beneath it.
/// </summary>
public readonly record struct StoredQuantity(
    QuantityKind? Kind,
    long? LowNumerator,
    long? LowDenominator,
    long? HighNumerator,
    long? HighDenominator)
{
    /// <summary>
    /// The columns for <paramref name="quantity"/>, or all empty for none. A term above
    /// <see cref="RecipeLimits.MaxQuantityTerm"/> is refused here, before the table would refuse it.
    /// </summary>
    public static StoredQuantity From(Quantity? quantity)
    {
        if (quantity is null)
        {
            return default;
        }

        var (low, high) = (quantity.Low, quantity.High);
        if (low is { } l)
        {
            RequireStorable(l, nameof(quantity));
        }

        if (high is { } h)
        {
            RequireStorable(h, nameof(quantity));
        }

        return new(quantity.Kind, low?.Numerator, low?.Denominator, high?.Numerator, high?.Denominator);
    }

    /// <summary>The quantity these columns hold, or none. Columns that do not make one are a bug, and throw.</summary>
    public Quantity? ToQuantity() => Kind switch
    {
        null when LowNumerator is null && LowDenominator is null && HighNumerator is null && HighDenominator is null => null,
        QuantityKind.ToTaste when LowNumerator is null && LowDenominator is null && HighNumerator is null && HighDenominator is null
            => Quantity.ToTaste,
        QuantityKind.Amount when HighNumerator is null && HighDenominator is null
            => Quantity.Amount(Rational.Create(Required(LowNumerator), Required(LowDenominator))),
        QuantityKind.Range => Quantity.Range(
            Rational.Create(Required(LowNumerator), Required(LowDenominator)),
            Rational.Create(Required(HighNumerator), Required(HighDenominator))),
        _ => throw Malformed(),
    };

    private static long Required(long? term) => term ?? throw Malformed();

    private static InvalidOperationException Malformed() =>
        new("A stored quantity's columns do not make a quantity; the table's checks should have refused them.");

    private static void RequireStorable(Rational value, string name)
    {
        if (value.Numerator > RecipeLimits.MaxQuantityTerm || value.Denominator > RecipeLimits.MaxQuantityTerm)
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                $"A stored quantity's numerator and denominator are at most {RecipeLimits.MaxQuantityTerm}.");
        }
    }
}
