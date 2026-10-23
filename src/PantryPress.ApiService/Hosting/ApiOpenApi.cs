using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using PantryPress.ApiService.Http;
using Scalar.AspNetCore;

namespace PantryPress.ApiService.Hosting;

internal static class ApiOpenApi
{
    /// <summary>
    /// One OpenAPI document per API surface (api-contract.md), each describing only its own routes, so the admin
    /// document never leaks the publishing API's routes once PP18 adds them. Problem schemas declare the stable
    /// <c>code</c> every failure carries, which the framework's <see cref="ProblemDetails"/> type does not know about.
    /// </summary>
    public static IServiceCollection AddApiOpenApi(this IServiceCollection services)
    {
        foreach (var surface in ApiSurfaces.All)
        {
            services.AddOpenApi(surface.Document, options =>
            {
                // The convention stamps every controller with its surface's document name (ApiSurfaceConvention), so
                // a route with no group - which would otherwise land in every document - cannot exist.
                options.ShouldInclude = description => description.GroupName == surface.Document;
                options.AddDocumentTransformer((document, _, _) =>
                {
                    document.Info.Title = surface.Title;
                    document.Info.Version = surface.Version;
                    return Task.CompletedTask;
                });
                options.AddSchemaTransformer((schema, context, _) =>
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
                });
            });
        }

        return services;
    }

    /// <summary>
    /// The documents and the Scalar reference UI, in Development only. The UI's script comes from this app (the
    /// package embeds it) and the CDN fonts are off, so reading the API's documentation fetches nothing from the
    /// internet. The published publishing-API document (API-009) arrives with PP18.
    /// </summary>
    public static WebApplication MapApiReference(this WebApplication app)
    {
        app.MapOpenApi();
        app.MapScalarApiReference(options => options
            .WithTitle("PantryPress API reference")
            .AddDocuments(ApiSurfaces.All.Select(surface => surface.Document))
            .DisableDefaultFonts());
        return app;
    }
}
