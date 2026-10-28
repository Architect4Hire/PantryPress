using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Domain.Modules.Sites.Facades;

namespace PantryPress.Tests.Sites;

// PP2.13 (SITE-008, SEO-007): what each status means to a reader. Open, coming soon, unavailable, or gone - and the two
// statuses that are private matters share an answer with another, so a reader cannot tell them apart.
public sealed class PublicSiteStatusTests
{
    private static readonly PublicSiteIdentity Supper = new("Sunday Supper", "Weeknight dinners", "en-US");

    public static TheoryData<SiteStatus, string?, ErrorKind?, int?> Answers() => new()
    {
        { SiteStatus.Live, null, null, null },
        { SiteStatus.ReadOnly, null, null, null },
        { SiteStatus.Provisioning, "site-coming-soon", ErrorKind.Unavailable, 30 },
        { SiteStatus.Suspended, "site-unavailable", ErrorKind.Unavailable, 3600 },
        { SiteStatus.Offline, "site-unavailable", ErrorKind.Unavailable, 3600 },
        { SiteStatus.Deleted, "site-gone", ErrorKind.Gone, null },
    };

    [Theory]
    [MemberData(nameof(Answers))]
    public void Each_status_has_its_public_answer(SiteStatus status, string? code, ErrorKind? kind, int? retryAfterSeconds)
    {
        var refusal = PublicSiteStatus.RefusalFor(status);

        Assert.Equal(code, refusal?.Code);
        Assert.Equal(kind, refusal?.Kind);
        Assert.Equal(retryAfterSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null, refusal?.RetryAfter);
    }

    [Fact]
    public void Every_status_is_answered()
    {
        var answered = Answers().Select(row => (SiteStatus)row[0]).ToHashSet();

        Assert.Equal(Enum.GetValues<SiteStatus>().ToHashSet(), answered);
    }

    [Fact]
    public void Suspended_and_offline_are_one_answer_and_live_and_read_only_another()
    {
        Assert.Same(PublicSiteStatus.RefusalFor(SiteStatus.Suspended), PublicSiteStatus.RefusalFor(SiteStatus.Offline));
        Assert.Null(PublicSiteStatus.RefusalFor(SiteStatus.Live));
        Assert.Null(PublicSiteStatus.RefusalFor(SiteStatus.ReadOnly));
    }

    [Fact]
    public void A_refusals_title_is_plain_and_names_no_reason()
    {
        foreach (var status in Enum.GetValues<SiteStatus>())
        {
            if (PublicSiteStatus.RefusalFor(status) is not { } refusal)
            {
                continue;
            }

            foreach (var word in new[] { "suspend", "payment", "billing", "offline", "delete", "trial" })
            {
                Assert.DoesNotContain(word, refusal.Title, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void An_open_site_is_given_its_public_context_and_nothing_else()
    {
        var site = Site(SiteStatus.ReadOnly, "www.example.com", "example.com", isCanonical: false);

        var context = FacadeFor(site).GetContext(site);

        Assert.True(context.IsSuccess);
        Assert.Equal(new PublicSiteDetail("Sunday Supper", "Weeknight dinners", "en-US", "example.com", false), context.Value);
        Assert.Equal(
            ["CanonicalHost", "IsCanonical", "Locale", "Name", "Tagline"],
            typeof(PublicSiteDetail).GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(SiteStatus.Provisioning)]
    [InlineData(SiteStatus.Suspended)]
    [InlineData(SiteStatus.Offline)]
    [InlineData(SiteStatus.Deleted)]
    public void A_closed_site_is_refused_its_context_with_its_status_answer(SiteStatus status)
    {
        var site = Site(status, "cook.pantrypress.app", "cook.pantrypress.app", isCanonical: true);
        var facade = FacadeFor(site);

        Assert.Same(PublicSiteStatus.RefusalFor(status), facade.CheckStatus(site).Error);
        Assert.Same(PublicSiteStatus.RefusalFor(status), facade.GetContext(site).Error);
    }

    [Fact]
    public void A_site_the_request_did_not_resolve_is_a_bug_not_an_answer()
    {
        var resolved = Site(SiteStatus.Live, "cook.pantrypress.app", "cook.pantrypress.app", isCanonical: true);
        var other = Site(SiteStatus.Live, "bake.pantrypress.app", "bake.pantrypress.app", isCanonical: true);
        var facade = FacadeFor(resolved);

        Assert.Throws<InvalidOperationException>(() => facade.CheckStatus(other));
        Assert.Throws<InvalidOperationException>(() => facade.GetContext(other));
        Assert.Throws<InvalidOperationException>(() => new PublicSitesFacade(new Entered(null)).GetContext(resolved));
    }

    private static PublicSitesFacade FacadeFor(PublicSite site) => new(new Entered(site.SiteId));

    private static PublicSite Site(SiteStatus status, string host, string canonical, bool isCanonical) =>
        new(new SiteId(Guid.NewGuid()), host, canonical, isCanonical, status, Supper);

    // The request's site context, as host resolution entered it.
    private sealed class Entered(SiteId? site) : ISiteContext
    {
        public SiteId? Current { get; } = site;

        public SiteId Required => Current ?? throw new InvalidOperationException("No site.");
    }
}
