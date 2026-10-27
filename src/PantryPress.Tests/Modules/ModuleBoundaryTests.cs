using System.Text.RegularExpressions;

namespace PantryPress.Tests.Modules;

// ARCH-001, ARCH-002 (PP1.5, PP2.1): the domain is module-first, a module reaches another only through its contracts and
// along a declared edge, and inside a module the seam's layers depend only the ways backend.md allows. Source scans, not
// reflection: modules live in one assembly, and a shared constant is inlined by the compiler, so a reference can leave no
// trace in the metadata. Every scan reads code with its comments removed and its qualified names joined up, and the
// hiding places a scan cannot see - global usings, aliases, a second namespace, a module namespace declared elsewhere -
// are refused outright.
public sealed class ModuleBoundaryTests
{
    /// <summary>The bounded contexts <c>backend.md</c> names. A module folder outside this list is a typo or a new context.</summary>
    private static readonly string[] DeclaredModules =
    [
        "Accounts", "Sites", "Recipes", "Posts", "Media", "Rendering", "Audience", "Monetization", "Analytics", "Billing",
        "Domains", "Publishing", "Import", "Platform",
    ];

    /// <summary>
    /// The folders a module may have: the seam's layers, plus Validation at the facade edge. <c>Managers/</c> is the
    /// shared kernel at the domain root, never a module folder (PP2.1). Entities, their configuration, options, outbox
    /// messages, and the registration sit at the module root.
    /// </summary>
    private static readonly string[] Layers =
        ["Contracts", "Facades", "Business", "DataLayer", "Repositories", "Gateways", "Validation"];

    /// <summary>The module root's files, other than its registration, scanned as a layer of their own.</summary>
    private const string Root = "(root)";

    /// <summary>
    /// The layers of its own module each layer may not name (ARCH-002, <c>backend.md</c>). Facades orchestrate and may
    /// name everything; Contracts are the public face and name nothing behind them; Business is pure, names nothing that
    /// does I/O, and any layer may use it.
    /// </summary>
    private static readonly Dictionary<string, string[]> ForbiddenWithinModule = new(StringComparer.Ordinal)
    {
        ["Contracts"] = ["Facades", "Business", "DataLayer", "Repositories", "Gateways", "Validation"],
        ["Business"] = ["Facades", "DataLayer", "Repositories", "Gateways", "Validation"],
        ["Validation"] = ["Facades", "DataLayer", "Repositories", "Gateways"],
        ["DataLayer"] = ["Facades", "Validation"],
        ["Repositories"] = ["Facades", "DataLayer", "Gateways", "Validation"],
        ["Gateways"] = ["Facades", "DataLayer", "Repositories", "Validation"],
        [Root] = ["Facades", "DataLayer", "Repositories", "Gateways", "Validation"],
    };

    /// <summary>The layers below a facade, which may not name a facade's interface either (in any module).</summary>
    private static readonly string[] BelowFacades = ["Business", "Validation", "DataLayer", "Repositories", "Gateways", Root];

    /// <summary>
    /// Business does no I/O: no database, none of the shared kernel's writers or senders, no file or network access, no
    /// service locator, and nothing asynchronous. Reading its own embedded reference data is not I/O in this sense.
    /// </summary>
    private const string BusinessIo =
        @"\bIOutbox\w*\b|\bIAuditLog\b|\bIEmailGateway\b|\bIIdempotencyStore\b|\bHttpClient\b|\bIServiceProvider\b"
        + @"|\bFile\.|\bDirectory\.|\basync\b|\bawait\b|\bTask\b|\bValueTask\b";

    /// <summary>EF Core belongs to repositories and entity configuration (and the registration that adds them).</summary>
    private const string EfCore = @"\bMicrosoft\.EntityFrameworkCore\b|\bPantryPressDbContext\b|\bDbContext\b|\bDbSet<";

    /// <summary>A data layer never hands a query upward (<c>backend.md</c>), so only it and repositories name one.</summary>
    private const string Queryable = @"\bIQueryable\b";

    /// <summary>Contracts cross module boundaries as records, never Identity's types or entities (api-contract.md).</summary>
    private const string ContractsIdentity =
        @"\bMicrosoft\.AspNetCore\.Identity\b|\bIdentity(?:User|Role|Result)\b|\bUserManager\b";

    /// <summary>
    /// The module dependencies this domain declares (ARCH-001). Platform asks Accounts for the Identity role it
    /// administers. Sites names the account a membership belongs to by its <c>AccountId</c> (PP2.3), and asks whether an
    /// account's email address is confirmed before letting it own more sites (PP2.10). Nothing goes the
    /// other way, so the graph stays acyclic. Add an edge here only with a reason.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedModuleEdges = new(StringComparer.Ordinal)
    {
        ["Platform"] = ["Accounts"],
        ["Sites"] = ["Accounts"],
    };

    /// <summary>
    /// The one file allowed past another module's contracts, and why: a module's composition root registers the
    /// services it is built on, and Accounts' registration lives at its root. Nothing else in the file may use the
    /// namespace: no other type declared at that module's root may appear (PP1.12, PP2.1).
    /// </summary>
    private static readonly Dictionary<string, string> CompositionRoots = new(StringComparer.Ordinal)
    {
        ["Platform/PlatformServiceCollectionExtensions.cs"] = "AddAccountRoles, the operator's role registration",
    };

    [Fact]
    public void Every_module_is_a_declared_bounded_context()
    {
        var names = ModuleDirectories().Select(d => d.Name).ToList();

        Assert.Contains("Accounts", names);
        Assert.Contains("Sites", names);
        Assert.All(names, name => Assert.True(
            DeclaredModules.Contains(name),
            $"Modules/{name} is not a bounded context backend.md declares."));
    }

    [Fact]
    public void Every_module_has_its_registration_at_its_root()
    {
        foreach (var module in ModuleDirectories())
        {
            Assert.True(
                File.Exists(Path.Combine(module.FullName, Registration(module.Name))),
                $"Modules/{module.Name} has no {Registration(module.Name)} at its root.");
        }
    }

    [Fact]
    public void A_module_has_only_the_seams_folders()
    {
        foreach (var module in ModuleDirectories())
        {
            Assert.All(module.GetDirectories(), folder => Assert.True(
                Layers.Contains(folder.Name),
                $"Modules/{module.Name}/{folder.Name} is not a layer of the seam ({string.Join(", ", Layers)})."));
        }
    }

    // What makes the layer scan sound: a file can name another layer's types without a qualifier only from inside that
    // layer's namespace, so every file declares one namespace, the one of the folder it sits in, and nothing outside a
    // module's folder declares that module's namespace.
    [Fact]
    public void Every_file_declares_one_namespace_and_only_its_folders()
    {
        var scanned = 0;
        foreach (var file in DomainFiles())
        {
            scanned++;
            var declared = Regex.Matches(Code(File.ReadAllText(file.FullName)), @"(?<![\w.])namespace\s+([\w.]+)")
                .Select(m => m.Groups[1].Value)
                .ToList();
            var name = Path.GetRelativePath(DomainSource.Project.FullName, file.FullName);
            Assert.True(declared.Count == 1, $"{name} declares {declared.Count} namespaces; one per file.");

            if (file.FullName.StartsWith(DomainSource.Modules.FullName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                var folder = Path.GetRelativePath(DomainSource.Modules.FullName, file.DirectoryName!)
                    .Replace(Path.DirectorySeparatorChar, '.');
                var expected = $"PantryPress.Domain.Modules.{folder}";
                Assert.True(declared[0] == expected, $"{name} declares {declared[0]}, not {expected}, the namespace of its folder.");
            }
            else
            {
                Assert.False(
                    declared[0].StartsWith("PantryPress.Domain.Modules", StringComparison.Ordinal),
                    $"{name} declares {declared[0]} outside the module's folder.");
            }
        }

        Assert.True(scanned > 50, "The scan found too few source files to be looking in the right place.");
    }

    // A global using or a namespace alias would let a file name a layer or a module with nothing the scans can see.
    [Fact]
    public void The_domain_has_no_global_usings_and_no_aliases_into_a_module()
    {
        foreach (var file in DomainFiles())
        {
            var code = Code(File.ReadAllText(file.FullName));
            var name = Path.GetRelativePath(DomainSource.Project.FullName, file.FullName);
            Assert.False(Regex.IsMatch(code, @"(?<![\w.])global\s+using\b"), $"{name} declares a global using.");
            Assert.False(
                Regex.IsMatch(code, @"(?<![\w.])using\s+\w+\s*=\s*(?:global::)?PantryPress\.Domain\.Modules\b"),
                $"{name} aliases a module namespace.");
        }

        foreach (var project in new[]
                 {
                     Path.Combine(DomainSource.Project.FullName, "PantryPress.Domain.csproj"),
                     Path.Combine(DomainSource.Project.Parent!.Parent!.FullName, "Directory.Build.props"),
                 })
        {
            Assert.True(File.Exists(project), $"{project} was not found.");
            Assert.DoesNotMatch(@"<Using\s+Include\s*=\s*""PantryPress", File.ReadAllText(project));
        }
    }

    [Fact]
    public void Inside_a_module_the_layers_depend_only_the_ways_the_seam_allows()
    {
        var checkedFiles = 0;
        foreach (var file in ModuleFiles())
        {
            var (module, layer) = Locate(file);
            if (!ForbiddenWithinModule.TryGetValue(layer, out var forbidden))
            {
                continue;
            }

            checkedFiles++;
            foreach (var named in LayersNamed(File.ReadAllText(file.FullName), module))
            {
                Assert.False(
                    forbidden.Contains(named),
                    $"{module}/{file.Name} ({layer}) names {module}.{named}, a layer {layer} may not depend on.");
            }
        }

        Assert.True(checkedFiles > 10, "The scan found too few layer files to be looking in the right place.");
    }

    // ARCH-002: a facade's interface lives in Contracts, which every layer may name, so the folder rule alone would let a
    // repository or a rule call back up through one. Nothing below the facades names one, in its own module or another.
    [Fact]
    public void Nothing_below_the_facades_names_a_facade_interface()
    {
        var facades = FacadeInterfaces();
        Assert.Contains("IAccountRoles", facades);
        Assert.Contains("IPlatformAdminFacade", facades);

        foreach (var file in ModuleFiles())
        {
            var (module, layer) = Locate(file);
            if (!BelowFacades.Contains(layer))
            {
                continue;
            }

            var code = Code(File.ReadAllText(file.FullName));
            foreach (var facade in facades)
            {
                Assert.False(
                    Regex.IsMatch(code, $@"\b{facade}\b"),
                    $"{module}/{file.Name} ({layer}) names the facade interface {facade}.");
            }
        }
    }

    [Fact]
    public void Each_layer_keeps_to_its_own_kind_of_work()
    {
        foreach (var file in ModuleFiles())
        {
            var (module, layer) = Locate(file);
            var code = Code(File.ReadAllText(file.FullName));
            var name = $"{module}/{file.Name} ({layer})";

            var efAllowed = layer == "Repositories"
                || (layer == Root && file.Name.EndsWith("Configuration.cs", StringComparison.Ordinal))
                || file.Name == Registration(module);
            AssertDoesNotName(code, EfCore, efAllowed ? null : name, "EF Core belongs to repositories and entity configuration");
            AssertDoesNotName(
                code, Queryable, layer is "Repositories" or "DataLayer" || file.Name == Registration(module) ? null : name,
                "a query never leaves the data layer");
            AssertDoesNotName(code, BusinessIo, layer == "Business" ? name : null, "Business does no I/O");
            AssertDoesNotName(code, ContractsIdentity, layer == "Contracts" ? name : null, "Contracts carry no Identity types");
        }
    }

    // ARCH-001, api-contract.md: entities and the other types at a module's root are its own. Its contracts never expose
    // one - a contract's namespace sits inside the module's, so a root type needs no qualifier there - and another module
    // never names one, its composition root included.
    [Fact]
    public void A_modules_root_types_stay_inside_it_and_out_of_its_contracts()
    {
        var rootTypes = ModuleDirectories().ToDictionary(d => d.Name, d => RootTypes(d));
        Assert.Contains("ApplicationUser", rootTypes["Accounts"]);

        foreach (var file in ModuleFiles())
        {
            var (module, layer) = Locate(file);
            var code = Code(File.ReadAllText(file.FullName));
            var forbidden = rootTypes
                .Where(r => r.Key != module || layer == "Contracts")
                .SelectMany(r => r.Value.Select(type => (Owner: r.Key, Type: type)));
            foreach (var (owner, type) in forbidden)
            {
                Assert.False(
                    Regex.IsMatch(code, $@"\b{type}\b"),
                    $"{module}/{file.Name} ({layer}) names {owner}'s root type {type}.");
            }
        }
    }

    [Fact]
    public void A_module_reaches_another_only_through_its_contracts_and_only_along_a_declared_edge()
    {
        var names = ModuleDirectories().Select(d => d.Name).ToList();

        foreach (var directory in ModuleDirectories())
        {
            var module = directory.Name;
            var allowed = AllowedModuleEdges.TryGetValue(module, out var edges) ? edges : [];
            foreach (var file in directory.EnumerateFiles("*.cs", SearchOption.AllDirectories))
            {
                var source = File.ReadAllText(file.FullName);
                var relative = Path.GetRelativePath(DomainSource.Modules.FullName, file.FullName).Replace('\\', '/');
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
    [InlineData("var code = PantryPress.Domain.Modules.Accounts\n    .Business.Totp.Compute(key, step);", "Business")]
    public void The_scan_sees_every_way_into_another_module(string source, string segment) =>
        Assert.Equal([segment], ReferencesTo(source, "Accounts"));

    [Fact]
    public void The_scan_does_not_mistake_a_longer_module_name_for_this_one() =>
        Assert.Empty(ReferencesTo("using PantryPress.Domain.Modules.AccountsArchive.Contracts;", "Accounts"));

    // PP2.1: every way a file can name a layer of its own module - in full, from the shared parent namespace, or from
    // the module's own namespace, which every layer sits inside - however the name is spaced or broken across lines.
    [Theory]
    [InlineData("using PantryPress.Domain.Modules.Sites.Facades;", "Facades")]
    [InlineData("using static PantryPress.Domain.Modules.Sites.DataLayer.SitesDataLayer;", "DataLayer")]
    [InlineData("var facade = Sites.Facades.SiteFacade.Create();", "Facades")]
    [InlineData("var site = await Repositories.SiteRepository.FindAsync(id);", "Repositories")]
    [InlineData("var site = await Repositories\n        .SiteRepository.FindAsync(id);", "Repositories")]
    [InlineData("using PantryPress.Domain.Modules.Sites . Gateways;", "Gateways")]
    public void The_layer_scan_sees_every_way_into_a_layer_of_the_same_module(string source, string layer) =>
        Assert.Equal([layer], LayersNamed(source, "Sites"));

    [Theory]
    [InlineData("// Facades own the transaction; Business holds the rules.")]
    [InlineData("// using PantryPress.Domain.Modules.Sites.Facades;")]
    [InlineData("/* var facade = Sites.Facades.SiteFacade.Create(); */")]
    [InlineData("/// <see cref=\"Facades.SiteFacade\"/>")]
    [InlineData("using PantryPress.Domain.Modules.Accounts.Facades;")]
    [InlineData("var code = Accounts.Business.Totp.Compute(key, step);")]
    [InlineData("var builder = new SitesFacadesBuilder();")]
    public void The_layer_scan_ignores_comments_prose_and_other_modules(string source) =>
        Assert.Empty(LayersNamed(source, "Sites"));

    [Theory]
    [InlineData("internal sealed class SiteFacade(SitesDataLayer data, IClock clock) : ISiteFacade\n{", "ISiteFacade")]
    [InlineData("internal sealed partial class SiteFacade(\n    IValidator<X> validator,\n    SitesDataLayer data)\n    : ISiteFacade, IOther\n{", "ISiteFacade,IOther")]
    [InlineData("internal sealed class SiteFacade : ISiteFacade<SiteId> where T : class\n{", "ISiteFacade")]
    [InlineData("internal static class SiteGuards\n{", "")]
    public void The_facade_scan_reads_a_classs_interfaces(string source, string interfaces) =>
        Assert.Equal(
            interfaces.Split(',', StringSplitOptions.RemoveEmptyEntries),
            ImplementedTypes(source));

    // The segment after the module's name in each reference to it: "Contracts", a folder or type, or "" for the root.
    // Written in full (Modules.Accounts...) or relative to the shared parent namespace (Accounts.Business...), which code
    // in another module's namespace can use without the prefix; the relative form needs a segment, so prose that names a
    // module is not a reference.
    private static List<string> ReferencesTo(string source, string module) =>
        [.. Regex.Matches(Code(source), $@"Modules\.{module}(?![\w])(?:\.(?<seg>\w+))?|(?<![\w.]){module}\.(?<seg>\w+)")
            .Select(m => m.Groups["seg"].Value)];

    // The layers of its own module a source names: in full (Modules.Sites.Facades), from the shared parent namespace
    // (Sites.Facades), or from the module's own namespace (Facades.SiteFacade). The last form needs a member after it,
    // so prose that names a layer is not a reference.
    private static List<string> LayersNamed(string source, string module)
    {
        var code = Code(source);
        return [.. Layers.Where(layer => Regex.IsMatch(
            code,
            $@"Modules\.{module}\.{layer}(?![\w])|(?<![\w.]){module}\.{layer}(?![\w])|(?<![\w.]){layer}\.\w"))];
    }

    // Code as the compiler reads it, for scanning: comments removed (so a doc reference or a commented-out line is not a
    // dependency) and the whitespace around each dot dropped (so a qualified name broken across lines is still one name).
    private static string Code(string source)
    {
        var code = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        code = Regex.Replace(code, @"//[^\r\n]*", string.Empty);
        return Regex.Replace(code, @"\s*\.\s*", ".");
    }

    // The interfaces facade classes implement that some module's Contracts declare.
    private static HashSet<string> FacadeInterfaces()
    {
        var declared = ModuleFiles()
            .Where(f => Locate(f).Layer == "Contracts")
            .SelectMany(f => Regex.Matches(Code(File.ReadAllText(f.FullName)), @"\binterface\s+(\w+)").Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        return ModuleFiles()
            .Where(f => Locate(f).Layer == "Facades")
            .SelectMany(f => ImplementedTypes(File.ReadAllText(f.FullName)))
            .Where(declared.Contains)
            .ToHashSet(StringComparer.Ordinal);
    }

    // The base types a class or record lists after its name and any primary constructor, generic arguments dropped.
    private static List<string> ImplementedTypes(string source) =>
        [.. Regex.Matches(
                Code(source),
                @"\b(?:class|record)\s+\w+(?:<[^>]*>)?\s*(?:\((?:[^()]|\([^()]*\))*\))?\s*:\s*(?<bases>[^{;]+)")
            .SelectMany(m => Regex.Replace(m.Groups["bases"].Value, @"\bwhere\b.*", string.Empty, RegexOptions.Singleline).Split(','))
            .Select(b => Regex.Match(b.Trim(), @"^\w+").Value)
            .Where(b => b.Length > 0)];

    // The types declared in a module's root files, other than its registration.
    private static HashSet<string> RootTypes(DirectoryInfo module) =>
        module.EnumerateFiles("*.cs", SearchOption.TopDirectoryOnly)
            .Where(f => f.Name != Registration(module.Name))
            .SelectMany(f => Regex.Matches(
                    Code(File.ReadAllText(f.FullName)),
                    @"\b(?:class|record|struct|interface|enum)\s+(?:(?:class|struct)\s+)?(\w+)")
                .Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

    private static void AssertDoesNotName(string code, string pattern, string? file, string rule)
    {
        if (file is null)
        {
            return;
        }

        var match = Regex.Match(code, pattern);
        Assert.False(match.Success, $"{file} names {match.Value}: {rule}.");
    }

    // PP2.3 (review finding): a host reaches a module through its contracts and its registration only. The rules a
    // module keeps are its own - a host that decided one itself (whether a role covers another) would be a second copy
    // nothing here checks. The migrations are generated and name no layer.
    [Fact]
    public void Hosts_reach_a_module_only_through_its_contracts_and_registration()
    {
        var source = DomainSource.Project.Parent!;
        var layers = string.Join("|", Layers.Where(l => l != "Contracts"));
        var scanned = 0;
        foreach (var host in source.GetDirectories("PantryPress.*")
            .Where(d => d.Name is not ("PantryPress.Domain" or "PantryPress.Tests")))
        {
            var skipped = new[] { "bin", "obj", "Migrations" }.Select(d => Path.Combine(host.FullName, d) + Path.DirectorySeparatorChar);
            foreach (var file in host.EnumerateFiles("*.cs", SearchOption.AllDirectories)
                .Where(f => !skipped.Any(s => f.FullName.StartsWith(s, StringComparison.OrdinalIgnoreCase))))
            {
                scanned++;
                AssertDoesNotName(
                    Code(File.ReadAllText(file.FullName)),
                    $@"\bPantryPress\.Domain\.Modules\.\w+\.(?:{layers})\b",
                    Path.GetRelativePath(source.FullName, file.FullName),
                    "a host names a module's contracts and its registration, never a layer behind them");
            }
        }

        Assert.True(scanned > 50, "The scan found too few host files to be looking in the right place.");
    }

    private static string Registration(string module) => $"{module}ServiceCollectionExtensions.cs";

    // The module a file belongs to and its layer: the folder it sits in, the root pseudo-layer for a root file, or the
    // registration's own name, which no layer rule covers - it is the module's composition root.
    private static (string Module, string Layer) Locate(FileInfo file)
    {
        var parts = Path.GetRelativePath(DomainSource.Modules.FullName, file.FullName).Split(Path.DirectorySeparatorChar);
        return parts.Length > 2 ? (parts[0], parts[1])
            : file.Name == Registration(parts[0]) ? (parts[0], "(registration)")
            : (parts[0], Root);
    }

    private static DirectoryInfo[] ModuleDirectories() => DomainSource.Modules.GetDirectories();

    private static IEnumerable<FileInfo> ModuleFiles() =>
        DomainSource.Modules.EnumerateFiles("*.cs", SearchOption.AllDirectories);

    private static IEnumerable<FileInfo> DomainFiles()
    {
        var generated = new[] { "bin", "obj" }.Select(d => Path.Combine(DomainSource.Project.FullName, d) + Path.DirectorySeparatorChar);
        return DomainSource.Project.EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(f => !generated.Any(g => f.FullName.StartsWith(g, StringComparison.OrdinalIgnoreCase)));
    }
}
