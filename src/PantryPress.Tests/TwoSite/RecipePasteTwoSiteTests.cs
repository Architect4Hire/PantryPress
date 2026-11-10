using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Outbox;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Recipes;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Tests.Persistence;
using PantryPress.Tests.Recipes;

namespace PantryPress.Tests.TwoSite;

// PP4.6: paste-to-parse over ApiService in the two-site world (tenancy.md, "Two-site test"). The routes read and write no
// site data, so what they owe is who may ask and that nothing of a site shows: every active member of a site is answered,
// alike on both sites for the account that belongs to both; anyone else is answered exactly as for a site that does not
// exist; and neither site's recipe tables change. The reading itself is pinned here on the wire - names, kebab-case values,
// exact fractions, explicit nulls - since the admin client is written from it.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class RecipePasteTwoSiteTests(MigratedDatabase database) : IAsyncLifetime
{
    private const string IngredientPaste = "For the crust:\n1 ½ cups flour, sifted\n\n2 3 cups\nsalt to taste";

    private const string StepPaste = "Step 1: Preheat the oven.\n## Bake\n2.\n3) Bake until golden.";

    private TwoSiteHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await TwoSiteHarness.CreateAsync(database, withApi: true);

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task Every_active_member_gets_the_same_reading_on_either_site()
    {
        var world = await _harness.SeedAsync("paste-members");
        await _harness.ChangeAsync(world.A.Id, db => RecipeRows.Add(db));
        var answers = new List<string>();
        var accounts = new List<string>();

        foreach (var (site, account) in new[]
        {
            (world.A, world.A.Owner), (world.A, world.A.Editor), (world.A, world.A.Author), (world.A, world.Shared),
            (world.B, world.B.Author), (world.B, world.Shared), (world.B, world.SuspendedOnA),
        })
        {
            using var client = _harness.ClientFor(account);
            accounts.Add(account.Value.ToString());
            foreach (var (list, paste) in new[] { ("ingredients", IngredientPaste), ("steps", StepPaste) })
            {
                using var response = await PasteAsync(client, site.Slug, list, paste);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
                answers.Add(list + " " + await response.Content.ReadAsStringAsync());
            }
        }

        // Nothing of either site, or of the caller, is in a reading: every member on both sites gets the very same bytes. Both
        // sites share a name and tagline, so those - and A's recipe - are looked for by value too.
        Assert.Equal(2, answers.Distinct(StringComparer.Ordinal).Count());
        string[] secrets =
        [
            world.A.Slug, world.B.Slug, world.A.Id.Value.ToString(), world.B.Id.Value.ToString(), world.A.Id.Value.ToString("N"),
            world.B.Id.Value.ToString("N"), "Sunday Supper", "Weeknight dinners", "Apple pie", "Whisk the flour", "all-purpose",
            .. accounts,
        ];
        Assert.All(answers, answer => Assert.All(secrets, secret => Assert.DoesNotContain(secret, answer, StringComparison.OrdinalIgnoreCase)));
    }

    // The paste allowance is the account's own, across every site it belongs to; a request the site policy refuses is not
    // counted, since authorization comes first.
    [Fact]
    public async Task Reading_pastes_has_its_own_allowance_across_the_accounts_sites()
    {
        await using var harness = await TwoSiteHarness.CreateAsync(
            database,
            withApi: true,
            apiSettings: new Dictionary<string, string?> { ["RateLimits:RecipePastesPerAccountPerMinute"] = "2" });
        var world = await harness.SeedAsync("paste-limit");
        var missing = $"none-{Guid.NewGuid():N}"[..30];
        using var shared = harness.ClientFor(world.Shared);

        using var refused = await PasteAsync(shared, missing, "steps", StepPaste);
        using var onA = await PasteAsync(shared, world.A.Slug, "ingredients", IngredientPaste);
        using var onB = await PasteAsync(shared, world.B.Slug, "steps", StepPaste);
        using var third = await PasteAsync(shared, world.A.Slug, "steps", StepPaste);
        using var site = await shared.GetAsync($"/api/v1/sites/{world.B.Slug}");
        using var other = harness.ClientFor(world.A.Owner);
        using var otherAccount = await PasteAsync(other, world.A.Slug, "steps", StepPaste);

        Assert.Equal(
            [HttpStatusCode.NotFound, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests, HttpStatusCode.OK, HttpStatusCode.OK],
            new[] { refused.StatusCode, onA.StatusCode, onB.StatusCode, third.StatusCode, site.StatusCode, otherAccount.StatusCode });
        Assert.True(third.Headers.RetryAfter is not null);
        Assert.Equal("too-many-requests", await third.CodeAsync());
    }

    [Fact]
    public async Task Anyone_else_is_answered_as_for_no_site_and_nothing_is_saved_on_either()
    {
        var world = await _harness.SeedAsync("paste-refused");
        await _harness.ChangeAsync(world.A.Id, db => RecipeRows.Add(db));
        await _harness.ChangeAsync(world.B.Id, db => RecipeRows.Add(db));
        var missing = $"none-{Guid.NewGuid():N}"[..30];
        var before = await RecipeRowsAsync(world);

        // Every body - a good one, and each the members would be refused for - is the site policy's 404 first, so neither a
        // validation answer nor JSON reading tells a real site from none.
        var escape = "\\" + "u";
        string[] bodies =
        [
            JsonSerializer.Serialize(new { text = IngredientPaste }),
            "{}",
            JsonSerializer.Serialize(new { text = " \n\n " }),
            JsonSerializer.Serialize(new { text = string.Join('\n', Enumerable.Repeat("1 egg", 201)) }),
            JsonSerializer.Serialize(new { text = new string('a', 30_001) }),
            "{\"text\":\"1 cup fl" + escape + "d83cour\"}",
            "{\"text\":",
        ];

        foreach (var account in new[] { world.A.Suspended, world.SuspendedOnA, world.Stranger, world.B.Owner, world.B.Author })
        {
            using var client = _harness.ClientFor(account);
            await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, async () =>
            {
                foreach (var list in new[] { "ingredients", "steps" })
                {
                    foreach (var body in bodies)
                    {
                        using var onReal = await PostRawAsync(client, Paste(world.A.Slug, list), body);
                        using var onNone = await PostRawAsync(client, Paste(missing, list), body);
                        await TwoSiteChecks.AssertIndistinguishableAsync(onReal, onNone, HttpStatusCode.NotFound, SiteErrors.SiteNotFound.Code);
                    }
                }
            });
        }

        // Members reading pastes on both sites save nothing either.
        foreach (var (site, account) in new[] { (world.A, world.A.Owner), (world.B, world.B.Owner), (world.B, world.Shared) })
        {
            using var client = _harness.ClientFor(account);
            using var ingredients = await PasteAsync(client, site.Slug, "ingredients", IngredientPaste);
            using var steps = await PasteAsync(client, site.Slug, "steps", StepPaste);
            Assert.Equal(HttpStatusCode.OK, ingredients.StatusCode);
            Assert.Equal(HttpStatusCode.OK, steps.StatusCode);
        }

        Assert.Equal(before, await RecipeRowsAsync(world));
    }

    [Fact]
    public async Task An_ingredient_reading_is_written_on_the_wire_as_the_contract_says()
    {
        var world = await _harness.SeedAsync("paste-wire-i");
        using var client = _harness.ClientFor(world.A.Author);
        using var response = await PasteAsync(client, world.A.Slug, "ingredients", IngredientPaste);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            Json("""
            {"groups":[{"heading":"For the crust","headingOriginalText":"For the crust:","headingSpan":{"start":0,"length":13},"lines":[
              {"originalText":"1 ½ cups flour, sifted","status":"parsed","problem":null,
               "quantity":{"kind":"amount","low":{"numerator":3,"denominator":2},"high":null},
               "quantitySpan":{"start":0,"length":3},"style":"fraction","unitCode":"cup","unitSpan":{"start":4,"length":4},
               "item":"flour","note":"sifted"},
              {"originalText":"2 3 cups","status":"unparsed","problem":"ambiguous-quantity","quantity":null,"quantitySpan":null,
               "style":null,"unitCode":null,"unitSpan":null,"item":null,"note":null},
              {"originalText":"salt to taste","status":"parsed","problem":null,"quantity":{"kind":"to-taste","low":null,"high":null},
               "quantitySpan":{"start":5,"length":8},"style":null,"unitCode":null,"unitSpan":null,"item":"salt","note":null}]}]}
            """),
            Json(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task A_step_reading_is_written_on_the_wire_as_the_contract_says()
    {
        var world = await _harness.SeedAsync("paste-wire-s");
        using var client = _harness.ClientFor(world.A.Author);
        using var response = await PasteAsync(client, world.A.Slug, "steps", StepPaste);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            Json("""
            {"groups":[
              {"heading":null,"headingOriginalText":null,"headingSpan":null,"steps":[
                {"originalText":"Step 1: Preheat the oven.","text":"Preheat the oven.","textSpan":{"start":8,"length":17},"problem":null}]},
              {"heading":"Bake","headingOriginalText":"## Bake","headingSpan":{"start":3,"length":4},"steps":[
                {"originalText":"2.","text":"","textSpan":{"start":2,"length":0},"problem":"no-text"},
                {"originalText":"3) Bake until golden.","text":"Bake until golden.","textSpan":{"start":3,"length":18},"problem":null}]}]}
            """),
            Json(await response.Content.ReadAsStringAsync()));
    }

    // Half a surrogate pair, sent as a JSON escape, never reaches the reader: the API's JSON reading refuses it as text it
    // cannot read, so ill-formed text is refused at the edge (recipes.md). A control character is well-formed JSON, so it
    // reaches the reader and is flagged on its own line - never a reason to refuse the paste - and the line comes back as
    // it was sent.
    [Fact]
    public async Task Broken_text_is_refused_at_the_edge_and_a_control_character_is_flagged_on_its_line()
    {
        var world = await _harness.SeedAsync("paste-broken");
        using var client = _harness.ClientFor(world.A.Author);
        var escape = "\\" + "u";

        foreach (var list in new[] { "ingredients", "steps" })
        {
            using (var lone = await PostRawAsync(client, Paste(world.A.Slug, list), "{\"text\":\"1 cup fl" + escape + "d83cour\\nStir.\"}"))
            {
                Assert.Equal(HttpStatusCode.BadRequest, lone.StatusCode);
                Assert.Equal("validation-failed", await lone.CodeAsync());
                using var problem = JsonDocument.Parse(await lone.Content.ReadAsStringAsync());
                Assert.Equal(["text"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(e => e.Name));
            }

            using var control = await PostRawAsync(client, Paste(world.A.Slug, list), "{\"text\":\"1 cup flour" + escape + "0007\\nStir.\"}");
            var body = await control.Content.ReadAsStringAsync();
            Assert.True(control.StatusCode == HttpStatusCode.OK, body);
            using var reading = JsonDocument.Parse(body);
            var lines = reading.RootElement.GetProperty("groups")[0].GetProperty(list == "steps" ? "steps" : "lines");
            Assert.Equal("1 cup flour" + (char)0x07, lines[0].GetProperty("originalText").GetString());
            Assert.Equal("ill-formed-text", lines[0].GetProperty("problem").GetString());
            Assert.Equal("Stir.", lines[1].GetProperty("originalText").GetString());
        }
    }

    private static async Task<HttpResponseMessage> PostRawAsync(HttpClient client, string path, string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task A_paste_with_nothing_to_read_or_too_much_is_a_validation_problem()
    {
        var world = await _harness.SeedAsync("paste-invalid");
        using var client = _harness.ClientFor(world.A.Author);

        foreach (var body in new object[]
        {
            new { },
            new { text = " \n\n " },
            new { text = string.Join('\n', Enumerable.Repeat("1 egg", 201)) },
            new { text = new string('a', 30_001) },
        })
        {
            foreach (var list in new[] { "ingredients", "steps" })
            {
                using var response = await client.PostAsync(Paste(world.A.Slug, list), JsonContent.Create(body));
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal("validation-failed", await response.CodeAsync());
                using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(["text"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(e => e.Name));
            }
        }
    }

    private static string Paste(string slug, string list) => $"/api/v1/sites/{slug}/recipes/paste/{list}";

    private static Task<HttpResponseMessage> PasteAsync(HttpClient client, string slug, string list, string text) =>
        client.PostAsJsonAsync(Paste(slug, list), new { text });

    // JSON written one way for comparison: no spaces, properties as they came.
    private static string Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    // Every recipe row on both sites, read on each site's own scope, and every audit entry and outbox message filed under
    // either, so any write by these routes would show.
    private async Task<string> RecipeRowsAsync(TwoSiteWorld world)
    {
        var counts = new List<string>();
        foreach (var site in new[] { world.A.Id, world.B.Id })
        {
            await using var scope = _harness.In(site);
            var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
            counts.Add(string.Join(
                ' ',
                await db.Set<Recipe>().CountAsync(),
                await db.Set<RecipeIngredientGroup>().CountAsync(),
                await db.Set<RecipeIngredient>().CountAsync(),
                await db.Set<RecipeStepGroup>().CountAsync(),
                await db.Set<RecipeStep>().CountAsync(),
                await db.Set<RecipeRevision>().CountAsync()));
        }

        // Nothing is audited or queued under either site: the operator tables hold no row for A or B.
        await using (var platform = _harness.Scope())
        {
            var db = platform.ServiceProvider.GetRequiredService<PantryPressDbContext>();
            Guid?[] sites = [world.A.Id.Value, world.B.Id.Value];
            counts.Add(string.Join(
                ' ',
                await db.Set<AuditEntry>().CountAsync(e => sites.Contains(e.SiteId)),
                await db.Set<OutboxMessage>().CountAsync(m => sites.Contains(m.SiteId))));
        }

        return string.Join(" | ", counts);
    }
}
