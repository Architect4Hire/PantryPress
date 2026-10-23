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

    /// <summary>
    /// The module dependencies this domain declares (ARCH-001). Platform asks Accounts for the Identity role it
    /// administers; nothing goes the other way, so the graph stays acyclic. Add an edge here only with a reason.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedModuleEdges = new(StringComparer.Ordinal)
    {
        ["Platform"] = ["Accounts"],
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
                foreach (var other in names.Where(n => n != module))
                {
                    foreach (var reference in Regex.Matches(source, $@"Modules\.{other}\.(\w+)").Cast<Match>())
                    {
                        Assert.True(
                            allowed.Contains(other),
                            $"{module} reaches into {other} ({file.Name}), which is not a declared module edge.");
                        Assert.True(
                            reference.Groups[1].Value == "Contracts",
                            $"{module} reaches {other}.{reference.Groups[1].Value} ({file.Name}); only Contracts is public.");
                    }
                }
            }
        }
    }

    private static DirectoryInfo ModulesDirectory([CallerFilePath] string testFile = "") =>
        new(Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(testFile)!,
            "..",
            "..",
            "PantryPress.Domain",
            "Modules")));
}
