using System.Buffers.Binary;
using System.Buffers.Text;
using FluentValidation;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Validation;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Domain.Modules.Sites.Validation;

namespace PantryPress.Tests.Sites;

// PP2.11b: the pure rules behind an Owner managing members - the member list's cursor, what a page and a new role may be,
// and the audit entries a role change and a removal leave.
public sealed class SiteMemberRulesTests
{
    private static readonly IValidator<ListSiteMembersQuery> ListValidator = new ListSiteMembersQueryValidator();
    private static readonly IValidator<ChangeMemberRoleRequest> RoleValidator = new ChangeMemberRoleRequestValidator();

    public static TheoryData<DateTimeOffset> JoinTimes => new()
    {
        new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero).AddTicks(1_234_567),
        DateTimeOffset.MinValue,
        DateTimeOffset.MaxValue,
    };

    [Theory]
    [MemberData(nameof(JoinTimes))]
    public void A_cursor_reads_back_the_position_it_was_made_from(DateTimeOffset joinedAt)
    {
        var position = new SiteMemberPosition(joinedAt, new AccountId(Guid.NewGuid()));

        var cursor = SiteMemberCursor.After(position);

        Assert.True(SiteMemberCursor.TryRead(cursor, out var read));
        Assert.Equal(position, read);
        Assert.Equal(TimeSpan.Zero, read.JoinedAt.Offset);
    }

    // The same instant written with another offset is the same position: SQL compares the instant.
    [Fact]
    public void A_cursor_names_an_instant_not_an_offset()
    {
        var account = new AccountId(Guid.NewGuid());
        var utc = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            SiteMemberCursor.After(new SiteMemberPosition(utc, account)),
            SiteMemberCursor.After(new SiteMemberPosition(utc.ToOffset(TimeSpan.FromHours(-5)), account)));
    }

    public static TheoryData<string?> NotCursors()
    {
        var real = SiteMemberCursor.After(new SiteMemberPosition(DateTimeOffset.UnixEpoch, new AccountId(Guid.NewGuid())));
        Span<byte> outOfRange = stackalloc byte[24];
        BinaryPrimitives.WriteInt64BigEndian(outOfRange, long.MaxValue);
        return new TheoryData<string?>
        {
            null,
            string.Empty,
            "not-a-cursor",
            "cGll",
            real + "A",
            real[..^1],
            real + "=",
            Base64Url.EncodeToString(new byte[23]),
            Base64Url.EncodeToString(new byte[25]),
            Base64Url.EncodeToString(outOfRange),
            new string('A', 200),
        };
    }

    [Theory]
    [MemberData(nameof(NotCursors))]
    public void Anything_else_is_not_a_cursor(string? cursor) => Assert.False(SiteMemberCursor.TryRead(cursor, out _));

    [Theory]
    [InlineData(null, false, true)]
    [InlineData(1, false, true)]
    [InlineData(100, false, true)]
    [InlineData(0, false, false)]
    [InlineData(101, false, false)]
    [InlineData(-1, false, false)]
    [InlineData(null, true, true)]
    public async Task A_page_asks_for_1_to_100_members_after_a_cursor_this_made(int? limit, bool withCursor, bool valid)
    {
        var cursor = withCursor ? SiteMemberCursor.After(new SiteMemberPosition(DateTimeOffset.UnixEpoch, new AccountId(Guid.NewGuid()))) : null;

        Assert.Equal(valid, await ListValidator.CheckAsync(new ListSiteMembersQuery(limit, cursor), CancellationToken.None) is null);
    }

    [Theory]
    [InlineData("cGll")]
    [InlineData("")]
    public async Task A_cursor_this_did_not_make_is_named(string cursor)
    {
        var error = await ListValidator.CheckAsync(new ListSiteMembersQuery(null, cursor), CancellationToken.None);

        Assert.Equal(["cursor"], error!.Fields.Keys);
    }

    [Theory]
    [InlineData(SiteRole.Owner, true)]
    [InlineData(SiteRole.Editor, true)]
    [InlineData(SiteRole.Author, true)]
    [InlineData(null, false)]
    [InlineData((SiteRole)3, false)]
    [InlineData((SiteRole)(-1), false)]
    public async Task A_new_role_is_one_of_the_three(SiteRole? role, bool valid)
    {
        var error = await RoleValidator.CheckAsync(new ChangeMemberRoleRequest(role), CancellationToken.None);

        Assert.Equal(valid, error is null);
        if (!valid)
        {
            Assert.Equal(["role"], error!.Fields.Keys);
        }
    }

    // An entry is ids and codes only: the member is the account it targets, and the change is codes in its reason.
    [Theory]
    [InlineData(SiteRole.Author, SiteRole.Owner, "role:author->owner")]
    [InlineData(SiteRole.Owner, SiteRole.Editor, "role:owner->editor")]
    [InlineData(SiteRole.Editor, SiteRole.Author, "role:editor->author")]
    public void A_role_change_is_audited_against_the_member_with_both_roles(SiteRole was, SiteRole now, string reason)
    {
        var owner = new AccountId(Guid.NewGuid());
        var member = new AccountId(Guid.NewGuid());

        var entry = SiteAudit.RoleChanged(owner, member, was, now);

        Assert.Equal(SiteAudit.MemberRoleChanged, entry.Action);
        Assert.Equal(AuditActor.Account(owner.Value), entry.Actor);
        Assert.Equal(AccountAudit.Target(member), entry.Target);
        Assert.Equal(reason, entry.Reason);
        Assert.True(AuditRules.IsValidAction(entry.Action) && AuditRules.IsValidTarget(entry.Target) && AuditRules.IsValidReason(entry.Reason));
    }

    [Fact]
    public void A_removal_is_audited_against_the_member()
    {
        var owner = new AccountId(Guid.NewGuid());
        var member = new AccountId(Guid.NewGuid());

        var entry = SiteAudit.Removed(owner, member);

        Assert.Equal(
            (SiteAudit.MemberRemoved, AuditActor.Account(owner.Value), AccountAudit.Target(member), (string?)null),
            (entry.Action, entry.Actor, entry.Target, entry.Reason));
        Assert.StartsWith(AuditRules.SiteActionPrefix, entry.Action, StringComparison.Ordinal);
    }

    [Fact]
    public void A_role_that_is_none_of_the_three_is_a_bug_in_an_audit_entry() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SiteAudit.RoleChanged(new AccountId(Guid.NewGuid()), new AccountId(Guid.NewGuid()), SiteRole.Owner, (SiteRole)3));

    // The member list asks the account directory for one page's addresses at a time, so a page may never be larger than
    // the directory answers for.
    [Fact]
    public void A_page_of_members_fits_what_the_account_directory_answers_for() =>
        Assert.True(SiteMemberCursor.MaxLimit <= IAccountDirectory.MaxAccounts);
}
