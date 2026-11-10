using System.Globalization;
using System.Text.Json;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Api;

// PP4.6: the paste routes' contract (api-contract.md), with no database behind them: what the admin document declares for
// each, and the wire names of the reasons a line is flagged. Those names became a contract when this prompt first returned
// them (PP4.5's hand-off), so renaming one is a breaking change; this pins them.
public sealed class RecipePasteApiContractTests
{
    private static readonly string[] Routes =
    [
        "/api/v1/sites/{siteSlug}/recipes/paste/ingredients",
        "/api/v1/sites/{siteSlug}/recipes/paste/steps",
    ];

    [Fact]
    public async Task The_admin_document_declares_every_answer_of_the_paste_routes()
    {
        using var document = await AdminDocumentAsync();
        var paths = document.RootElement.GetProperty("paths");

        foreach (var route in Routes)
        {
            var post = Assert.Single(paths.GetProperty(route).EnumerateObject());
            Assert.Equal("post", post.Name);
            var operation = post.Value;

            // A write that is not [Idempotent]: the gateway's antiforgery and same-origin answers, and the Author policy's 404
            // with no 403, since every member is at least an Author. No key, because nothing is saved.
            Assert.Equal(["200", "400", "401", "403", "404", "413", "415", "429", "500", "502", "504"], Statuses(operation));
            Assert.Contains("site-not-found", Description(operation, "404"), StringComparison.Ordinal);
            Assert.Contains("validation-failed", Description(operation, "400"), StringComparison.Ordinal);
            // The description's limits are written out by hand, since an attribute cannot build them; this keeps them true.
            Assert.Contains($"{RecipeLimits.MaxPastedLines} lines", Description(operation, "400"), StringComparison.Ordinal);
            Assert.Contains(
                RecipeLimits.MaxPastedTextLength.ToString("N0", CultureInfo.InvariantCulture) + " characters",
                Description(operation, "400"),
                StringComparison.Ordinal);
            Assert.Contains("broken character", Description(operation, "400"), StringComparison.Ordinal);
            Assert.Contains("ignored", operation.GetProperty("description").GetString(), StringComparison.Ordinal);
            Assert.DoesNotContain("site-role-required", Description(operation, "403"), StringComparison.Ordinal);
            Assert.Contains("Saves nothing", operation.GetProperty("description").GetString(), StringComparison.Ordinal);
            Assert.DoesNotContain(
                operation.GetProperty("parameters").EnumerateArray(),
                p => p.GetProperty("name").GetString() == "Idempotency-Key");
        }
    }

    [Fact]
    public async Task The_reasons_a_line_is_flagged_have_stable_wire_names()
    {
        using var document = await AdminDocumentAsync();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        Assert.Equal(
            [
                "empty", "too-long", "ill-formed-text", "unbalanced-parentheses", "unreadable-quantity", "reversed-range",
                "quantity-out-of-range", "ambiguous-quantity", "no-item", "item-too-long", "note-too-long",
            ],
            EnumValues(schemas, "IngredientParseProblem"));
        Assert.Equal(["no-text", "too-long", "ill-formed-text"], EnumValues(schemas, "StepPasteProblem"));
        Assert.Equal(["parsed", "unparsed"], EnumValues(schemas, "IngredientParseStatus"));
        Assert.Equal(["amount", "range", "to-taste"], EnumValues(schemas, "QuantityKind"));
        Assert.Equal(["fraction", "decimal"], EnumValues(schemas, "QuantityStyle"));
    }

    [Fact]
    public async Task A_reading_names_every_part_a_line_and_a_step_have()
    {
        using var document = await AdminDocumentAsync();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        Assert.Equal(
            ["originalText", "status", "problem", "quantity", "quantitySpan", "style", "unitCode", "unitSpan", "item", "note"],
            Properties(schemas, "PastedIngredientLine"));
        Assert.Equal(["originalText", "text", "textSpan", "problem"], Properties(schemas, "PastedStep"));
        Assert.Equal(["kind", "low", "high"], Properties(schemas, "PastedQuantity"));
        Assert.Equal(["numerator", "denominator"], Properties(schemas, "PastedFraction"));
        Assert.Equal(["start", "length"], Properties(schemas, "PastedSpan"));
        Assert.Equal(["heading", "headingOriginalText", "headingSpan", "lines"], Properties(schemas, "PastedIngredientGroup"));
        Assert.Equal(["heading", "headingOriginalText", "headingSpan", "steps"], Properties(schemas, "PastedStepGroup"));
        Assert.Equal(["text"], Properties(schemas, "PasteListRequest"));
    }

    private static async Task<JsonDocument> AdminDocumentAsync()
    {
        await using var api = ApiServiceFactory.WithoutDatabase(
            "Development",
            new Dictionary<string, string?> { ["DevelopmentMail:Host"] = "localhost", ["DevelopmentMail:Port"] = "1025" });
        using var response = await api.CreateClient().GetAsync("/openapi/v1.json");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static List<string?> EnumValues(JsonElement schemas, string name) =>
        [.. schemas.GetProperty(name).GetProperty("enum").EnumerateArray()
            .Where(v => v.ValueKind != JsonValueKind.Null)
            .Select(v => v.GetString())];

    private static List<string> Properties(JsonElement schemas, string name) =>
        [.. schemas.GetProperty(name).GetProperty("properties").EnumerateObject().Select(p => p.Name)];

    private static string Description(JsonElement operation, string status) =>
        operation.GetProperty("responses").GetProperty(status).GetProperty("description").GetString() ?? string.Empty;

    private static List<string> Statuses(JsonElement operation) =>
        [.. operation.GetProperty("responses").EnumerateObject().Select(r => r.Name).Order(StringComparer.Ordinal)];
}
