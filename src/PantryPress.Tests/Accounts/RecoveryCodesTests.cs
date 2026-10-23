using PantryPress.Domain.Modules.Accounts.Business;

namespace PantryPress.Tests.Accounts;

// PP1.3: recovery codes are random, readable, single-use, and stored only as salted hashes.
public sealed class RecoveryCodesTests
{
    [Fact]
    public void Generated_codes_are_two_groups_of_five_without_look_alike_characters()
    {
        var codes = Enumerable.Range(0, 200).Select(_ => RecoveryCodes.Generate()).ToList();

        Assert.All(codes, code => Assert.Matches("^[2-9A-HJ-NP-Z]{5}-[2-9A-HJ-NP-Z]{5}$", code));
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("abcde-fghjk", "ABCDEFGHJK")]
    [InlineData("ABCDEFGHJK", "ABCDEFGHJK")]
    [InlineData(" ab cde-FGHJK ", "ABCDEFGHJK")]
    public void Normalize_accepts_any_case_with_or_without_separators(string input, string expected)
    {
        Assert.Equal(expected, RecoveryCodes.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ABCDE-FGHJ")]
    [InlineData("ABCDE-FGHJKL")]
    [InlineData("ABCDE-FGHJ0")]
    [InlineData("ABCDE-FGHJI")]
    public void Normalize_refuses_values_that_cannot_be_codes(string? input)
    {
        Assert.Null(RecoveryCodes.Normalize(input));
    }

    [Fact]
    public void Stored_form_contains_no_code_and_round_trips()
    {
        var codes = Enumerable.Range(0, RecoveryCodes.Count).Select(_ => RecoveryCodes.Generate()).ToList();

        var stored = RecoveryCodeHashes.Create(codes).ToString();

        Assert.StartsWith("v1;", stored, StringComparison.Ordinal);
        Assert.All(codes, code =>
        {
            Assert.DoesNotContain(code, stored, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(RecoveryCodes.Normalize(code)!, stored, StringComparison.OrdinalIgnoreCase);
        });
        Assert.True(RecoveryCodeHashes.TryParse(stored, out var parsed));
        Assert.Equal(RecoveryCodes.Count, parsed.Count);
    }

    [Fact]
    public void Each_code_redeems_once_in_any_typed_form()
    {
        var codes = new[] { RecoveryCodes.Generate(), RecoveryCodes.Generate() };
        Assert.True(RecoveryCodeHashes.TryParse(RecoveryCodeHashes.Create(codes).ToString(), out var hashes));

        Assert.True(hashes.TryRedeem(codes[0].ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal)));
        Assert.False(hashes.TryRedeem(codes[0]));
        Assert.Equal(1, hashes.Count);

        Assert.True(RecoveryCodeHashes.TryParse(hashes.ToString(), out var remaining));
        Assert.False(remaining.TryRedeem(codes[0]));
        Assert.True(remaining.TryRedeem(codes[1]));
        Assert.Equal(0, remaining.Count);
    }

    [Fact]
    public void The_same_codes_hash_differently_for_each_account()
    {
        var codes = new[] { RecoveryCodes.Generate() };

        Assert.NotEqual(RecoveryCodeHashes.Create(codes).ToString(), RecoveryCodeHashes.Create(codes).ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABCDE-FGHJK;LMNPQ-RSTUV")]
    [InlineData("v2;AAAA;BBBB")]
    [InlineData("v1;not base64!")]
    public void Unknown_stored_forms_hold_no_codes(string? stored)
    {
        Assert.False(RecoveryCodeHashes.TryParse(stored, out _));
    }
}
