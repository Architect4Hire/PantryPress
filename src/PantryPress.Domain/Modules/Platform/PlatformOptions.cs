using PantryPress.Domain.Modules.Platform.Business;

namespace PantryPress.Domain.Modules.Platform;

/// <summary>Settings for operator administration, from the <c>Platform</c> configuration section.</summary>
public sealed class PlatformOptions
{
    /// <summary>
    /// Who holds <c>PlatformAdmin</c>: email addresses separated by commas, semicolons, or new lines. This list is the
    /// only way the role is assigned (ACCT-005), so it comes from the operator's own secret store and never from a
    /// request. Empty or missing means the reconciliation does nothing at all: a lost secret must not be able to
    /// revoke every operator.
    /// </summary>
    public string Admins { get; set; } = string.Empty;

    public bool IsValid() => PlatformAdminList.TryParse(Admins, out _);
}
