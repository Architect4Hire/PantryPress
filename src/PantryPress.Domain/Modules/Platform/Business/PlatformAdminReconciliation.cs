using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.Domain.Modules.Platform.Business;

/// <summary>
/// The rule that turns the operator's list into who holds <c>PlatformAdmin</c> (ACCT-005): once the list names anyone,
/// it is the whole truth - listed accounts hold the role and every other holder loses it. A list that names no one is
/// no opinion at all, so a missing or emptied secret cannot lock the operators out of their own platform.
/// </summary>
public static class PlatformAdminReconciliation
{
    /// <summary>Whether the list says anything about who holds the role.</summary>
    public static bool HasOpinion(IReadOnlyCollection<string> listed) => listed.Count > 0;

    /// <summary>
    /// The holders to revoke: every one the listed addresses did not resolve to. An address with no account yet resolves
    /// to nobody, and nobody can hold the role under it either.
    /// </summary>
    public static IReadOnlyList<AccountId> Unlisted(IEnumerable<AccountId> holders, IReadOnlySet<AccountId> listed) =>
        [.. holders.Where(holder => !listed.Contains(holder))];
}
