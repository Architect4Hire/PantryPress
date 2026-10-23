using System.Collections.Frozen;

namespace PantryPress.Domain.Modules.Accounts.Business;

/// <summary>
/// Disposable-email blocking at sign-up (ACCT-003, default yes). The list is the CC0 blocklist from
/// github.com/disposable-email-domains/disposable-email-domains (commit b5e699f, fetched 2026-10-23), embedded so no
/// request depends on a remote service. Refresh it by replacing <c>disposable-email-domains.txt</c>.
/// </summary>
internal static class DisposableEmailDomains
{
    private const string ResourceName = "PantryPress.Accounts.DisposableEmailDomains";

    private static readonly Lazy<FrozenSet<string>> Domains = new(Load);

    public static int Count => Domains.Value.Count;

    /// <summary>
    /// True when the address's domain, or any parent domain, is on the list: providers hand out unlimited
    /// subdomains (<c>anything.example-disposable.com</c>).
    /// </summary>
    public static bool IsDisposable(string email)
    {
        var domain = EmailAddressRules.DomainOf(email);
        while (true)
        {
            if (Domains.Value.Contains(domain))
            {
                return true;
            }

            var dot = domain.IndexOf('.', StringComparison.Ordinal);
            if (dot < 0)
            {
                return false;
            }

            domain = domain[(dot + 1)..];
        }
    }

    private static FrozenSet<string> Load()
    {
        using var stream = typeof(DisposableEmailDomains).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded resource '{ResourceName}' is missing.");
        using var reader = new StreamReader(stream);

        var domains = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length > 0 && !line.StartsWith('#'))
            {
                domains.Add(line.ToLowerInvariant());
            }
        }

        return domains.ToFrozenSet(StringComparer.Ordinal);
    }
}
