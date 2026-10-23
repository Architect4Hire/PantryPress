using System.Security.Cryptography;
using System.Text;

namespace PantryPress.Domain.Modules.Accounts.Business;

/// <summary>
/// Two-factor recovery codes (ACCT-001, PP1.3). Each code is ten characters from a 32-character alphabet without the
/// look-alikes 0, 1, I, and O (50 bits), shown as <c>ABCDE-FGHJK</c>. Codes are shown once and stored only as salted
/// hashes (<see cref="RecoveryCodeHashes"/>), unlike Identity's default plain text.
/// </summary>
internal static class RecoveryCodes
{
    public const int Count = 10;
    public const int Length = 10;

    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public static string Generate()
    {
        Span<char> code = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
        {
            code[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return string.Concat(code[..(Length / 2)], "-", code[(Length / 2)..]);
    }

    /// <summary>What people type: any case, with or without the hyphen or spaces. Null when it cannot be a code.</summary>
    public static string? Normalize(string? input)
    {
        if (input is null || input.Length > 32)
        {
            return null;
        }

        var normalized = new string([.. input.Where(c => c is not (' ' or '-')).Select(char.ToUpperInvariant)]);
        return normalized.Length == Length && normalized.All(c => Alphabet.Contains(c, StringComparison.Ordinal))
            ? normalized
            : null;
    }
}

/// <summary>
/// The stored form of an account's unused recovery codes: one random salt and an HMAC-SHA256 of each normalized code.
/// Serialized as <c>v1;salt;hash;hash…</c> in base64url, so a database copy does not reveal usable codes.
/// </summary>
internal sealed class RecoveryCodeHashes
{
    private const string Version = "v1";
    private const int SaltLength = 16;

    private readonly byte[] _salt;
    private readonly List<byte[]> _hashes;

    private RecoveryCodeHashes(byte[] salt, List<byte[]> hashes)
    {
        _salt = salt;
        _hashes = hashes;
    }

    public int Count => _hashes.Count;

    public static RecoveryCodeHashes Create(IEnumerable<string> codes)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hashes = codes
            .Select(c => RecoveryCodes.Normalize(c) ?? throw new ArgumentException("Not a recovery code.", nameof(codes)))
            .Select(c => Hash(salt, c))
            .ToList();
        return new RecoveryCodeHashes(salt, hashes);
    }

    public static bool TryParse(string? stored, out RecoveryCodeHashes hashes)
    {
        hashes = null!;
        var parts = stored?.Split(';');
        if (parts is not { Length: >= 2 } || parts[0] != Version)
        {
            return false;
        }

        try
        {
            var salt = System.Buffers.Text.Base64Url.DecodeFromChars(parts[1]);
            var list = parts.Skip(2).Where(p => p.Length > 0).Select(p => System.Buffers.Text.Base64Url.DecodeFromChars(p)).ToList();
            hashes = new RecoveryCodeHashes(salt, list);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Removes the code if it is one of the unused codes. Compares against every hash in constant time.</summary>
    public bool TryRedeem(string? code)
    {
        var normalized = RecoveryCodes.Normalize(code);
        if (normalized is null)
        {
            return false;
        }

        var given = Hash(_salt, normalized);
        var match = -1;
        for (var i = 0; i < _hashes.Count; i++)
        {
            if (CryptographicOperations.FixedTimeEquals(given, _hashes[i]) && match < 0)
            {
                match = i;
            }
        }

        if (match < 0)
        {
            return false;
        }

        _hashes.RemoveAt(match);
        return true;
    }

    public override string ToString() =>
        string.Join(';', [Version, Encode(_salt), .. _hashes.Select(Encode)]);

    private static byte[] Hash(byte[] salt, string normalizedCode) =>
        HMACSHA256.HashData(salt, Encoding.ASCII.GetBytes(normalizedCode));

    private static string Encode(byte[] bytes) => System.Buffers.Text.Base64Url.EncodeToString(bytes);
}
