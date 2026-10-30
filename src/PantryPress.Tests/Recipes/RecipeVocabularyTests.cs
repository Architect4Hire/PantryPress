using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.1: the recipe vocabulary as written in code, which every table is seeded from (PP-D06, ADR 0006). Codes are what
// recipes store, so their shape and uniqueness are checked here before any migration carries them.
public sealed class RecipeVocabularyTests
{
    public static TheoryData<string, IReadOnlyList<VocabularyEntry>, int> Lists => new()
    {
        { "courses", Courses.All, Courses.Version },
        { "cuisines", Cuisines.All, Cuisines.Version },
        { "allergens", Allergens.All, Allergens.Version },
        { "diets", [.. Diets.All.Select(d => new VocabularyEntry(d.Code, d.Name, d.Since))], Diets.Version },
        { "units", [.. Units.All.Select(u => new VocabularyEntry(u.Code, u.Singular, u.Since))], Units.Version },
    };

    [Theory]
    [MemberData(nameof(Lists))]
    public void Every_code_is_well_formed_and_unique(string list, IReadOnlyList<VocabularyEntry> entries, int version)
    {
        _ = version;
        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.True(VocabularyCodes.IsWellFormed(e.Code), $"{list}: '{e.Code}' is not a well-formed code."));
        Assert.Equal(entries.Count, entries.Select(e => e.Code).Distinct(StringComparer.Ordinal).Count());
    }

    // Names are display text, and two words a blogger could not tell apart would be a bug in the list.
    [Theory]
    [MemberData(nameof(Lists))]
    public void Every_name_is_present_bounded_and_unique(string list, IReadOnlyList<VocabularyEntry> entries, int version)
    {
        _ = version;
        Assert.All(entries, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Name), $"{list}: '{e.Code}' has no name.");
            Assert.Equal(e.Name.Trim(), e.Name);
            Assert.True(e.Name.Length <= VocabularyCodes.NameMaxLength, $"{list}: '{e.Code}' has a name too long to store.");
        });
        Assert.Equal(entries.Count, entries.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [MemberData(nameof(Lists))]
    public void The_version_is_the_newest_entry(string list, IReadOnlyList<VocabularyEntry> entries, int version)
    {
        Assert.All(entries, e => Assert.InRange(e.Since, 1, version));
        Assert.True(entries.Max(e => e.Since) == version, $"{list}: the version is not the newest entry's.");
    }

    [Theory]
    [InlineData("ab", true)]
    [InlineData("main-course", true)]
    [InlineData("crustacean-shellfish", true)]
    [InlineData("a", false)]
    [InlineData("Main-course", false)]
    [InlineData("main course", false)]
    [InlineData("main_course", false)]
    [InlineData("-main", false)]
    [InlineData("main-", false)]
    [InlineData("main--course", false)]
    [InlineData("caf\u00e9", false)]
    [InlineData("\u0430pple", false)]
    public void The_code_shape_is_kebab_case_ascii(string code, bool wellFormed) =>
        Assert.Equal(wellFormed, VocabularyCodes.IsWellFormed(code));

    [Fact]
    public void A_code_of_the_longest_length_is_well_formed_and_one_more_is_not()
    {
        Assert.True(VocabularyCodes.IsWellFormed(new string('a', VocabularyCodes.MaxLength)));
        Assert.False(VocabularyCodes.IsWellFormed(new string('a', VocabularyCodes.MaxLength + 1)));
    }

    // Units

    [Fact]
    public void Every_unit_word_fits_its_column()
    {
        Assert.All(Units.All, u =>
        {
            Assert.InRange(u.Singular.Length, 1, Units.WordMaxLength);
            Assert.InRange(u.Plural.Length, 1, Units.WordMaxLength);
            Assert.True(u.Abbreviation is null or { Length: > 0 and <= Units.AbbreviationMaxLength }, $"'{u.Code}' has a bad abbreviation.");
        });
    }

    [Fact]
    public void Every_alias_names_a_unit_and_fits_its_column()
    {
        var codes = Units.All.Select(u => u.Code).ToHashSet(StringComparer.Ordinal);

        Assert.All(Units.Aliases, a =>
        {
            Assert.Contains(a.UnitCode, codes);
            Assert.InRange(a.Alias.Length, 1, Units.AliasMaxLength);
            Assert.Equal(a.Alias.Trim(), a.Alias);
            Assert.InRange(a.Since, 1, Units.Version);
        });
    }

    // A spelling is never older than its unit, and the list's version is its newest unit or spelling.
    [Fact]
    public void Every_alias_is_versioned_within_its_unit_and_the_list()
    {
        var units = Units.All.ToDictionary(u => u.Code, StringComparer.Ordinal);

        Assert.All(Units.Aliases, a => Assert.InRange(a.Since, units[a.UnitCode].Since, Units.Version));
        Assert.Equal(Units.Version, Units.All.Select(u => u.Since).Concat(Units.Aliases.Select(a => a.Since)).Max());
    }

    // The table's check: one lower-case spelling stands for every case of it.
    [Fact]
    public void A_case_insensitive_alias_is_lower_case()
    {
        Assert.All(
            Units.Aliases.Where(a => !a.CaseSensitive),
            a => Assert.Equal(a.Alias.ToLowerInvariant(), a.Alias));
    }

    // No spelling may mean two units: two aliases collide when they are the same text, or when either matches in any case
    // and the two fold to the same text. Only two case-sensitive aliases may differ by case alone, as T and t do.
    [Fact]
    public void No_spelling_names_two_units()
    {
        var aliases = Units.Aliases;
        for (var i = 0; i < aliases.Count; i++)
        {
            for (var j = i + 1; j < aliases.Count; j++)
            {
                var (a, b) = (aliases[i], aliases[j]);
                var collide = string.Equals(a.Alias, b.Alias, StringComparison.Ordinal)
                    || ((!a.CaseSensitive || !b.CaseSensitive) && string.Equals(a.Alias, b.Alias, StringComparison.OrdinalIgnoreCase));

                Assert.False(collide, $"'{a.Alias}' ({a.UnitCode}) and '{b.Alias}' ({b.UnitCode}) are one spelling.");
            }
        }
    }

    // A unit's own words are spellings of it, so the parser recognizes what the editor displays.
    [Fact]
    public void Every_unit_answers_to_its_own_words()
    {
        Assert.All(Units.All, u =>
        {
            Assert.Equal(u.Code, Resolve(u.Singular));
            Assert.Equal(u.Code, Resolve(u.Plural));
            if (u.Abbreviation is { } abbreviation)
            {
                Assert.Equal(u.Code, Resolve(abbreviation));
            }
        });
    }

    [Theory]
    [InlineData("T", "tablespoon")]
    [InlineData("t", "teaspoon")]
    [InlineData("Tbsp", "tablespoon")]
    [InlineData("TBSP", "tablespoon")]
    [InlineData("tsp.", "teaspoon")]
    [InlineData("Tsp", "teaspoon")]
    [InlineData("C", "cup")]
    [InlineData("c", "cup")]
    [InlineData("L", "liter")]
    [InlineData("mL", "milliliter")]
    [InlineData("litre", "liter")]
    [InlineData("lbs", "pound")]
    [InlineData("°F", "fahrenheit")]
    [InlineData("ºC", "celsius")]
    [InlineData("degrees F", "fahrenheit")]
    public void Common_spellings_name_the_expected_unit(string written, string unit) =>
        Assert.Equal(unit, Resolve(written));

    // A bare F or C is never a temperature: "1 C flour" is a cup, and a lone F is nothing.
    [Theory]
    [InlineData("F")]
    [InlineData("f")]
    [InlineData("tablespoonz")]
    public void Some_spellings_name_no_unit(string written) =>
        Assert.Null(Resolve(written));

    [Fact]
    public void Only_fahrenheit_and_celsius_measure_temperature()
    {
        Assert.Equal(
            ["celsius", "fahrenheit"],
            Units.All.Where(u => u.Dimension == UnitDimension.Temperature).Select(u => u.Code).Order(StringComparer.Ordinal));
    }

    // Counts never convert, so they belong to no measuring system; everything that converts belongs to one.
    [Fact]
    public void A_count_is_neutral_and_a_measure_is_not()
    {
        Assert.All(Units.All, u => Assert.Equal(u.Dimension == UnitDimension.Count, u.System == UnitSystem.Neutral));
    }

    [Fact]
    public void Volume_weight_and_temperature_each_have_us_and_metric_units()
    {
        foreach (var dimension in new[] { UnitDimension.Volume, UnitDimension.Weight, UnitDimension.Temperature })
        {
            var systems = Units.All.Where(u => u.Dimension == dimension).Select(u => u.System).ToHashSet();
            Assert.Equal([UnitSystem.UsCustomary, UnitSystem.Metric], systems.Order());
        }
    }

    // The numbers are what the rows store, so renumbering an enum would silently change every unit.
    [Fact]
    public void The_unit_enums_keep_their_stored_numbers()
    {
        Assert.Equal([0, 1, 2], Enum.GetValues<UnitSystem>().Select(s => (int)s));
        Assert.Equal(
            [UnitSystem.UsCustomary, UnitSystem.Metric, UnitSystem.Neutral],
            Enum.GetValues<UnitSystem>());
        Assert.Equal([0, 1, 2, 3], Enum.GetValues<UnitDimension>().Select(d => (int)d));
        Assert.Equal(
            [UnitDimension.Volume, UnitDimension.Weight, UnitDimension.Temperature, UnitDimension.Count],
            Enum.GetValues<UnitDimension>());
    }

    // Append-only: recipes store these codes under foreign keys (PP4.3), so every code version 1 seeded must stay in its
    // list, whatever a later version adds. This is the one place the lists are written out twice, on purpose: a rename in
    // the list, with its migration, passes every seed test and fails here. Extend these only by appending.
    private static readonly string[] SeededUnits =
    [
        "teaspoon", "tablespoon", "fluid-ounce", "cup", "pint", "quart", "gallon", "milliliter", "deciliter", "liter",
        "ounce", "pound", "gram", "kilogram", "fahrenheit", "celsius", "pinch", "dash", "drop", "clove", "can", "jar",
        "package", "stick", "slice", "piece", "bunch", "sprig", "head", "handful",
    ];

    private static readonly string[] SeededCourses =
    [
        "breakfast", "brunch", "appetizer", "snack", "soup", "salad", "side-dish", "main-course", "dessert", "bread",
        "drink", "sauce",
    ];

    private static readonly string[] SeededCuisines =
    [
        "american", "southern", "cajun-creole", "tex-mex", "mexican", "caribbean", "latin-american", "british", "irish",
        "french", "italian", "spanish", "portuguese", "german", "greek", "mediterranean", "middle-eastern",
        "north-african", "indian", "chinese", "japanese", "korean", "thai", "vietnamese", "filipino",
    ];

    private static readonly string[] SeededDiets =
    [
        "vegetarian", "vegan", "pescatarian", "gluten-free", "dairy-free", "low-lactose", "low-fat", "low-calorie",
        "low-salt", "diabetic", "halal", "kosher", "hindu", "keto", "paleo",
    ];

    private static readonly string[] SeededAllergens =
    [
        "milk", "eggs", "fish", "crustacean-shellfish", "mollusks", "tree-nuts", "peanuts", "wheat", "gluten", "soy",
        "sesame", "celery", "mustard", "sulfites", "lupin",
    ];

    [Fact]
    public void Every_code_ever_seeded_is_still_in_its_list()
    {
        Assert.Empty(SeededUnits.Except(Units.All.Select(u => u.Code), StringComparer.Ordinal));
        Assert.Empty(SeededCourses.Except(Courses.All.Select(c => c.Code), StringComparer.Ordinal));
        Assert.Empty(SeededCuisines.Except(Cuisines.All.Select(c => c.Code), StringComparer.Ordinal));
        Assert.Empty(SeededDiets.Except(Diets.All.Select(d => d.Code), StringComparer.Ordinal));
        Assert.Empty(SeededAllergens.Except(Allergens.All.Select(a => a.Code), StringComparer.Ordinal));
    }

    // Every alias version 1 seeded still names the unit it named: an ingredient line parsed against it keeps its meaning.
    [Theory]
    [InlineData("T", "tablespoon")]
    [InlineData("t", "teaspoon")]
    [InlineData("c", "cup")]
    [InlineData("l", "liter")]
    [InlineData("oz", "ounce")]
    [InlineData("fl oz", "fluid-ounce")]
    [InlineData("°f", "fahrenheit")]
    [InlineData("°c", "celsius")]
    public void A_seeded_alias_keeps_its_unit(string alias, string unit) =>
        Assert.Contains(Units.Aliases, a => a.Alias == alias && a.UnitCode == unit);

    // Diets

    // Written out from https://schema.org/RestrictedDiet, so a typo in the list that feeds the column check and JSON-LD
    // cannot pass by being compared with itself.
    [Fact]
    public void The_schema_org_members_are_the_restricted_diets_exactly()
    {
        string[] restrictedDiet =
        [
            "DiabeticDiet", "GlutenFreeDiet", "HalalDiet", "HinduDiet", "KosherDiet", "LowCalorieDiet", "LowFatDiet",
            "LowLactoseDiet", "LowSaltDiet", "VeganDiet", "VegetarianDiet",
        ];

        Assert.Equal(restrictedDiet.Order(StringComparer.Ordinal), Diets.SchemaOrgMembers.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_diets_schema_org_member_is_a_real_one_used_once()
    {
        var members = Diets.All.Where(d => d.SchemaOrgDiet is not null).Select(d => d.SchemaOrgDiet!).ToList();

        Assert.All(members, m => Assert.True(Diets.SchemaOrgMembers.Contains(m), $"'{m}' is no RestrictedDiet member."));
        Assert.Equal(members.Count, members.Distinct(StringComparer.Ordinal).Count());
        Assert.All(Diets.SchemaOrgMembers, m => Assert.True(m.Length <= Diets.SchemaOrgDietMaxLength));
    }

    // RCP-010: a diet badge affects JSON-LD only when the recipe's diet field agrees, so each diet badge needs its diet.
    [Theory]
    [InlineData("gluten-free")]
    [InlineData("dairy-free")]
    [InlineData("vegan")]
    public void Every_diet_badge_has_a_diet(string code) =>
        Assert.Contains(code, Diets.All.Select(d => d.Code));

    // Allergens

    private static readonly string[] UsMajorAllergens =
        ["milk", "eggs", "fish", "crustacean-shellfish", "tree-nuts", "peanuts", "wheat", "soy", "sesame"];

    private static readonly string[] EuAllergens =
    [
        "gluten", "crustacean-shellfish", "eggs", "fish", "peanuts", "soy", "milk", "tree-nuts", "celery", "mustard",
        "sesame", "sulfites", "lupin", "mollusks",
    ];

    [Fact]
    public void The_allergens_are_the_us_and_eu_lists_together()
    {
        Assert.Equal(9, UsMajorAllergens.Length);
        Assert.Equal(14, EuAllergens.Length);
        Assert.Equal(
            UsMajorAllergens.Union(EuAllergens).Order(StringComparer.Ordinal),
            Allergens.All.Select(a => a.Code).Order(StringComparer.Ordinal));
    }

    // The matching rule the aliases are written for: an exact case-sensitive spelling first, then any case-insensitive
    // one. Only what the list must make possible; the parser's own forgiveness is PP4.5's.
    private static string? Resolve(string written) =>
        Units.Aliases.FirstOrDefault(a => a.CaseSensitive && a.Alias == written)?.UnitCode
        ?? Units.Aliases.FirstOrDefault(a => !a.CaseSensitive && a.Alias == written.ToLowerInvariant())?.UnitCode;
}
