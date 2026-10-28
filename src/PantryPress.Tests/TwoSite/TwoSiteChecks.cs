using System.Linq.Expressions;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;

namespace PantryPress.Tests.TwoSite;

// Inside the namespace, so the types win over the PantryPress.Tests.Site namespace (the public site host's tests).
using PantryPress.Domain.Modules.Sites;

/// <summary>
/// The checks a change touching site-owned, public, or reader data owes (tenancy.md, "Two-site test"; ARCH-015,
/// PSEC-007; PP2.7). Each takes two sites and proves site A cannot read, list, count, find, change, remove, or move site
/// B's data, that a call about one site leaves the other's tenant-root rows alone, or that a refusal tells a stranger
/// nothing. The entity check is a floor, not the whole debt: a change's own read and write paths - its repository, facade,
/// and route - owe the read-path check and two-site tests of their own.
/// </summary>
internal static class TwoSiteChecks
{
    /// <summary>
    /// The host-header tricks a public path owes (tenancy.md, "Two-site test"; PP2.12): Hosts that sit next to the two
    /// sites' own - <c>www.</c> before either, one slug under the other's host, near-miss slugs, a foreign suffix, a slug
    /// under another zone, the platform's own <c>app</c> host, an <c>xn--</c> look-alike, the bare zone, and IP literals -
    /// none of which may reach either site. Case, a trailing dot, and a port are the same host, and are owed as such.
    /// </summary>
    public static IReadOnlyList<string> HostTricks(TwoSiteWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var zone = TwoSiteHarness.SubdomainZone;
        var (a, b) = (world.A.Host, world.B.Host);
        return
        [
            $"www.{a}",
            $"www.{b}",
            $"{world.A.Slug}.{b}",
            $"{world.A.Slug}a{world.A.Slug}.{zone}",
            $"{world.B.Slug}b.{zone}",
            $"{world.A.Slug}.evil.example",
            $"{a}.evil.example",
            $"{world.A.Slug}.localhost",
            $"app.{zone}",
            "app.localhost",
            $"{new System.Globalization.IdnMapping().GetAscii(world.A.Slug[..10] + "ö")}.{zone}",
            zone,
            "127.0.0.1",
            "127.0.0.0x1",
            "[::1]",
        ];
    }

    /// <summary>
    /// The headers a client could use to name another site than its Host does (PP2.12): each forwarding spelling, naming
    /// <paramref name="otherHost"/>. A public path owes that none of them moves a request off its Host's site.
    /// </summary>
    public static IReadOnlyList<(string Name, string Value)> ForwardingTricks(string otherHost) =>
    [
        ("X-Forwarded-Host", otherHost),
        ("Forwarded", $"host={otherHost}"),
        ("X-Original-Host", otherHost),
        ("X-Original-URL", $"https://{otherHost}/"),
        ("X-Host", otherHost),
    ];

    /// <summary>A scope working on <paramref name="site"/>, entered as a resolver would.</summary>
    public static AsyncServiceScope In(IServiceProvider services, SiteId site)
    {
        var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ISiteContextWriter>().Enter(site);
        return scope;
    }

    /// <summary>
    /// For a site-owned entity: one row is made for each site by <paramref name="make"/> (which adds it, and anything it
    /// needs, to the context it is given and returns it), and then - after proving that working on no site, a read throws -
    /// working as A:
    /// <list type="bullet">
    /// <item>listing, counting, finding, and asking after B's row see only A's rows, A's own included;</item>
    /// <item>a new row naming B is refused;</item>
    /// <item>a bulk update (<paramref name="touch"/>) and a bulk delete aimed at B's row reach nothing;</item>
    /// <item>B's row written back with a change (<paramref name="change"/>), or removed, is refused by the save, which sees
    /// it is not A's;</item>
    /// <item>the same row passed off as A's - the lie only the database can catch - changes nothing, through the
    /// concurrency token on <see cref="ISiteOwned.SiteId"/>;</item>
    /// <item>moving A's own row to B is refused;</item>
    /// </list>
    /// and B's row is found afterwards exactly as it was written, so a write that slipped through would show.
    /// </summary>
    public static async Task AssertSiteOwnedIsolatedAsync<T>(
        IServiceProvider services,
        SiteId a,
        SiteId b,
        Func<PantryPressDbContext, Task<T>> make,
        Action<UpdateSettersBuilder<T>> touch,
        Action<T> change)
        where T : class, ISiteOwned
    {
        var (aKey, _) = await SeedAsync(services, a, make);
        var (bKey, bRow) = await SeedAsync(services, b, make);

        T Copy(SiteId site)
        {
            var values = bRow.Clone();
            values[nameof(ISiteOwned.SiteId)] = site;
            var copy = (T)values.ToObject();
            change(copy);
            return copy;
        }

        await using (var scope = In(services, a))
        {
            var db = Db(scope);
            var rows = await db.Set<T>().AsNoTracking().ToListAsync();
            Assert.All(rows, row => Assert.Equal(a, row.SiteId));
            Assert.Contains(rows, row => KeyOf(db, row).SequenceEqual(aKey));
            Assert.DoesNotContain(rows, row => KeyOf(db, row).SequenceEqual(bKey));
            Assert.Equal(rows.Count, await db.Set<T>().CountAsync());
            Assert.Null(await db.Set<T>().FindAsync(bKey));
            Assert.False(await db.Set<T>().AnyAsync(ByKey<T>(db, bKey)));
            Assert.Equal(0, await db.Set<T>().Where(ByKey<T>(db, bKey)).ExecuteUpdateAsync(touch));
            Assert.Equal(0, await db.Set<T>().Where(ByKey<T>(db, bKey)).ExecuteDeleteAsync());
        }

        await using (var none = services.CreateAsyncScope())
        {
            var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Db(none).Set<T>().ToListAsync());
            Assert.Contains("needs a site", error.ToString(), StringComparison.Ordinal);
        }

        var notOurs = await AssertRefusedAsync<InvalidOperationException>(services, a, db => db.Add(Copy(b)));
        Assert.Contains("another site", notOurs.Message, StringComparison.Ordinal);
        notOurs = await AssertRefusedAsync<InvalidOperationException>(services, a, db => db.Update(Copy(b)));
        Assert.Contains("not this site's row", notOurs.Message, StringComparison.Ordinal);
        notOurs = await AssertRefusedAsync<InvalidOperationException>(services, a, db => db.Remove(Copy(b)));
        Assert.Contains("not this site's row", notOurs.Message, StringComparison.Ordinal);

        await AssertRefusedAsync<DbUpdateConcurrencyException>(services, a, db => db.Update(Copy(a)));
        await AssertRefusedAsync<DbUpdateConcurrencyException>(services, a, db => db.Remove(Copy(a)));

        // Refused by the save ("never changes"), or by EF Core as it is staged when the site is part of a key.
        await AssertRefusedAsync<InvalidOperationException>(services, a, db =>
        {
            var own = db.Set<T>().Find(aKey) ?? throw new Xunit.Sdk.XunitException("A cannot find its own row.");
            db.Entry(own).Property(nameof(ISiteOwned.SiteId)).CurrentValue = b;
        });

        await using (var scope = In(services, b))
        {
            var db = Db(scope);
            var row = await db.Set<T>().FindAsync(bKey);
            Assert.NotNull(row);
            var now = db.Entry(row).CurrentValues;
            Assert.All(now.Properties, property => Assert.Equal(bRow[property], now[property]));
        }

        await using (var scope = In(services, a))
        {
            Assert.NotNull(await Db(scope).Set<T>().FindAsync(aKey));
        }
    }

    /// <summary>
    /// For a read path other than the set itself - a repository's raw query, a projection, a join - which the set checks
    /// cannot see: one row is made for each site by <paramref name="make"/>, and <paramref name="readKeys"/>, run working as
    /// A, returns the keys of the rows it read. It must return A's and never B's.
    /// </summary>
    public static async Task AssertReadPathIsolatedAsync<T>(
        IServiceProvider services,
        SiteId a,
        SiteId b,
        Func<PantryPressDbContext, Task<T>> make,
        Func<IServiceProvider, Task<IReadOnlyCollection<object?[]>>> readKeys)
        where T : class, ISiteOwned
    {
        var (aKey, _) = await SeedAsync(services, a, make);
        var (bKey, _) = await SeedAsync(services, b, make);

        await using var scope = In(services, a);
        var keys = await readKeys(scope.ServiceProvider);
        Assert.Contains(keys, key => key.SequenceEqual(aKey));
        Assert.DoesNotContain(keys, key => key.SequenceEqual(bKey));
    }

    /// <summary>
    /// For the tenant root, which has no filter behind it: <paramref name="act"/> - a call working on or about another
    /// site, which may itself expect a refusal - leaves <paramref name="site"/>'s row and every one of its memberships and
    /// hosts exactly as they were, down to their row versions.
    /// </summary>
    public static async Task AssertTenantRootUnchangedAsync(IServiceProvider services, SiteId site, Func<Task> act)
    {
        var before = await TenantRootRowsAsync(services, site);
        await act();
        Assert.Equal(before, await TenantRootRowsAsync(services, site));
    }

    /// <summary>
    /// Two refusals that tell their callers the same thing, and the thing they were meant to: the expected status and
    /// problem code on both, the same problem document once each request's own trace id is set aside, and the same headers
    /// but the date and length. A stranger's answer about a real site must be this alike to the answer for no site at
    /// all, or the difference enumerates sites. Content only: how long each took is not compared.
    /// </summary>
    public static async Task AssertIndistinguishableAsync(
        HttpResponseMessage first,
        HttpResponseMessage second,
        HttpStatusCode status,
        string code)
    {
        Assert.Equal(status, first.StatusCode);
        Assert.Equal(status, second.StatusCode);
        Assert.Equal(code, await first.CodeAsync());
        Assert.Equal(await WithoutTraceAsync(second), await WithoutTraceAsync(first));
        Assert.Equal(Headers(second), Headers(first));
    }

    private static PantryPressDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();

    // The site's row, memberships, and hosts as text, row versions included, so any write to them shows.
    private static async Task<string> TenantRootRowsAsync(IServiceProvider services, SiteId site)
    {
        await using var scope = services.CreateAsyncScope();
        var db = Db(scope);
        var row = await db.Set<Site>().AsNoTracking().SingleAsync(s => s.Id == site);
        var members = await db.Set<SiteMember>().AsNoTracking().Where(m => m.SiteId == site).ToListAsync();
        var hosts = await db.Set<SiteHost>().AsNoTracking().Where(h => h.SiteId == site).ToListAsync();
        return string.Join(
            "\n",
            new[] { $"{row.Id} {row.Slug} {row.Name} {row.Tagline} {row.Status} {row.TimeZone} {row.Locale} {Convert.ToHexString(row.Version)}" }
                .Concat(members
                    .OrderBy(m => m.AccountId.Value)
                    .Select(m => $"{m.AccountId} {m.Role} {m.Status} {Convert.ToHexString(m.Version)}"))
                .Concat(hosts
                    .OrderBy(h => h.Id.Value)
                    .Select(h => $"{h.Id} {h.Host} {h.Kind} {h.IsCanonical} {h.Status} {Convert.ToHexString(h.Version)}")));
    }

    // Writes the row as its site and keeps its key and every value as stored.
    private static async Task<(object?[] Key, PropertyValues Row)> SeedAsync<T>(
        IServiceProvider services,
        SiteId site,
        Func<PantryPressDbContext, Task<T>> make)
        where T : class, ISiteOwned
    {
        await using var scope = In(services, site);
        var db = Db(scope);
        var row = await make(db);
        await db.SaveChangesAsync();
        Assert.Equal(site, row.SiteId);
        return (KeyOf(db, row), db.Entry(row).CurrentValues.Clone());
    }

    // The write must fail with the refusal expected of it - staged or saved - and the caller then proves nothing changed.
    private static async Task<TException> AssertRefusedAsync<TException>(
        IServiceProvider services,
        SiteId site,
        Action<PantryPressDbContext> write)
        where TException : Exception
    {
        await using var scope = In(services, site);
        var db = Db(scope);
        return await Assert.ThrowsAnyAsync<TException>(async () =>
        {
            write(db);
            await db.SaveChangesAsync();
        });
    }

    private static object?[] KeyOf<T>(DbContext db, T row)
        where T : class
    {
        var entry = db.Entry(row);
        return [.. PrimaryKey<T>(db).Properties.Select(p => entry.Property(p.Name).CurrentValue)];
    }

    private static IKey PrimaryKey<T>(DbContext db) =>
        db.Model.FindEntityType(typeof(T))?.FindPrimaryKey()
        ?? throw new InvalidOperationException($"{typeof(T).Name} is not an entity with a key.");

    // entity => EF.Property<K1>(entity, "K1") == k1 && ...
    private static Expression<Func<T, bool>> ByKey<T>(DbContext db, object?[] key)
    {
        var entity = Expression.Parameter(typeof(T), "entity");
        var property = typeof(EF).GetMethod(nameof(EF.Property))!;
        var body = PrimaryKey<T>(db).Properties
            .Select((p, i) => (Expression)Expression.Equal(
                Expression.Call(property.MakeGenericMethod(p.ClrType), entity, Expression.Constant(p.Name)),
                Expression.Constant(key[i], p.ClrType)))
            .Aggregate(Expression.AndAlso);
        return Expression.Lambda<Func<T, bool>>(body, entity);
    }

    private static async Task<string> WithoutTraceAsync(HttpResponseMessage response)
    {
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.True(problem.Remove("traceId"));
        return problem.ToJsonString();
    }

    private static string Headers(HttpResponseMessage response) => string.Join(
        "\n",
        response.Headers.Concat(response.Content.Headers)
            .Where(h => h.Key is not ("Date" or "Content-Length"))
            .OrderBy(h => h.Key, StringComparer.Ordinal)
            .Select(h => $"{h.Key}: {string.Join(",", h.Value)}"));
}
