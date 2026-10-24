using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using PantryPress.ApiService.Http;
using PantryPress.Domain.Managers.Idempotency;
using PantryPress.ServiceDefaults.EdgeHardening;
using Scalar.AspNetCore;

namespace PantryPress.ApiService.Hosting;

internal static class ApiOpenApi
{
    /// <summary>
    /// What <see cref="ApiIdempotency.TryParseKey"/> accepts: visible ASCII but a quote (<c>\x22</c>), a comma
    /// (<c>\x2C</c>), or a backslash (<c>\x5C</c>), bare or in quotes.
    /// </summary>
    internal const string KeyPattern = """^(?:[!#-+\x2D-\x5B\x5D-~]{1,128}|"[!#-+\x2D-\x5B\x5D-~]{1,128}")$""";

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

                // The key a write accepts (API-005; PP1.9). Its 409 and 422 answers come from IdempotencyConvention.
                options.AddOperationTransformer((operation, context, _) =>
                {
                    if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IdempotentAttribute>().FirstOrDefault()
                        is not { } idempotent)
                    {
                        return Task.CompletedTask;
                    }

                    var retention = context.ApplicationServices.GetRequiredService<IOptions<IdempotencyOptions>>().Value.Retention;
                    operation.Parameters ??= [];
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = ApiIdempotency.KeyHeader,
                        In = ParameterLocation.Header,
                        Required = idempotent.Required,
                        Description = "A key of your choosing, unique to this request: 1 to "
                            + $"{IdempotencyRequest.MaxKeyLength} visible ASCII characters other than a quote, a "
                            + "backslash, or a comma, optionally in quotes. For "
                            + $"{retention.TotalHours.ToString(CultureInfo.InvariantCulture)} hours, a retry with the same "
                            + "key and the same request gets the first answer again, marked Idempotent-Replayed: true. "
                            + "Refusals are answers too, so fix the cause and send a new key. Not kept: a server error, "
                            + "a rate limit, a timeout, a 401, or an answer over 1 MiB - a retry of those runs again.",
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.String,
                            MinLength = 1,
                            MaxLength = IdempotencyRequest.MaxKeyLength + 2,
                            Pattern = KeyPattern,
                        },
                    });

                    foreach (var (status, response) in operation.Responses ?? [])
                    {
                        if (response is not OpenApiResponse declared)
                        {
                            continue;
                        }

                        if (status.StartsWith('2'))
                        {
                            declared.Headers ??= new Dictionary<string, IOpenApiHeader>();
                            declared.Headers[ApiIdempotency.ReplayedHeader] = new OpenApiHeader
                            {
                                Description = "true when this is the stored answer to an earlier request with the same key.",
                                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Enum = [JsonValue.Create("true")] },
                            };
                        }
                        else if (status == "409")
                        {
                            declared.Headers ??= new Dictionary<string, IOpenApiHeader>();
                            declared.Headers["Retry-After"] = new OpenApiHeader
                            {
                                Description = "Seconds to wait before retrying while the first request is still running.",
                                Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
                            };
                        }
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
    /// <remarks>
    /// The page starts itself with an inline script, which the document policy refuses, so its answers carry their own
    /// policy. Set before the response starts, it is the one UseSecurityHeaders leaves alone.
    /// </remarks>
    public static WebApplication MapApiReference(this WebApplication app)
    {
        app.MapOpenApi();
        app.MapScalarApiReference(options => options
                .WithTitle("PantryPress API reference")
                .AddDocuments(ApiSurfaces.All.Select(surface => surface.Document))
                .DisableDefaultFonts())
            .AddEndpointFilter((context, next) =>
            {
                context.HttpContext.Response.Headers[SecurityHeaders.ContentSecurityPolicy] =
                    ContentSecurityPolicies.DevelopmentApiReference;
                return next(context);
            });
        return app;
    }
}
