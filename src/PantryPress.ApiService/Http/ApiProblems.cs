using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using PantryPress.Domain.Managers.Results;

namespace PantryPress.ApiService.Http;

/// <summary>
/// RFC 9457 problem details for every failure the API returns (api-contract.md): a stable <c>code</c> and a
/// plain-language <c>title</c>. Facade errors carry their own code; failures the framework produces (an unknown route,
/// an unhandled exception, a rate limit) get one from their status.
/// </summary>
internal static class ApiProblems
{
    public const string ContentType = "application/problem+json";
    public const string CodeKey = "code";

    public static int StatusFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation or ErrorKind.Rejected => StatusCodes.Status400BadRequest,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.TooManyAttempts => StatusCodes.Status429TooManyRequests,
        ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
        ErrorKind.Gone => StatusCodes.Status410Gone,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Fills in a code and a plain title for problems the framework writes, keeping any code already set.</summary>
    public static void ApplyDefaults(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        var status = problem.Status ?? context.HttpContext.Response.StatusCode;

        // A handler that already chose a code chose its title with it (the session scheme's "you were signed out"),
        // so the status's generic title does not replace it.
        var chosen = problem.Extensions.ContainsKey(CodeKey);
        problem.Extensions.TryAdd(CodeKey, CodeFor(status));
        if (!chosen && TitleFor(status) is { } title)
        {
            problem.Title = title;
        }

        // Exception details never reach a client, in any environment.
        problem.Detail = status >= 500 ? null : problem.Detail;
        problem.Extensions.Remove("exception");
    }

    public static ObjectResult FromError(ControllerBase controller, Error error)
    {
        var status = StatusFor(error.Kind);
        ProblemDetails problem;
        if (error.Kind == ErrorKind.Validation)
        {
            var modelState = new ModelStateDictionary();
            foreach (var (field, messages) in error.Fields)
            {
                foreach (var message in messages)
                {
                    modelState.AddModelError(field, message);
                }
            }

            problem = controller.ProblemDetailsFactory.CreateValidationProblemDetails(
                controller.HttpContext,
                modelState,
                status,
                error.Title);
        }
        else
        {
            problem = controller.ProblemDetailsFactory.CreateProblemDetails(controller.HttpContext, status, error.Title);
        }

        problem.Title = error.Title;
        problem.Extensions[CodeKey] = error.Code;

        // A lockout says how long in words and, here, in seconds (PP1.12); never less than one.
        if (error.RetryAfter is { } wait)
        {
            controller.Response.Headers.RetryAfter =
                Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { ContentType } };
    }

    /// <summary>
    /// Requests the framework could not bind (malformed JSON, a value of the wrong type). The framework's messages
    /// quote parser internals, so each field gets a plain one instead.
    /// </summary>
    public static IActionResult FromInvalidModelState(ActionContext context)
    {
        var plain = new ModelStateDictionary();
        foreach (var key in context.ModelState.Where(e => e.Value?.Errors.Count > 0).Select(e => e.Key))
        {
            plain.AddModelError(ToFieldName(key, QueryNames(context)), "This isn't in the format we expected.");
        }

        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var error = Error.Validation(new Dictionary<string, string[]>());
        var problem = factory.CreateValidationProblemDetails(
            context.HttpContext,
            plain,
            StatusCodes.Status400BadRequest,
            error.Title);
        problem.Extensions[CodeKey] = error.Code;
        return new BadRequestObjectResult(problem) { ContentTypes = { ContentType } };
    }

    public static string CodeFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "bad-request",
        StatusCodes.Status401Unauthorized => "sign-in-required",
        StatusCodes.Status403Forbidden => "forbidden",
        StatusCodes.Status404NotFound => "not-found",
        StatusCodes.Status405MethodNotAllowed => "method-not-allowed",
        StatusCodes.Status413PayloadTooLarge => "request-too-large",
        StatusCodes.Status415UnsupportedMediaType => "unsupported-media-type",
        StatusCodes.Status429TooManyRequests => "too-many-requests",
        >= 500 => "unexpected-error",
        _ => "request-failed",
    };

    /// <summary>The plain title for a status the framework produced, or null when the status needs none.</summary>
    internal static string? TitleFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "We couldn't read that request.",
        StatusCodes.Status401Unauthorized => "Please sign in to continue.",
        StatusCodes.Status403Forbidden => "You don't have access to this.",
        StatusCodes.Status404NotFound => "We couldn't find that.",
        StatusCodes.Status405MethodNotAllowed => "That action isn't available here.",
        StatusCodes.Status413PayloadTooLarge => "That request is too large.",
        StatusCodes.Status415UnsupportedMediaType => "Send the details as JSON.",
        StatusCodes.Status429TooManyRequests => "Too many requests. Please wait a minute and try again.",
        >= 500 => "Something went wrong on our side. Please try again in a moment.",
        _ => null,
    };

    // "$.accountId" from System.Text.Json becomes the JSON field name, and a query parameter's own name is its field
    // ("limit" in "?limit=abc", PP2.10); a missing or unreadable body ("request", "$") belongs to no field.
    private static string ToFieldName(string key, IReadOnlySet<string> queryNames) =>
        key.StartsWith("$.", StringComparison.Ordinal) ? key[2..]
        : queryNames.Contains(key) ? key
        : string.Empty;

    private static HashSet<string> QueryNames(ActionContext context) =>
        context.ActionDescriptor.Parameters
            .Where(p => p.BindingInfo?.BindingSource == BindingSource.Query)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
}
