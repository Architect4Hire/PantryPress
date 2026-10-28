using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Tests.Sites;

// PP2.12: the host map's entries. What a host names - or that it names nothing - round-trips exactly, and anything this
// version did not write reads as a miss, so the database answers and the entry is written again.
public sealed class SiteHostMapTests
{
    private static readonly SiteId Site = new(Guid.Parse("4b7e2a10-9d3c-4f6e-8a21-5c0d9e7f1b33"));

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
        var match = new SiteHostMatch(Site, status, isCanonical, canonical);

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
    public void The_entry_says_only_the_site_its_status_and_its_canonical_host() =>
        Assert.Equal(
            "1|4b7e2a10-9d3c-4f6e-8a21-5c0d9e7f1b33|1|0|cook.pantrypress.app",
            SiteHostMap.Write(new SiteHostMatch(Site, SiteStatus.Live, false, "cook.pantrypress.app")));

    [Theory]
    [InlineData("")]
    [InlineData("--")]
    [InlineData("2|4b7e2a10-9d3c-4f6e-8a21-5c0d9e7f1b33|1|0|cook.pantrypress.app")]
    [InlineData("1|4b7e2a10-9d3c-4f6e-8a21-5c0d9e7f1b33|1|0")]
    [InlineData("1|4b7e2a10-9d3c-4f6e-8a21-5c0d9e7f1b33|1|0|cook.pantrypress.app|x")]
    [InlineData("1|4b7e2a109d3c4f6e8a215c0d9e7f1b33|1|0|cook.pantrypress.app")]
    [InlineData("1|00000000-0000-0000-0000-000000000000|1|0|cook.pantrypress.app")]
    [InlineData("1|not-a-site|1|0|cook.pantrypress.app")]
    [InlineData("1|4b7e2a10-9d3c-4f6e-8a21-5c0d9e7f1b33|9|0|cook.pantrypress.app")]
    [InlineData("1|4b7e2a10-9d3c-4f6e-8a21-5c0d9e7f1b33|-1|0|cook.pantrypress.app")]
    [InlineData("1|4b7e2a10-9d3c-4f6e-8a21-5c0d9e7f1b33| 1|0|cook.pantrypress.app")]
    [InlineData("1|4b7e2a10-9d3c-4f6e-8a21-5c0d9e7f1b33|1|2|cook.pantrypress.app")]
    [InlineData("1|4b7e2a10-9d3c-4f6e-8a21-5c0d9e7f1b33|1|0|Cook.pantrypress.app")]
    [InlineData("1|4b7e2a10-9d3c-4f6e-8a21-5c0d9e7f1b33|1|0|cook.pantrypress.app.")]
    [InlineData("1|4b7e2a10-9d3c-4f6e-8a21-5c0d9e7f1b33|1|0|127.0.0.1")]
    [InlineData("1|4b7e2a10-9d3c-4f6e-8a21-5c0d9e7f1b33|1|0|")]
    public void Anything_this_version_did_not_write_is_a_miss(string entry) =>
        Assert.False(SiteHostMap.TryRead(entry, out _));

    [Fact]
    public void A_host_that_names_nothing_is_cached_far_shorter_than_one_that_names_a_site()
    {
        Assert.True(SiteHostMap.NotFoundLifetime <= TimeSpan.FromMinutes(1));
        Assert.True(SiteHostMap.NotFoundLifetime < SiteHostMap.FoundLifetime);
        Assert.True(SiteHostMap.FoundLifetime <= TimeSpan.FromMinutes(10));
    }
}
