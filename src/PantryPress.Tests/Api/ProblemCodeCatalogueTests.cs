using System.Reflection;
using System.Text.RegularExpressions;
using PantryPress.ApiService.Http;
using PantryPress.Domain.Managers.Results;
using PantryPress.Gateway.Hosting;
using PantryPress.ServiceDefaults.EdgeHardening;

namespace PantryPress.Tests.Api;

// PP1.12 (audit finding): problem codes are declared in five places - the facades' errors, the API's status vocabulary,
// the idempotency step, the gateway's mirror and its own refusals, and the shared edge - and nothing checked them as a
// whole. A code is a stable, machine-readable name (api-contract.md): kebab-case, and one meaning per code.
public sealed partial class ProblemCodeCatalogueTests
{
    private static readonly Assembly[] Declaring =
    [
        typeof(Error).Assembly,
        typeof(ApiProblems).Assembly,
        typeof(GatewayApi).Assembly,
        typeof(RequestLimits).Assembly,
    ];

    private static readonly int[] FrameworkStatuses = [400, 401, 403, 404, 405, 413, 415, 429, 500, 502, 504];

    [Fact]
    public void Every_problem_code_is_kebab_case()
    {
        var codes = DeclaredCodes().ToList();

        Assert.True(codes.Count > 30, "The catalogue found too few codes to be looking in the right places.");
        Assert.All(codes, c => Assert.True(KebabCase().IsMatch(c.Code), $"{c.Where} declares '{c.Code}'."));
    }

    [Fact]
    public void A_facade_error_code_means_one_kind_of_failure()
    {
        // Two errors may share a code only when they are one failure worded for its moment (a lockout's wait).
        var conflicting = FacadeErrors()
            .GroupBy(e => e.Error.Code, StringComparer.Ordinal)
            .Where(group => group.Select(e => e.Error.Kind).Distinct().Count() > 1)
            .Select(group => group.Key);

        Assert.Empty(conflicting);
    }

    [Fact]
    public void A_facade_error_never_borrows_a_framework_status_code()
    {
        // "not-found" from a facade and "not-found" from an unknown route would mean different things to the admin app.
        var framework = FrameworkStatuses.Select(ApiProblems.CodeFor).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(FacadeErrors(), e => framework.Contains(e.Error.Code));
    }

    private static IEnumerable<(string Where, string Code)> DeclaredCodes()
    {
        foreach (var assembly in Declaring)
        {
            foreach (var type in assembly.GetTypes())
            {
                foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (field.FieldType == typeof(string)
                        && field.Name.EndsWith("Code", StringComparison.Ordinal)
                        && field.GetValue(null) is string code)
                    {
                        yield return ($"{type.Name}.{field.Name}", code);
                    }
                }
            }
        }

        foreach (var (where, error) in FacadeErrors())
        {
            yield return (where, error.Code);
        }

        foreach (var status in FrameworkStatuses)
        {
            yield return ($"ApiProblems.CodeFor({status})", ApiProblems.CodeFor(status));
        }
    }

    private static IEnumerable<(string Where, Error Error)> FacadeErrors()
    {
        yield return ("Error.Validation", Error.Validation(new Dictionary<string, string[]>()));
        foreach (var type in typeof(Error).Assembly.GetTypes())
        {
            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.FieldType == typeof(Error) && field.GetValue(null) is Error error)
                {
                    yield return ($"{type.Name}.{field.Name}", error);
                }
            }
        }
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex KebabCase();
}
