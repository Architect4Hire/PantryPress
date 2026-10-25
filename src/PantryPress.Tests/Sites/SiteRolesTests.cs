using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Tests.Sites;

// PP2.3: what each site role covers (SITE-010, auth.md), and the rule that a site keeps an active owner.
public sealed class SiteRolesTests
{
    [Theory]
    [InlineData(SiteRole.Owner, SiteRole.Owner, true)]
    [InlineData(SiteRole.Owner, SiteRole.Editor, true)]
    [InlineData(SiteRole.Owner, SiteRole.Author, true)]
    [InlineData(SiteRole.Editor, SiteRole.Owner, false)]
    [InlineData(SiteRole.Editor, SiteRole.Editor, true)]
    [InlineData(SiteRole.Editor, SiteRole.Author, true)]
    [InlineData(SiteRole.Author, SiteRole.Owner, false)]
    [InlineData(SiteRole.Author, SiteRole.Editor, false)]
    [InlineData(SiteRole.Author, SiteRole.Author, true)]
    public void A_role_covers_itself_and_those_below_it(SiteRole held, SiteRole required, bool covers) =>
        Assert.Equal(covers, SiteRoles.Covers(held, required));

    [Fact]
    public void An_unknown_role_is_a_bug_not_a_refusal() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SiteRoles.Covers((SiteRole)7, SiteRole.Author));

    [Fact]
    public void The_policies_name_each_role_once()
    {
        Assert.Equal(Enum.GetValues<SiteRole>().Order(), SitePolicies.LeastRole.Values.Order());
        Assert.Equal(SiteRole.Owner, SitePolicies.LeastRole[SitePolicies.SiteOwner]);
        Assert.Equal(SiteRole.Editor, SitePolicies.LeastRole[SitePolicies.SiteEditor]);
        Assert.Equal(SiteRole.Author, SitePolicies.LeastRole[SitePolicies.SiteAuthor]);
    }

    [Theory]
    [InlineData(SiteRole.Editor)]
    [InlineData(SiteRole.Author)]
    public void The_last_owner_cannot_be_demoted(SiteRole newRole) =>
        Assert.Equal(SiteErrors.LastOwner, SiteRoles.CanChangeRole(SiteRole.Owner, SiteMemberStatus.Active, newRole, activeOwners: 1).Error);

    [Fact]
    public void The_last_owner_cannot_be_suspended_or_removed()
    {
        Assert.Equal(SiteErrors.LastOwner, SiteRoles.CanSuspend(SiteRole.Owner, SiteMemberStatus.Active, activeOwners: 1).Error);
        Assert.Equal(SiteErrors.LastOwner, SiteRoles.CanRemove(SiteRole.Owner, SiteMemberStatus.Active, activeOwners: 1).Error);
    }

    [Fact]
    public void An_owner_with_another_beside_them_can_step_down()
    {
        Assert.True(SiteRoles.CanChangeRole(SiteRole.Owner, SiteMemberStatus.Active, SiteRole.Editor, activeOwners: 2).IsSuccess);
        Assert.True(SiteRoles.CanSuspend(SiteRole.Owner, SiteMemberStatus.Active, activeOwners: 2).IsSuccess);
        Assert.True(SiteRoles.CanRemove(SiteRole.Owner, SiteMemberStatus.Active, activeOwners: 2).IsSuccess);
    }

    [Fact]
    public void The_last_owner_keeping_the_role_is_no_change() =>
        Assert.True(SiteRoles.CanChangeRole(SiteRole.Owner, SiteMemberStatus.Active, SiteRole.Owner, activeOwners: 1).IsSuccess);

    // A suspended owner is not the one keeping the site owned, and neither is anyone who is not an owner.
    [Theory]
    [InlineData(SiteRole.Owner, SiteMemberStatus.Suspended)]
    [InlineData(SiteRole.Editor, SiteMemberStatus.Active)]
    [InlineData(SiteRole.Author, SiteMemberStatus.Active)]
    public void Members_who_are_not_active_owners_are_never_the_last_owner(SiteRole role, SiteMemberStatus status)
    {
        Assert.True(SiteRoles.CanChangeRole(role, status, SiteRole.Author, activeOwners: 1).IsSuccess);
        Assert.True(SiteRoles.CanSuspend(role, status, activeOwners: 1).IsSuccess);
        Assert.True(SiteRoles.CanRemove(role, status, activeOwners: 1).IsSuccess);
    }

    [Fact]
    public void Counting_an_active_owner_out_of_the_owners_is_a_bug() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SiteRoles.CanRemove(SiteRole.Owner, SiteMemberStatus.Active, activeOwners: 0));
}
