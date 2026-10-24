using System.Text.Json.Serialization;
using PantryPress.Domain.Managers.Outbox;

namespace PantryPress.Domain.Modules.Accounts;

/// <summary>
/// Which account email to send. Stored by name in the outbox, so renaming a member strands the messages queued under
/// the old name: add new members, never rename one.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AccountEmailKind>))]
internal enum AccountEmailKind
{
    Verification,
    PasswordReset,
    PasswordChanged,
    TwoFactorTurnedOn,
    TwoFactorTurnedOff,
    RecoveryCodesReplaced,
    RecoveryCodeUsed,
}

/// <summary>
/// An account email, through the transactional outbox (PP1.10b, backend.md). The facade stages it immediately before
/// its one save, so the email goes out if and only if the change it describes was saved, and survives a crash after the
/// save. It carries ids, not data: the address, and any link token, are read and made by the Worker when it delivers.
/// A live reset token or an email address never sits in the outbox table - a poisoned message keeps its payload for an
/// operator for 30 days.
/// </summary>
/// <param name="RequestedAt">
/// When the email was asked for, so a link email that waited longer than its link would live is not sent (PP1.12).
/// </param>
/// <param name="StampFingerprint">
/// For a reset only: a one-way fingerprint of the security stamp when the reset was asked for. A stamp that moved since
/// - a new password, a change to two-step sign-in - would have voided a link made then, so the email is not sent.
/// </param>
internal sealed record AccountEmailMessage(
    Guid AccountId,
    AccountEmailKind Kind,
    DateTimeOffset RequestedAt,
    string? StampFingerprint = null) : IOutboxMessage
{
    public static string OutboxType => "accounts.email";
}
