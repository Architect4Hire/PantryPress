using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Platform.Business;

namespace PantryPress.Tests.Platform;

// PP1.4, moved into Business at PP1.11: the rule that turns the operator's list into who holds PlatformAdmin.
public sealed class PlatformAdminReconciliationTests
{
    [Fact]
    public void A_list_that_names_no_one_is_no_opinion()
    {
        Assert.False(PlatformAdminReconciliation.HasOpinion([]));
        Assert.True(PlatformAdminReconciliation.HasOpinion(["ops@example.test"]));
    }

    [Fact]
    public void Every_holder_the_list_did_not_resolve_to_is_revoked_and_no_one_else()
    {
        var kept = new AccountId(Guid.NewGuid());
        var dropped = new AccountId(Guid.NewGuid());
        var newlyListed = new AccountId(Guid.NewGuid());

        var unlisted = PlatformAdminReconciliation.Unlisted([kept, dropped], new HashSet<AccountId> { kept, newlyListed });

        Assert.Equal([dropped], unlisted);
    }

    [Fact]
    public void A_list_that_resolved_to_no_account_revokes_every_holder()
    {
        var holder = new AccountId(Guid.NewGuid());

        Assert.Equal([holder], PlatformAdminReconciliation.Unlisted([holder], new HashSet<AccountId>()));
    }
}
