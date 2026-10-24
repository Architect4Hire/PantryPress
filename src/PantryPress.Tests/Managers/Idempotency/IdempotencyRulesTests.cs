using PantryPress.Domain.Managers.Idempotency;

namespace PantryPress.Tests.Managers.Idempotency;

// PP1.9: what a key's existing record means for the request that sent it again.
public sealed class IdempotencyRulesTests
{
    private static readonly DateTimeOffset Claimed = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Same = Fingerprint(1);
    private static readonly byte[] Other = Fingerprint(2);

    [Fact]
    public void A_finished_request_with_the_same_fingerprint_is_replayed() =>
        Assert.Equal(IdempotencyDecision.Replay, IdempotencyRules.Decide(Record(IdempotencyState.Completed), Same, Claimed.AddHours(1)));

    [Theory]
    [InlineData((int)IdempotencyState.Completed)]
    [InlineData((int)IdempotencyState.InProgress)]
    public void A_different_fingerprint_is_a_mismatch_while_the_key_is_remembered(int stateValue)
    {
        var state = (IdempotencyState)stateValue;
        Assert.Equal(IdempotencyDecision.Mismatch, IdempotencyRules.Decide(Record(state), Other, Claimed.AddMinutes(1)));

        // Even once an unfinished claim's lease has run out: reusing a key for something else is a client bug.
        Assert.Equal(IdempotencyDecision.Mismatch, IdempotencyRules.Decide(Record(state), Other, Claimed.AddHours(1)));
    }

    [Fact]
    public void An_unfinished_claim_holds_the_key_until_its_lease_ends()
    {
        var record = Record(IdempotencyState.InProgress);

        Assert.Equal(IdempotencyDecision.Wait, IdempotencyRules.Decide(record, Same, Claimed));
        Assert.Equal(IdempotencyDecision.Wait, IdempotencyRules.Decide(record, Same, record.LockedUntil.AddTicks(-1)));
        Assert.Equal(IdempotencyDecision.TakeOver, IdempotencyRules.Decide(record, Same, record.LockedUntil));
    }

    [Theory]
    [InlineData((int)IdempotencyState.Completed)]
    [InlineData((int)IdempotencyState.InProgress)]
    public void A_record_past_its_retention_is_forgotten_whatever_it_held(int stateValue)
    {
        var record = Record((IdempotencyState)stateValue);

        Assert.Equal(IdempotencyDecision.TakeOver, IdempotencyRules.Decide(record, Same, record.ExpiresAt));
        Assert.Equal(IdempotencyDecision.TakeOver, IdempotencyRules.Decide(record, Other, record.ExpiresAt));
    }

    [Fact]
    public void A_waiting_retry_is_told_to_come_back_in_a_second_not_at_the_end_of_the_lease() =>
        Assert.Equal(TimeSpan.FromSeconds(1), IdempotencyRules.RetryAfter);

    [Fact]
    public void The_defaults_are_24_hours_and_a_5_minute_lease()
    {
        var options = new IdempotencyOptions();

        Assert.Equal(TimeSpan.FromHours(24), options.Retention);
        Assert.Equal(TimeSpan.FromMinutes(5), options.Lease);
        Assert.True(options.IsValid());
        Assert.False(new IdempotencyOptions { Lease = TimeSpan.Zero }.IsValid());
        Assert.False(new IdempotencyOptions { Lease = TimeSpan.FromHours(1), Retention = TimeSpan.FromHours(1) }.IsValid());
    }

    private static IdempotencyRecord Record(IdempotencyState state) => new()
    {
        CallerScope = "account:test",
        Key = "key",
        Fingerprint = Same,
        State = state,
        LeaseToken = Guid.NewGuid(),
        CreatedAt = Claimed,
        LockedUntil = Claimed.AddMinutes(5),
        ExpiresAt = Claimed.AddHours(24),
    };

    private static byte[] Fingerprint(byte fill) => Enumerable.Repeat(fill, 32).ToArray();
}
