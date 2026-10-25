using System.Text.RegularExpressions;
using PantryPress.Domain.Managers.Persistence;

namespace PantryPress.Tests.Persistence;

// PP0.3, PP0.6: Domain references no host project, no web or Aspire framework, and no database provider. The SQL
// Server provider belongs to the hosts that configure the context (backend.md).
// PP1.1: the one exception is Identity's EF Core stores. Despite the name it is not the web framework; it depends only
// on Microsoft.Extensions.Identity.Stores and EF Core Relational, and it maps the Identity tables (ACCT-001).
// PP1.2: the data protection abstractions (interfaces only, no web framework) let Accounts protect link tokens and
// authenticator keys; each host configures the key ring.
// PP1.10b: background work is the Worker's, through the outbox, so Domain hosts nothing - no BackgroundService, no
// hosted service - and references no Microsoft.Extensions.Hosting at all.
// PP2.1: the boundaries between and inside modules are ModuleBoundaryTests'.
public sealed class DomainBoundaryTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Microsoft.EntityFrameworkCore.SqlServer",
        "Microsoft.Data.SqlClient",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions.Hosting",
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

    // PP1.10: the outbox table holds every site's messages with no query filter, so only the outbox itself may name it.
    // Modules write through IOutbox and read nothing; an operator view, when PP19 adds one, is a declared exception.
    [Fact]
    public void Only_the_outbox_reaches_its_unfiltered_table()
    {
        var domain = DomainSource.Project;
        var outbox = Path.Combine(domain.FullName, "Managers", "Outbox") + Path.DirectorySeparatorChar;
        var generated = new[] { Path.Combine(domain.FullName, "bin"), Path.Combine(domain.FullName, "obj") };

        foreach (var file in domain.EnumerateFiles("*.cs", SearchOption.AllDirectories))
        {
            if (file.FullName.StartsWith(outbox, StringComparison.OrdinalIgnoreCase)
                || generated.Any(g => file.FullName.StartsWith(g, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            Assert.DoesNotMatch(@"\bOutboxMessage\b|\bOutboxState\b", File.ReadAllText(file.FullName));
        }
    }

    // PP1.11: the audit log holds every site's and account's entries with no query filter, and nothing may change one.
    // Modules record through IAuditLog and read nothing; the context names the guard, never the entry. Every project is
    // scanned, by type and by table name, so a host cannot read the table with raw SQL either. The migrations that create
    // it are the one exception, and the operator console's reader (PP19) will be a declared one.
    [Fact]
    public void Only_the_audit_log_reaches_its_unfiltered_table()
    {
        var source = DomainSource.Project.Parent!;
        var allowed = new[]
        {
            Path.Combine(source.FullName, "PantryPress.Domain", "Managers", "Audit"),
            Path.Combine(source.FullName, "PantryPress.MigrationService", "Migrations"),
        };

        var scanned = 0;
        foreach (var project in source.GetDirectories("PantryPress.*").Where(d => d.Name != "PantryPress.Tests"))
        {
            var generated = new[] { Path.Combine(project.FullName, "bin"), Path.Combine(project.FullName, "obj") };
            foreach (var file in project.EnumerateFiles("*.cs", SearchOption.AllDirectories))
            {
                if (allowed.Concat(generated).Any(a => file.FullName.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                scanned++;
                Assert.False(
                    Regex.IsMatch(File.ReadAllText(file.FullName), @"\bAuditEntry\b|\bAuditEntryConfiguration\b|\bAuditEntries\b"),
                    $"{Path.GetRelativePath(source.FullName, file.FullName)} reaches the audit table outside Managers/Audit.");
            }
        }

        Assert.True(scanned > 100, "The scan found too few source files to be looking in the right place.");
    }
}
