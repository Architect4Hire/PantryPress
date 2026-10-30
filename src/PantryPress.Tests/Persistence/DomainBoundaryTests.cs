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

    // PP4.1: the recipe vocabulary is seeded by migrations and never written by the app. Recipes will store its codes under
    // foreign keys (PP4.3), so only the Recipes module and the migrations name its types handed to EF Core, its
    // configurations, or its tables; other modules ask a Recipes contract. The lists in Business and the bare type names
    // are let through, since "Unit" and "Course" are ordinary words.
    private const string VocabularyTypes = @"(?:Unit|UnitAlias|Course|Cuisine|Diet|Allergen)";
    private const string VocabularyTables = @"(?:Units|UnitAliases|Courses|Cuisines|Diets|Allergens)";

    private const string RecipeVocabularyReference =
        @"\b(?:(?:Db)?Set|Find(?:Async)?|Entry)<\s*(?:[\w.]+\.)?" + VocabularyTypes + @"\s*>"
        + @"|\btypeof\(\s*(?:[\w.]+\.)?" + VocabularyTypes + @"\s*\)"
        + @"|\b" + VocabularyTypes + @"Configuration\b|\bUnitAliases\b"
        + @"|\[" + VocabularyTables + @"\]|""" + VocabularyTables + @"""|\bdbo\." + VocabularyTables + @"\b"
        + @"|(?i:\b(?:FROM|JOIN|UPDATE|INTO|TABLE|DELETE|MERGE)\s+(?:\[?dbo\]?\.)?\[?" + VocabularyTables + @"\b)";

    [Fact]
    public void Only_the_recipes_module_reaches_the_vocabulary_tables() =>
        AssertOnlyModuleReaches("Recipes", RecipeVocabularyReference, "a recipe vocabulary table");

    [Theory]
    [InlineData("db.Set<Unit>().Where(u => u.Code == code)", true)]
    [InlineData("DbSet<PantryPress.Domain.Modules.Recipes.Course> Courses", true)]
    [InlineData("model.FindEntityType(typeof(UnitAlias))", true)]
    [InlineData("new DietConfiguration()", true)]
    [InlineData("SELECT [Code] FROM [Allergens]", true)]
    [InlineData("select code from cuisines", true)]
    [InlineData("INSERT INTO UnitAliases VALUES (N'tbsp')", true)]
    [InlineData("DELETE Diets WHERE Code = 'keto'", true)]
    [InlineData("MERGE Courses AS t USING source AS s ON t.Code = s.Code", true)]
    [InlineData("await db.FindAsync<Unit>(code)", true)]
    [InlineData("db.Entry<Cuisine>(row)", true)]
    [InlineData("Units.All.Select(u => u.Code)", false)]
    [InlineData("UnitDimension.Volume", false)]
    [InlineData("// One unit per course of the meal.", false)]
    public void The_vocabulary_scan_sees_the_tables_and_not_the_lists(string code, bool reaches) =>
        Assert.Equal(reaches, Regex.IsMatch(code, RecipeVocabularyReference));

    private static void AssertOnlySitesModuleReaches(string pattern, string what) => AssertOnlyModuleReaches("Sites", pattern, what);

    private static void AssertOnlyModuleReaches(string module, string pattern, string what)
    {
        var source = DomainSource.Project.Parent!;
        var allowed = new[]
        {
            Path.Combine(source.FullName, "PantryPress.Domain", "Modules", module),
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
                    $"{Path.GetRelativePath(source.FullName, file.FullName)} reaches {what} outside Modules/{module}.");
            }
        }

        Assert.True(scanned > 100, "The scan found too few source files to be looking in the right place.");
    }

    // PP2.4: a request's site is set by the code that resolved it and by nothing else (tenancy.md rules 2, 4, and 7). Each
    // resolver is listed by its exact file, with its reason, so a new facade beside them cannot enter a site unnoticed. The
    // writer
    // interface and the class behind it are both named here, so neither a cast nor a direct resolve gets past.
    private const string SiteContextWriterReference = @"\bISiteContextWriter\b|(?<!\w)SiteContext\b";

    private static readonly string[] SiteContextResolvers =
    [
        // The context itself: the writer's declaration, the class behind it, its registration, and the check that a
        // context is that class - by file, so a new kernel file cannot enter a site unnoticed either (PP2.14).
        Path.Combine("PantryPress.Domain", "Managers", "Tenancy", "ISiteContext.cs"),
        Path.Combine("PantryPress.Domain", "Managers", "Tenancy", "SiteContext.cs"),
        Path.Combine("PantryPress.Domain", "Managers", "Tenancy", "TenancyServiceCollectionExtensions.cs"),
        Path.Combine("PantryPress.Domain", "Managers", "Tenancy", "ResolvedSiteContext.cs"),

        // An admin route's site, from its slug and the caller's membership (PP2.4).
        Path.Combine("PantryPress.Domain", "Modules", "Sites", "Facades", "SiteMembershipFacade.cs"),

        // A site the request has just created, with a server-minted id and the caller as its Owner (PP2.10).
        Path.Combine("PantryPress.Domain", "Modules", "Sites", "Facades", "SitesFacade.cs"),

        // The site sealed inside an invitation's link, which the caller is not yet a member of (PP2.11).
        Path.Combine("PantryPress.Domain", "Modules", "Sites", "Facades", "SiteInvitationAcceptance.cs"),

        // A public request's site, from its Host through the host map, and nothing else (PP2.12).
        Path.Combine("PantryPress.Domain", "Modules", "Sites", "Facades", "PublicSiteResolution.cs"),

        // The Worker's resolver: an outbox delivery's scope works on the site its message was staged under (PP2.11).
        Path.Combine("PantryPress.Domain", "Managers", "Outbox", "OutboxHandlerRegistration.cs"),
    ];

    // PP3.9a (architecture review): a DI scope of its own is how work gets a site context of its own, so a scope factory
    // in the wrong hands is a way around "one scope, one site" (tenancy.md rule 2). The three files that may make one are
    // listed, with each one's reason, as the resolvers are.
    private const string ScopeFactoryReference = @"\bIServiceScopeFactory\b|\bCreateAsyncScope\b|\bCreateScope\b";

    private static readonly string[] ScopeFactories =
    [
        // The outbox dispatcher: each delivery runs in its own scope, on the site its message was staged under (PP1.10).
        Path.Combine("PantryPress.Domain", "Managers", "Outbox", "OutboxStore.cs"),

        // The idempotency store: a key is completed outside the request's own transaction (PP1.9).
        Path.Combine("PantryPress.Domain", "Managers", "Idempotency", "IdempotencyStore.cs"),

        // The development seed: one scope per site it creates, because a scope may enter only one (PP3.9a).
        Path.Combine("PantryPress.Domain", "Modules", "Platform", "Facades", "DevelopmentSeeder.cs"),
    ];

    // PP2.12 (isolation audit): the public tier's resolver enters a site from a Host with no membership behind it, which is
    // right only on the anonymous public host. So only the Sites module and the Site host name it or its registration, and
    // only the Site host's resolution middleware names the holder its endpoints read the site from.
    private const string PublicResolutionReference = @"\bIPublicSiteResolution\b|\bAddSitePublicResolution\b";

    private const string PublicSiteHolderReference = @"(?<![\w])PublicSiteContext\b";

    [Fact]
    public void Only_the_public_site_resolves_a_site_from_a_host()
    {
        AssertOnlyNamedIn(
            PublicResolutionReference,
            [Path.Combine("PantryPress.Domain", "Modules", "Sites"), "PantryPress.Site"],
            "resolves a site from a Host, which only the public site may do");
        AssertOnlyNamedIn(
            PublicSiteHolderReference,
            [Path.Combine("PantryPress.Site", "Hosting", "PublicSiteHosts.cs")],
            "sets the public site its endpoints read, which only host resolution may do");
    }

    [Theory]
    [InlineData("services.AddSitePublicResolution();", true)]
    [InlineData("IPublicSiteResolution resolution", true)]
    [InlineData("IPublicSiteContext site", false)]
    public void The_public_resolution_scan_sees_a_caller(string code, bool names) =>
        Assert.Equal(names, Regex.IsMatch(code, PublicResolutionReference));

    [Theory]
    [InlineData("sp.GetRequiredService<PublicSiteContext>().Enter(site)", true)]
    [InlineData("IPublicSiteContext site", false)]
    public void The_public_holder_scan_sees_a_setter(string code, bool names) =>
        Assert.Equal(names, Regex.IsMatch(code, PublicSiteHolderReference));

    private static void AssertOnlyNamedIn(string pattern, string[] allowedPaths, string what)
    {
        var source = DomainSource.Project.Parent!;
        var allowed = allowedPaths.Select(r => Path.Combine(source.FullName, r)).ToList();
        Assert.All(allowed, a => Assert.True(File.Exists(a) || Directory.Exists(a), $"{a} is listed but is not there."));

        var scanned = 0;
        foreach (var project in source.GetDirectories("PantryPress.*").Where(d => d.Name != "PantryPress.Tests"))
        {
            var generated = new[] { Path.Combine(project.FullName, "bin"), Path.Combine(project.FullName, "obj") };
            foreach (var file in project.EnumerateFiles("*.cs", SearchOption.AllDirectories))
            {
                if (allowed.Any(a => file.FullName.Equals(a, StringComparison.OrdinalIgnoreCase))
                    || allowed.Concat(generated).Any(a => file.FullName.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                scanned++;
                Assert.False(
                    Regex.IsMatch(File.ReadAllText(file.FullName), pattern),
                    $"{Path.GetRelativePath(source.FullName, file.FullName)} {what}.");
            }
        }

        Assert.True(scanned > 100, "The scan found too few source files to be looking in the right place.");
    }

    // PP2.11 (isolation audit): an outbox delivery's scope enters the site its delivery names, so whatever makes a delivery
    // chooses a site. Only the Worker's processor may, from the row it leased - whose site the writer's own request had
    // resolved (SiteScope.Current) - and only the kernel's outbox folder names the registration that runs it. Anything
    // else building a delivery or invoking a registration would enter a site no resolver decided.
    private const string OutboxDeliveryReference = @"\bnew\s+OutboxDelivery\s*\(|\bOutboxHandlerRegistration\b";

    private static readonly string[] OutboxDeliveryMakers =
    [
        Path.Combine("PantryPress.Domain", "Managers", "Outbox"),
        Path.Combine("PantryPress.Worker", "Outbox", "OutboxProcessor.cs"),
    ];

    [Fact]
    public void Only_the_workers_processor_makes_a_delivery()
    {
        var source = DomainSource.Project.Parent!;
        var allowed = OutboxDeliveryMakers.Select(r => Path.Combine(source.FullName, r)).ToList();

        var scanned = 0;
        foreach (var project in source.GetDirectories("PantryPress.*").Where(d => d.Name != "PantryPress.Tests"))
        {
            var generated = new[] { Path.Combine(project.FullName, "bin"), Path.Combine(project.FullName, "obj") };
            foreach (var file in project.EnumerateFiles("*.cs", SearchOption.AllDirectories))
            {
                if (allowed.Any(a => file.FullName.Equals(a, StringComparison.OrdinalIgnoreCase))
                    || allowed.Concat(generated).Any(a => file.FullName.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                scanned++;
                Assert.False(
                    Regex.IsMatch(File.ReadAllText(file.FullName), OutboxDeliveryReference),
                    $"{Path.GetRelativePath(source.FullName, file.FullName)} makes an outbox delivery, which enters a site; only the Worker's processor may.");
            }
        }

        Assert.True(scanned > 100, "The scan found too few source files to be looking in the right place.");
        Assert.All(allowed, a => Assert.True(File.Exists(a) || Directory.Exists(a), $"{a} is listed as a delivery maker but is not there."));
    }

    [Theory]
    [InlineData("new OutboxDelivery(id, site, 1)", true)]
    [InlineData("services.GetServices<OutboxHandlerRegistration>()", true)]
    [InlineData("public Task HandleAsync(Work message, OutboxDelivery delivery, CancellationToken ct)", false)]
    public void The_delivery_scan_catches_a_delivery_being_made(string code, bool makes) =>
        Assert.Equal(makes, Regex.IsMatch(code, OutboxDeliveryReference));

    // PP2.11b (isolation audit): the account directory hands out sign-in addresses for whatever account ids it is given, with
    // no site or membership check of its own. Its one caller asks only for the members of the site its Owner policy
    // entered; any other caller is declared here, with its reason.
    private const string AccountDirectoryReference = @"\bI?AccountDirectory\b";

    private static readonly string[] AccountDirectoryReaders =
    [
        // The contract, its facade, and its registration.
        Path.Combine("PantryPress.Domain", "Modules", "Accounts"),

        // A site's Owner reading the site's member list (SITE-010; PP2.11b).
        Path.Combine("PantryPress.Domain", "Modules", "Sites", "Facades", "SiteMembersFacade.cs"),
    ];

    [Fact]
    public void Only_the_member_list_reads_the_account_directory()
    {
        var source = DomainSource.Project.Parent!;
        var allowed = AccountDirectoryReaders.Select(r => Path.Combine(source.FullName, r)).ToList();

        var scanned = 0;
        foreach (var project in source.GetDirectories("PantryPress.*").Where(d => d.Name != "PantryPress.Tests"))
        {
            var generated = new[] { Path.Combine(project.FullName, "bin"), Path.Combine(project.FullName, "obj") };
            foreach (var file in project.EnumerateFiles("*.cs", SearchOption.AllDirectories))
            {
                if (allowed.Any(a => file.FullName.Equals(a, StringComparison.OrdinalIgnoreCase))
                    || allowed.Concat(generated).Any(a => file.FullName.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                scanned++;
                Assert.False(
                    Regex.IsMatch(File.ReadAllText(file.FullName), AccountDirectoryReference),
                    $"{Path.GetRelativePath(source.FullName, file.FullName)} reads accounts' addresses; only a declared reader may.");
            }
        }

        Assert.True(scanned > 100, "The scan found too few source files to be looking in the right place.");
        Assert.All(allowed, a => Assert.True(File.Exists(a) || Directory.Exists(a), $"{a} is listed as a directory reader but is not there."));
    }

    [Theory]
    [InlineData("IAccountDirectory directory", true)]
    [InlineData("services.AddScoped<IAccountDirectory, AccountDirectory>()", true)]
    [InlineData("IAccountAddressCheck addresses", false)]
    public void The_directory_scan_catches_a_reader(string code, bool reads) =>
        Assert.Equal(reads, Regex.IsMatch(code, AccountDirectoryReference));

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
                if (allowed.Any(a => file.FullName.Equals(a, StringComparison.OrdinalIgnoreCase))
                    || allowed.Concat(generated).Any(a => file.FullName.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
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
        Assert.All(allowed, a => Assert.True(File.Exists(a) || Directory.Exists(a), $"{a} is listed as a resolver but is not there."));
    }

    [Fact]
    public void Only_the_declared_files_make_a_di_scope()
    {
        var domain = DomainSource.Project;
        var allowed = ScopeFactories.Select(r => Path.Combine(domain.Parent!.FullName, r)).ToList();

        var scanned = 0;
        foreach (var file in domain.EnumerateFiles("*.cs", SearchOption.AllDirectories))
        {
            if (allowed.Any(a => file.FullName.Equals(a, StringComparison.OrdinalIgnoreCase))
                || file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            scanned++;
            Assert.False(
                Regex.IsMatch(File.ReadAllText(file.FullName), ScopeFactoryReference),
                $"{Path.GetRelativePath(domain.FullName, file.FullName)} makes a DI scope, which only a declared file may do: "
                + "a scope of its own is a site context of its own (tenancy.md rule 2).");
        }

        Assert.True(scanned > 100, "The scan found too few source files to be looking in the right place.");
        Assert.All(allowed, a => Assert.True(File.Exists(a), $"{a} is listed as a scope factory but is not there."));
    }

    [Theory]
    [InlineData("IServiceScopeFactory scopes", true)]
    [InlineData("await using var scope = scopes.CreateAsyncScope();", true)]
    [InlineData("IServiceProvider services", false)]
    public void The_scope_scan_catches_a_scope(string code, bool makes) =>
        Assert.Equal(makes, Regex.IsMatch(code, ScopeFactoryReference));

    // PP2.14: the membership decision enters the site it admits (tenancy.md rule 4), so only the site policy's handler asks
    // it, with the slug from the route. A question about a site from anywhere else is FindAsync, which enters nothing.
    [Fact]
    public void Only_the_site_policy_asks_the_membership_decision()
    {
        var source = DomainSource.Project.Parent!;
        var declaring = new[]
        {
            Path.Combine(DomainSource.Modules.FullName, "Sites", "Contracts", "ISiteMembershipFacade.cs"),
            Path.Combine(DomainSource.Modules.FullName, "Sites", "Facades", "SiteMembershipFacade.cs"),
        };
        Assert.All(declaring, d => Assert.True(File.Exists(d), $"{d} declares the decision but is not there."));
        var callers = new List<string>();
        foreach (var project in source.GetDirectories("PantryPress.*").Where(d => d.Name != "PantryPress.Tests"))
        {
            var generated = new[] { Path.Combine(project.FullName, "bin"), Path.Combine(project.FullName, "obj") };
            foreach (var file in project.EnumerateFiles("*.cs", SearchOption.AllDirectories)
                .Where(f => !generated.Any(g => f.FullName.StartsWith(g + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    && !declaring.Any(d => f.FullName.Equals(d, StringComparison.OrdinalIgnoreCase))))
            {
                // The interface or the class, and the method by name: a call, or a method group passed along.
                var text = File.ReadAllText(file.FullName);
                if (Regex.IsMatch(text, @"\bI?SiteMembershipFacade\b") && Regex.IsMatch(text, @"\bAuthorizeAsync\b"))
                {
                    callers.Add(Path.GetRelativePath(source.FullName, file.FullName));
                }
            }
        }

        Assert.Equal([Path.Combine("PantryPress.ApiService", "Hosting", "SiteAuthorization.cs")], callers);
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

    // PP2.6: a cache key is a SiteCacheKey or a PlatformCacheKey, never a string someone built (tenancy.md rule 8). No source
    // file or project names a cache - not by type, namespace, registration, or package, and not through a project-level
    // alias - except an adapter listed in CacheAdapters by its exact file, which takes only those key types and says why,
    // and the projects in CacheProjects. The AppHost declares the Redis resource and caches nothing, so it is exempt.
    private const string RawCacheReference =
        @"\b(?:StackExchange\.Redis|Microsoft\.Extensions\.Caching|Microsoft\.AspNetCore\.OutputCaching|System\.Runtime\.Caching)\b"
        + @"|\b(?:I?DistributedCache|I?MemoryCache|MemoryCacheOptions|HybridCache|I?ConnectionMultiplexer|IDatabase(?:Async)?|IServer|ISubscriber|IBatch|RedisKey|RedisValue|RedisCache|IOutputCacheStore|OutputCache(?:Attribute)?)\b"
        + @"|\b(?:Add|Use)\w*(?:Redis|HybridCache|MemoryCache|DistributedCache|OutputCache)\w*";

    private const string RawCacheProjectReference =
        @"Include=""(?:StackExchange\.Redis|Microsoft\.Extensions\.Caching|Aspire\.StackExchange\.Redis|Aspire\.Microsoft\.Extensions\.Caching|Microsoft\.AspNetCore\.OutputCaching|System\.Runtime\.Caching)[^""]*""";

    private static readonly string[] CacheAdapters =
    [
        // The platform cache (PP2.12): the one adapter, which takes only a PlatformCacheKey. The public tier's host map is
        // its first user; site entries under a SiteCacheKey join it with page data (PP7.12).
        Path.Combine("PantryPress.Domain", "Managers", "Caching", "RedisPlatformCache.cs"),

        // The connection the adapter is given: the Aspire Redis client, registered for the hosts that cache. It reads and
        // writes nothing.
        Path.Combine("PantryPress.ServiceDefaults", "Caching", "CacheConnection.cs"),
    ];

    // The one cache package each of those projects may carry, for the adapter and the connection above alone; any other
    // cache package in them still fails the scan.
    private static readonly Dictionary<string, string> CachePackages = new(StringComparer.OrdinalIgnoreCase)
    {
        [Path.Combine("PantryPress.Domain", "PantryPress.Domain.csproj")] = "StackExchange.Redis",
        [Path.Combine("PantryPress.ServiceDefaults", "PantryPress.ServiceDefaults.csproj")] = "Aspire.StackExchange.Redis",
    };

    [Fact]
    public void Nothing_reaches_a_cache_except_through_a_site_or_platform_key()
    {
        var source = DomainSource.Project.Parent!;
        var adapters = CacheAdapters.Select(a => Path.Combine(source.FullName, a)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var packages = CachePackages.ToDictionary(p => Path.Combine(source.FullName, p.Key), p => p.Value, StringComparer.OrdinalIgnoreCase);
        Assert.All(adapters.Concat(packages.Keys), a => Assert.True(File.Exists(a), $"{a} is listed for the cache but is not there."));

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
                var text = File.ReadAllText(file.FullName);
                if (packages.TryGetValue(file.FullName, out var package))
                {
                    var allowed = $"<PackageReference Include=\"{package}\"";
                    Assert.Contains(allowed, text, StringComparison.Ordinal);
                    text = text.Replace(allowed, string.Empty, StringComparison.Ordinal);
                }

                Assert.False(
                    Regex.IsMatch(text, pattern),
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
