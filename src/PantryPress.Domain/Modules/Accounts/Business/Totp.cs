using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PantryPress.Domain.Modules.Accounts.Business;

/// <summary>
/// Time-based one-time passwords (RFC 6238) as authenticator apps compute them: HMAC-SHA1, 30-second steps, six
/// digits. Pure: the caller passes the time step, so verification is deterministic and testable.
/// </summary>
internal static class Totp
{
    public const int Digits = 6;
    public const int StepSeconds = 30;

    /// <summary>
    /// Steps accepted either side of now: one, so a code works for about 30 seconds after it changes and a phone clock
    /// a little fast still works. Identity's default allows two.
    /// </summary>
    public const int AllowedDrift = 1;

    public static long StepAt(DateTimeOffset instant) => instant.ToUnixTimeSeconds() / StepSeconds;

    public static string Compute(byte[] key, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);

        Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];
#pragma warning disable CA5350 // RFC 6238 and every authenticator app use HMAC-SHA1; it is not used for collision resistance.
        HMACSHA1.HashData(key, counter, hash);
#pragma warning restore CA5350

        // Dynamic truncation (RFC 4226 section 5.3).
        var offset = hash[^1] & 0x0F;
        var binary = BinaryPrimitives.ReadInt32BigEndian(hash[offset..]) & 0x7FFF_FFFF;
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The time step a code was computed for, within <see cref="AllowedDrift"/> of <paramref name="currentStep"/>, or
    /// null when it matches none. Every candidate is compared in constant time.
    /// </summary>
    public static long? Match(byte[] key, string code, long currentStep)
    {
        var given = Encoding.ASCII.GetBytes(code);
        long? matched = null;
        for (var step = currentStep - AllowedDrift; step <= currentStep + AllowedDrift; step++)
        {
            if (CryptographicOperations.FixedTimeEquals(given, Encoding.ASCII.GetBytes(Compute(key, step))))
            {
                matched ??= step;
            }
        }

        return matched;
    }

    /// <summary>Accepts what people type ("123 456", "123-456") and returns the six digits, or false.</summary>
    public static bool TryNormalize(string? input, out string code)
    {
        code = string.Empty;
        if (input is null || input.Length > 16)
        {
            return false;
        }

        var digits = new string([.. input.Where(c => c is not (' ' or '-'))]);
        if (digits.Length != Digits || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }

        code = digits;
        return true;
    }
}

/// <summary>RFC 4648 base32, the encoding of authenticator shared keys.</summary>
internal static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Decodes, ignoring case, spaces, and padding. Throws <see cref="FormatException"/> on other input.</summary>
    public static byte[] Decode(string input)
    {
        var output = new List<byte>(input.Length * 5 / 8);
        var buffer = 0;
        var bits = 0;
        foreach (var c in input)
        {
            if (c is ' ' or '=')
            {
                continue;
            }

            var value = Alphabet.IndexOf(char.ToUpperInvariant(c), StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException("The value is not base32.");
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)(buffer >> bits));
                buffer &= (1 << bits) - 1;
            }
        }

        return [.. output];
    }
}
