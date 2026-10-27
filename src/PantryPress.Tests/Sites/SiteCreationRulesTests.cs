using FluentValidation;
using Microsoft.Extensions.Time.Testing;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Managers.Validation;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Domain.Modules.Sites.Validation;

namespace PantryPress.Tests.Sites;

// PP2.10: the pure rules behind creating and listing sites - a site's own address under the configured zone (SITE-003,
// SITE-009), the list's cursor, and what a site's name, tagline, time zone, and locale may be.
public sealed class SiteCreationRulesTests
{
    private static readonly IValidator<CreateSiteRequest> CreateValidator = new CreateSiteRequestValidator(
        new TzdbTimeZoneService(new TimeProviderClock(new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero)))));

    private static readonly IValidator<ListSitesQuery> ListValidator = new ListSitesQueryValidator();

    [Theory]
    [InlineData("pantrypress.app")]
    [InlineData("dev.pantrypress.app")]
    [InlineData("localhost")]
    public void A_zone_in_stored_form_makes_a_subdomain(string zone)
    {
        Assert.True(SiteSubdomains.IsValidZone(zone));
        Assert.Equal($"pie.{zone}", SiteSubdomains.For("pie", zone));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("PantryPress.app")]
    [InlineData(".pantrypress.app")]
    [InlineData("pantrypress.app.")]
    [InlineData("pantrypress.app:443")]
    [InlineData("127.0.0.1")]
    [InlineData("pantrypress..app")]
    [InlineData("-pantrypress.app")]
    [InlineData("pantrypréss.app")]
    public void A_zone_out_of_stored_form_is_refused(string? zone) => Assert.False(SiteSubdomains.IsValidZone(zone));

    // The longest slug plus the zone must still be a host: a zone that leaves no room is refused at startup, not when the
    // forty-character address is first asked for.
    [Fact]
    public void A_zone_too_long_for_the_longest_slug_is_refused()
    {
        var label = new string('z', 63);
        var zone = $"{label}.{label}.{label}.{new string('z', 30)}";
        Assert.True(SiteLimits.MaxSlugLength + 1 + zone.Length > SiteLimits.MaxHostLength);
        Assert.True(SiteHostNames.TryNormalize($"pie.{zone}", out _));
        Assert.False(SiteSubdomains.IsValidZone(zone));
    }

    [Theory]
    [InlineData("Pie")]
    [InlineData("pie-")]
    [InlineData("pi")]
    public void A_subdomain_is_made_only_from_a_slug_the_policy_stored(string slug) =>
        Assert.Throws<ArgumentException>(() => SiteSubdomains.For(slug, "pantrypress.app"));

    [Theory]
    [InlineData("pie")]
    [InlineData("a234567890123456789012345678901234567890")]
    [InlineData("sunday-supper")]
    public void A_cursor_reads_back_as_the_slug_it_was_made_from(string slug)
    {
        Assert.True(SiteListCursor.TryRead(SiteListCursor.After(slug), out var read));
        Assert.Equal(slug, read);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pie")]
    [InlineData("cGll=")]
    [InlineData("UGll")]
    [InlineData("cGl")]
    [InlineData("cGllIQ")]
    [InlineData("!!!!")]
    [InlineData("cGll cGll")]
    public void Anything_but_a_cursor_this_made_is_refused(string cursor) =>
        Assert.False(SiteListCursor.TryRead(cursor, out _));

    [Fact]
    public void A_slug_in_another_case_is_not_a_cursor() =>
        Assert.False(SiteListCursor.TryRead(SiteListCursor.After("Pie"), out _));

    [Theory]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("fr-CA")]
    [InlineData("es-419")]
    [InlineData("zh-Hant-TW")]
    [InlineData("haw")]
    public void A_locale_tag_in_its_usual_case_is_accepted(string locale) => Assert.True(SiteText.IsLocale(locale));

    [Theory]
    [InlineData("")]
    [InlineData("EN-us")]
    [InlineData("en-us")]
    [InlineData("en_US")]
    [InlineData("english")]
    [InlineData("en-US-x-private")]
    [InlineData("zh-hant-TW")]
    [InlineData("en-")]
    [InlineData("e")]
    public void Anything_else_is_not_a_locale(string locale) => Assert.False(SiteText.IsLocale(locale));

    [Theory]
    [InlineData("Sunday Supper", true)]
    [InlineData("Café & Crème — 🍰", true)]
    [InlineData("Line\nbreak", false)]
    [InlineData("Tab\there", false)]
    [InlineData("Nul\0", false)]
    [InlineData("Line\u2028separator", false)]
    [InlineData("Para\u2029separator", false)]
    public void A_name_is_one_line_of_plain_text(string value, bool expected) =>
        Assert.Equal(expected, SiteText.IsSingleLine(value));

    // Built at run time: an attribute's string cannot hold an unpaired surrogate.
    [Fact]
    public void An_unpaired_surrogate_is_not_plain_text()
    {
        Assert.False(SiteText.IsSingleLine("Lone " + (char)0xD83C + " high"));
        Assert.False(SiteText.IsSingleLine("Lone " + (char)0xDF70 + " low"));
        Assert.False(SiteText.IsSingleLine("Ends high " + (char)0xD83C));
        Assert.True(SiteText.IsSingleLine("Paired " + (char)0xD83C + (char)0xDF70));
    }

    [Fact]
    public async Task A_complete_request_is_valid() =>
        Assert.Null(await CreateValidator.CheckAsync(Valid(), CancellationToken.None));

    [Fact]
    public async Task Only_the_slug_name_and_time_zone_are_required() =>
        Assert.Null(await CreateValidator.CheckAsync(Valid() with { Tagline = null, Locale = null }, CancellationToken.None));

    public static TheoryData<CreateSiteRequest, string> Refused => new()
    {
        { Valid() with { Slug = "app-" }, "slug" },
        { Valid() with { Slug = null! }, "slug" },
        { Valid() with { Slug = "pі" + "e" }, "slug" },
        { Valid() with { Name = "" }, "name" },
        { Valid() with { Name = "   " }, "name" },
        { Valid() with { Name = null! }, "name" },
        { Valid() with { Name = new string('a', SiteLimits.MaxNameLength + 1) }, "name" },
        { Valid() with { Name = "Two\nlines" }, "name" },
        { Valid() with { Tagline = new string('a', SiteLimits.MaxTaglineLength + 1) }, "tagline" },
        { Valid() with { Tagline = "Two\rlines" }, "tagline" },
        { Valid() with { TimeZone = "Central Standard Time" }, "timeZone" },
        { Valid() with { TimeZone = "" }, "timeZone" },
        { Valid() with { TimeZone = null! }, "timeZone" },
        { Valid() with { Locale = "english" }, "locale" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task A_bad_field_is_named(CreateSiteRequest request, string field)
    {
        var error = await CreateValidator.CheckAsync(request, CancellationToken.None);

        Assert.NotNull(error);
        Assert.Equal("validation-failed", error.Code);
        Assert.Equal([field], error.Fields.Keys);
    }

    // The slug's message is the slug policy's, so the field reads the same wherever an address is refused.
    [Fact]
    public async Task A_bad_slug_gets_the_slug_policys_message()
    {
        var error = await CreateValidator.CheckAsync(Valid() with { Slug = "-pie" }, CancellationToken.None);

        Assert.Equal([SlugPolicy.InvalidMessage], error!.Fields[SlugPolicy.Field]);
    }

    // Surrounding spaces do not count toward the limits: the facade stores the trimmed text.
    [Fact]
    public async Task Spaces_around_a_name_do_not_count() =>
        Assert.Null(await CreateValidator.CheckAsync(
            Valid() with { Name = $"  {new string('a', SiteLimits.MaxNameLength)}  " },
            CancellationToken.None));

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(1, null, true)]
    [InlineData(100, null, true)]
    [InlineData(0, null, false)]
    [InlineData(101, null, false)]
    [InlineData(-1, null, false)]
    [InlineData(null, "cGll", true)]
    [InlineData(null, "not-a-cursor", false)]
    public async Task A_page_asks_for_1_to_100_sites_after_a_cursor_this_made(int? limit, string? cursor, bool valid) =>
        Assert.Equal(valid, await ListValidator.CheckAsync(new ListSitesQuery(limit, cursor), CancellationToken.None) is null);

    private static CreateSiteRequest Valid() =>
        new("sunday-supper", "Sunday Supper", "Weeknight dinners", "America/Chicago", "en-US");
}
