using System.Globalization;
using System.Net;
using PantryPress.Domain.Managers.Email;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.Domain.Modules.Accounts.Business;

/// <summary>
/// The account emails and the admin-app links inside them. Plain language, no infrastructure words (CLAUDE.md). Tokens
/// go in the URL fragment, which browsers never send to a server, so they stay out of gateway and proxy logs; the
/// admin app reads them and posts them to the API. Pure: the caller supplies the message id.
/// </summary>
internal sealed class AccountEmails(string appBaseUrl)
{
    private readonly string _base = appBaseUrl.TrimEnd('/');

    public string VerifyEmailLink(AccountId account, string token) =>
        $"{_base}/verify-email#account={account}&token={Uri.EscapeDataString(token)}";

    public string ResetPasswordLink(AccountId account, string token) =>
        $"{_base}/reset-password#account={account}&token={Uri.EscapeDataString(token)}";

    public string ForgotPasswordLink => $"{_base}/forgot-password";

    public string SecurityLink => $"{_base}/account/security";

    public EmailMessage Verification(Guid id, string to, AccountId account, string token) =>
        Build(
            id,
            to,
            "Confirm your email for PantryPress",
            [
                "Welcome to PantryPress! Please confirm your email address so you can publish your site.",
                Link(VerifyEmailLink(account, token)),
                "This link works for 24 hours. If you didn't sign up for PantryPress, you can ignore this email.",
            ]);

    public EmailMessage PasswordReset(Guid id, string to, AccountId account, string token) =>
        Build(
            id,
            to,
            "Reset your PantryPress password",
            [
                "Someone asked to reset the password for your PantryPress account. To choose a new password, open this link:",
                Link(ResetPasswordLink(account, token)),
                "This link works for 1 hour and only once. If you didn't ask, you can ignore this email. Your password stays the same.",
            ]);

    public EmailMessage PasswordChanged(Guid id, string to) =>
        Build(
            id,
            to,
            "Your PantryPress password was changed",
            [
                "The password for your PantryPress account was just changed.",
                "If that was you, there's nothing else to do. If it wasn't, reset your password now:",
                Link(ForgotPasswordLink),
            ]);

    public EmailMessage TwoFactorTurnedOn(Guid id, string to) =>
        Build(
            id,
            to,
            "Two-step sign-in is now on",
            [
                "Two-step sign-in is now on for your PantryPress account. You'll enter a code from your authenticator app when you sign in.",
                "Keep your recovery codes somewhere safe. If this wasn't you, reset your password now:",
                Link(ForgotPasswordLink),
            ]);

    public EmailMessage TwoFactorTurnedOff(Guid id, string to) =>
        Build(
            id,
            to,
            "Two-step sign-in is now off",
            [
                "Two-step sign-in was turned off for your PantryPress account. Signing in now needs only your password.",
                "If this wasn't you, reset your password now and turn two-step sign-in back on:",
                Link(ForgotPasswordLink),
            ]);

    public EmailMessage RecoveryCodesReplaced(Guid id, string to) =>
        Build(
            id,
            to,
            "You have new recovery codes",
            [
                "New recovery codes were made for your PantryPress account. Your old codes no longer work.",
                "If this wasn't you, reset your password now:",
                Link(ForgotPasswordLink),
            ]);

    public EmailMessage RecoveryCodeUsed(Guid id, string to, int codesLeft) =>
        Build(
            id,
            to,
            "A recovery code was used to sign in",
            [
                "Someone just signed in to your PantryPress account with a recovery code. "
                    + string.Create(CultureInfo.InvariantCulture, $"You have {codesLeft} {(codesLeft == 1 ? "code" : "codes")} left."),
                "If you're running low, make new codes in your account settings:",
                Link(SecurityLink),
                "If this wasn't you, reset your password now:",
                Link(ForgotPasswordLink),
            ]);

    private static EmailMessage Build(Guid id, string to, string subject, IReadOnlyList<Paragraph> paragraphs)
    {
        var text = string.Join("\n\n", paragraphs.Select(p => p.Text)) + "\n\nThe PantryPress team\n";
        var html = string.Concat(paragraphs.Select(p => p.Html)) + "<p>The PantryPress team</p>";
        return new EmailMessage(id, to, subject, text, html);
    }

    private static Paragraph Link(string url) => new(url, $"<p><a href=\"{WebUtility.HtmlEncode(url)}\">{WebUtility.HtmlEncode(url)}</a></p>");

    private readonly record struct Paragraph(string Text, string Html)
    {
        public static implicit operator Paragraph(string text) => new(text, $"<p>{WebUtility.HtmlEncode(text)}</p>");
    }
}
