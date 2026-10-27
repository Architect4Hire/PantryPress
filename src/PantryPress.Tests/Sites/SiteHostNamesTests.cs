using PantryPress.Domain.Modules.Sites.Business;

namespace PantryPress.Tests.Sites;

// PP2.9: a host is stored, and looked up, lower-case with no port or trailing dot (SITE-005). The ASCII check comes
// before anything is lower-cased, so no look-alike letter becomes a real one; SiteHostSchemaTests runs the same shapes
// against the schema's check.
public sealed class SiteHostNamesTests
{
    [Theory]
    [InlineData("pie.pantrypress.app", "pie.pantrypress.app")]
    [InlineData("Pie.PantryPress.App", "pie.pantrypress.app")]
    [InlineData("pie.pantrypress.app.", "pie.pantrypress.app")]
    [InlineData("pie.localhost", "pie.localhost")]
    [InlineData("www.example.co.uk", "www.example.co.uk")]
    [InlineData("1pie.example.com", "1pie.example.com")]
    [InlineData("a.b", "a.b")]
    [InlineData("xn--pple-43d.com", "xn--pple-43d.com")] // an IDN custom domain is a host; whether to take one is PP17's
    [InlineData("pie.example.c0m", "pie.example.c0m")]
    public void A_host_name_is_stored_lower_case_without_its_trailing_dot(string value, string host)
    {
        Assert.True(SiteHostNames.TryNormalize(value, out var normalized));
        Assert.Equal(host, normalized);
    }

    [Fact]
    public void The_longest_labels_and_host_are_accepted()
    {
        var label = new string('a', 63);
        var longest = $"{label}.{label}.{label}.{new string('b', 61)}";

        Assert.Equal(253, longest.Length);
        Assert.True(SiteHostNames.TryNormalize(longest, out var host));
        Assert.Equal(longest, host);
        Assert.True(SiteHostNames.TryNormalize(longest + ".", out host));
        Assert.Equal(longest, host);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("localhost")] // one label
    [InlineData("pie.pantrypress.app:443")] // a port, which the caller splits off
    [InlineData("pie.pantrypress.app..")]
    [InlineData(".pie.pantrypress.app")]
    [InlineData("pie..pantrypress.app")]
    [InlineData("-pie.pantrypress.app")]
    [InlineData("pie-.pantrypress.app")]
    [InlineData("pie.-pantrypress.app")]
    [InlineData("pie crust.example.com")]
    [InlineData("pie_crust.example.com")]
    [InlineData(" pie.example.com")]
    [InlineData("pie.example.com ")]
    [InlineData("pie.example.com\0")]
    [InlineData("pie.example.com/")]
    [InlineData("user@pie.example.com")]
    [InlineData("127.0.0.1")]
    [InlineData("127.1")]
    [InlineData("0x7f.1")]
    [InlineData("1.2.3.4.5")]
    [InlineData("pie.example.123")]
    [InlineData("127.0.0.0x1")] // hex, which a browser reads as 127.0.0.1
    [InlineData("pie.0x7f")]
    [InlineData("pie.example.1com")] // no top-level domain starts with a digit
    [InlineData("[::1]")]
    [InlineData("::1")]
    [InlineData("café.example.com")]
    [InlineData("pіe.example.com")] // Cyrillic і, which a code page conversion could otherwise turn into a real host's letter
    [InlineData("pıe.example.com")] // dotless ı, which upper-cases to an ASCII I
    [InlineData("Kiwi.example.com")] // the Kelvin sign, which lower-cases to an ASCII k
    [InlineData("ｐie.example.com")] // a fullwidth p
    [InlineData("pie\u200D.example.com")] // a zero-width joiner
    [InlineData("pie。example。com")] // ideographic full stops, which a browser maps to dots
    public void Anything_but_a_bare_host_name_is_refused(string? value)
    {
        Assert.False(SiteHostNames.TryNormalize(value, out var host));
        Assert.Equal(string.Empty, host);
    }

    [Fact]
    public void A_label_or_host_over_its_limit_is_refused()
    {
        var label = new string('a', 63);

        Assert.False(SiteHostNames.TryNormalize($"{new string('a', 64)}.example.com", out _));
        Assert.False(SiteHostNames.TryNormalize($"{label}.{label}.{label}.{new string('b', 62)}", out _));
        Assert.False(SiteHostNames.TryNormalize($"{label}.{label}.{label}.{new string('b', 62)}.", out _));
    }
}
