using System.Runtime.CompilerServices;
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

    /// <summary>
    /// The module dependencies this domain declares (ARCH-001). Platform asks Accounts for the Identity role it
    /// administers; nothing goes the other way, so the graph stays acyclic. Add an edge here only with a reason.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedModuleEdges = new(StringComparer.Ordinal)
    {
        ["Platform"] = ["Accounts"],
    };

    /// <summary>
    /// The one file allowed past another module's contracts, and why: a module's composition root registers the
    /// services it is built on, and Accounts' registration lives at its root. Nothing else in the file may use the
    /// namespace (PP1.12 closed the gap that let a root reference go unseen).
    /// </summary>
    private static readonly Dictionary<string, string> CompositionRoots = new(StringComparer.Ordinal)
    {
        ["Platform/PlatformServiceCollectionExtensions.cs"] = "AddAccountRoles, the operator's role registration",
    };

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

    // PP1.5: a source scan, not reflection. Modules live in one assembly, and a shared constant is inlined by the
    // compiler, so a module reaching into another can leave no trace in the metadata at all.
    [Fact]
    public void A_module_reaches_another_only_through_its_contracts_and_only_along_a_declared_edge()
    {
        var modules = ModulesDirectory().GetDirectories();
        var names = modules.Select(d => d.Name).ToList();
        Assert.Contains("Accounts", names);

        foreach (var directory in modules)
        {
            var module = directory.Name;
            var allowed = AllowedModuleEdges.TryGetValue(module, out var edges) ? edges : [];
            foreach (var file in directory.EnumerateFiles("*.cs", SearchOption.AllDirectories))
            {
                var source = File.ReadAllText(file.FullName);
                var relative = Path.GetRelativePath(ModulesDirectory().FullName, file.FullName).Replace('\\', '/');
                foreach (var other in names.Where(n => n != module))
                {
                    foreach (var segment in ReferencesTo(source, other))
                    {
                        Assert.True(
                            allowed.Contains(other),
                            $"{module} reaches into {other} ({file.Name}), which is not a declared module edge.");
                        Assert.True(
                            segment == "Contracts" || (segment.Length == 0 && CompositionRoots.ContainsKey(relative)),
                            $"{module} reaches {other}{(segment.Length == 0 ? " at its root" : "." + segment)} ({file.Name}); "
                            + "only Contracts is public.");
                    }
                }
            }
        }
    }

    // PP1.12: the scan sees a reference to another module's root namespace as well as to its folders - the first
    // version needed a segment after the module name, so a using of the bare namespace went unseen.
    [Theory]
    [InlineData("using PantryPress.Domain.Modules.Accounts;", "")]
    [InlineData("using PantryPress.Domain.Modules.Accounts.Contracts;", "Contracts")]
    [InlineData("var user = new PantryPress.Domain.Modules.Accounts.ApplicationUser();", "ApplicationUser")]
    [InlineData("global using static PantryPress.Domain.Modules.Accounts.Business.Totp;", "Business")]
    [InlineData("var code = Accounts.Business.Totp.Compute(key, step);", "Business")]
    [InlineData("throw new Exception(Accounts.Contracts.AccountErrors.AccountNotFound.Title);", "Contracts")]
    public void The_scan_sees_every_way_into_another_module(string source, string segment) =>
        Assert.Equal([segment], ReferencesTo(source, "Accounts"));

    [Fact]
    public void The_scan_does_not_mistake_a_longer_module_name_for_this_one() =>
        Assert.Empty(ReferencesTo("using PantryPress.Domain.Modules.AccountsArchive.Contracts;", "Accounts"));

    // PP1.10: the outbox table holds every site's messages with no query filter, so only the outbox itself may name it.
    // Modules write through IOutbox and read nothing; an operator view, when PP19 adds one, is a declared exception.
    [Fact]
    public void Only_the_outbox_reaches_its_unfiltered_table()
    {
        var domain = ModulesDirectory().Parent!;
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
        var source = ModulesDirectory().Parent!.Parent!;
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

    // The segment after the module's name in each reference to it: "Contracts", a folder or type, or "" for the root.
    // Written in full (Modules.Accounts...) or relative to the shared parent namespace (Accounts.Business...), which code
    // in another module's namespace can use without the prefix; the relative form needs a segment, so prose that names a
    // module is not a reference.
    private static List<string> ReferencesTo(string source, string module) =>
        [.. Regex.Matches(source, $@"Modules\.{module}(?![\w])(?:\.(?<seg>\w+))?|(?<![\w.]){module}\.(?<seg>\w+)")
            .Select(m => m.Groups["seg"].Value)];

    private static DirectoryInfo ModulesDirectory([CallerFilePath] string testFile = "") =>
        new(Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(testFile)!,
            "..",
            "..",
            "PantryPress.Domain",
            "Modules")));
}
