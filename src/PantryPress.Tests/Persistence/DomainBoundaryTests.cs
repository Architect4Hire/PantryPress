using PantryPress.Domain.Managers.Persistence;

namespace PantryPress.Tests.Persistence;

// PP0.3, PP0.6: Domain references no host project, no web or Aspire framework, and no database provider. The SQL
// Server provider belongs to the hosts that configure the context (backend.md).
// PP1.1: the one exception is Identity's EF Core stores. Despite the name it is not the web framework; it depends only
// on Microsoft.Extensions.Identity.Stores and EF Core Relational, and it maps the Identity tables (ACCT-001).
// PP1.2: the data protection abstractions (interfaces only, no web framework) let Accounts protect link tokens and
// authenticator keys; each host configures the key ring.
public sealed class DomainBoundaryTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Microsoft.EntityFrameworkCore.SqlServer",
        "Microsoft.Data.SqlClient",
        "Microsoft.AspNetCore",
        "Aspire",
        "PantryPress.",
    ];

    private static readonly string[] AllowedExceptions =
    [
        "Microsoft.AspNetCore.Identity.EntityFrameworkCore",
        "Microsoft.AspNetCore.DataProtection.Abstractions",
    ];

    [Fact]
    public void Domain_references_no_host_framework_or_database_provider()
    {
        var referenced = typeof(PantryPressDbContext).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty);

        Assert.DoesNotContain(
            referenced,
            name => !AllowedExceptions.Contains(name)
                && ForbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));
    }
}
