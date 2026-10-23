using System.Text.Json;
using PantryPress.ApiService.Http;

namespace PantryPress.Tests;

/// <summary>Reading the one error shape every PantryPress surface answers with (api-contract.md).</summary>
internal static class ProblemAssertions
{
    /// <summary>The <c>code</c> from a problem document, with its content type checked on the way past.</summary>
    public static async Task<string?> CodeAsync(this HttpResponseMessage response)
    {
        Assert.Equal(ApiProblems.ContentType, response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty(ApiProblems.CodeKey).GetString();
    }
}
