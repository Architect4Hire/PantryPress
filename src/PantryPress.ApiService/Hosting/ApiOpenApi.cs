using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using PantryPress.ApiService.Http;

namespace PantryPress.ApiService.Hosting;

internal static class ApiOpenApi
{
    /// <summary>
    /// The OpenAPI document for the admin API. Problem schemas declare the stable <c>code</c> every failure carries
    /// (api-contract.md), which the framework's <see cref="ProblemDetails"/> type does not know about.
    /// </summary>
    public static IServiceCollection AddApiOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options => options.AddSchemaTransformer((schema, context, _) =>
        {
            if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(ProblemDetails)))
            {
                schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
                schema.Properties[ApiProblems.CodeKey] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Description = "Stable, machine-readable failure code, such as validation-failed.",
                };
                schema.Required ??= new HashSet<string>();
                schema.Required.Add(ApiProblems.CodeKey);
            }

            return Task.CompletedTask;
        }));
}
