using System.Globalization;
using System.Text.Json;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Api;

// PP4.11: the recipe routes' contract (api-contract.md), with no database behind them: what the admin document declares
// for each route, the names of every field the admin client is written from, and the limits the descriptions spell out.
public sealed class RecipesApiContractTests
{
    private const string List = "/api/v1/sites/{siteSlug}/recipes";
    private const string One = List + "/{recipeId}";

    public static TheoryData<string, string, string[]> Answers => new()
    {
        { List, "post", ["201", "400", "401", "403", "404", "409", "413", "415", "422", "429", "500", "502", "504"] },
        { List, "get", ["200", "400", "401", "404", "413", "429", "500", "502", "504"] },
        { One, "get", ["200", "401", "404", "413", "429", "500", "502", "504"] },
        { One, "put", ["200", "400", "401", "403", "404", "409", "413", "415", "422", "429", "500", "502", "504"] },
        { One + "/archive", "post", ["204", "400", "401", "403", "404", "409", "413", "422", "429", "500", "502", "504"] },
        { One + "/restore", "post", ["204", "400", "401", "403", "404", "409", "413", "422", "429", "500", "502", "504"] },
        { One + "/duplicate", "post", ["201", "400", "401", "403", "404", "409", "413", "422", "429", "500", "502", "504"] },
        { List + "/vocabulary", "get", ["200", "401", "404", "413", "429", "500", "502", "504"] },
    };

    // PP4.12a: the list's search and filters are query parameters the admin client is written from, and its 400 says what
    // each refusal means, the cursor's binding to them included.
    [Fact]
    public async Task The_list_declares_its_search_and_filters()
    {
        using var document = await AdminDocumentAsync();
        var get = document.RootElement.GetProperty("paths").GetProperty(List).GetProperty("get");

        Assert.Equal(
            ["siteSlug", "limit", "cursor", "archived", "search", "course", "cuisine", "diet"],
            get.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()));
        var invalid = Description(get, "400");
        foreach (var said in new[] { "same search, filters, and archived", "longer than 100 characters", "code in the vocabulary" })
        {
            Assert.Contains(said, invalid, StringComparison.Ordinal);
        }

        Assert.Contains("same search, filters, and archived", get.GetProperty("description").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Answers))]
    public async Task The_admin_document_declares_every_answer_of_each_recipe_route(string path, string method, string[] statuses)
    {
        using var document = await AdminDocumentAsync();
        var operation = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method);

        Assert.Equal(statuses, Statuses(operation));
        Assert.Contains("site-not-found", Description(operation, "404"), StringComparison.Ordinal);
        if (path.StartsWith(One, StringComparison.Ordinal))
        {
            Assert.Contains("recipe-not-found", Description(operation, "404"), StringComparison.Ordinal);
            Assert.Contains("format", operation.GetProperty("parameters").EnumerateArray()
                .Single(p => p.GetProperty("name").GetString() == "recipeId").GetProperty("schema").GetRawText(), StringComparison.Ordinal);
        }

        if (method != "get")
        {
            Assert.Contains(operation.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "Idempotency-Key");
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("description").GetString()));
        }
    }

    [Fact]
    public async Task A_save_says_what_its_conflicts_and_refusals_mean()
    {
        using var document = await AdminDocumentAsync();
        var put = document.RootElement.GetProperty("paths").GetProperty(One).GetProperty("put");

        Assert.Contains("recipe-changed", Description(put, "409"), StringComparison.Ordinal);
        Assert.Contains("recipe-archived", Description(put, "409"), StringComparison.Ordinal);
        Assert.Contains("recipe-not-yours", Description(put, "403"), StringComparison.Ordinal);
        Assert.Contains("saves nothing", put.GetProperty("description").GetString(), StringComparison.Ordinal);

        // The limits are written out by hand, since an attribute cannot build them; this keeps them true.
        var invalid = Description(put, "400");
        foreach (var limit in new[]
        {
            $"{RecipeLimits.MaxIngredientGroups} groups",
            $"{RecipeLimits.MaxIngredientLines} ingredient lines",
            $"{RecipeLimits.MaxSteps} steps",
            $"{RecipeLimits.MaxEquipment} pieces of equipment",
            $"{RecipeLimits.MaxNotes} notes",
            $"{RecipeLimits.MaxKeywords} keywords",
            $"{RecipeLimits.MaxCourses} courses or cuisines",
            $"0 to {RecipeLimits.MaxMinutes.ToString("N0", CultureInfo.InvariantCulture)}",
            $"1 to {RecipeLimits.MaxQuantityTerm.ToString("N0", CultureInfo.InvariantCulture)}",
            "9,999,999.99",
        })
        {
            Assert.Contains(limit, invalid, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_recipe_names_every_field_the_editor_reads_and_sends()
    {
        using var document = await AdminDocumentAsync();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        string[] content =
        [
            "title", "summary", "yield", "yieldUnit", "prepMinutes", "cookMinutes", "additionalMinutes", "additionalTimeLabel",
            "videoUrl", "nutrition", "ingredientGroups", "stepGroups", "equipment", "notes", "keywords", "courses", "cuisines",
            "diets", "allergens",
        ];
        Assert.Equal(content, Properties(schemas, "RecipeContent"));
        Assert.Equal(["id", "version", .. content, "createdAt", "updatedAt", "archivedAt", "canEdit"], Properties(schemas, "RecipeDetail"));
        Assert.Equal(["version", "content"], Properties(schemas, "UpdateRecipeRequest"));
        Assert.Equal(["id", "heading", "lines"], Properties(schemas, "RecipeIngredientGroupInput"));
        Assert.Equal(["id", "originalText"], Properties(schemas, "RecipeIngredientLineInput"));
        Assert.Equal(["id", "heading", "steps"], Properties(schemas, "RecipeStepGroupInput"));
        Assert.Equal(["id", "text"], Properties(schemas, "RecipeStepInput"));
        Assert.Equal(
            ["id", "originalText", "status", "problem", "quantity", "quantitySpan", "unitCode", "unitSpan", "item", "note"],
            Properties(schemas, "RecipeIngredientLineDetail"));
        Assert.Equal(["kind", "low", "high"], Properties(schemas, "RecipeQuantity"));
        Assert.Equal(["numerator", "denominator"], Properties(schemas, "RecipeFraction"));
        Assert.Equal(["items", "nextCursor"], Properties(schemas, "RecipeSummaryPage"));
        Assert.Equal(["id", "title", "updatedAt", "archivedAt", "canEdit"], Properties(schemas, "RecipeSummary"));
        Assert.Equal(["courses", "cuisines", "diets", "allergens"], Properties(schemas, "RecipeVocabulary"));
        Assert.Equal(["code", "name"], Properties(schemas, "RecipeVocabularyWord"));
        Assert.Equal(
            [
                "servingSize", "calories", "fatGrams", "saturatedFatGrams", "transFatGrams", "unsaturatedFatGrams",
                "cholesterolMilligrams", "sodiumMilligrams", "carbohydrateGrams", "fiberGrams", "sugarGrams", "proteinGrams",
            ],
            Properties(schemas, "RecipeNutrition"));
    }

    private static async Task<JsonDocument> AdminDocumentAsync()
    {
        await using var api = ApiServiceFactory.WithoutDatabase(
            "Development",
            new Dictionary<string, string?> { ["DevelopmentMail:Host"] = "localhost", ["DevelopmentMail:Port"] = "1025" });
        using var response = await api.CreateClient().GetAsync("/openapi/v1.json");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static List<string> Properties(JsonElement schemas, string name) =>
        [.. schemas.GetProperty(name).GetProperty("properties").EnumerateObject().Select(p => p.Name)];

    private static string Description(JsonElement operation, string status) =>
        operation.GetProperty("responses").GetProperty(status).GetProperty("description").GetString() ?? string.Empty;

    private static string[] Statuses(JsonElement operation) =>
        [.. operation.GetProperty("responses").EnumerateObject().Select(r => r.Name).Order(StringComparer.Ordinal)];
}
