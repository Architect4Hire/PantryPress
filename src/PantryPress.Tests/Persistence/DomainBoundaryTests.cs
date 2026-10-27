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

    // PP2.2, PP2.3, PP2.9: the site, its memberships, and its hosts are the tenant root, so their tables have no query
    // filter behind them (tenancy.md, "Tenant root"). Only the Sites module reaches them - inside the domain the module
    // rules keep its root types to itself, and this keeps every host from querying the sets or the tables directly. The
    // migrations that create them are the one exception.
    // The sets in either spelling, the entity types handed to EF Core by type, the configurations, and the tables by name:
    // SiteMembers and SiteHosts anywhere (no other name shares them), Sites bracketed, quoted, schema-qualified, or after a
    // SQL keyword. The bare name SiteHost is let through, because the public tier's code speaks of the host map; its set,
    // type handed to EF Core, configuration, and table are not.
    private const string TenantRootReference =
        @"\b(?:Db)?Set<\s*(?:[\w.]+\.)?Site(?:Member|Host)?\s*>|\btypeof\(\s*(?:[\w.]+\.)?Site(?:Member|Host)?\s*\)"
        + @"|\bSite(?:Member|Host)?Configuration\b|\bSiteMembers?\b|\bSiteHosts\b"
        + @"|\[Sites\]|""Sites""|\bdbo\.Sites\b|(?i:\b(?:FROM|JOIN|UPDATE|INTO|TABLE)\s+(?:\[?dbo\]?\.)?\[?Sites\b)";

    [Fact]
    public void Only_the_sites_module_reaches_the_tenant_root_tables() =>
        AssertOnlySitesModuleReaches(TenantRootReference, "a tenant root table");

    [Theory]
    [InlineData("db.Set<SiteHost>().Where(h => h.Host == host)", true)]
    [InlineData("DbSet<PantryPress.Domain.Modules.Sites.SiteHost> Hosts", true)]
    [InlineData("model.FindEntityType(typeof(SiteHost))", true)]
    [InlineData("new SiteHostConfiguration()", true)]
    [InlineData("SELECT [SiteId] FROM [SiteHosts] WHERE [Host] = @host", true)]
    [InlineData("SiteHostKind.Custom", false)]
    [InlineData("SiteHostNames.TryNormalize(value, out var host)", false)]
    [InlineData("// Host to SiteId through SiteHost.", false)]
    public void The_tenant_root_scan_sees_the_host_table_and_not_its_contracts(string code, bool reaches) =>
        Assert.Equal(reaches, Regex.IsMatch(code, TenantRootReference));

    // PP2.8: the reserved list is seeded by migrations and never written by the app. Its rows are the database's backstop
    // under the slug policy - "app" above all - so only the Sites module and the migrations name the type, its
    // configuration, or its table.
    private const string ReservedSlugReference = @"\bReservedSlug(?:Configuration)?\b|\bReservedSlugs\b";

    [Fact]
    public void Only_the_sites_module_reaches_the_reserved_slug_table() =>
        AssertOnlySitesModuleReaches(ReservedSlugReference, "the reserved slug table");

    [Theory]
    [InlineData("db.Set<ReservedSlug>().Add(row);", true)]
    [InlineData("DELETE FROM [ReservedSlugs]", true)]
    [InlineData("SlugPolicy.Check(slug)", false)]
    public void The_reserved_slug_scan_sees_the_table_and_not_the_policy(string code, bool reaches) =>
        Assert.Equal(reaches, Regex.IsMatch(code, ReservedSlugReference));

    private static void AssertOnlySitesModuleReaches(string pattern, string what)
    {
        var source = DomainSource.Project.Parent!;
        var allowed = new[]
        {
            Path.Combine(source.FullName, "PantryPress.Domain", "Modules", "Sites"),
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
                    Regex.IsMatch(File.ReadAllText(file.FullName), pattern),
                    $"{Path.GetRelativePath(source.FullName, file.FullName)} reaches {what} outside Modules/Sites.");
            }
        }

        Assert.True(scanned > 100, "The scan found too few source files to be looking in the right place.");
    }

    // PP2.4: a request's site is set by the code that resolved it and by nothing else (tenancy.md rules 2, 4, and 7). Today
    // that is the Sites module's facades, from the route's slug and the caller's membership; the public tier's host
    // resolution (PP2.12) and the Worker's site jobs join this list when they arrive, each with its reason. The writer
    // interface and the class behind it are both named here, so neither a cast nor a direct resolve gets past.
    private const string SiteContextWriterReference = @"\bISiteContextWriter\b|(?<!\w)SiteContext\b";

    private static readonly string[] SiteContextResolvers =
    [
        Path.Combine("PantryPress.Domain", "Managers", "Tenancy"),
        Path.Combine("PantryPress.Domain", "Modules", "Sites", "Facades"),
    ];

    [Fact]
    public void Only_the_resolvers_set_the_site_context()
    {
        var source = DomainSource.Project.Parent!;
        var allowed = SiteContextResolvers.Select(r => Path.Combine(source.FullName, r)).ToList();

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
                    Regex.IsMatch(File.ReadAllText(file.FullName), SiteContextWriterReference),
                    $"{Path.GetRelativePath(source.FullName, file.FullName)} sets the site context, which only a resolver may do.");
            }
        }

        Assert.True(scanned > 100, "The scan found too few source files to be looking in the right place.");
    }

    [Theory]
    [InlineData("public sealed class Facade(ISiteContextWriter site)", true)]
    [InlineData("services.GetRequiredService<PantryPress.Domain.Managers.Tenancy.ISiteContextWriter>()", true)]
    [InlineData("var context = (SiteContext)services.GetRequiredService<ISiteContext>();", true)]
    [InlineData("services.GetRequiredService<SiteContext>().Enter(site);", true)]
    [InlineData("sp.GetRequiredService<PantryPress.Domain.Managers.Tenancy.SiteContext>().Enter(site);", true)]
    [InlineData("public sealed class Facade(ISiteContext site)", false)]
    [InlineData("services.AddSiteContext();", false)]
    [InlineData("var current = siteContext.Current;", false)]
    public void The_site_context_scan_sees_the_writer_and_not_the_reader(string code, bool sets) =>
        Assert.Equal(sets, Regex.IsMatch(code, SiteContextWriterReference));

    // PP2.5, ADR 0003: turning the site filter off is operator work, allowed only in Modules/Platform/ and only under a
    // "// Cross-site:" comment that says why (tenancy.md rule 9). Cross-site erasure lives there too (PP0.2
    // reconciliation). Every project is scanned, tests included; the editor hook says the same, but only for edits made
    // through it. Any mention of the method counts - with type arguments, its parenthesis on the next line, or as a method
    // group - and a mention in a string or comment is no less suspect.
    private static readonly string IgnoredFilters = @"\bIgnoreQuery" + @"Filters\b";

    private const string CrossSiteReason = "// Cross-site:";

    private static readonly string OperatorCode = Path.Combine("PantryPress.Domain", "Modules", "Platform") + Path.DirectorySeparatorChar;

    [Fact]
    public void Only_operator_code_turns_the_site_filter_off_and_says_why()
    {
        var source = DomainSource.Project.Parent!;

        var scanned = 0;
        foreach (var project in source.GetDirectories("PantryPress.*"))
        {
            var generated = new[] { Path.Combine(project.FullName, "bin"), Path.Combine(project.FullName, "obj") };
            foreach (var file in project.EnumerateFiles("*.cs", SearchOption.AllDirectories))
            {
                if (generated.Any(g => file.FullName.StartsWith(g + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                scanned++;
                var relative = Path.GetRelativePath(source.FullName, file.FullName);
                Assert.Empty(UnexplainedFilterBypasses(relative, File.ReadAllText(file.FullName)));
            }
        }

        Assert.True(scanned > 100, "The scan found too few source files to be looking in the right place.");
    }

    public static TheoryData<string, string, int> PlantedBypasses => new()
    {
        { Path.Combine("PantryPress.Domain", "Modules", "Sites", "Repositories", "Planted.cs"), "db.Recipes.IgnoreQuery" + "Filters().ToList();", 1 },
        { Path.Combine("PantryPress.Tests", "Planted.cs"), "var all = db.Recipes\n    .IgnoreQuery" + "Filters\n    ([\"site\"]);", 1 },
        { Path.Combine("PantryPress.Domain", "Modules", "Sites", "Planted.cs"), "var all = db.Recipes.IgnoreQuery" + "Filters<Recipe>();", 1 },
        { Path.Combine("PantryPress.Domain", "Modules", "Sites", "Planted.cs"), "Func<IQueryable<Recipe>, IQueryable<Recipe>> all = EntityFrameworkQueryableExtensions.IgnoreQuery" + "Filters;", 1 },
        { Path.Combine("PantryPress.Domain", "Modules", "Platform", "Planted.cs"), "var all = db.Recipes.IgnoreQuery" + "Filters();", 1 },
        { Path.Combine("PantryPress.Domain", "Modules", "Platform", "Planted.cs"), "// TODO: tidy this up.\nvar all = db.Recipes.IgnoreQuery" + "Filters();", 1 },
        { Path.Combine("PantryPress.Domain", "Modules", "Platform", "Planted.cs"), "// Cross-site: the operator lists every site's recipes (OPS-006).\nvar all = db.Recipes.IgnoreQuery" + "Filters();", 0 },
        { Path.Combine("PantryPress.Domain", "Modules", "Sites", "Planted.cs"), "// Cross-site: says why, in the wrong place.\nvar all = db.Recipes.IgnoreQuery" + "Filters();", 1 },
        { Path.Combine("PantryPress.Domain", "Modules", "Sites", "Planted.cs"), "var none = db.Recipes.IgnoreQueryFiltersSoon();", 0 },
    };

    [Theory]
    [MemberData(nameof(PlantedBypasses))]
    public void The_filter_scan_sees_an_unexplained_or_misplaced_bypass(string path, string code, int found) =>
        Assert.Equal(found, UnexplainedFilterBypasses(path, code).Count);

    // The lines that name the bypass where they may not: anywhere outside operator code, or in it without a cross-site
    // reason on one of the three lines above.
    private static List<int> UnexplainedFilterBypasses(string relativePath, string text)
    {
        var lines = text.Split('\n');
        var operatorCode = relativePath.StartsWith(OperatorCode, StringComparison.OrdinalIgnoreCase);
        var found = new List<int>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!Regex.IsMatch(lines[i], IgnoredFilters))
            {
                continue;
            }

            var explained = lines[Math.Max(0, i - 3)..i].Any(l => l.TrimStart().StartsWith(CrossSiteReason, StringComparison.Ordinal));
            if (!operatorCode || !explained)
            {
                found.Add(i + 1);
            }
        }

        return found;
    }

    // PP2.6: a cache key is a SiteCacheKey or a PlatformCacheKey, never a string someone built (tenancy.md rule 8). Nothing
    // caches yet, so no source file or project names a cache: not by type, namespace, registration, or package, and not
    // through a project-level alias. The first cache adapter (PP7.12) joins CacheAdapters by its exact file, takes only
    // those key types, and says why. The AppHost declares the Redis resource and caches nothing, so it is exempt.
    private const string RawCacheReference =
        @"\b(?:StackExchange\.Redis|Microsoft\.Extensions\.Caching|Microsoft\.AspNetCore\.OutputCaching|System\.Runtime\.Caching)\b"
        + @"|\b(?:I?DistributedCache|I?MemoryCache|MemoryCacheOptions|HybridCache|I?ConnectionMultiplexer|IDatabase(?:Async)?|IServer|ISubscriber|IBatch|RedisKey|RedisValue|RedisCache|IOutputCacheStore|OutputCache(?:Attribute)?)\b"
        + @"|\b(?:Add|Use)\w*(?:Redis|HybridCache|MemoryCache|DistributedCache|OutputCache)\w*";

    private const string RawCacheProjectReference =
        @"Include=""(?:StackExchange\.Redis|Microsoft\.Extensions\.Caching|Aspire\.StackExchange\.Redis|Aspire\.Microsoft\.Extensions\.Caching|Microsoft\.AspNetCore\.OutputCaching|System\.Runtime\.Caching)[^""]*""";

    private static readonly string[] CacheAdapters = [];

    [Fact]
    public void Nothing_reaches_a_cache_except_through_a_site_or_platform_key()
    {
        var source = DomainSource.Project.Parent!;
        var adapters = CacheAdapters.Select(a => Path.Combine(source.FullName, a)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var scanned = 0;
        foreach (var project in source.GetDirectories("PantryPress.*").Where(d => d.Name is not ("PantryPress.Tests" or "PantryPress.AppHost")))
        {
            var generated = new[] { Path.Combine(project.FullName, "bin"), Path.Combine(project.FullName, "obj") };
            var files = project.EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(f => f.Extension is ".cs" or ".csproj" or ".props" or ".targets")
                .Where(f => !generated.Any(g => f.FullName.StartsWith(g + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                .Where(f => !adapters.Contains(f.FullName));
            foreach (var file in files)
            {
                scanned++;
                var pattern = file.Extension == ".cs" ? RawCacheReference : RawCacheProjectReference;
                Assert.False(
                    Regex.IsMatch(File.ReadAllText(file.FullName), pattern),
                    $"{Path.GetRelativePath(source.FullName, file.FullName)} reaches a cache directly; cache through a key-typed adapter.");
            }
        }

        foreach (var shared in source.EnumerateFiles("*.props").Concat(source.Parent!.EnumerateFiles("*.props")))
        {
            Assert.DoesNotMatch(RawCacheProjectReference, File.ReadAllText(shared.FullName));
        }

        Assert.True(scanned > 100, "The scan found too few source files to be looking in the right place.");
    }

    [Theory]
    [InlineData("public sealed class PageData(IDistributedCache cache)", true)]
    [InlineData("await cache.GetOrCreateAsync(key, factory); // HybridCache", true)]
    [InlineData("services.GetRequiredService<IConnectionMultiplexer>().GetDatabase()", true)]
    [InlineData("var redis = ConnectionMultiplexer.Connect(endpoint);", true)]
    [InlineData("IDatabase redis = multiplexer.GetDatabase();", true)]
    [InlineData("foreach (var key in server.Keys(pattern: prefix)) { } // IServer", true)]
    [InlineData("var local = new MemoryCache(new MemoryCacheOptions());", true)]
    [InlineData("using StackExchange.Redis;", true)]
    [InlineData("using System.Runtime.Caching;", true)]
    [InlineData("using Microsoft.AspNetCore.OutputCaching;", true)]
    [InlineData("[OutputCache(Duration = 60)]", true)]
    [InlineData("app.UseOutputCache();", true)]
    [InlineData("builder.AddRedisClient(\"redis\");", true)]
    [InlineData("builder.AddKeyedRedisClient(\"redis\");", true)]
    [InlineData("builder.AddKeyedRedisDistributedCache(\"redis\");", true)]
    [InlineData("services.AddStackExchangeRedisCache(o => o.Configuration = endpoint);", true)]
    [InlineData("services.AddHybridCache();", true)]
    [InlineData("services.AddMemoryCache();", true)]
    [InlineData("var key = SiteCacheKey.For(siteContext, \"page-data\", version);", false)]
    [InlineData("var key = PlatformCacheKey.For(\"site-host\", host);", false)]
    [InlineData("var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Database;", false)]
    public void The_cache_scan_sees_a_raw_cache_and_not_a_keyed_one(string code, bool raw) =>
        Assert.Equal(raw, Regex.IsMatch(code, RawCacheReference));

    [Theory]
    [InlineData("<PackageReference Include=\"Aspire.StackExchange.Redis.DistributedCaching\" Version=\"13.0.0\" />", true)]
    [InlineData("<PackageReference Include=\"Microsoft.Extensions.Caching.Hybrid\" Version=\"10.0.0\" />", true)]
    [InlineData("<Using Include=\"Microsoft.Extensions.Caching.Distributed.IDistributedCache\" Alias=\"PageStore\" />", true)]
    [InlineData("<PackageReference Include=\"StackExchange.Redis\" Version=\"2.9.0\" />", true)]
    [InlineData("<PackageReference Include=\"Aspire.Hosting.Redis\" Version=\"13.6.3\" />", false)]
    public void The_cache_scan_sees_a_cache_package_or_alias(string project, bool raw) =>
        Assert.Equal(raw, Regex.IsMatch(project, RawCacheProjectReference));
}
