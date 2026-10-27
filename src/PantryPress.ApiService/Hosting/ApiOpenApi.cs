using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using PantryPress.ApiService.Http;
using PantryPress.Domain.Managers.Idempotency;
using PantryPress.Domain.Modules.Sites.Contracts;
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

                        // A route's 400 is a validation problem with field messages or a plain refusal (a bad link, a
                        // wrong code), declared as one type (PP1.12): the field messages are therefore optional, so
                        // both shapes are what the document says.
                        if (context.JsonTypeInfo.Type == typeof(ValidationProblemDetails))
                        {
                            schema.Required.Remove("errors");
                            schema.Description = "A problem with a stable code. Field messages, under errors, appear "
                                + "only when the request failed validation (code validation-failed).";
                        }
                    }

                    return Task.CompletedTask;
                });

                // The admin app reaches this surface only through the gateway, which answers some requests itself
                // (PP1.12): the document the admin client is written from says so on every route.
                if (surface == ApiSurfaces.AdminV1)
                {
                    options.AddOperationTransformer((operation, context, _) =>
                    {
                        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                        GatewayAnswers.Add(
                            operation,
                            context.Description.HttpMethod,
                            signedIn: metadata.OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Any()
                                && !metadata.OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Any());

                        // A site route's policy answers before the route does (PP2.3). Its 403 only where some member's
                        // role can be too low: every member is at least an Author.
                        var leastRoles = metadata.OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>()
                            .Where(a => a.Policy is { } policy && SitePolicies.LeastRole.ContainsKey(policy))
                            .Select(a => SitePolicies.LeastRole[a.Policy!])
                            .ToList();
                        if (leastRoles.Count > 0)
                        {
                            GatewayAnswers.AddOrExtend(operation, "404", "No such site, or the caller is not an active member of "
                                + "it - one answer, so a slug reveals nothing (site-not-found).");
                        }

                        if (leastRoles.Any(r => r != SiteRole.Author))
                        {
                            GatewayAnswers.AddOrExtend(operation, "403", "The caller's role on this site is too low "
                                + "(site-role-required).");
                        }

                        // A created thing says where it is read (PP2.10).
                        if (operation.Responses?.TryGetValue("201", out var created) is true && created is OpenApiResponse declared)
                        {
                            declared.Headers ??= new Dictionary<string, IOpenApiHeader>();
                            declared.Headers["Location"] = new OpenApiHeader
                            {
                                Description = "Where the created thing is read in this API.",
                                Required = true,
                                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                            };
                        }

                        return Task.CompletedTask;
                    });
                }

                // A route that reads a body refuses one that is not JSON (PP1.12: it was answered, never declared).
                options.AddOperationTransformer((operation, context, _) =>
                {
                    if (operation.RequestBody is not null)
                    {
                        GatewayAnswers.AddOrExtend(operation, "415", "The body is not JSON (unsupported-media-type).");
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
                                Description = "With idempotency-key-in-use only: seconds to wait before retrying while the "
                                    + "first request with this key is still running.",
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
    /// What the gateway answers on the admin surface before a request reaches this service (PP1.7, PP1.8, PP1.12), so
    /// the admin client written from this document expects every one of them. Each is a problem document with a stable
    /// code, in the same shape as this service's own.
    /// </summary>
    internal static class GatewayAnswers
    {
        public static void Add(OpenApiOperation operation, string? method, bool signedIn)
        {
            // What the route itself can answer comes first; the gateway's own answers are added beside it, and a status the
            // route already declares has the gateway's meaning appended, so neither reading is lost (PP1.12).
            AddOrExtend(
                operation,
                "401",
                signedIn
                    ? "Not signed in, or the session has ended (session-ended, which also clears the gateway's cookie). From the "
                        + "gateway: a sign-in that still owes its second step (second-factor-required)."
                    : "From the gateway only: a sign-in that still owes its second step (second-factor-required) is held to the "
                        + "second-factor route until it is finished, abandoned by signing out, or runs out after five minutes.");
            if (!HttpMethods.IsGet(method ?? string.Empty) && !HttpMethods.IsHead(method ?? string.Empty))
            {
                AddOrExtend(operation, "400", "From the gateway: the antiforgery check failed (antiforgery-failed); read a fresh "
                    + "token and try again.");
                AddOrExtend(operation, "403", "From the gateway: a write from another origin (cross-origin-blocked).");
            }

            AddOrExtend(operation, "502", "From the gateway: the service could not be reached.");
            AddOrExtend(operation, "504", "From the gateway: the service did not answer in time.");
        }

        /// <summary>
        /// Declares <paramref name="status"/> as a problem, or appends <paramref name="description"/> to the description of
        /// one already declared.
        /// </summary>
        public static void AddOrExtend(OpenApiOperation operation, string status, string description)
        {
            if (operation.Responses?.TryGetValue("500", out var fault) != true || fault is not OpenApiResponse template)
            {
                return;
            }

            if (operation.Responses.TryGetValue(status, out var existing) && existing is OpenApiResponse declared)
            {
                if (declared.Description?.Contains(description, StringComparison.Ordinal) != true)
                {
                    declared.Description = string.IsNullOrWhiteSpace(declared.Description)
                        ? description
                        : $"{declared.Description.TrimEnd()} {description}";
                }

                return;
            }

            operation.Responses[status] = new OpenApiResponse { Description = description, Content = template.Content };
        }
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
