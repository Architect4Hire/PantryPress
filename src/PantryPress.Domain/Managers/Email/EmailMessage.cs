using System.Net.Mail;

namespace PantryPress.Domain.Managers.Email;

/// <summary>
/// One transactional email to one recipient (PP-D14: account, signup, contact, and billing mail only, never
/// campaigns). The sender address belongs to the gateway, not the message.
/// </summary>
public sealed record EmailMessage
{
    // RFC 5321 path limit; providers reject longer addresses anyway.
    private const int MaxAddressLength = 254;
    private const int MaxSubjectLength = 200;

    public EmailMessage(Guid id, string to, string subject, string textBody, string? htmlBody = null)
    {
        // A bad address or a control character in the subject is a bug in the caller: user input is validated long
        // before it reaches an email.
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The message needs an id.", nameof(id));
        }

        if (to.Length > MaxAddressLength
            || !MailAddress.TryCreate(to, out var address)
            || address.Address != to
            || !string.IsNullOrEmpty(address.DisplayName))
        {
            throw new ArgumentException("The recipient must be one bare email address.", nameof(to));
        }

        if (string.IsNullOrWhiteSpace(subject) || subject.Length > MaxSubjectLength || !IsOneLine(subject))
        {
            throw new ArgumentException("The subject must be one short, non-empty line.", nameof(subject));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(textBody);
        if (htmlBody is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(htmlBody);
        }

        Id = id;
        To = to;
        Subject = subject;
        TextBody = textBody;
        HtmlBody = htmlBody;
    }

    /// <summary>
    /// Chosen by the caller and stable across retries (an outbox message id from PP1.10), so the provider and the
    /// mailbox can recognize a resend of the same email.
    /// </summary>
    public Guid Id { get; }

    public string To { get; }

    public string Subject { get; }

    public string TextBody { get; }

    /// <summary>An optional HTML alternative, built by PantryPress from its own templates, never from user HTML.</summary>
    public string? HtmlBody { get; }

    /// <summary>The id only: the recipient is personal data and the bodies can carry sign-in links (backend.md).</summary>
    public override string ToString() => $"EmailMessage {{ Id = {Id} }}";

    // Control characters, plus the Unicode next-line and line and paragraph separators.
    private static bool IsOneLine(string subject) =>
        !subject.Any(c => char.IsControl(c) || c is (char)0x0085 or (char)0x2028 or (char)0x2029);
}
