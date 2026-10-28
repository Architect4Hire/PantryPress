using System.Globalization;
using System.Net;
using PantryPress.Domain.Managers.Email;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites.Business;

/// <summary>
/// The invitation email and the admin-app link inside it (SITE-010; PP2.11). Plain language, no infrastructure words
/// (CLAUDE.md). The token goes in the URL fragment, which browsers never send to a server, so it stays out of gateway and
/// proxy logs; the admin app reads it and posts it to the API. The site's name is the blogger's own text, one line by
/// rule, and is encoded in the HTML part. Pure: the caller supplies the message id and the token.
/// </summary>
internal sealed class SiteInvitationEmails(string appBaseUrl)
{
    private readonly string _base = appBaseUrl.TrimEnd('/');

    public string JoinLink(string token) => $"{_base}/dashboard/join#token={Uri.EscapeDataString(token)}";

    public EmailMessage Invitation(Guid id, string to, string siteName, SiteRole role, int days, string token)
    {
        string[] paragraphs =
        [
            $"You're invited to help run \"{siteName}\" on PantryPress, as {RoleInWords(role)}.",
            "To accept, open this link and sign in with this email address. If you don't have a PantryPress account yet, "
                + "you can create one with this address first:",
        ];
        string[] after =
        [
            string.Create(CultureInfo.InvariantCulture, $"This invitation works for {days} {(days == 1 ? "day" : "days")} and only once.")
                + " If you weren't expecting it, you can ignore this email.",
        ];

        var link = JoinLink(token);
        var text = string.Join("\n\n", [.. paragraphs, link, .. after]) + "\n\nThe PantryPress team\n";
        var html = string.Concat(paragraphs.Select(Html))
            + $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">{WebUtility.HtmlEncode(link)}</a></p>"
            + string.Concat(after.Select(Html))
            + "<p>The PantryPress team</p>";
        return new EmailMessage(id, to, "You're invited to help run a site on PantryPress", text, html);
    }

    private static string RoleInWords(SiteRole role) => role switch
    {
        SiteRole.Owner => "an owner, who can do everything, including billing and members",
        SiteRole.Editor => "an editor, who can work on all posts, recipes, design, and comments",
        SiteRole.Author => "an author, who can write their own posts",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Not a site role."),
    };

    private static string Html(string paragraph) => $"<p>{WebUtility.HtmlEncode(paragraph)}</p>";
}
