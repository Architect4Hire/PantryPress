using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using PantryPress.Gateway.Hosting;
using PantryPress.ServiceDefaults.EdgeHardening;

namespace PantryPress.Tests.Gateway;

// PPL.3: the admin app owns the list of public paths (PPL.2's public-paths.ts) and the gateway is configured with a copy,
// because it serves those documents and redirects a signed-in visitor past two of them before the app runs. This reads
// the app's file as text and fails when the copies drift.
public sealed partial class GatewayPublicPathsTests
{
    private static readonly string Src = Path.GetFullPath(Path.Combine(DomainSource.Project.FullName, ".."));

    private static readonly string PublicPathsFile = Path.Combine(
        Src, "web", "projects", "pantrypress-admin", "src", "app", "public-paths.ts");

    [Fact]
    public void The_gateways_public_paths_are_the_admin_apps()
    {
        Assert.Equal(AppList("PUBLIC_PATHS"), GatewayList(nameof(AdminAppOptions.PublicPaths)));
    }

    [Fact]
    public void The_gateways_redirected_paths_are_the_admin_apps()
    {
        Assert.Equal(AppList("SIGNED_IN_REDIRECT_PATHS"), GatewayList(nameof(AdminAppOptions.SignedInRedirectPaths)));
    }

    [Fact]
    public void The_test_build_has_a_document_for_every_public_path() =>
        Assert.Equal(AppList("PUBLIC_PATHS"), AdminAppBuild.PublicPaths);

    [Fact]
    public void Development_takes_the_lists_as_they_are()
    {
        // Only the build's folder differs by environment; a Development copy of either list could drift unseen.
        var development = Configuration("appsettings.Development.json").GetSection(AdminAppOptions.Section);

        Assert.Equal([nameof(AdminAppOptions.Root)], development.GetChildren().Select(child => child.Key));
    }

    [Fact]
    public void The_document_policy_admits_self_hosted_fonts_and_scripts_only()
    {
        // Every page the gateway serves runs under this: fonts from the build, no inline script (PPL.3, design-system.md).
        foreach (var policy in new[] { ContentSecurityPolicies.Document })
        {
            var directives = policy.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            Assert.Contains("font-src 'self'", directives);
            Assert.Contains("script-src 'self'", directives);
        }
    }

    [Fact]
    public void The_reader_takes_a_list_prettier_wrapped_across_lines() =>
        Assert.Equal(
            ["/", "/pricing", "/terms"],
            AppList("export const PUBLIC_PATHS = [\n  '',\n  'pricing',\n  'terms',\n] as const;\n", "PUBLIC_PATHS"));

    [Theory]
    [InlineData("export const PUBLIC_PATHS = [\"\", \"pricing\"] as const;")]
    [InlineData("export const PUBLIC_PATHS: readonly string[] = ['', 'pricing'];")]
    [InlineData("export const PUBLIC_PATHS = ['', 'pricing' /* soon: 'blog' */] as const;")]
    [InlineData("export const PUBLIC_PATHS = ['', ...LEGAL] as const;")]
    [InlineData("export const PUBLIC_PATHS = [] as const;")]
    public void The_reader_fails_rather_than_misread_a_list_it_cannot_take_whole(string source) =>
        Assert.ThrowsAny<Exception>(() => AppList(source, "PUBLIC_PATHS"));

    private static string[] AppList(string name) => AppList(File.ReadAllText(PublicPathsFile), name);

    // The app's list, read as text: one array literal of single-quoted router paths, written as URL paths.
    private static string[] AppList(string source, string name)
    {
        var literal = Regex.Match(
            source,
            $@"export const {name} = \[(?<items>[^\]]*)\]",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(literal.Success, $"{name} is not a plain array literal in {PublicPathsFile}.");

        var items = literal.Groups["items"].Value;
        var paths = QuotedPath().Matches(items).Select(match => "/" + match.Groups["path"].Value).ToArray();

        // Nothing but quoted paths and the commas between them: anything computed could not be read here.
        Assert.Equal(string.Empty, QuotedPath().Replace(items, string.Empty).Replace(",", string.Empty, StringComparison.Ordinal).Trim());
        Assert.NotEmpty(paths);
        return paths;
    }

    private static string[] GatewayList(string key) =>
        Configuration("appsettings.json").GetSection($"{AdminAppOptions.Section}:{key}").Get<string[]>() ?? [];

    private static IConfiguration Configuration(string file) =>
        new ConfigurationBuilder().AddJsonFile(Path.Combine(Src, "PantryPress.Gateway", file)).Build();

    [GeneratedRegex(@"'(?<path>[^']*)'", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedPath();
}
