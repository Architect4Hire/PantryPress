using Microsoft.AspNetCore.DataProtection;

namespace PantryPress.Domain.Modules.Accounts.Repositories;

/// <summary>
/// Stands in for the key ring in the operator's process (<c>AddAccountRoles</c>), which administers the platform role
/// and nothing else. <see cref="AccountUserStore"/> needs a provider to be constructed, but granting a role never
/// touches an authenticator key. Refusing loudly keeps a future change from protecting real data with a key ring that
/// process does not own, which would leave it unreadable elsewhere.
/// </summary>
internal sealed class UnusedDataProtection : IDataProtectionProvider, IDataProtector
{
    public IDataProtector CreateProtector(string purpose) => this;

    public byte[] Protect(byte[] plaintext) => throw Unsupported();

    public byte[] Unprotect(byte[] protectedData) => throw Unsupported();

    private static InvalidOperationException Unsupported() => new(
        "This process registered Accounts for platform-role administration only and has no data protection key ring.");
}
