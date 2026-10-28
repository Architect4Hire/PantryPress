using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

namespace PantryPress.Site.Hosting;

/// <summary>
/// The public surface's OpenAPI document (api-contract.md), served in Development only at
/// <c>/openapi/public-v1.json</c>. Like every request here it is answered only on a host that names a site, since host
/// resolution runs first; the SSR server's client is written from it (PP7.13a).
/// </summary>
internal static class SiteOpenApi
{
    public static IServiceCollection AddSiteOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(PublicSurface.V1Document, options =>
        {
            // The convention files every controller under the surface's document (PublicSurfaceConvention).
            options.ShouldInclude = description => description.GroupName == PublicSurface.V1Document;
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = PublicSurface.V1Title;
                document.Info.Version = "v1";
                return Task.CompletedTask;
            });

            // Every refusal carries a stable code, which the framework's ProblemDetails type does not know about; a 503
            // says how long to wait (SEO-007).
            options.AddSchemaTransformer((schema, context, _) =>
            {
                if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(ProblemDetails)))
                {
                    schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
                    schema.Properties[SiteProblems.CodeKey] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Description = "Stable, machine-readable failure code, such as site-unavailable.",
                    };
                    schema.Required ??= new HashSet<string>();
                    schema.Required.Add(SiteProblems.CodeKey);
                }

                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, _, _) =>
            {
                if (operation.Responses?.TryGetValue("503", out var unavailable) is true && unavailable is OpenApiResponse declared)
                {
                    declared.Headers ??= new Dictionary<string, IOpenApiHeader>();
                    declared.Headers["Retry-After"] = new OpenApiHeader
                    {
                        Description = "Seconds to wait before asking again.",
                        Required = true,
                        Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
                    };
                }

                return Task.CompletedTask;
            });
        });
}
