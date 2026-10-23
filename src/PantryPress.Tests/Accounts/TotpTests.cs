using System.Text;
using PantryPress.Domain.Modules.Accounts.Business;

namespace PantryPress.Tests.Accounts;

// PP1.3: authenticator codes match RFC 6238 and are checked deterministically for a given time step.
public sealed class TotpTests
{
    // RFC 6238 appendix B, SHA-1 seed; the six-digit codes are the last six digits of the eight-digit vectors.
    private static readonly byte[] RfcKey = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void Compute_matches_the_RFC_6238_test_vectors(long unixSeconds, string expected)
    {
        var step = Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixSeconds));

        Assert.Equal(expected, Totp.Compute(RfcKey, step));
    }

    [Fact]
    public void Match_accepts_one_step_either_side_and_reports_the_step_the_code_was_made_for()
    {
        const long now = 1_000_000;

        Assert.Equal(now - 1, Totp.Match(RfcKey, Totp.Compute(RfcKey, now - 1), now));
        Assert.Equal(now, Totp.Match(RfcKey, Totp.Compute(RfcKey, now), now));
        Assert.Equal(now + 1, Totp.Match(RfcKey, Totp.Compute(RfcKey, now + 1), now));
    }

    [Fact]
    public void Match_refuses_codes_two_steps_away()
    {
        const long now = 1_000_000;

        Assert.Null(Totp.Match(RfcKey, Totp.Compute(RfcKey, now - 2), now));
        Assert.Null(Totp.Match(RfcKey, Totp.Compute(RfcKey, now + 2), now));
    }

    [Theory]
    [InlineData("123456", "123456")]
    [InlineData("123 456", "123456")]
    [InlineData(" 123-456 ", "123456")]
    public void TryNormalize_accepts_what_people_type(string input, string expected)
    {
        Assert.True(Totp.TryNormalize(input, out var code));
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    [InlineData("١٢٣٤٥٦")]
    [InlineData("12345678901234567")]
    public void TryNormalize_refuses_anything_but_six_ascii_digits(string? input)
    {
        Assert.False(Totp.TryNormalize(input, out _));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("MY======", "f")]
    [InlineData("MZXW6YTBOI======", "foobar")]
    [InlineData("mzxw 6ytb oi", "foobar")]
    public void Base32_decodes_RFC_4648_vectors_ignoring_case_spaces_and_padding(string input, string expected)
    {
        Assert.Equal(expected, Encoding.ASCII.GetString(Base32.Decode(input)));
    }

    [Fact]
    public void Base32_refuses_characters_outside_the_alphabet()
    {
        Assert.Throws<FormatException>(() => Base32.Decode("MZXW1"));
    }
}
