using System.Globalization;
using System.Text.RegularExpressions;
using PantryPress.Domain.Modules.Accounts.Business;

namespace PantryPress.Tests.Accounts;

// PP3.8: the admin app tells a person the shortest password the API takes before they pick one. That is the API's rule
// written again in a template, so this reads every hint the app gives and fails when it stops saying the same number.
public sealed partial class PasswordHintTests
{
    private static readonly string Auth = Path.GetFullPath(Path.Combine(
        DomainSource.Project.FullName, "..", "web", "projects", "pantrypress-admin", "src", "app", "features", "auth"));

    [Fact]
    public void Every_password_hint_names_the_apis_minimum_length()
    {
        var hints = Directory.EnumerateFiles(Auth, "*.html")
            .SelectMany(file => Hint().Matches(File.ReadAllText(file)).Select(match => (file, match)))
            .ToList();

        // Sign-up and reset are the two places a person chooses one.
        Assert.Equal(2, hints.Count);
        Assert.All(hints, hint => Assert.Equal(
            PasswordRules.MinLength.ToString(CultureInfo.InvariantCulture),
            hint.match.Groups["length"].Value));
    }

    [GeneratedRegex(@"at least (?<length>\d+) characters", RegexOptions.CultureInvariant)]
    private static partial Regex Hint();
}
