using PantryPress.Domain.Modules.Sites.Business;

namespace PantryPress.Tests.Sites;

// PP2.3: a slug is looked up only in its stored shape - ASCII lower-case, 3 to 40 of a-z, 0-9, and '-' - and that is
// checked in .NET, before any query (tenancy.md, "Tenant root"). What may be taken is PP2.8's.
public sealed class SiteSlugsTests
{
    [Theory]
    [InlineData("pie", "pie")]
    [InlineData("Pie", "pie")]
    [InlineData("SUNDAY-SUPPER-2", "sunday-supper-2")]
    [InlineData("a234567890123456789012345678901234567890", "a234567890123456789012345678901234567890")]
    public void A_slug_shape_is_lower_cased_for_lookup(string value, string slug)
    {
        Assert.True(SiteSlugs.TryNormalize(value, out var normalized));
        Assert.Equal(slug, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pi")]
    [InlineData("a2345678901234567890123456789012345678901")]
    [InlineData("pie crust")]
    [InlineData("pie_crust")]
    [InlineData("pie.crust")]
    [InlineData("pie ")]
    [InlineData("café")]
    [InlineData("pіe")] // Cyrillic і, which a code page conversion could otherwise turn into a real slug's letter
    [InlineData("pıe")] // dotless ı, which upper-cases to an ASCII I
    [InlineData("ﬁsh")] // the fi ligature
    public void Anything_outside_the_shape_finds_nothing(string? value)
    {
        Assert.False(SiteSlugs.TryNormalize(value, out var slug));
        Assert.Equal(string.Empty, slug);
    }
}
