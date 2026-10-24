using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Accounts.Business;

namespace PantryPress.Tests.Accounts;

// PP1.12: the account security rules moved out of the facades into Business (ARCH-002), with the audit's fixes.
public sealed class AccountSecurityRulesTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Only_a_confirmed_account_may_be_granted_the_platform_role(bool confirmed, bool mayBeGranted) =>
        Assert.Equal(mayBeGranted, PlatformRoleRules.MayBeGranted(confirmed));

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void A_role_already_as_asked_is_no_change(bool holds, bool grant, bool noChange) =>
        Assert.Equal(noChange, PlatformRoleRules.IsNoChange(holds, grant));

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Two_step_sign_in_needs_a_confirmed_address(bool confirmed, bool maySetUp) =>
        Assert.Equal(maySetUp, TwoFactorRules.MaySetUp(confirmed));

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    public void A_reset_clears_only_a_second_factor_set_up_before_the_address_was_confirmed(
        bool confirmedBefore,
        bool twoFactor,
        bool clears) =>
        Assert.Equal(clears, TwoFactorRules.ResetClearsSecondFactor(confirmedBefore, twoFactor));

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    public void An_account_with_two_step_sign_in_acts_only_on_a_session_that_passed_it(
        bool twoFactor,
        bool passed,
        bool mayAct) =>
        Assert.Equal(mayAct, SessionRules.MayAct(twoFactor, passed));

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public void A_second_step_counts_only_while_the_account_still_has_one(bool twoFactor, bool passed, bool counts) =>
        Assert.Equal(counts, SessionRules.PassedSecondStep(twoFactor, passed));

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void Only_the_attempt_that_locks_the_account_is_audited(bool before, bool after, bool audited) =>
        Assert.Equal(audited, LockoutRules.LockedByThisAttempt(before, after));

    [Theory]
    [InlineData(10, null, false)]
    [InlineData(10, 9L, false)]
    [InlineData(10, 10L, true)]
    [InlineData(9, 10L, true)]
    public void A_code_from_the_last_accepted_step_or_earlier_is_a_replay(long step, long? last, bool replay) =>
        Assert.Equal(replay, TwoFactorRules.IsReplay(step, last));

    [Fact]
    public void A_queued_link_email_goes_stale_with_its_link_or_with_the_account()
    {
        var asked = new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
        var stamp = LinkRules.StampFingerprint("STAMP-ONE");

        Assert.False(LinkRules.IsStale(AccountEmailKind.Verification, asked, asked.AddHours(23), null, stamp));
        Assert.True(LinkRules.IsStale(AccountEmailKind.Verification, asked, asked.AddHours(25), null, stamp));
        Assert.False(LinkRules.IsStale(AccountEmailKind.PasswordReset, asked, asked.AddMinutes(59), stamp, stamp));
        Assert.True(LinkRules.IsStale(AccountEmailKind.PasswordReset, asked, asked.AddMinutes(61), stamp, stamp));
        Assert.True(LinkRules.IsStale(AccountEmailKind.PasswordReset, asked, asked.AddMinutes(1), stamp, LinkRules.StampFingerprint("STAMP-TWO")));
        Assert.True(LinkRules.IsStale(AccountEmailKind.PasswordReset, asked, asked.AddMinutes(1), null, stamp));
        Assert.False(LinkRules.IsStale(AccountEmailKind.PasswordChanged, asked, asked.AddDays(30), null, stamp));
    }

    [Fact]
    public void A_stamp_fingerprint_is_stable_and_is_not_the_stamp()
    {
        Assert.Equal(LinkRules.StampFingerprint("STAMP-ONE"), LinkRules.StampFingerprint("STAMP-ONE"));
        Assert.NotEqual(LinkRules.StampFingerprint("STAMP-ONE"), LinkRules.StampFingerprint("STAMP-TWO"));
        Assert.DoesNotContain("STAMP", LinkRules.StampFingerprint("STAMP-ONE"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_wait_is_what_is_left_of_the_lockout_within_one_second_and_the_whole_lockout()
    {
        var now = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var lockout = TimeSpan.FromMinutes(15);

        Assert.Equal(TimeSpan.FromMinutes(7), LockoutRules.Remaining(now.AddMinutes(7), now, lockout));
        Assert.Equal(TimeSpan.FromSeconds(1), LockoutRules.Remaining(now, now, lockout));
        Assert.Equal(TimeSpan.FromSeconds(1), LockoutRules.Remaining(now.AddMinutes(-1), now, lockout));
        Assert.Equal(TimeSpan.FromSeconds(1), LockoutRules.Remaining(null, now, lockout));
        Assert.Equal(lockout, LockoutRules.Remaining(now.AddHours(2), now, lockout));
    }
}
