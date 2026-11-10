using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// How much a recipe is scaled by (RCP-004; PP4.7): an exact <see cref="Rational"/> above zero, never floating point. The
/// reader's fixed choices are ½×, 1×, 2×, and 3× (<see cref="Multipliers"/>); custom servings are
/// <see cref="ForServings"/>, the servings a reader asks for over the recipe's yield. Both terms are at most
/// <see cref="RecipeLimits.MaxQuantityTerm"/>, as a stored quantity's are, so a scaled quantity's terms stay below 10^12 and
/// its arithmetic never comes near the 64 bits a <see cref="Rational"/> holds.
/// </summary>
public sealed record ScaleFactor
{
    private ScaleFactor(Rational value) => Value = value;

    public static ScaleFactor Halved { get; } = new(Rational.Create(1, 2));

    /// <summary>1×: every line is shown exactly as written.</summary>
    public static ScaleFactor Unchanged { get; } = new(Rational.One);

    public static ScaleFactor Doubled { get; } = new(2);

    public static ScaleFactor Tripled { get; } = new(3);

    /// <summary>The reader's fixed choices, in the order a recipe card shows them: ½×, 1×, 2×, 3×.</summary>
    public static IReadOnlyList<ScaleFactor> Multipliers { get; } = [Halved, Unchanged, Doubled, Tripled];

    public Rational Value { get; }

    public bool IsUnchanged => Value == Rational.One;

    /// <summary>A factor of exactly <paramref name="value"/>: above zero, with both terms at most the stored cap.</summary>
    public static ScaleFactor Of(Rational value)
    {
        if (value.Sign <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A scale factor is above zero.");
        }

        if (value.Numerator > RecipeLimits.MaxQuantityTerm || value.Denominator > RecipeLimits.MaxQuantityTerm)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A scale factor's terms are at most the stored quantity cap.");
        }

        return new(value);
    }

    /// <summary>
    /// The factor that makes <paramref name="servings"/> from <paramref name="yield"/>, exactly: servings over the yield.
    /// Custom servings are offered only for a yield of one whole number (<c>4 servings</c>, <c>12 cookies</c>); a yield that
    /// is a range (<c>4–6</c>), a fraction, or absent has none, and its recipe keeps the four <see cref="Multipliers"/>.
    /// </summary>
    public static ScaleFactor? ForServings(Quantity? yield, long servings)
    {
        if (servings < 1 || servings > RecipeLimits.MaxQuantityTerm)
        {
            throw new ArgumentOutOfRangeException(nameof(servings), servings, "Servings are a whole number from 1 to the stored quantity cap.");
        }

        if (yield is not { Kind: QuantityKind.Amount, Low: { IsInteger: true } whole } || whole.Numerator > RecipeLimits.MaxQuantityTerm)
        {
            return null;
        }

        return new(Rational.Create(servings, whole.Numerator));
    }

    public override string ToString() => Value + "x";
}
