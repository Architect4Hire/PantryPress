using FluentValidation;
using PantryPress.Domain.Managers.Results;

namespace PantryPress.Domain.Managers.Validation;

public static class ValidatorExtensions
{
    /// <summary>
    /// Validates at the facade edge (backend.md) and turns failures into a <see cref="ErrorKind.Validation"/> error
    /// keyed by camel-cased property name, matching the JSON the caller sent. Null when the input is valid.
    /// </summary>
    public static async Task<Error?> CheckAsync<T>(
        this IValidator<T> validator,
        T? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Error.Validation(new Dictionary<string, string[]> { [""] = ["The request is empty."] });
        }

        var result = await validator.ValidateAsync(request, cancellationToken);
        if (result.IsValid)
        {
            return null;
        }

        var fields = result.Errors
            .GroupBy(e => ToCamelCase(e.PropertyName), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray(), StringComparer.Ordinal);
        return Error.Validation(fields);
    }

    private static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) || char.IsLower(name[0]) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
