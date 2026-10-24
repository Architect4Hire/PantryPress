using PantryPress.Domain.Managers.Email;

namespace PantryPress.Domain.Modules.Accounts.DataLayer;

/// <summary>
/// Account email's way to the provider (ARCH-002: the data layer composes repositories and gateways). Registered only
/// where account email is sent - the Worker (PP1.10b) - so no other host needs an email gateway.
/// </summary>
internal sealed class AccountMailDataLayer(IEmailGateway gateway)
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
        gateway.SendAsync(message, cancellationToken);
}
