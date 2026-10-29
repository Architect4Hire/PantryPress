using System.Text.Json;
using System.Text.RegularExpressions;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Tests.Sites;

// PP2.8: the reserved list (SITE-002) is platform reference data with a version. It reserves every host PantryPress itself
// answers on, read here from where those hosts are configured - the gateway's hosts and every literal label the Terraform
// writes - so a new one cannot be added there without reserving it.
public sealed partial class ReservedSlugsTests
{
    private static readonly DirectoryInfo Repository = new(Path.Combine(DomainSource.Project.FullName, "..", ".."));

    [Fact]
    public void Every_entry_is_a_slug_a_site_could_otherwise_take()
    {
        Assert.All(ReservedSlugs.All, e =>
        {
            Assert.True(SlugPolicy.IsWellFormed(e.Slug, out var stored), $"'{e.Slug}' could never be taken anyway.");
            Assert.Equal(stored, e.Slug);
            Assert.True(Enum.IsDefined(e.Reason));
        });
    }

    [Fact]
    public void Every_entry_is_listed_once()
    {
        Assert.Empty(ReservedSlugs.All.GroupBy(e => e.Slug, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key));
    }

    [Fact]
    public void The_version_is_the_newest_entrys()
    {
        Assert.Equal(ReservedSlugs.Version, ReservedSlugs.All.Max(e => e.Since));
        Assert.All(ReservedSlugs.All, e => Assert.InRange(e.Since, 1, ReservedSlugs.Version));
    }

    // Since PP-D19 app.<zone> and www.<zone> are Front Door's 301s to the platform home on the apex, which holds the BFF's
    // host-only __Host- cookies: a tenant site at either would pose as the platform. app has been reserved since the
    // first version; www with it. The Terraform names app today (admin_host, until PP9.1a moves it) and www from PP9.1a.
    [Theory]
    [InlineData("app")]
    [InlineData("www")]
    public void The_hosts_that_redirect_to_the_platform_home_are_reserved_as_platform_hosts(string label)
    {
        Assert.Contains(new ReservedSlugEntry(label, ReservedSlugReason.PlatformHost, 1), ReservedSlugs.All);
    }

    // PP-D19: the gateway answers on the zone's apex alone - the production zone deployed, localhost (the local zone,
    // SITE-009) in Development - so no slug can name it and it has no label to reserve. A gateway host with a label would
    // be a subdomain a site could otherwise take; this fails until that label is the apex again.
    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void Every_host_the_gateway_answers_on_is_the_zones_apex(string file)
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(Repository.FullName, "src", "PantryPress.Gateway", file)));
        var hosts = settings.RootElement.GetProperty("Gateway").GetProperty("Hosts").EnumerateArray().Select(h => h.GetString()!).ToList();
        var prod = File.ReadAllText(Path.Combine(Repository.FullName, "docs", "infrastructure", "terraform", "pantrypress", "env", "prod.tfvars"));
        var apex = file == "appsettings.Development.json" ? "localhost" : ZoneName().Match(prod).Groups["zone"].Value;

        Assert.Equal([apex], hosts);
    }

    // Every host label the Terraform writes: each "<label>.${var.dns_zone_name}" it builds, each DNS record it names
    // literally, the account-email subdomain, and the dev environment's zone, which is delegated under the production one -
    // a production site with that slug would sit on top of the dev environment. A label that cannot be a slug ("*", "@",
    // "_dnsauth") needs no entry.
    [Fact]
    public void Every_host_the_infrastructure_names_is_reserved()
    {
        var terraform = new DirectoryInfo(Path.Combine(Repository.FullName, "docs", "infrastructure", "terraform", "pantrypress"));
        var sources = terraform.GetFiles("*.tf").Select(f => File.ReadAllText(f.FullName)).ToList();
        var dev = File.ReadAllText(Path.Combine(terraform.FullName, "env", "dev.tfvars"));
        var prod = File.ReadAllText(Path.Combine(terraform.FullName, "env", "prod.tfvars"));

        var devZone = ZoneName().Match(dev).Groups["zone"].Value;
        var prodZone = ZoneName().Match(prod).Groups["zone"].Value;
        Assert.EndsWith("." + prodZone, devZone, StringComparison.Ordinal);

        var labels = sources
            .SelectMany(tf => ZoneHost().Matches(tf).Select(m => m.Groups["label"].Value))
            .Concat(sources.SelectMany(tf => DnsRecord().Matches(tf).Select(m => m.Groups["name"].Value.Split('.')[0])))
            .Concat(sources.SelectMany(tf => EmailSubdomain().Matches(tf).Select(m => m.Groups["label"].Value)))
            .Append(devZone[..^(prodZone.Length + 1)])
            .Where(label => label is not ("*" or "@") && !label.StartsWith('_'))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        // The ones known today, so a scan that silently stopped matching fails here.
        Assert.Superset(new HashSet<string>(["app", "mail", "dev"]), labels.ToHashSet());
        Assert.All(labels, AssertReservedAsPlatformHost);
    }

    private static void AssertReservedAsPlatformHost(string label)
    {
        var entry = Assert.Single(ReservedSlugs.All, e => e.Slug == label);
        Assert.Equal(ReservedSlugReason.PlatformHost, entry.Reason);
        Assert.True(ReservedSlugs.IsReserved(label));
    }

    [GeneratedRegex(@"""(?<label>[a-z0-9-]+)\.\$\{var\.dns_zone_name\}""")]
    private static partial Regex ZoneHost();

    // A DNS record's literal name, inside its resource block; a computed one (each.key, a local) has no quotes to match.
    [GeneratedRegex(@"resource\s+""azurerm_dns_\w+_record""\s+""\w+""\s*\{[^}]*?\bname\s*=\s*""(?<name>[^""$]+)""")]
    private static partial Regex DnsRecord();

    [GeneratedRegex(@"variable\s+""email_subdomain""\s*\{[^}]*default\s*=\s*""(?<label>[a-z0-9-]+)""")]
    private static partial Regex EmailSubdomain();

    [GeneratedRegex(@"dns_zone_name\s*=\s*""(?<zone>[a-z0-9.-]+)""")]
    private static partial Regex ZoneName();
}
