using PantryPress.Domain.Managers.Email;

namespace PantryPress.Domain.Modules.Sites;

/// <summary>
/// Settings for sending invitation email, from the <c>Accounts</c> configuration section of the host that sends it - the
/// Worker (PP2.11) - because account and invitation links open one origin, <c>Accounts:AppBaseUrl</c> (auth.md; PP2.14).
/// Apart from <see cref="SitesOptions"/>, because the Worker needs only this, and the API none of it.
/// </summary>
public sealed class SiteInvitationEmailOptions
{
    /// <summary>
    /// The admin app's public origin, where an invitation's link opens (<c>https://app.pantrypress.app</c>; the gateway at
    /// <c>https://app.localhost:…</c> in local runs - the apex and <c>localhost</c> once PPL.1 lands, PP-D19). Set by the
    /// AppHost, the same origin account email links use.
    /// </summary>
    public string AppBaseUrl { get; set; } = string.Empty;

    /// <summary>An absolute origin with no path, query, or fragment; plain HTTP only on this machine.</summary>
    public bool IsValid() => EmailLinkOrigin.IsValid(AppBaseUrl);
}
