using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.ApiService.Hosting;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Recipes;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Tests.Persistence;
using PantryPress.Tests.Recipes;

namespace PantryPress.Tests.TwoSite;

// PP4.11: the recipe routes over ApiService in the two-site world (tenancy.md, "Two-site test"; the PP4.3 and PP4.4
// hand-offs). Both sites hold look-alike recipes. Site B's recipes, and its parts, are no recipe and no part on site A's
// routes, answered exactly as an unknown id; nobody but A's active members reaches A's routes at all; the accounts that
// cross the sites carry each site's own role; and nothing done on one site changes a row of the other.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class RecipesApiTwoSiteTests(MigratedDatabase database) : IAsyncLifetime
{
    // The API's own JSON: camel-case names and kebab-case enums.
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    private TwoSiteHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await TwoSiteHarness.CreateAsync(database, withApi: true);

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task A_list_holds_only_the_sites_own_recipes()
    {
        var world = await _harness.SeedAsync("rca-list");
        var onA = await CreateAsync(world.A, world.A.Owner);
        var onB = await CreateAsync(world.B, world.B.Owner);
        using var shared = _harness.ClientFor(world.Shared);

        Assert.Equal([onA.Id], await ListIdsAsync(shared, world.A.Slug, archived: false));
        Assert.Equal([onB.Id], await ListIdsAsync(shared, world.B.Slug, archived: false));

        // A cursor from one site's list only positions the other's: it names no site, and reaches none.
        await CreateAsync(world.A, world.A.Owner);
        using var firstA = await shared.GetAsync($"{Recipes(world.A.Slug)}?limit=1");
        var cursor = (await firstA.Content.ReadFromJsonAsync<RecipeSummaryPage>(Web))!.NextCursor!;
        using var onBWithA = await shared.GetAsync($"{Recipes(world.B.Slug)}?cursor={Uri.EscapeDataString(cursor)}");
        Assert.Equal(HttpStatusCode.OK, onBWithA.StatusCode);
        Assert.All((await onBWithA.Content.ReadFromJsonAsync<RecipeSummaryPage>(Web))!.Items, i => Assert.Equal(onB.Id, i.Id));
    }

    // The other site's recipe is no recipe here: on every route, its id is answered exactly as an id nobody holds, and
    // neither site changes.
    [Fact]
    public async Task The_other_sites_recipe_is_answered_as_an_unknown_one()
    {
        var world = await _harness.SeedAsync("rca-other");
        var onA = await CreateAsync(world.A, world.A.Owner);
        var onB = await CreateAsync(world.B, world.B.Owner);
        var unknown = Guid.NewGuid();
        var before = await StateAsync(world);

        // Shared is an Owner on B and an Author on A; A's Owner belongs to A alone.
        foreach (var account in new[] { world.A.Owner, world.Shared })
        {
            using var client = _harness.ClientFor(account);
            foreach (var (method, suffix, body) in Routes(onA))
            {
                using var other = await SendAsync(client, method, $"{Recipes(world.A.Slug)}/{onB.Id}{suffix}", body);
                using var none = await SendAsync(client, method, $"{Recipes(world.A.Slug)}/{unknown}{suffix}", body);
                await TwoSiteChecks.AssertIndistinguishableAsync(other, none, HttpStatusCode.NotFound, RecipeErrors.RecipeNotFound.Code);
            }
        }

        Assert.Equal(before, await StateAsync(world));
    }

    // Anyone who is not an active member of A - a stranger, a suspended member, B's members, and the accounts that are
    // suspended on A or members of B alone - is answered on A's routes exactly as for a site that does not exist, for a
    // good body and a bad one alike, so not even a validation answer tells a real site from none.
    [Fact]
    public async Task Anyone_else_is_answered_as_for_no_site_and_nothing_changes()
    {
        var world = await _harness.SeedAsync("rca-refused");
        var onA = await CreateAsync(world.A, world.A.Owner);
        await CreateAsync(world.B, world.B.Owner);
        var missing = $"none-{Guid.NewGuid():N}"[..30];
        var before = await StateAsync(world);

        foreach (var account in new[] { world.A.Suspended, world.SuspendedOnA, world.Stranger, world.B.Owner, world.B.Author })
        {
            using var client = _harness.ClientFor(account);
            await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, async () =>
            {
                foreach (var (method, path, body) in AllRoutes(onA).Concat(AllRoutes(onA, broken: true)))
                {
                    using var onReal = await SendAsync(client, method, $"/api/v1/sites/{world.A.Slug}/recipes{path}", body);
                    using var onNone = await SendAsync(client, method, $"/api/v1/sites/{missing}/recipes{path}", body);
                    await TwoSiteChecks.AssertIndistinguishableAsync(onReal, onNone, HttpStatusCode.NotFound, SiteErrors.SiteNotFound.Code);
                }
            });
        }

        Assert.Equal(before, await StateAsync(world));
    }

    // A save that names the other site's group or line - ids the caller could have read there - is a field error at that
    // id, never a 500 and never a write to either site (the PP4.3 hand-off).
    [Fact]
    public async Task A_save_naming_the_other_sites_parts_is_refused_at_their_ids()
    {
        var world = await _harness.SeedAsync("rca-parts");
        var onA = await CreateAsync(world.A, world.A.Owner);
        var onB = await CreateAsync(world.B, world.B.Owner);
        var before = await StateAsync(world);
        using var shared = _harness.ClientFor(world.Shared);
        using var owner = _harness.ClientFor(world.A.Owner);
        var content = RecipesFacadeTests.Echo(onA) with
        {
            IngredientGroups = [new RecipeIngredientGroupInput(onB.IngredientGroups[0].Id, null, [new(onB.IngredientGroups[0].Lines[0].Id, "1 egg")])],
            StepGroups = [new RecipeStepGroupInput(onA.StepGroups[0].Id, null, [new(onB.StepGroups[0].Steps[0].Id, "Stir.")])],
        };

        using var response = await owner.PutAsJsonAsync($"{Recipes(world.A.Slug)}/{onA.Id}", new UpdateRecipeRequest(onA.Version, content), Web);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation-failed", await response.CodeAsync());
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            ["content.ingredientGroups[0].id", "content.ingredientGroups[0].lines[0].id", "content.stepGroups[0].steps[0].id"],
            problem.RootElement.GetProperty("errors").EnumerateObject().Select(e => e.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(world.B.Slug, problem.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(before, await StateAsync(world));
    }

    // The account that crosses the sites carries each site's role: an Author on A, it cannot change A's Owner's recipe; an
    // Owner on B, it changes B's; and the change on B leaves every row of A as it was.
    [Fact]
    public async Task The_shared_account_carries_each_sites_own_role()
    {
        var world = await _harness.SeedAsync("rca-roles");
        var onA = await CreateAsync(world.A, world.A.Owner);
        var onB = await CreateAsync(world.B, world.B.Author);
        using var shared = _harness.ClientFor(world.Shared);
        var stateOfA = await StateOfAsync(world.A.Id);

        var read = await shared.GetFromJsonAsync<RecipeDetail>($"{Recipes(world.A.Slug)}/{onA.Id}", Web);
        Assert.False(read!.CanEdit);
        using var change = await shared.PutAsJsonAsync($"{Recipes(world.A.Slug)}/{onA.Id}", new UpdateRecipeRequest(onA.Version, RecipesFacadeTests.Echo(onA) with { Title = "Mine" }), Web);
        using var archive = await shared.PostAsync($"{Recipes(world.A.Slug)}/{onA.Id}/archive", null);
        Assert.Equal(HttpStatusCode.Forbidden, change.StatusCode);
        Assert.Equal(RecipeErrors.RecipeNotYours.Code, await change.CodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, archive.StatusCode);

        using var onBChange = await shared.PutAsJsonAsync($"{Recipes(world.B.Slug)}/{onB.Id}", new UpdateRecipeRequest(onB.Version, RecipesFacadeTests.Echo(onB) with { Title = "Owner's edit" }), Web);
        Assert.Equal(HttpStatusCode.OK, onBChange.StatusCode);
        Assert.Equal(stateOfA, await StateOfAsync(world.A.Id));
    }

    // Copying A's recipe on B's route, as the Owner of B who can read A, finds nothing: a duplicate never crosses sites.
    [Fact]
    public async Task A_duplicate_never_crosses_sites()
    {
        var world = await _harness.SeedAsync("rca-copy");
        var onA = await CreateAsync(world.A, world.A.Owner);
        var before = await StateAsync(world);
        using var shared = _harness.ClientFor(world.Shared);

        using var across = await shared.PostAsync($"{Recipes(world.B.Slug)}/{onA.Id}/duplicate", null);

        Assert.Equal(HttpStatusCode.NotFound, across.StatusCode);
        Assert.Equal(RecipeErrors.RecipeNotFound.Code, await across.CodeAsync());
        Assert.Equal(before, await StateAsync(world));

        using var within = await shared.PostAsync($"{Recipes(world.A.Slug)}/{onA.Id}/duplicate", null);
        Assert.Equal(HttpStatusCode.Created, within.StatusCode);
        var copy = (await within.Content.ReadFromJsonAsync<RecipeDetail>(Web))!;
        Assert.Equal($"/api/v1/sites/{world.A.Slug}/recipes/{copy.Id:D}", within.Headers.Location!.OriginalString);
        Assert.Contains(copy.Id, await ListIdsAsync(shared, world.A.Slug, archived: false));
        Assert.DoesNotContain(copy.Id, await ListIdsAsync(shared, world.B.Slug, archived: false));
    }

    [Fact]
    public async Task A_recipe_is_written_on_the_wire_as_the_contract_says()
    {
        var world = await _harness.SeedAsync("rca-wire");
        using var client = _harness.ClientFor(world.A.Author);
        var content = RecipeEditorTests.Content(ingredients: [new RecipeIngredientGroupInput(null, null, [new(null, "2 3 cups"), new(null, "salt to taste")])]);

        using var response = await client.PostAsJsonAsync(Recipes(world.A.Slug), content, Web);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(
            [
                "id", "version", "title", "summary", "yield", "yieldUnit", "prepMinutes", "cookMinutes", "additionalMinutes",
                "additionalTimeLabel", "videoUrl", "nutrition", "ingredientGroups", "stepGroups", "equipment", "notes",
                "keywords", "courses", "cuisines", "diets", "allergens", "createdAt", "updatedAt", "archivedAt", "canEdit",
            ],
            root.EnumerateObject().Select(p => p.Name));
        Assert.Equal("""{"kind":"range","low":{"numerator":6,"denominator":1},"high":{"numerator":8,"denominator":1}}""", root.GetProperty("yield").GetRawText());
        Assert.Equal(410.5m, root.GetProperty("nutrition").GetProperty("calories").GetDecimal());
        var lines = root.GetProperty("ingredientGroups")[0].GetProperty("lines");
        Assert.Equal(
            """{"originalText":"2 3 cups","status":"unparsed","problem":"ambiguous-quantity","quantity":null,"quantitySpan":null,"unitCode":null,"unitSpan":null,"item":null,"note":null}""",
            WithoutId(lines[0]));
        Assert.Equal(
            """{"originalText":"salt to taste","status":"parsed","problem":null,"quantity":{"kind":"to-taste","low":null,"high":null},"quantitySpan":{"start":5,"length":8},"unitCode":null,"unitSpan":null,"item":"salt","note":null}""",
            WithoutId(lines[1]));
        Assert.Equal(JsonValueKind.Null, root.GetProperty("archivedAt").ValueKind);
        Assert.True(root.GetProperty("canEdit").GetBoolean());
        Assert.Equal(12, root.GetProperty("version").GetString()!.Length);
    }

    // What a client sends that the API cannot read, or must not keep, is a validation answer at the field, never a 500:
    // parsed parts sent beside a line are not read at all (the PP4.6 hand-off), and a lone surrogate is refused at the edge.
    [Fact]
    public async Task Parts_a_client_parsed_are_ignored_and_broken_text_is_refused()
    {
        var world = await _harness.SeedAsync("rca-edge");
        using var client = _harness.ClientFor(world.A.Owner);
        var escape = "\\" + "u";

        using var claimed = await PostRawAsync(client, Recipes(world.A.Slug), """
            {"title":"Toast","ingredientGroups":[{"lines":[{"originalText":"2 cups flour","quantity":{"kind":"amount","low":{"numerator":9,"denominator":1}},"unitCode":"gram","item":"sugar","status":"unparsed"}]}]}
            """);
        Assert.Equal(HttpStatusCode.Created, claimed.StatusCode);
        var line = (await claimed.Content.ReadFromJsonAsync<RecipeDetail>(Web))!.IngredientGroups[0].Lines[0];
        Assert.Equal((IngredientParseStatus.Parsed, "cup", "flour"), (line.Status, line.UnitCode, line.Item));
        Assert.Equal(new RecipeFraction(2, 1), line.Quantity!.Low);

        using var lone = await PostRawAsync(client, Recipes(world.A.Slug), "{\"title\":\"To" + escape + "d83cast\"}");
        Assert.Equal(HttpStatusCode.BadRequest, lone.StatusCode);
        Assert.Equal("validation-failed", await lone.CodeAsync());

        using var tabs = await client.PostAsJsonAsync(Recipes(world.A.Slug), new { title = "\t\r\n" }, Web);
        Assert.Equal(HttpStatusCode.BadRequest, tabs.StatusCode);
        using var problem = JsonDocument.Parse(await tabs.Content.ReadAsStringAsync());
        Assert.Equal(["title"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(e => e.Name));
    }

    // {recipeId:guid}: a word of the routes' own beside the id - recipes/paste above all - is never read as one.
    [Fact]
    public async Task Only_a_guid_is_read_as_a_recipe_id()
    {
        var world = await _harness.SeedAsync("rca-guid");
        using var client = _harness.ClientFor(world.A.Owner);

        using var paste = await client.GetAsync($"{Recipes(world.A.Slug)}/paste");
        using var word = await client.GetAsync($"{Recipes(world.A.Slug)}/not-an-id");

        Assert.Contains(paste.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Equal(HttpStatusCode.NotFound, word.StatusCode);
        Assert.NotEqual(RecipeErrors.RecipeNotFound.Code, await word.CodeAsync());
    }

    // A create retried with its Idempotency-Key makes one recipe, and answers the first answer again.
    [Fact]
    public async Task A_create_retried_with_its_key_makes_one_recipe()
    {
        var world = await _harness.SeedAsync("rca-key");
        using var client = _harness.ClientFor(world.A.Owner);
        var key = Guid.NewGuid().ToString("N");

        using var first = await SendAsync(client, HttpMethod.Post, Recipes(world.A.Slug), RecipeEditorTests.Content(), key);
        using var again = await SendAsync(client, HttpMethod.Post, Recipes(world.A.Slug), RecipeEditorTests.Content(), key);

        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Equal("true", again.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(await first.Content.ReadAsStringAsync(), await again.Content.ReadAsStringAsync());
        Assert.Single(await ListIdsAsync(client, world.A.Slug, archived: false));
    }

    // From review: the list's raw keyset query (tenancy.md: a read path beyond the set owes its own check). Working as A, a
    // page - with a cursor and without, library and archived - holds A's recipe and never B's.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task The_library_page_read_sees_only_the_entered_sites_recipes(bool withCursor, bool archived)
    {
        var world = await _harness.SeedAsync("rca-readpath");
        await TwoSiteChecks.AssertReadPathIsolatedAsync<Recipe>(
            _harness.Services,
            world.A.Id,
            world.B.Id,
            db =>
            {
                var recipe = RecipeRows.Add(db).Recipe;
                recipe.ArchivedAt = archived ? RecipeRows.Created : null;
                return Task.FromResult(recipe);
            },
            async services =>
            {
                var repository = services.GetRequiredService<Domain.Modules.Recipes.Repositories.RecipeRepository>();
                Domain.Modules.Recipes.Business.RecipeListPosition? after = withCursor
                    ? new(RecipeRows.Created.AddDays(1), new RecipeId(Guid.Empty))
                    : null;
                var page = await repository.ListPageAsync(archived, after, 1_000, CancellationToken.None);
                return [.. page.Select(r => new object?[] { r.SiteId, r.Id })];
            });
    }

    // From review: B holds a recipe with the very id and edit time of A's, so a cursor just past A's position sits right on
    // B's twin; A's page after it never holds it, and A's page before it holds only A's.
    [Fact]
    public async Task A_page_never_reaches_the_other_sites_twin()
    {
        var world = await _harness.SeedAsync("rca-twin");
        var id = new RecipeId(Guid.NewGuid());
        await _harness.ChangeAsync(world.A.Id, db => RecipeRows.Add(db, recipeId: id));
        await _harness.ChangeAsync(world.B.Id, db => RecipeRows.Add(db, recipeId: id));

        await using var scope = _harness.In(world.A.Id);
        var repository = scope.ServiceProvider.GetRequiredService<Domain.Modules.Recipes.Repositories.RecipeRepository>();
        var after = await repository.ListPageAsync(false, new(RecipeRows.Created, id), 1_000, CancellationToken.None);
        var before = await repository.ListPageAsync(false, new(RecipeRows.Created.AddTicks(1), id), 1_000, CancellationToken.None);

        Assert.DoesNotContain(after, r => r.Id == id);
        Assert.Equal(world.A.Id, Assert.Single(before, r => r.Id == id).SiteId);
    }

    // From review (all four): a create that names part ids - posting back a recipe read on this site, or the other site's
    // ids - is a field error at the ids, never a 500 and never a row on either site.
    [Fact]
    public async Task A_create_naming_part_ids_is_refused_on_either_site_and_writes_nothing()
    {
        var world = await _harness.SeedAsync("rca-create-ids");
        var onA = await CreateAsync(world.A, world.A.Owner);
        var onB = await CreateAsync(world.B, world.B.Owner);
        var before = await StateAsync(world);
        using var shared = _harness.ClientFor(world.Shared);

        foreach (var (slug, body) in new[] { (world.A.Slug, RecipesFacadeTests.Echo(onA)), (world.A.Slug, RecipesFacadeTests.Echo(onB)), (world.B.Slug, RecipesFacadeTests.Echo(onA)) })
        {
            using var response = await shared.PostAsJsonAsync(Recipes(slug), body, Web);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Contains("ingredientGroups[0].lines[0].id", problem.RootElement.GetProperty("errors").EnumerateObject().Select(e => e.Name));
        }

        Assert.Equal(before, await StateAsync(world));
    }

    [Fact]
    public async Task An_archived_list_holds_only_the_sites_own()
    {
        var world = await _harness.SeedAsync("rca-archived");
        var onA = await CreateAsync(world.A, world.A.Owner);
        var onB = await CreateAsync(world.B, world.B.Owner);
        using var shared = _harness.ClientFor(world.Shared);
        using var owner = _harness.ClientFor(world.A.Owner);
        using var ownerB = _harness.ClientFor(world.B.Owner);
        (await owner.PostAsync($"{Recipes(world.A.Slug)}/{onA.Id}/archive", null)).Dispose();
        (await ownerB.PostAsync($"{Recipes(world.B.Slug)}/{onB.Id}/archive", null)).Dispose();

        Assert.Equal([onA.Id], await ListIdsAsync(shared, world.A.Slug, archived: true));
        Assert.Equal([onB.Id], await ListIdsAsync(shared, world.B.Slug, archived: true));
        Assert.Empty(await ListIdsAsync(shared, world.A.Slug, archived: false));
    }

    // From review: each recipe allowance is the account's own bucket across its sites; a request the site policy refuses is
    // not counted, and reading never spends a save.
    [Fact]
    public async Task Each_recipe_allowance_is_one_bucket_per_account_across_its_sites()
    {
        await using var harness = await TwoSiteHarness.CreateAsync(
            database,
            withApi: true,
            apiSettings: new Dictionary<string, string?>
            {
                ["RateLimits:RecipeCreatesPerAccountPerMinute"] = "2",
                ["RateLimits:RecipeSavesPerAccountPerMinute"] = "1",
                ["RateLimits:RecipeReadsPerAccountPerMinute"] = "2",
            });
        var world = await harness.SeedAsync("rca-limits");
        var missing = $"none-{Guid.NewGuid():N}"[..30];
        using var shared = harness.ClientFor(world.Shared);
        using var other = harness.ClientFor(world.A.Owner);

        using var refused = await shared.PostAsJsonAsync(Recipes(missing), RecipeEditorTests.Content(), Web);
        using var onA = await shared.PostAsJsonAsync(Recipes(world.A.Slug), RecipeEditorTests.Content(), Web);
        using var onB = await shared.PostAsJsonAsync(Recipes(world.B.Slug), RecipeEditorTests.Content(), Web);
        using var third = await shared.PostAsJsonAsync(Recipes(world.A.Slug), RecipeEditorTests.Content(), Web);
        var made = (await onB.Content.ReadFromJsonAsync<RecipeDetail>(Web))!;
        using var archive = await shared.PostAsync($"{Recipes(world.B.Slug)}/{made.Id}/archive", null);
        using var restore = await shared.PostAsync($"{Recipes(world.B.Slug)}/{made.Id}/restore", null);
        using var read = await shared.GetAsync($"{Recipes(world.B.Slug)}/{made.Id}");
        using var otherAccount = await other.PostAsJsonAsync(Recipes(world.A.Slug), RecipeEditorTests.Content(), Web);

        Assert.Equal(
            [HttpStatusCode.NotFound, HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.TooManyRequests, HttpStatusCode.NoContent, HttpStatusCode.TooManyRequests, HttpStatusCode.OK, HttpStatusCode.Created],
            new[] { refused.StatusCode, onA.StatusCode, onB.StatusCode, third.StatusCode, archive.StatusCode, restore.StatusCode, read.StatusCode, otherAccount.StatusCode });
        Assert.NotNull(third.Headers.RetryAfter);
        Assert.Equal("too-many-requests", await restore.CodeAsync());
    }

    // From the contract check: a save that added a part, retried with its Idempotency-Key after a lost answer, gets the first
    // answer back; retried without one, its new part is a different part, and it is told the recipe changed.
    [Fact]
    public async Task A_save_that_added_a_part_is_retried_safely_with_its_key_only()
    {
        var world = await _harness.SeedAsync("rca-retry");
        var onA = await CreateAsync(world.A, world.A.Owner);
        using var owner = _harness.ClientFor(world.A.Owner);
        var group = RecipesFacadeTests.Echo(onA).IngredientGroups![0]!;
        var body = new UpdateRecipeRequest(onA.Version, RecipesFacadeTests.Echo(onA) with
        {
            IngredientGroups = [group with { Lines = [.. group.Lines!, new RecipeIngredientLineInput(null, "1 egg")] }],
        });
        var key = Guid.NewGuid().ToString("N");

        using var first = await SendAsync(owner, HttpMethod.Put, $"{Recipes(world.A.Slug)}/{onA.Id}", body, key);
        using var keyed = await SendAsync(owner, HttpMethod.Put, $"{Recipes(world.A.Slug)}/{onA.Id}", body, key);
        using var bare = await SendAsync(owner, HttpMethod.Put, $"{Recipes(world.A.Slug)}/{onA.Id}", body);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await keyed.Content.ReadAsStringAsync());
        Assert.Equal("true", keyed.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(HttpStatusCode.Conflict, bare.StatusCode);
        Assert.Equal(RecipeErrors.RecipeChanged.Code, await bare.CodeAsync());
    }

    private async Task<RecipeDetail> CreateAsync(SeededSite site, Domain.Modules.Accounts.Contracts.AccountId account)
    {
        using var client = _harness.ClientFor(account);
        using var response = await client.PostAsJsonAsync(Recipes(site.Slug), RecipeEditorTests.Content(), Web);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RecipeDetail>(Web))!;
    }

    private static async Task<List<Guid>> ListIdsAsync(HttpClient client, string slug, bool archived)
    {
        var page = await client.GetFromJsonAsync<RecipeSummaryPage>($"{Recipes(slug)}?archived={archived.ToString().ToLowerInvariant()}", Web);
        return [.. page!.Items.Select(i => i.Id)];
    }

    // Every route that names a recipe, with a body it would accept.
    private static IEnumerable<(HttpMethod Method, string Suffix, object? Body)> Routes(RecipeDetail recipe) =>
    [
        (HttpMethod.Get, string.Empty, null),
        (HttpMethod.Put, string.Empty, new UpdateRecipeRequest(recipe.Version, RecipesFacadeTests.Echo(recipe) with { Title = "Taken" })),
        (HttpMethod.Post, "/archive", null),
        (HttpMethod.Post, "/restore", null),
        (HttpMethod.Post, "/duplicate", null),
    ];

    // Every recipe route, under /recipes, with a good body - or, when broken, one each route would refuse.
    private static IEnumerable<(HttpMethod Method, string Path, object? Body)> AllRoutes(RecipeDetail recipe, bool broken = false) =>
    [
        (HttpMethod.Post, string.Empty, broken ? new { title = (string?)null } : RecipeEditorTests.Content()),
        (HttpMethod.Get, broken ? "?limit=0" : string.Empty, null),
        .. Routes(recipe).Select(r => (r.Method, "/" + recipe.Id + r.Suffix, broken && r.Body is not null ? new { version = "x" } : r.Body)),
    ];

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body, string? key = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Web);
        }

        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation(ApiIdempotency.KeyHeader, key);
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostRawAsync(HttpClient client, string path, string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return await client.SendAsync(request);
    }

    private static string Recipes(string slug) => $"/api/v1/sites/{slug}/recipes";

    private static string WithoutId(JsonElement line) =>
        JsonSerializer.Serialize(line.EnumerateObject().Where(p => p.Name != "id").ToDictionary(p => p.Name, p => p.Value));

    // Both sites' recipe rows, versions and every part included, as text, so any write to either shows.
    private async Task<string> StateAsync(TwoSiteWorld world) =>
        await StateOfAsync(world.A.Id) + "\n" + await StateOfAsync(world.B.Id);

    private async Task<string> StateOfAsync(SiteId site)
    {
        await using var scope = _harness.In(site);
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        var recipes = await db.Set<Recipe>().AsNoTracking().OrderBy(r => r.Id).Select(r => new { r.Id, r.Title, r.Version, r.ArchivedAt, r.UpdatedAt }).ToListAsync();
        var lines = await db.Set<RecipeIngredient>().AsNoTracking().OrderBy(l => l.Id).Select(l => new { l.Id, l.GroupId, l.Position, l.OriginalText }).ToListAsync();
        var steps = await db.Set<RecipeStep>().AsNoTracking().OrderBy(s => s.Id).Select(s => new { s.Id, s.GroupId, s.Position, s.Text }).ToListAsync();
        var groups = await db.Set<RecipeIngredientGroup>().AsNoTracking().CountAsync() + await db.Set<RecipeStepGroup>().AsNoTracking().CountAsync();
        var lists = await db.Set<RecipeKeyword>().AsNoTracking().CountAsync() + await db.Set<RecipeAllergen>().AsNoTracking().CountAsync();
        return JsonSerializer.Serialize(new { recipes, lines, steps, groups, lists });
    }
}
