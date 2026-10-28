using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Tests.Sites;

// PP2.12, PP2.13: the host map's entries. What a host names - or that it names nothing - round-trips exactly, the site's
// public identity included whatever its text, and anything this version did not write reads as a miss, so the database
// answers and the entry is written again.
public sealed class SiteHostMapTests
{
    private const string SiteText = "4b7e2a10-9d3c-4f6e-8a21-5c0d9e7f1b33";
    private const string Valid = "2|" + SiteText + "|1|0|cook.pantrypress.app|Sunday%20Supper|Weeknight%20dinners|en-US";

    private static readonly SiteId Site = new(Guid.Parse(SiteText));
    private static readonly PublicSiteIdentity Supper = new("Sunday Supper", "Weeknight dinners", "en-US");

    public static TheoryData<SiteStatus, bool, string> Matches()
    {
        var data = new TheoryData<SiteStatus, bool, string>();
        foreach (var status in Enum.GetValues<SiteStatus>())
        {
            data.Add(status, true, "cook.pantrypress.app");
            data.Add(status, false, "www.example.com");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Matches))]
    public void A_match_round_trips(SiteStatus status, bool isCanonical, string canonical)
    {
        var match = new SiteHostMatch(Site, status, isCanonical, canonical, Supper);

        Assert.True(SiteHostMap.TryRead(SiteHostMap.Write(match), out var read));
        Assert.Equal(match, read);
    }

    [Theory]
    [InlineData("Pie | Mash", "")]
    [InlineData("100% Butter", "50%2F50 and a %zz")]
    [InlineData("Crème brûlée & co.", "Recettes de grand-mère 🥐")]
    [InlineData("日本の家庭料理", "ご飯, 味噌汁; 漬物")]
    [InlineData("-", "|")]
    public void Any_name_and_tagline_round_trip_as_written(string name, string tagline)
    {
        var match = new SiteHostMatch(Site, SiteStatus.Live, true, "cook.pantrypress.app", new PublicSiteIdentity(name, tagline, "fr-CA"));

        var entry = SiteHostMap.Write(match);

        Assert.True(SiteHostMap.TryRead(entry, out var read));
        Assert.Equal(match, read);
        Assert.Equal(8, entry.Split('|').Length);
    }

    [Fact]
    public void Names_at_their_limits_round_trip()
    {
        var identity = new PublicSiteIdentity(
            new string('é', SiteLimits.MaxNameLength),
            new string('|', SiteLimits.MaxTaglineLength),
            "zh-Hant-TW");
        var match = new SiteHostMatch(Site, SiteStatus.Live, true, "cook.pantrypress.app", identity);

        Assert.True(SiteHostMap.TryRead(SiteHostMap.Write(match), out var read));
        Assert.Equal(match, read);
    }

    [Theory]
    [InlineData("", "", "")]
    [InlineData("Two\nlines", "A\ttab", "EN-us")]
    [InlineData("Sunday Supper", "", "not a locale")]
    public void Whatever_the_columns_hold_round_trips_so_no_row_is_read_on_every_request(string name, string tagline, string locale)
    {
        // The create validator is stricter than the columns, but an operator's edit or an import is not, and an entry the
        // reader refused would send every request for that host to the database (ARCH-013). A withheld identity is the
        // empty one.
        var match = new SiteHostMatch(Site, SiteStatus.Live, true, "cook.pantrypress.app", new PublicSiteIdentity(name, tagline, locale));

        Assert.True(SiteHostMap.TryRead(SiteHostMap.Write(match), out var read));
        Assert.Equal(match, read);
    }

    [Fact]
    public void A_host_that_names_nothing_round_trips()
    {
        Assert.True(SiteHostMap.TryRead(SiteHostMap.Write(null), out var read));
        Assert.Null(read);
    }

    [Fact]
    public void The_entry_says_only_the_site_its_status_its_canonical_host_and_its_public_identity() =>
        Assert.Equal(Valid, SiteHostMap.Write(new SiteHostMatch(Site, SiteStatus.Live, false, "cook.pantrypress.app", Supper)));

    [Fact]
    public void The_entry_this_test_damages_is_itself_valid() =>
        Assert.True(SiteHostMap.TryRead(Valid, out _));

    [Theory]
    [InlineData("")]
    [InlineData("--")]

    // Version 1, from before PP2.13, and a version nobody wrote.
    [InlineData("1|" + SiteText + "|1|0|cook.pantrypress.app")]
    [InlineData("3|" + SiteText + "|1|0|cook.pantrypress.app|Sunday%20Supper|Weeknight%20dinners|en-US")]

    // Too few fields, or too many.
    [InlineData("2|" + SiteText + "|1|0|cook.pantrypress.app|Sunday%20Supper|Weeknight%20dinners")]
    [InlineData("2|" + SiteText + "|1|0|cook.pantrypress.app|Sunday%20Supper|Weeknight%20dinners|en-US|x")]

    // The site, status, and canonical flag.
    [InlineData("2|4b7e2a109d3c4f6e8a215c0d9e7f1b33|1|0|cook.pantrypress.app|Sunday%20Supper|Weeknight%20dinners|en-US")]
    [InlineData("2|00000000-0000-0000-0000-000000000000|1|0|cook.pantrypress.app|Sunday%20Supper|Weeknight%20dinners|en-US")]
    [InlineData("2|not-a-site|1|0|cook.pantrypress.app|Sunday%20Supper|Weeknight%20dinners|en-US")]
    [InlineData("2|" + SiteText + "|9|0|cook.pantrypress.app|Sunday%20Supper|Weeknight%20dinners|en-US")]
    [InlineData("2|" + SiteText + "|-1|0|cook.pantrypress.app|Sunday%20Supper|Weeknight%20dinners|en-US")]
    [InlineData("2|" + SiteText + "| 1|0|cook.pantrypress.app|Sunday%20Supper|Weeknight%20dinners|en-US")]
    [InlineData("2|" + SiteText + "|1|2|cook.pantrypress.app|Sunday%20Supper|Weeknight%20dinners|en-US")]

    // The canonical host, not in stored form.
    [InlineData("2|" + SiteText + "|1|0|Cook.pantrypress.app|Sunday%20Supper|Weeknight%20dinners|en-US")]
    [InlineData("2|" + SiteText + "|1|0|cook.pantrypress.app.|Sunday%20Supper|Weeknight%20dinners|en-US")]
    [InlineData("2|" + SiteText + "|1|0|127.0.0.1|Sunday%20Supper|Weeknight%20dinners|en-US")]
    [InlineData("2|" + SiteText + "|1|0||Sunday%20Supper|Weeknight%20dinners|en-US")]

    // The identity: text not as Write encodes it - unescaped, a damaged escape, an escape Write would not make.
    [InlineData("2|" + SiteText + "|1|0|cook.pantrypress.app|Sunday Supper|Weeknight%20dinners|en-US")]
    [InlineData("2|" + SiteText + "|1|0|cook.pantrypress.app|Sunday%20Supper|Weeknight%2zdinners|en-US")]
    [InlineData("2|" + SiteText + "|1|0|cook.pantrypress.app|Sunday%20Supper|Weeknight%20dinners|en%2DUS")]
    [InlineData("2|" + SiteText + "|1|0|cook.pantrypress.app|Sunday%7cSupper|Weeknight%20dinners|en-US")]
    public void Anything_this_version_did_not_write_is_a_miss(string entry) =>
        Assert.False(SiteHostMap.TryRead(entry, out _));

    [Fact]
    public void A_name_or_tagline_over_its_limit_is_a_miss()
    {
        var longName = Uri.EscapeDataString(new string('a', SiteLimits.MaxNameLength + 1));
        var longTagline = Uri.EscapeDataString(new string('a', SiteLimits.MaxTaglineLength + 1));

        Assert.False(SiteHostMap.TryRead($"2|{SiteText}|1|0|cook.pantrypress.app|{longName}|x|en-US", out _));
        Assert.False(SiteHostMap.TryRead($"2|{SiteText}|1|0|cook.pantrypress.app|x|{longTagline}|en-US", out _));
    }

    [Fact]
    public void A_host_that_names_nothing_is_cached_far_shorter_than_one_that_names_a_site()
    {
        Assert.True(SiteHostMap.NotFoundLifetime <= TimeSpan.FromMinutes(1));
        Assert.True(SiteHostMap.NotFoundLifetime < SiteHostMap.FoundLifetime);
        Assert.True(SiteHostMap.FoundLifetime <= TimeSpan.FromMinutes(10));
    }
}
