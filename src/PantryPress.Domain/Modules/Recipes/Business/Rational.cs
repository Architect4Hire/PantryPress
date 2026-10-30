using System.Globalization;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// An exact fraction (PP-D05, RCP-002, RCP-004; PP4.2): the one number type recipe quantities use, so scaling and
/// conversion never meet floating point. It is always in lowest terms with a positive denominator, so two equal values
/// have the same numerator and denominator, and equality, hashing, and storage (PP4.3) agree. Arithmetic is worked in
/// 128 bits, reduced, and checked back into 64, so a result throws <see cref="OverflowException"/> only when its lowest
/// terms do not fit - a bug, never a rounded answer. It converts to and from no floating-point type.
/// </summary>
public readonly record struct Rational : IComparable<Rational>
{
    // Stored less one, so default(Rational) is 0/1 rather than a fraction with no denominator.
    private readonly long _denominatorLessOne;

    private Rational(long numerator, long denominator)
    {
        Numerator = numerator;
        _denominatorLessOne = denominator - 1;
    }

    public static Rational Zero => default;

    public static Rational One => new(1, 1);

    public long Numerator { get; }

    public long Denominator => _denominatorLessOne + 1;

    public int Sign => Numerator.CompareTo(0);

    public bool IsInteger => Denominator == 1;

    /// <summary>The fraction <paramref name="numerator"/>/<paramref name="denominator"/> in lowest terms.</summary>
    public static Rational Create(long numerator, long denominator)
    {
        if (denominator == 0)
        {
            throw new DivideByZeroException("A fraction cannot have a denominator of zero.");
        }

        return Normalize(numerator, denominator);
    }

    public static implicit operator Rational(long whole) => new(whole, 1);

    public static Rational operator +(Rational a, Rational b) =>
        Normalize(((Int128)a.Numerator * b.Denominator) + ((Int128)b.Numerator * a.Denominator), (Int128)a.Denominator * b.Denominator);

    public static Rational operator -(Rational a, Rational b) =>
        Normalize(((Int128)a.Numerator * b.Denominator) - ((Int128)b.Numerator * a.Denominator), (Int128)a.Denominator * b.Denominator);

    public static Rational operator -(Rational a) => Normalize(-(Int128)a.Numerator, a.Denominator);

    public static Rational operator *(Rational a, Rational b) =>
        Normalize((Int128)a.Numerator * b.Numerator, (Int128)a.Denominator * b.Denominator);

    public static Rational operator /(Rational a, Rational b)
    {
        if (b.Numerator == 0)
        {
            throw new DivideByZeroException("A value cannot be divided by zero.");
        }

        return Normalize((Int128)a.Numerator * b.Denominator, (Int128)a.Denominator * b.Numerator);
    }

    public static bool operator <(Rational a, Rational b) => a.CompareTo(b) < 0;

    public static bool operator >(Rational a, Rational b) => a.CompareTo(b) > 0;

    public static bool operator <=(Rational a, Rational b) => a.CompareTo(b) <= 0;

    public static bool operator >=(Rational a, Rational b) => a.CompareTo(b) >= 0;

    /// <summary>One over this value; zero has none.</summary>
    public Rational Reciprocal() => One / this;

    // Both denominators are positive, so the cross products compare as the fractions do; 128 bits hold them exactly.
    public int CompareTo(Rational other) =>
        ((Int128)Numerator * other.Denominator).CompareTo((Int128)other.Numerator * Denominator);

    /// <summary>The fraction as written for a log or a test, <c>n/d</c>; display text is <see cref="QuantityText"/>'s.</summary>
    public override string ToString() =>
        IsInteger
            ? Numerator.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{Numerator}/{Denominator}");

    private static Rational Normalize(Int128 numerator, Int128 denominator)
    {
        if (denominator < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        var divisor = GreatestCommonDivisor(Int128.Abs(numerator), denominator);
        return new(checked((long)(numerator / divisor)), checked((long)(denominator / divisor)));
    }

    // Euclid's algorithm; gcd(0, d) is d, so zero normalizes to 0/1.
    private static Int128 GreatestCommonDivisor(Int128 a, Int128 b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }
}
