using System.Net;
using System.Net.Mail;

namespace PantryPress.Domain.Integrations.DevelopmentMail;

/// <summary>Where the development sink delivers: the local mail catcher's SMTP endpoint, from the AppHost.</summary>
public sealed class DevelopmentMailOptions
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public string From { get; set; } = "PantryPress <no-reply@pantrypress.localhost>";

    /// <summary>How long one send may take before it fails as transient.</summary>
    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The sink speaks plain SMTP with no TLS or credentials, so it may only reach a catcher on this machine. A
    /// misconfigured host can never turn it into a real, unencrypted mail sender.
    /// </summary>
    public bool IsValid() =>
        IsLoopback(Host)
        && Port is > 0 and <= IPEndPoint.MaxPort
        && SendTimeout > TimeSpan.Zero
        && MailAddress.TryCreate(From, out _);

    private static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));
}
