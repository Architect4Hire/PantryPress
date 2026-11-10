using System.Globalization;
using System.Numerics;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// An exact fraction with no size limit (PP4.8), for the one step between a scaled quantity and its rounding where a
/// <see cref="Rational"/>'s 64 bits are not enough: a scaled term (up to 10^12) times a unit's exact factor (a teaspoon is
/// 157725491/32000000 ml) can pass them. It is always in lowest terms with a positive denominator, like a
/// <see cref="Rational"/>, so equal values are equal records. Nothing is ever rounded here, and it meets no floating point;
/// <see cref="FriendlyRounding"/> turns it back into a <see cref="Rational"/>.
/// </summary>
internal readonly record struct ExactValue : IComparable<ExactValue>
{
    private readonly BigInteger _denominatorLessOne;

    private ExactValue(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero)
        {
            throw new DivideByZeroException("A fraction cannot have a denominator of zero.");
        }

        if (denominator.Sign < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        Numerator = numerator / divisor;
        _denominatorLessOne = (denominator / divisor) - 1;
    }

    public BigInteger Numerator { get; }

    // Stored less one, so default(ExactValue) is 0/1, as default(Rational) is.
    public BigInteger Denominator => _denominatorLessOne + 1;

    public int Sign => Numerator.Sign;

    public static implicit operator ExactValue(Rational value) => new(value.Numerator, value.Denominator);

    public static ExactValue operator *(ExactValue a, ExactValue b) => new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);

    public static ExactValue operator /(ExactValue a, ExactValue b) =>
        b.Numerator.IsZero
            ? throw new DivideByZeroException("A value cannot be divided by zero.")
            : new(a.Numerator * b.Denominator, a.Denominator * b.Numerator);

    public static ExactValue operator +(ExactValue a, ExactValue b) =>
        new((a.Numerator * b.Denominator) + (b.Numerator * a.Denominator), a.Denominator * b.Denominator);

    public static ExactValue operator -(ExactValue a, ExactValue b) =>
        new((a.Numerator * b.Denominator) - (b.Numerator * a.Denominator), a.Denominator * b.Denominator);

    public static bool operator <(ExactValue a, ExactValue b) => a.CompareTo(b) < 0;

    public static bool operator >(ExactValue a, ExactValue b) => a.CompareTo(b) > 0;

    public static bool operator <=(ExactValue a, ExactValue b) => a.CompareTo(b) <= 0;

    public static bool operator >=(ExactValue a, ExactValue b) => a.CompareTo(b) >= 0;

    public static ExactValue Whole(BigInteger value) => new(value, BigInteger.One);

    /// <summary>The largest whole number at or below this value.</summary>
    public BigInteger Floor()
    {
        var quotient = BigInteger.DivRem(Numerator, Denominator, out var remainder);
        return remainder.Sign < 0 ? quotient - 1 : quotient;
    }

    /// <summary>This value as a <see cref="Rational"/>; one whose lowest terms do not fit in 64 bits is a bug, and throws.</summary>
    public Rational ToRational() => Rational.Create(checked((long)Numerator), checked((long)Denominator));

    public int CompareTo(ExactValue other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

    /// <summary>The fraction as written for a log or a test, <c>n/d</c>.</summary>
    public override string ToString() =>
        Denominator.IsOne
            ? Numerator.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{Numerator}/{Denominator}");
}
