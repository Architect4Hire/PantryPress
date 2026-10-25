using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Tenancy;

namespace PantryPress.Tests.Managers.Tenancy;

// PP2.4: the site context (tenancy.md rule 2) is one per scope, set once, and read the same through either face.
public sealed class SiteContextTests
{
    private static readonly SiteId SiteA = new(Guid.Parse("0f7d8a1e-5c2b-4e6f-9a10-aabbccddeeff"));
    private static readonly SiteId SiteB = new(Guid.Parse("1e8c9b2f-6d3a-4f70-8b21-bbccddeeff00"));

    [Fact]
    public void A_scope_starts_with_no_site_and_site_work_there_is_a_bug()
    {
        using var services = Services();
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ISiteContext>();

        Assert.Null(context.Current);
        Assert.Throws<InvalidOperationException>(() => context.Required);
    }

    [Fact]
    public void Entering_a_site_is_what_the_scope_reads_through_either_face()
    {
        using var services = Services();
        using var scope = services.CreateScope();

        scope.ServiceProvider.GetRequiredService<ISiteContextWriter>().Enter(SiteA);

        var context = scope.ServiceProvider.GetRequiredService<ISiteContext>();
        Assert.Equal(SiteA, context.Current);
        Assert.Equal(SiteA, context.Required);
    }

    [Fact]
    public void One_scope_works_on_one_site()
    {
        using var services = Services();
        using var scope = services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<ISiteContextWriter>();
        writer.Enter(SiteA);

        // The same site again is the several policies on one route agreeing; another site is a bug.
        writer.Enter(SiteA);
        Assert.Throws<InvalidOperationException>(() => writer.Enter(SiteB));

        Assert.Equal(SiteA, scope.ServiceProvider.GetRequiredService<ISiteContext>().Current);
    }

    [Fact]
    public void No_site_is_not_a_site()
    {
        using var services = Services();
        using var scope = services.CreateScope();

        Assert.Throws<ArgumentException>(() => scope.ServiceProvider.GetRequiredService<ISiteContextWriter>().Enter(default));
        Assert.Null(scope.ServiceProvider.GetRequiredService<ISiteContext>().Current);
    }

    [Fact]
    public void Each_scope_has_its_own_site()
    {
        using var services = Services();
        using var first = services.CreateScope();
        using var second = services.CreateScope();

        first.ServiceProvider.GetRequiredService<ISiteContextWriter>().Enter(SiteA);
        second.ServiceProvider.GetRequiredService<ISiteContextWriter>().Enter(SiteB);

        Assert.Equal(SiteA, first.ServiceProvider.GetRequiredService<ISiteContext>().Current);
        Assert.Equal(SiteB, second.ServiceProvider.GetRequiredService<ISiteContext>().Current);
    }

    [Fact]
    public void Registering_it_twice_is_still_one_context_per_scope()
    {
        using var services = new ServiceCollection().AddSiteContext().AddSiteContext().BuildServiceProvider(validateScopes: true);
        using var scope = services.CreateScope();

        scope.ServiceProvider.GetRequiredService<ISiteContextWriter>().Enter(SiteA);

        Assert.Single(scope.ServiceProvider.GetServices<ISiteContext>());
        Assert.Equal(SiteA, scope.ServiceProvider.GetRequiredService<ISiteContext>().Current);
    }

    private static ServiceProvider Services() =>
        new ServiceCollection().AddSiteContext().BuildServiceProvider(validateScopes: true);
}
