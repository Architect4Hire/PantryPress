using PantryPress.Domain.Managers.Email;

namespace PantryPress.Domain.Modules.Accounts;

/// <summary>Settings for the Accounts module, from the <c>Accounts</c> configuration section.</summary>
public sealed class AccountsOptions
{
    /// <summary>
    /// The platform home's public origin, used for links in account emails (<c>https://pantrypress.app</c>, the zone's
    /// apex; the gateway at <c>https://localhost:…</c> in local runs; PP-D19). Set by the AppHost; never derived from a request's Host header,
    /// so a forged Host cannot point a reset link somewhere else.
    /// </summary>
    public string AppBaseUrl { get; set; } = string.Empty;

    /// <summary>An absolute origin with no path, query, or fragment; plain HTTP only on this machine.</summary>
    public bool IsValid() => EmailLinkOrigin.IsValid(AppBaseUrl);
}
