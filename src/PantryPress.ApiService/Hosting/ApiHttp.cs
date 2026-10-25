using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using PantryPress.ApiService.Http;

namespace PantryPress.ApiService.Hosting;

internal static class ApiHttp
{
    /// <summary>
    /// Controllers with the API's JSON conventions (api-contract.md: camel-case names, kebab-case enum strings), problem
    /// details for every failure, and plain-language binding errors. Non-nullable properties are not implicitly
    /// required: the facade validators own every rule and its message.
    /// </summary>
    public static IServiceCollection AddApiControllers(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = ApiProblems.ApplyDefaults);
        services.AddControllers(options =>
            {
                options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;

                // Every route belongs to a declared, versioned API surface, and the surface names the document that
                // describes it (api-contract.md).
                options.Conventions.Add(new ApiSurfaceConvention());

                // Idempotency-Key only on writes with a signed-in caller (PP1.9).
                options.Conventions.Add(new IdempotencyConvention());

                // A site route carries a site policy, and a site policy sits only on a site route (PP2.3).
                options.Conventions.Add(new SitePolicyConvention());

                // Nothing a client sends can name a site; the slug and the membership do (PP2.4).
                options.Conventions.Add(new SiteIdBindingConvention());

                // Any route can be rate-limited, carry too much body, or fail unexpectedly. Declared globally: a
                // controller's own response-type attributes hide ones inherited from a base class.
                options.Filters.Add(
                    new ProducesResponseTypeAttribute<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, ApiProblems.ContentType));
                options.Filters.Add(
                    new ProducesResponseTypeAttribute<ProblemDetails>(StatusCodes.Status429TooManyRequests, ApiProblems.ContentType));
                options.Filters.Add(
                    new ProducesResponseTypeAttribute<ProblemDetails>(StatusCodes.Status500InternalServerError, ApiProblems.ContentType));
            })
            .AddJsonOptions(options => Configure(options.JsonSerializerOptions))
            .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ApiProblems.FromInvalidModelState);

        // The OpenAPI generator and minimal endpoints read these options rather than MVC's.
        services.ConfigureHttpJsonOptions(options => Configure(options.SerializerOptions));
        return services;
    }

    /// <summary>
    /// Admin API responses are private to one account, so nothing is cached by a browser or proxy unless an endpoint
    /// says otherwise.
    /// </summary>
    public static IApplicationBuilder UseNoStoreByDefault(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                if (!context.Response.Headers.ContainsKey(HeaderNames.CacheControl))
                {
                    context.Response.Headers.CacheControl = "no-store";
                }

                return Task.CompletedTask;
            });
            return next(context);
        });

    private static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false));
    }
}
