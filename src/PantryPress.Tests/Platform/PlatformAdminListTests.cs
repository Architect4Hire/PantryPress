using PantryPress.Domain.Modules.Platform;
using PantryPress.Domain.Modules.Platform.Business;

namespace PantryPress.Tests.Platform;

// PP1.4: the operator's list as it arrives from one configuration value (ACCT-005).
public sealed class PlatformAdminListTests
{
    [Fact]
    public void Separators_whitespace_and_repeats_make_one_list()
    {
        Assert.True(PlatformAdminList.TryParse(
            " ops@example.test, second@example.test;\n OPS@example.test \n\n third@example.test ",
            out var emails));

        Assert.Equal(new[] { "ops@example.test", "second@example.test", "third@example.test" }, emails);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",;\n")]
    public void Nothing_configured_is_an_empty_list_not_a_failure(string? configured)
    {
        Assert.True(PlatformAdminList.TryParse(configured, out var emails));
        Assert.Empty(emails);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("@example.test")]
    [InlineData("ops@")]
    [InlineData("ops@one@example.test")]
    [InlineData("ops @example.test")]
    [InlineData("ops@example.test, broken")]
    public void An_entry_that_is_not_an_address_fails_the_whole_list(string configured)
    {
        Assert.False(PlatformAdminList.TryParse(configured, out _));
        Assert.False(new PlatformOptions { Admins = configured }.IsValid());
    }

    [Fact]
    public void Options_accept_a_list_and_an_empty_value()
    {
        Assert.True(new PlatformOptions().IsValid());
        Assert.True(new PlatformOptions { Admins = "ops@example.test;second@example.test" }.IsValid());
    }

    [Theory]
    [InlineData("ops@example.test", "o***@example.test")]
    [InlineData("  Ops.Lead@Example.Test  ", "O***@Example.Test")]
    [InlineData("nonsense", "***")]
    [InlineData(null, "***")]
    public void A_logged_address_keeps_only_its_first_letter_and_domain(string? email, string expected)
    {
        Assert.Equal(expected, MaskedEmail.From(email));
    }
}
