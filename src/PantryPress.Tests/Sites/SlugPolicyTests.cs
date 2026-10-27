using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Tests.Sites;

// PP2.8: what slug a site may take (SITE-002). ASCII only and case-insensitive: the shape is checked before anything is
// lower-cased, a host name's label adds its hyphen rules, and the reserved list comes last. Whether a site already holds a
// slug is PP2.10's, under the same unavailable answer.
public sealed class SlugPolicyTests
{
    [Theory]
    [InlineData("pie", "pie")]
    [InlineData("Pie", "pie")]
    [InlineData("SUNDAY-SUPPER", "sunday-supper")]
    [InlineData("3-bean-chili", "3-bean-chili")]
    [InlineData("123", "123")]
    [InlineData("a-b", "a-b")]
    [InlineData("a234567890123456789012345678901234567890", "a234567890123456789012345678901234567890")]
    // Near a reserved slug is not reserved.
    [InlineData("apps", "apps")]
    [InlineData("myapp", "myapp")]
    [InlineData("app-recipes", "app-recipes")]
    [InlineData("happ", "happ")]
    [InlineData("www2", "www2")]
    [InlineData("api-cooking", "api-cooking")]
    // The checkpoint's demo site (PP2.14) and a sample blog stay claimable.
    [InlineData("demo", "demo")]
    [InlineData("cook", "cook")]
    // Near a site id's shape is not one.
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950", "0f8fad5b-d9cb-469f-a165-70867728950")]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950g", "0f8fad5b-d9cb-469f-a165-70867728950g")]
    public void A_slug_a_site_may_take_comes_back_in_its_stored_form(string requested, string stored)
    {
        var result = SlugPolicy.Check(requested);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(stored, result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pi")]
    [InlineData("a2345678901234567890123456789012345678901")]
    [InlineData("pie crust")]
    [InlineData(" pie")]
    [InlineData("pie ")]
    [InlineData("pie_crust")]
    [InlineData("pie.crust")]
    [InlineData("pie/crust")]
    [InlineData("café")]
    [InlineData("pіe")] // Cyrillic і
    [InlineData("pıe")] // dotless ı, which upper-cases to an ASCII I
    [InlineData("ﬁsh")] // the fi ligature
    [InlineData("аpp")] // Cyrillic а: a look-alike of a reserved slug is refused for its shape, never lower-cased into it
    [InlineData("-pie")]
    [InlineData("pie-")]
    [InlineData("-")]
    [InlineData("---")]
    [InlineData("pie--crust")]
    [InlineData("xn--80ak6aa92e")] // an internationalized label, which a browser would show in Cyrillic
    [InlineData("XN--pple-43d")]
    // Refused for their shape before anything is lower-cased, so none can become a reserved slug's letters.
    [InlineData("Kitchen")] // the Kelvin sign, which lower-cases to an ASCII k
    [InlineData("ａｐｐ")] // fullwidth app
    [InlineData("ap\u200Dp")] // a zero-width joiner
    [InlineData("app\0")] // an embedded NUL
    // A site id's shape, which begins every site's cache key (PP2.6), in any case.
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e")]
    [InlineData("0F8FAD5B-D9CB-469F-A165-70867728950E")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void A_slug_outside_the_rules_is_refused_against_its_field(string? requested)
    {
        var result = SlugPolicy.Check(requested);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Equal([SlugPolicy.InvalidMessage], Assert.Contains(SlugPolicy.Field, result.Error.Fields));
        Assert.False(SlugPolicy.IsWellFormed(requested, out var slug));
        Assert.Equal(string.Empty, slug);
    }

    [Theory]
    [InlineData("app")]
    [InlineData("APP")]
    [InlineData("App")]
    [InlineData("aPp")]
    [InlineData("dev")]
    [InlineData("www")]
    [InlineData("api")]
    [InlineData("mail")]
    [InlineData("status")]
    [InlineData("pantrypress")]
    [InlineData("PantryPress")]
    [InlineData("no-reply")]
    [InlineData("support")]
    public void A_reserved_slug_is_unavailable_in_any_case(string requested)
    {
        var result = SlugPolicy.Check(requested);

        Assert.False(result.IsSuccess);
        Assert.Same(SiteErrors.SlugUnavailable, result.Error);
        Assert.True(SlugPolicy.IsWellFormed(requested, out _));
    }

    // Reserved and held are one answer (SITE-002), and its words name neither.
    [Fact]
    public void The_unavailable_answer_names_no_reason_or_holder()
    {
        Assert.Equal(ErrorKind.Conflict, SiteErrors.SlugUnavailable.Kind);
        Assert.Equal("slug-unavailable", SiteErrors.SlugUnavailable.Code);
        Assert.DoesNotContain("reserved", SiteErrors.SlugUnavailable.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("taken", SiteErrors.SlugUnavailable.Title, StringComparison.OrdinalIgnoreCase);
    }

    // Every well-formed slug is a valid lookup, so a slug the policy admits can always be found again.
    [Theory]
    [InlineData("pie")]
    [InlineData("Sunday-Supper")]
    [InlineData("app")]
    public void A_well_formed_slug_has_the_lookup_shape(string requested)
    {
        Assert.True(SlugPolicy.IsWellFormed(requested, out var slug));
        Assert.True(SiteSlugs.TryNormalize(requested, out var lookup));
        Assert.Equal(lookup, slug);
    }
}
