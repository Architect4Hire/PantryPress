using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Validation;

/// <summary>A whole recipe to create (PP4.11): every rule a save holds, each failure at the field's own JSON path.</summary>
internal sealed class RecipeContentValidator : AbstractValidator<RecipeContent>
{
    public RecipeContentValidator() =>
        RuleFor(c => c).Custom((content, context) => RecipeContentRules.Check(content, string.Empty, context.AddFailure));
}

/// <summary>A save of a recipe's working copy (PP4.11): the version it was read at, and the whole recipe.</summary>
internal sealed class UpdateRecipeRequestValidator : AbstractValidator<UpdateRecipeRequest>
{
    public UpdateRecipeRequestValidator()
    {
        RuleFor(r => r.Version)
            .Must(version => RecipeVersion.TryRead(version, out _))
            .WithMessage("Reload the recipe and try again.");
        RuleFor(r => r.Content)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("Send the recipe.")
            .Custom((content, context) => RecipeContentRules.Check(content!, "content.", context.AddFailure));
    }
}

/// <summary>A page of a site's recipes (PP4.11).</summary>
internal sealed class ListRecipesQueryValidator : AbstractValidator<ListRecipesQuery>
{
    public ListRecipesQueryValidator()
    {
        RuleFor(q => q.Limit)
            .InclusiveBetween(1, RecipeListCursor.MaxLimit)
            .When(q => q.Limit is not null)
            .WithMessage($"Ask for 1 to {RecipeListCursor.MaxLimit} recipes at a time.");
        RuleFor(q => q.Search)
            .Cascade(CascadeMode.Stop)
            .Must(search => RecipeText.IsWellFormed(search!, multiline: false))
            .WithMessage("This search has a character we can't use. Retype it and try again.")
            .Must(search => (RecipeListFilter.NormalizeSearch(search)?.Length ?? 0) <= RecipeListFilter.MaxSearchLength)
            .WithMessage($"Search for up to {RecipeListFilter.MaxSearchLength} characters.")
            .When(q => q.Search is not null);
        RuleFor(q => q.CourseCode)
            .Must(code => RecipeContentRules.IsCourse(code!))
            .When(q => !string.IsNullOrEmpty(q.CourseCode))
            .OverridePropertyName("Course")
            .WithMessage("Choose a course from the list.");
        RuleFor(q => q.CuisineCode)
            .Must(code => RecipeContentRules.IsCuisine(code!))
            .When(q => !string.IsNullOrEmpty(q.CuisineCode))
            .OverridePropertyName("Cuisine")
            .WithMessage("Choose a cuisine from the list.");
        RuleFor(q => q.DietCode)
            .Must(code => RecipeContentRules.IsDiet(code!))
            .When(q => !string.IsNullOrEmpty(q.DietCode))
            .OverridePropertyName("Diet")
            .WithMessage("Choose a diet from the list.");

        // A cursor is bound to the list it came from (PP4.12a): one made under another search, filter, or tab is refused
        // here rather than continuing a different list.
        RuleFor(q => q.Cursor)
            .Must((query, cursor) => RecipeListCursor.TryRead(cursor, RecipeListFilter.From(query), out _))
            .When(q => q.Cursor is not null)
            .WithMessage("Start again from the first page.");
    }
}

/// <summary>
/// The rules a whole recipe holds when it is saved (RCP-001, RCP-002, RCP-006; PP4.3, PP4.4, PP4.11), checked before
/// anything is read, so a refusal says what to fix and the tables' checks are never what refuses. Text is stored exactly as
/// sent, so these only refuse: half of a surrogate pair, a control character, text that is only white space, too long, or
/// on more than one line where one is meant. Each failure is named by its JSON path under the given prefix.
/// </summary>
internal static class RecipeContentRules
{
    private const string Blank = "Write something here, or leave it out.";
    private const string Broken = "This has a character we can't keep. Retype it and try again.";
    private const string OneLine = "Keep this on one line, with no character we can't keep.";
    private const string NamedTwice = "This part is in the recipe twice. Reload the recipe and try again.";

    private static readonly HashSet<string> CourseCodes = [.. Courses.All.Select(c => c.Code)];
    private static readonly HashSet<string> CuisineCodes = [.. Cuisines.All.Select(c => c.Code)];
    private static readonly HashSet<string> DietCodes = [.. Diets.All.Select(d => d.Code)];
    private static readonly HashSet<string> AllergenCodes = [.. Allergens.All.Select(a => a.Code)];

    public static bool IsCourse(string code) => CourseCodes.Contains(code);

    public static bool IsCuisine(string code) => CuisineCodes.Contains(code);

    public static bool IsDiet(string code) => DietCodes.Contains(code);

    public static void Check(RecipeContent content, string prefix, Action<ValidationFailure> addFailure)
    {
        void Fail(string path, string message) => addFailure(new ValidationFailure(prefix + path, message));

        if (content.Title is null)
        {
            Fail("title", "Give the recipe a title.");
        }
        else
        {
            Text(content.Title, "title", RecipeLimits.MaxTitleLength, multiline: false, Fail);
        }

        Text(content.Summary, "summary", RecipeLimits.MaxSummaryLength, multiline: true, Fail);
        Yield(content.Yield, Fail);
        Text(content.YieldUnit, "yieldUnit", RecipeLimits.MaxYieldUnitLength, multiline: false, Fail);
        if (content.YieldUnit is not null && content.Yield is null)
        {
            Fail("yieldUnit", "Enter how much the recipe makes first.");
        }

        Minutes(content.PrepMinutes, "prepMinutes", Fail);
        Minutes(content.CookMinutes, "cookMinutes", Fail);
        Minutes(content.AdditionalMinutes, "additionalMinutes", Fail);
        Text(content.AdditionalTimeLabel, "additionalTimeLabel", RecipeLimits.MaxAdditionalTimeLabelLength, multiline: false, Fail);
        if (content.AdditionalTimeLabel is not null && content.AdditionalMinutes is null)
        {
            Fail("additionalTimeLabel", "Enter the additional time first.");
        }

        if (content.VideoUrl is { } video && (video.Length > RecipeLimits.MaxVideoUrlLength || !RecipeVideoHosts.IsAllowed(video)))
        {
            Fail("videoUrl", "Use the address of a video on YouTube or Vimeo, starting https://.");
        }

        Nutrition(content.Nutrition, Fail);

        Groups(
            content.IngredientGroups,
            new GroupShape("ingredientGroups", "lines", "originalText", RecipeLimits.MaxIngredientGroups, RecipeLimits.MaxIngredientLines, RecipeLimits.MaxIngredientTextLength, false, "ingredient groups", "ingredient lines"),
            g => g.Id,
            g => g.Heading,
            g => g.Lines?.Select(l => l is null ? ((Guid?, string?)?)null : (l.Id, l.OriginalText)).ToList(),
            Fail);
        Groups(
            content.StepGroups,
            new GroupShape("stepGroups", "steps", "text", RecipeLimits.MaxStepGroups, RecipeLimits.MaxSteps, RecipeLimits.MaxStepTextLength, true, "step groups", "steps"),
            g => g.Id,
            g => g.Heading,
            g => g.Steps?.Select(s => s is null ? ((Guid?, string?)?)null : (s.Id, s.Text)).ToList(),
            Fail);

        Texts(content.Equipment, "equipment", RecipeLimits.MaxEquipment, RecipeLimits.MaxEquipmentLength, multiline: false, "pieces of equipment", Fail);
        Texts(content.Notes, "notes", RecipeLimits.MaxNotes, RecipeLimits.MaxNoteLength, multiline: true, "notes", Fail);
        Texts(content.Keywords, "keywords", RecipeLimits.MaxKeywords, RecipeLimits.MaxKeywordLength, multiline: false, "keywords", Fail);

        Codes(content.Courses, "courses", CourseCodes, RecipeLimits.MaxCourses, "courses", Fail);
        Codes(content.Cuisines, "cuisines", CuisineCodes, RecipeLimits.MaxCuisines, "cuisines", Fail);
        Codes(content.Diets, "diets", DietCodes, DietCodes.Count, "diets", Fail);
        Codes(content.Allergens, "allergens", AllergenCodes, AllergenCodes.Count, "allergens", Fail);
    }

    private static void Text(string? text, string path, int maxLength, bool multiline, Action<string, string> fail)
    {
        if (text is null)
        {
            return;
        }

        if (!RecipeText.IsWellFormed(text, multiline))
        {
            fail(path, multiline ? Broken : OneLine);
        }
        else if (RecipeText.IsBlank(text))
        {
            fail(path, Blank);
        }
        else if (text.Length > maxLength)
        {
            fail(path, $"Keep this to {N(maxLength)} characters.");
        }
    }

    // Grouped with commas whatever the server's culture, so every message reads the same everywhere.
    private static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static void Minutes(int? minutes, string path, Action<string, string> fail)
    {
        if (minutes is < 0 or > RecipeLimits.MaxMinutes)
        {
            fail(path, $"Enter a time from 0 to {N(RecipeLimits.MaxMinutes)} minutes.");
        }
    }

    private static void Yield(RecipeQuantity? yield, Action<string, string> fail)
    {
        if (yield is null)
        {
            return;
        }

        if (yield.Kind is not (QuantityKind.Amount or QuantityKind.Range))
        {
            fail("yield.kind", "Enter how much the recipe makes as an amount or a range.");
            return;
        }

        var low = Fraction(yield.Low, "yield.low", fail);
        if (yield.Kind == QuantityKind.Amount)
        {
            if (yield.High is not null)
            {
                fail("yield.high", "Only a range has a high end.");
            }

            return;
        }

        var high = Fraction(yield.High, "yield.high", fail);
        if (low is { } l && high is { } h && h <= l)
        {
            fail("yield.high", "Make the high end of the range more than the low end.");
        }
    }

    private static Rational? Fraction(RecipeFraction? fraction, string path, Action<string, string> fail)
    {
        if (fraction is not { Numerator: >= 1 and <= RecipeLimits.MaxQuantityTerm, Denominator: >= 1 and <= RecipeLimits.MaxQuantityTerm })
        {
            fail(path, $"Enter an amount above zero, as a fraction with both numbers from 1 to {N(RecipeLimits.MaxQuantityTerm)}.");
            return null;
        }

        return Rational.Create(fraction.Numerator, fraction.Denominator);
    }

    private static void Nutrition(RecipeNutrition? nutrition, Action<string, string> fail)
    {
        if (nutrition is null)
        {
            return;
        }

        Text(nutrition.ServingSize, "nutrition.servingSize", RecipeLimits.MaxServingSizeLength, multiline: false, fail);
        Amount(nutrition.Calories, "calories");
        Amount(nutrition.FatGrams, "fatGrams");
        Amount(nutrition.SaturatedFatGrams, "saturatedFatGrams");
        Amount(nutrition.TransFatGrams, "transFatGrams");
        Amount(nutrition.UnsaturatedFatGrams, "unsaturatedFatGrams");
        Amount(nutrition.CholesterolMilligrams, "cholesterolMilligrams");
        Amount(nutrition.SodiumMilligrams, "sodiumMilligrams");
        Amount(nutrition.CarbohydrateGrams, "carbohydrateGrams");
        Amount(nutrition.FiberGrams, "fiberGrams");
        Amount(nutrition.SugarGrams, "sugarGrams");
        Amount(nutrition.ProteinGrams, "proteinGrams");

        // Exactly as entered (RCP-006): a third decimal place would be rounded by its two-place column, so it is refused.
        void Amount(decimal? value, string name)
        {
            if (value is not { } v)
            {
                return;
            }

            // The range first, so an amount too large for a hundredth to be taken of it is refused, never an overflow.
            if (v < 0 || v > RecipeLimits.MaxNutritionHundredths / 100m || decimal.Round(v, RecipeLimits.NutritionScale) != v)
            {
                fail("nutrition." + name, "Enter an amount from 0 to 9,999,999.99, with at most two decimal places.");
            }
        }
    }

    private sealed record GroupShape(
        string Path,
        string PartsName,
        string TextName,
        int MaxGroups,
        int MaxParts,
        int MaxTextLength,
        bool Multiline,
        string GroupsWord,
        string PartsWord);

    private static void Groups<TGroup>(
        IReadOnlyList<TGroup?>? groups,
        GroupShape shape,
        Func<TGroup, Guid?> groupId,
        Func<TGroup, string?> heading,
        Func<TGroup, List<(Guid? Id, string? Text)?>?> parts,
        Action<string, string> fail)
        where TGroup : class
    {
        if (groups is null)
        {
            return;
        }

        if (groups.Count > shape.MaxGroups)
        {
            fail(shape.Path, $"A recipe can have up to {shape.MaxGroups} {shape.GroupsWord}.");
            return;
        }

        var groupIds = new HashSet<Guid>();
        var partIds = new HashSet<Guid>();
        var total = 0;
        for (var g = 0; g < groups.Count; g++)
        {
            var at = $"{shape.Path}[{g}]";
            if (groups[g] is not { } group)
            {
                fail(at, "Send each group, or leave it out.");
                continue;
            }

            Id(groupId(group), at + ".id", groupIds, fail);
            Text(heading(group), at + ".heading", RecipeLimits.MaxGroupHeadingLength, multiline: false, fail);

            var list = parts(group) ?? [];
            total += list.Count;
            if (total > shape.MaxParts)
            {
                continue;
            }

            for (var p = 0; p < list.Count; p++)
            {
                var partAt = $"{at}.{shape.PartsName}[{p}]";
                if (list[p] is not { } part)
                {
                    fail(partAt, "Send each part, or leave it out.");
                    continue;
                }

                Id(part.Id, partAt + ".id", partIds, fail);
                if (part.Text is null)
                {
                    fail($"{partAt}.{shape.TextName}", Blank);
                }
                else
                {
                    Text(part.Text, $"{partAt}.{shape.TextName}", shape.MaxTextLength, shape.Multiline, fail);
                }
            }
        }

        if (total > shape.MaxParts)
        {
            fail(shape.Path, $"A recipe can have up to {shape.MaxParts} {shape.PartsWord}.");
        }
    }

    private static void Id(Guid? id, string path, HashSet<Guid> seen, Action<string, string> fail)
    {
        if (id is not { } value)
        {
            return;
        }

        if (value == Guid.Empty)
        {
            fail(path, RecipeErrors.PartNotFoundMessage);
        }
        else if (!seen.Add(value))
        {
            fail(path, NamedTwice);
        }
    }

    private static void Texts(
        IReadOnlyList<string?>? texts,
        string path,
        int maxCount,
        int maxLength,
        bool multiline,
        string word,
        Action<string, string> fail)
    {
        if (texts is null)
        {
            return;
        }

        if (texts.Count > maxCount)
        {
            fail(path, $"A recipe can have up to {maxCount} {word}.");
            return;
        }

        for (var i = 0; i < texts.Count; i++)
        {
            if (texts[i] is null)
            {
                fail($"{path}[{i}]", Blank);
            }
            else
            {
                Text(texts[i], $"{path}[{i}]", maxLength, multiline, fail);
            }
        }
    }

    private static void Codes(
        IReadOnlyList<string?>? codes,
        string path,
        HashSet<string> known,
        int maxCount,
        string word,
        Action<string, string> fail)
    {
        if (codes is null)
        {
            return;
        }

        if (codes.Count > maxCount)
        {
            fail(path, $"A recipe can have up to {maxCount} {word}.");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < codes.Count; i++)
        {
            if (codes[i] is not { } code || !known.Contains(code))
            {
                fail($"{path}[{i}]", "Choose one from the list.");
            }
            else if (!seen.Add(code))
            {
                fail($"{path}[{i}]", "This is chosen twice.");
            }
        }
    }
}
