using Microsoft.AspNetCore.Identity;

namespace PantryPress.Domain.Modules.Accounts;

/// <summary>
/// A PantryPress account (ACCT-001). Platform data with no <c>SiteId</c>: one account can belong to many sites, and
/// site roles come from membership, not Identity roles. The key is a <see cref="Guid"/> because Identity's stores
/// convert keys to and from strings; a typed id arrives at the Accounts contracts when another module needs one.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>;
