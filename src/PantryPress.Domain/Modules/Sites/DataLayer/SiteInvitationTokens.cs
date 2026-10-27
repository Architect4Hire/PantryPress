using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites.DataLayer;

/// <summary>
/// The token in an invitation's link (PP2.11): the site and the invitation, sealed with the data protection key ring the
/// API and the Worker share - encrypted and authenticated, so nobody but PantryPress can make one or change what it names.
/// It is how accepting finds its site, as a publishing key does (tenancy.md rules 6 and 7): the site id in it is the
/// server's own, never one a client chose. It holds no expiry and is never stored: the invitation row decides whether it
/// still works, so the Worker can make it again on a redelivery and both emails' links work alike until one is used.
/// </summary>
internal sealed class SiteInvitationTokens(IDataProtectionProvider dataProtection)
{
    private const string Purpose = "PantryPress.SiteInvitation";
    private const string Version = "1";

    private readonly IDataProtector _protector = dataProtection.CreateProtector(Purpose);

    public string Create(SiteId site, SiteInvitationId invitation) =>
        Base64Url.EncodeToString(_protector.Protect(Encoding.UTF8.GetBytes(
            string.Join('\n', Version, site.Value.ToString("N"), invitation.Value.ToString("N")))));

    /// <summary>The site and invitation a token names, or false for anything that is not a token this ring sealed.</summary>
    public bool TryRead(string token, out SiteId site, out SiteInvitationId invitation)
    {
        site = default;
        invitation = default;
        string[] parts;
        try
        {
            parts = Encoding.UTF8.GetString(_protector.Unprotect(Base64Url.DecodeFromChars(token))).Split('\n');
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }

        if (parts is not [Version, var siteId, var invitationId]
            || !Guid.TryParseExact(siteId, "N", out var siteGuid)
            || !Guid.TryParseExact(invitationId, "N", out var invitationGuid)
            || siteGuid == Guid.Empty
            || invitationGuid == Guid.Empty)
        {
            return false;
        }

        site = new SiteId(siteGuid);
        invitation = new SiteInvitationId(invitationGuid);
        return true;
    }
}
