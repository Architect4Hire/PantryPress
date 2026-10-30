using Aspire.Hosting.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.MigrationService;
using PantryPress.Tests.TwoSite;

namespace PantryPress.Tests.Persistence;

// PP2.5: the global site filter and the SiteId stamp (tenancy.md rules 2 and 3; ARCH-015), through the real context and
// its registration over two look-alike sites with the same recipe names. A site reads, counts, finds, includes, and
// bulk-changes only its own rows; a new row takes the scope's site; and every way of writing another site's row, or of
// moving one, fails. The planted tables sit in a schema of their own on the migrated database, dropped afterwards.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class SiteOwnershipDatabaseTests(MigratedDatabase database) : IAsyncLifetime
{
    private ServiceProvider _services = null!;

    private static CancellationToken Timeout => CancellationToken.None;

    public async Task InitializeAsync()
    {
        var connectionString = await database.App.GetConnectionStringAsync(MigrationServiceExtensions.ConnectionName, Timeout)
            ?? throw new InvalidOperationException("The test AppHost has no pantrypress connection string.");

        var services = new ServiceCollection();
        services.AddAccountsIdentitySchema();
        services.AddPantryPressDbContext((_, options) => options
            .UseSqlServer(connectionString)
            .ConfigureWarnings(IgnoreProviderCount)
            .ReplaceService<IModelCustomizer, SiteOwnedProbeCustomizer>());
        _services = services.BuildServiceProvider(validateScopes: true);

        // The planted tables, by hand: only the migration service builds the product's schema, and these are not part of
        // it. They live in a schema of their own and go when the class is done.
        await using var scope = _services.CreateAsyncScope();
        var db = Db(scope).Database;
        await db.ExecuteSqlRawAsync("CREATE SCHEMA probe", Timeout);
        await db.ExecuteSqlRawAsync(
            """
            CREATE TABLE probe.ProbeRecipes (
                Id uniqueidentifier NOT NULL PRIMARY KEY,
                SiteId uniqueidentifier NOT NULL,
                Name nvarchar(100) NOT NULL,
                CONSTRAINT AK_ProbeRecipes_SiteId_Id UNIQUE (SiteId, Id));
            CREATE INDEX IX_ProbeRecipes_SiteId_Name ON probe.ProbeRecipes (SiteId, Name);
            CREATE TABLE probe.ProbeSteps (
                Id uniqueidentifier NOT NULL PRIMARY KEY,
                SiteId uniqueidentifier NOT NULL,
                RecipeId uniqueidentifier NOT NULL,
                Text nvarchar(100) NOT NULL,
                CONSTRAINT FK_ProbeSteps_ProbeRecipes FOREIGN KEY (SiteId, RecipeId)
                    REFERENCES probe.ProbeRecipes (SiteId, Id) ON DELETE CASCADE);
            CREATE INDEX IX_ProbeSteps_SiteId_RecipeId ON probe.ProbeSteps (SiteId, RecipeId);
            """,
            Timeout);
    }

    public async Task DisposeAsync()
    {
        await using (var scope = _services.CreateAsyncScope())
        {
            await Db(scope).Database.ExecuteSqlRawAsync(
                "DROP TABLE IF EXISTS probe.ProbeSteps; DROP TABLE IF EXISTS probe.ProbeRecipes; DROP SCHEMA IF EXISTS probe;",
                Timeout);
        }

        await _services.DisposeAsync();
    }

    [Fact]
    public async Task A_site_reads_only_its_own_rows_of_look_alike_sites()
    {
        var (a, b) = (NewSite(), NewSite());
        var aSupper = await SeedAsync(a, "Supper", "Pie");
        var bSupper = await SeedAsync(b, "Supper", "Pie");

        await using var scope = Enter(b);
        var db = Db(scope);

        var suppers = await db.Set<ProbeRecipe>().Where(r => r.Name == "Supper").ToListAsync(Timeout);
        Assert.Equal(bSupper, Assert.Single(suppers).Id);
        Assert.All(await db.Set<ProbeRecipe>().ToListAsync(Timeout), r => Assert.Equal(b, r.SiteId));
        Assert.Equal(2, await db.Set<ProbeRecipe>().CountAsync(Timeout));
        Assert.Equal(2, await db.Set<ProbeStep>().CountAsync(Timeout));
        Assert.Null(await db.Set<ProbeRecipe>().FindAsync([aSupper], Timeout));
        Assert.False(await db.Set<ProbeRecipe>().AnyAsync(r => r.Id == aSupper, Timeout));
        Assert.Empty(await db.Set<ProbeStep>().Where(s => s.RecipeId == aSupper).ToListAsync(Timeout));
    }

    [Fact]
    public async Task Included_and_raw_reads_stay_inside_the_site()
    {
        var (a, b) = (NewSite(), NewSite());
        var aSupper = await SeedAsync(a, "Supper");
        await SeedAsync(b, "Supper");

        await using var scope = Enter(a);
        var db = Db(scope);

        var recipe = await db.Set<ProbeRecipe>().Include(r => r.Steps).SingleAsync(r => r.Id == aSupper, Timeout);
        Assert.Equal(["Supper step"], recipe.Steps.Select(s => s.Text));

        var named = await db.Set<ProbeRecipe>().FromSql($"SELECT * FROM probe.ProbeRecipes WHERE Name = {"Supper"}").ToListAsync(Timeout);
        Assert.Equal(aSupper, Assert.Single(named).Id);
    }

    // An id a person sends - a recipe card naming a recipe (RCP-009) - must not link one site's row to another's. The
    // reference is keyed by site, so the step takes B's site and finds no B recipe with A's id, by a tracked save or raw.
    [Fact]
    public async Task A_row_cannot_point_at_another_sites_row()
    {
        var (a, b) = (NewSite(), NewSite());
        var aSupper = await SeedAsync(a, "Supper");
        await SeedAsync(b, "Supper");

        await using (var scope = Enter(b))
        {
            var db = Db(scope);
            db.Add(new ProbeStep { RecipeId = aSupper, Text = "stolen" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Timeout));
        }

        // Through a stub of A's recipe passed off as B's: the stub is never written, so its concurrency token never runs.
        // The save or the key refuses the step - which one depends on how EF Core takes a new step reached by navigation.
        await using (var scope = Enter(b))
        {
            var db = Db(scope);
            var stub = new ProbeRecipe { Id = aSupper, SiteId = b };
            db.Attach(stub);
            stub.Steps.Add(new ProbeStep { Text = "stolen" });
            await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync(Timeout));
        }

        // Moving one of B's own steps under A's recipe, with the synchronous save.
        await using (var scope = Enter(b))
        {
            var db = Db(scope);
            var step = await db.Set<ProbeStep>().SingleAsync(Timeout);
            step.RecipeId = aSupper;
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }

        await using (var scope = Enter(b))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => Db(scope).Database.ExecuteSqlAsync(
                $"INSERT INTO probe.ProbeSteps (Id, SiteId, RecipeId, Text) VALUES ({Guid.NewGuid()}, {b.Value}, {aSupper}, {"stolen"})",
                Timeout));
        }

        await using (var scope = Enter(a))
        {
            Assert.Equal(["Supper step"], await Db(scope).Set<ProbeStep>().Select(s => s.Text).ToListAsync(Timeout));
        }
    }

    // A site removing its own row cascades only to its own: no other site's row ever hangs off it.
    [Fact]
    public async Task Removing_a_row_cascades_only_inside_its_site()
    {
        var (a, b) = (NewSite(), NewSite());
        var aSupper = await SeedAsync(a, "Supper");
        await SeedAsync(b, "Supper");

        await using (var scope = Enter(a))
        {
            Assert.Equal(1, await Db(scope).Set<ProbeRecipe>().Where(r => r.Id == aSupper).ExecuteDeleteAsync(Timeout));
        }

        await using (var scope = Enter(b))
        {
            var db = Db(scope);
            Assert.Equal(1, await db.Set<ProbeRecipe>().CountAsync(Timeout));
            Assert.Equal(1, await db.Set<ProbeStep>().CountAsync(Timeout));
        }
    }

    [Fact]
    public async Task Bulk_updates_and_deletes_reach_only_the_sites_own_rows()
    {
        var (a, b) = (NewSite(), NewSite());
        var aSupper = await SeedAsync(a, "Supper");
        var bSupper = await SeedAsync(b, "Supper");

        await using (var scope = Enter(b))
        {
            var db = Db(scope);
            Assert.Equal(0, await db.Set<ProbeRecipe>().Where(r => r.Id == aSupper)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Name, "Taken"), Timeout));
            Assert.Equal(0, await db.Set<ProbeStep>().Where(s => s.RecipeId == aSupper).ExecuteDeleteAsync(Timeout));
            Assert.Equal(0, await db.Set<ProbeRecipe>().Where(r => r.Id == aSupper).ExecuteDeleteAsync(Timeout));
            Assert.Equal(1, await db.Set<ProbeRecipe>().Where(r => r.Name == "Supper")
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Name, "Dinner"), Timeout));
        }

        await using (var scope = Enter(a))
        {
            var recipe = await Db(scope).Set<ProbeRecipe>().Include(r => r.Steps).SingleAsync(Timeout);
            Assert.Equal(("Supper", 1), (recipe.Name, recipe.Steps.Count));
        }

        Assert.Equal("Dinner", await NameOfAsync(bSupper));
    }

    [Fact]
    public async Task Each_scope_filters_by_its_own_site_over_one_cached_model()
    {
        var (a, b) = (NewSite(), NewSite());
        var aSupper = await SeedAsync(a, "Supper");
        var bSupper = await SeedAsync(b, "Supper");

        await using var first = Enter(a);
        await using var second = Enter(b);
        var (dbA, dbB) = (Db(first), Db(second));

        Assert.Same(dbA.Model, dbB.Model);
        for (var round = 0; round < 3; round++)
        {
            Assert.Equal(aSupper, (await dbA.Set<ProbeRecipe>().AsNoTracking().SingleAsync(Timeout)).Id);
            Assert.Equal(bSupper, (await dbB.Set<ProbeRecipe>().AsNoTracking().SingleAsync(Timeout)).Id);
        }
    }

    // The membership check makes the context before it enters the site (PP2.4), so the filter must read the site per query.
    [Fact]
    public async Task A_site_entered_after_the_context_was_made_is_the_one_it_filters_by()
    {
        var a = NewSite();
        var aSupper = await SeedAsync(a, "Supper");
        await SeedAsync(NewSite(), "Supper");

        await using var scope = _services.CreateAsyncScope();
        var db = Db(scope);
        scope.ServiceProvider.GetRequiredService<ISiteContextWriter>().Enter(a);

        Assert.Equal(aSupper, (await db.Set<ProbeRecipe>().SingleAsync(Timeout)).Id);
    }

    [Fact]
    public async Task With_no_site_a_site_owned_query_throws_and_other_queries_still_run()
    {
        await SeedAsync(NewSite(), "Supper");

        await using var scope = _services.CreateAsyncScope();
        var db = Db(scope);

        await AssertNeedsASiteAsync(() => db.Set<ProbeRecipe>().ToListAsync(Timeout));
        await AssertNeedsASiteAsync(() => db.Set<ProbeStep>().CountAsync(Timeout));
        await AssertNeedsASiteAsync(() => db.Set<ProbeRecipe>().ExecuteDeleteAsync(Timeout));
        Assert.Equal(0, await db.Users.CountAsync(u => u.Id == Guid.NewGuid(), Timeout));
    }

    [Fact]
    public async Task A_new_row_takes_the_scopes_site_and_never_another()
    {
        var (a, b) = (NewSite(), NewSite());

        await using (var scope = Enter(a))
        {
            var db = Db(scope);
            var stamped = new ProbeRecipe { Name = "Stamped" };
            var named = new ProbeRecipe { Name = "Named", SiteId = a };
            db.AddRange(stamped, named);
            await db.SaveChangesAsync(Timeout);

            Assert.Equal(a, stamped.SiteId);
            Assert.Equal(a, await SiteOfAsync(stamped.Id));
            Assert.Equal(a, await SiteOfAsync(named.Id));
        }

        var elsewhere = new ProbeRecipe { Name = "Elsewhere", SiteId = b };
        await using (var scope = Enter(a))
        {
            var db = Db(scope);
            db.AddRange(new ProbeRecipe { Name = "Innocent" }, elsewhere);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Timeout));
            Assert.Contains("another site", error.Message, StringComparison.Ordinal);
        }

        Assert.Null(await SiteOfAsync(elsewhere.Id));

        var orphan = new ProbeRecipe { Name = "Orphan" };
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = Db(scope);
            db.Add(orphan);
            await AssertNeedsASiteAsync(() => db.SaveChangesAsync(Timeout));
        }

        Assert.Null(await SiteOfAsync(orphan.Id));
    }

    [Fact]
    public async Task A_rows_site_never_changes()
    {
        var (a, b) = (NewSite(), NewSite());
        var aSupper = await SeedAsync(a, "Supper");

        // A step's site is its own; a recipe's is also part of the key its steps point at, which EF Core itself will not
        // let change.
        await using (var scope = Enter(a))
        {
            var db = Db(scope);
            var step = await db.Set<ProbeStep>().SingleAsync(Timeout);
            step.SiteId = b;
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Timeout));
            Assert.Contains("never changes", error.Message, StringComparison.Ordinal);
        }

        await using (var scope = Enter(a))
        {
            var db = Db(scope);
            var recipe = await db.Set<ProbeRecipe>().SingleAsync(Timeout);
            recipe.SiteId = b;
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => db.SaveChangesAsync(Timeout));
        }

        await using (var scope = Enter(a))
        {
            var db = Db(scope);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.Set<ProbeRecipe>()
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.SiteId, b), Timeout));
            Assert.Contains("bulk update", error.Message, StringComparison.Ordinal);

            error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.Set<ProbeStep>()
                .ExecuteUpdateAsync(s => s.SetProperty(r => EF.Property<SiteId>(r, "SiteId"), b), Timeout));
            Assert.Contains("bulk update", error.Message, StringComparison.Ordinal);

            error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.Set<ProbeStep>()
                .Select(r => new { Step = r })
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Step.SiteId, b), Timeout));
            Assert.Contains("bulk update", error.Message, StringComparison.Ordinal);

            // A bulk update that leaves the site alone still runs.
            Assert.Equal(1, await db.Set<ProbeRecipe>().ExecuteUpdateAsync(s => s.SetProperty(r => r.Name, "Supper"), Timeout));
        }

        Assert.Equal(a, await SiteOfAsync(aSupper));
    }

    [Fact]
    public async Task Another_sites_row_cannot_be_attached_and_written()
    {
        var (a, b) = (NewSite(), NewSite());
        var aSupper = await SeedAsync(a, "Supper");

        // Attached as A's row it is refused before anything is sent; attached as a stub with no site, too.
        await using (var scope = Enter(b))
        {
            var db = Db(scope);
            db.Update(new ProbeRecipe { Id = aSupper, SiteId = a, Name = "Taken" });
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Timeout));
        }

        await using (var scope = Enter(b))
        {
            var db = Db(scope);
            db.Remove(new ProbeRecipe { Id = aSupper });
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Timeout));
        }

        // Attached under B's own site - a lie the save cannot see - the concurrency token on SiteId still matches A's row
        // to nothing, so the database changes nothing.
        await using (var scope = Enter(b))
        {
            var db = Db(scope);
            db.Update(new ProbeRecipe { Id = aSupper, SiteId = b, Name = "Taken" });
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync(Timeout));
        }

        await using (var scope = Enter(b))
        {
            var db = Db(scope);
            db.Remove(new ProbeRecipe { Id = aSupper, SiteId = b });
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync(Timeout));
        }

        Assert.Equal("Supper", await NameOfAsync(aSupper));
        Assert.Equal(a, await SiteOfAsync(aSupper));
    }

    [Fact]
    public async Task A_site_changes_and_removes_its_own_rows()
    {
        var a = NewSite();
        var aSupper = await SeedAsync(a, "Supper");

        await using (var scope = Enter(a))
        {
            var db = Db(scope);
            var recipe = await db.Set<ProbeRecipe>().Include(r => r.Steps).SingleAsync(Timeout);
            recipe.Name = "Dinner";
            db.RemoveRange(recipe.Steps);
            await db.SaveChangesAsync(Timeout);
        }

        Assert.Equal("Dinner", await NameOfAsync(aSupper));
        await using (var scope = Enter(a))
        {
            Assert.Equal(0, await Db(scope).Set<ProbeStep>().CountAsync(Timeout));
        }
    }

    // PP2.7: the harness's checks, run through the harness itself over both probe types - a root and a dependent keyed
    // by site - so they are proven before the first real entity leans on them.
    [Fact]
    public async Task The_two_site_check_holds_for_both_probe_types()
    {
        await using var harness = await ProbeHarnessAsync();

        await TwoSiteChecks.AssertSiteOwnedIsolatedAsync<ProbeRecipe>(
            harness.Services,
            NewSite(),
            NewSite(),
            MakeRecipe,
            set => set.SetProperty(r => r.Name, "Taken"),
            r => r.Name = "Taken");

        await TwoSiteChecks.AssertSiteOwnedIsolatedAsync<ProbeStep>(
            harness.Services,
            NewSite(),
            NewSite(),
            db =>
            {
                var recipe = db.Add(new ProbeRecipe { Name = "Supper" }).Entity;
                var step = new ProbeStep { Text = "Supper step" };
                recipe.Steps.Add(step);
                return Task.FromResult(step);
            },
            set => set.SetProperty(s => s.Text, "Taken"),
            s => s.Text = "Taken");
    }

    // A check must be able to fail: handed one site twice, A sees "B's" row, and the check says so.
    [Fact]
    public async Task The_two_site_check_catches_a_site_that_can_see_the_other()
    {
        await using var harness = await ProbeHarnessAsync();
        var same = NewSite();

        await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(() => TwoSiteChecks.AssertSiteOwnedIsolatedAsync<ProbeRecipe>(
            harness.Services,
            same,
            same,
            MakeRecipe,
            set => set.SetProperty(r => r.Name, "Taken"),
            r => r.Name = "Taken"));
    }

    // A raw read composed over the set is filtered; one past it is not, and the read-path check catches it.
    [Fact]
    public async Task The_read_path_check_passes_a_filtered_read_and_catches_a_raw_one()
    {
        await using var harness = await ProbeHarnessAsync();

        await TwoSiteChecks.AssertReadPathIsolatedAsync<ProbeRecipe>(
            harness.Services,
            NewSite(),
            NewSite(),
            MakeRecipe,
            async services => [.. (await services.GetRequiredService<PantryPressDbContext>().Set<ProbeRecipe>()
                .FromSql($"SELECT * FROM probe.ProbeRecipes WHERE Name = {"Supper"}")
                .Select(r => r.Id)
                .ToListAsync(Timeout)).Select(id => new object?[] { id })]);

        await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(() => TwoSiteChecks.AssertReadPathIsolatedAsync<ProbeRecipe>(
            harness.Services,
            NewSite(),
            NewSite(),
            MakeRecipe,
            async services => [.. (await services.GetRequiredService<PantryPressDbContext>().Database
                .SqlQuery<Guid>($"SELECT Id AS Value FROM probe.ProbeRecipes WHERE Name = {"Supper"}")
                .ToListAsync(Timeout)).Select(id => new object?[] { id })]));
    }

    private async Task<TwoSiteHarness> ProbeHarnessAsync() =>
        await TwoSiteHarness.CreateAsync(
            database,
            configure: o => o.ConfigureWarnings(IgnoreProviderCount).ReplaceService<IModelCustomizer, SiteOwnedProbeCustomizer>());

    // The planted model is EF Core's own internal provider, which it counts across the whole test process; the count says
    // nothing about the site rules, so it is ignored, as SiteOwnershipModelTests does with the same planted model.
    private static void IgnoreProviderCount(WarningsConfigurationBuilder warnings) =>
        warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning);

    private static Task<ProbeRecipe> MakeRecipe(PantryPressDbContext db) =>
        Task.FromResult(db.Add(new ProbeRecipe { Name = "Supper" }).Entity);

    private static SiteId NewSite() => new(Guid.NewGuid());

    private static PantryPressDbContext Db(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();

    private static async Task AssertNeedsASiteAsync(Func<Task> work)
    {
        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(work);
        Assert.Contains("needs a site", error.Message, StringComparison.Ordinal);
    }

    private AsyncServiceScope Enter(SiteId site)
    {
        var scope = _services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ISiteContextWriter>().Enter(site);
        return scope;
    }

    // One recipe per name, each with one step, written as the site; returns the first recipe's id.
    private async Task<Guid> SeedAsync(SiteId site, params string[] names)
    {
        await using var scope = Enter(site);
        var db = Db(scope);
        var recipes = names.Select(name => new ProbeRecipe { Name = name }).ToList();
        foreach (var recipe in recipes)
        {
            recipe.Steps.Add(new ProbeStep { Text = $"{recipe.Name} step" });
        }

        db.AddRange(recipes);
        await db.SaveChangesAsync(Timeout);
        return recipes[0].Id;
    }

    // Straight from the table, past the filter, so a test can see what was really written.
    private async Task<SiteId?> SiteOfAsync(Guid recipe)
    {
        await using var scope = _services.CreateAsyncScope();
        var site = await Db(scope).Database
            .SqlQuery<Guid>($"SELECT SiteId AS Value FROM probe.ProbeRecipes WHERE Id = {recipe}")
            .ToListAsync(Timeout);
        return site is [var value] ? new SiteId(value) : null;
    }

    private async Task<string?> NameOfAsync(Guid recipe)
    {
        await using var scope = _services.CreateAsyncScope();
        return (await Db(scope).Database
            .SqlQuery<string>($"SELECT Name AS Value FROM probe.ProbeRecipes WHERE Id = {recipe}")
            .ToListAsync(Timeout)).SingleOrDefault();
    }
}
