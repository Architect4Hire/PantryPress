using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

/// <summary>
/// The recipes the JSON-LD golden files are made from (PP4.10). Each ingredient line is read by the real parser, as a
/// stored line would have been, so the snapshot is one the recipe tables could hold; the generator reads only the text.
/// </summary>
internal static class RecipeJsonLdCases
{
    // Built from code points, so no literal separator sits in this file and ends a C# string.
    private static readonly string LineSeparator = char.ConvertFromUtf32(0x2028);
    private static readonly string ParagraphSeparator = char.ConvertFromUtf32(0x2029);
    private static readonly string Custard = char.ConvertFromUtf32(0x1F36E);

    private static readonly DateTimeOffset Published = new(2026, 11, 2, 14, 30, 5, TimeSpan.Zero);

    public static RecipeJsonLdContext FullContext { get; } = new(
        "https://apple-pie.pantrypress.app/posts/brown-butter-apple-pie",
        [
            "https://apple-pie.pantrypress.app/media/pie-16x9.jpg",
            "https://apple-pie.pantrypress.app/media/pie-4x3.jpg",
            "https://apple-pie.pantrypress.app/media/pie-1x1.jpg",
        ],
        new RecipeJsonLdAuthor("June Baker", "https://apple-pie.pantrypress.app/about"),
        Published);

    /// <summary>Every case by name: the snapshot, and the page's context.</summary>
    public static IReadOnlyDictionary<string, (RecipeSnapshot Snapshot, RecipeJsonLdContext Context)> All { get; } =
        new Dictionary<string, (RecipeSnapshot, RecipeJsonLdContext)>(StringComparer.Ordinal)
        {
            ["full"] = (Full(), FullContext),
            ["bare"] = (Recipe("Toast"), RecipeJsonLdContext.None),
            ["sections"] = (Recipe(
                "Layer cake",
                summary: "Two layers, one frosting.",
                ingredients:
                [
                    ("For the cake", ["2 1/4 cups cake flour", "1 1/2 cups sugar", "3 eggs"]),
                    ("For the frosting:", ["1 cup butter, softened", "3 cups powdered sugar"]),
                ],
                steps:
                [
                    (null, ["Heat the oven to 350°F."]),
                    ("Cake", ["Whisk the dry ingredients.", "Beat in the eggs."]),
                    ("Frosting", ["Beat the butter until pale.", "Add the sugar a cup at a time."]),
                    ("Empty group", []),
                ],
                prep: 30,
                cook: 35), FullContext),
            ["yield-and-unparsed"] = (Recipe(
                "Sourdough loaves",
                yield: Quantity.Range(Rational.Create(2, 1), Rational.Create(3, 1)),
                yieldUnit: "loaves",
                ingredients: [(null, ["1000 g bread flour", "750 g water", "a good glug of olive oil (trust me)", "salt"])],
                steps: [(null, ["Mix.", "Wait.", "Bake."])],
                additional: 1440), FullContext),
            ["fractional-yield"] = (Recipe(
                "Half batch",
                yield: Quantity.Amount(Rational.Create(3, 2)),
                yieldUnit: "  ",
                ingredients: [(null, ["1/2 cup oats"])],
                steps: [(null, ["Stir."])]), FullContext),
            ["zero-times"] = (Recipe(
                "Overnight oats",
                ingredients: [(null, ["1/2 cup oats", "1/2 cup milk"])],
                steps: [(null, ["Stir and chill overnight."])],
                prep: 5,
                cook: 0,
                additional: 480), FullContext),
            ["additional-only"] = (Recipe(
                "Fridge pickles",
                steps: [(null, ["Pack the jar and wait."])],
                additional: 2880), FullContext),
            ["cook-only"] = (Recipe("Boiled egg", cook: 9), FullContext),
            ["prep-only"] = (Recipe("Salad", prep: 10), FullContext),
            ["nutrition-without-yield"] = (Recipe(
                "Granola bar",
                nutrition: Nutrition(calories: 21_050, protein: 450)), FullContext),
            ["nutrition-zero-and-serving"] = (Recipe(
                "Lemonade",
                yield: Quantity.Amount(Rational.Create(4, 1)),
                yieldUnit: "glasses",
                nutrition: Nutrition(servingSize: "1 glass (250 ml)", fat: 0, sodium: 1_250, sugar: 2_475)), FullContext),
            ["serving-size-alone"] = (Recipe(
                "Water",
                nutrition: Nutrition(servingSize: "1 glass")), FullContext),
            ["diets"] = (Recipe(
                "Lentil soup",
                diets: ["vegan", "keto", "dairy-free", "gluten-free", "pescatarian"]), FullContext),
            ["keyword-overlap"] = (Recipe(
                "Tiramisu",
                courses: ["dessert"],
                cuisines: ["italian"],
                keywords: ["DESSERT", "italian", "Italian ", "no-bake", "  ", "coffee"]), FullContext),
            ["keywords-all-taken"] = (Recipe(
                "Tiramisu",
                courses: ["dessert"],
                keywords: ["Dessert"]), FullContext),
            ["escaping"] = (Recipe(
                "Crème brûlée </script><script>alert(1)</script> & \"friends\" 'too'",
                summary: "Line" + LineSeparator + "separator, paragraph" + ParagraphSeparator + "separator, a tab\there, and a " + Custard + ".",
                ingredients: [(null, ["2 cups crème fraîche <b>", "1 pinch sel de Guérande & pepper"])],
                steps: [("Bake <now>", ["Heat to 150°C → bake 40 min."])],
                keywords: ["café", "<b>bold</b>"]), FullContext),
            ["video"] = (Recipe("Pancakes", videoUrl: "https://www.youtube.com/watch?v=abc123"), FullContext),
            ["no-url-or-author-url"] = (Recipe("Tea"), new RecipeJsonLdContext(null, ["https://cdn.example.com/tea.jpg"], new RecipeJsonLdAuthor("Sam", null), Published)),
        };

    public static RecipeSnapshot Full() => Recipe(
        "Brown butter apple pie",
        summary: "A deep-dish apple pie with a brown butter crust.",
        yield: Quantity.Amount(Rational.Create(8, 1)),
        yieldUnit: "servings",
        ingredients:
        [
            (null, ["2 1/2 cups all-purpose flour", "1 cup butter, browned and chilled", "6 to 8 tablespoons ice water"]),
            ("Filling", ["3 pounds apples, peeled and sliced", "¾ cup brown sugar", "1 tsp cinnamon", "salt to taste"]),
        ],
        steps:
        [
            (null, ["Brown the butter and chill it.", "Rub it into the flour, then add water until it holds.", "Fill, top, and bake at 425°F for 50 minutes."]),
        ],
        prep: 45,
        cook: 50,
        additional: 240,
        courses: ["dessert"],
        cuisines: ["american"],
        keywords: ["apple pie", "brown butter", "thanksgiving"],
        diets: ["vegetarian"],
        allergens: ["wheat", "milk"],
        equipment: ["9-inch deep-dish pie plate"],
        notes: ["Chill the dough overnight for the flakiest crust."],
        nutrition: Nutrition(servingSize: "1 slice", calories: 41_250, fat: 2_150, saturatedFat: 1_300, cholesterol: 5_500, sodium: 26_000, carbohydrate: 5_400, fiber: 400, sugar: 3_150, protein: 400));

    public static RecipeSnapshot Recipe(
        string title,
        string? summary = null,
        Quantity? yield = null,
        string? yieldUnit = null,
        IReadOnlyList<(string? Heading, string[] Lines)>? ingredients = null,
        IReadOnlyList<(string? Heading, string[] Steps)>? steps = null,
        int? prep = null,
        int? cook = null,
        int? additional = null,
        string[]? courses = null,
        string[]? cuisines = null,
        string[]? keywords = null,
        string[]? diets = null,
        string[]? allergens = null,
        string[]? equipment = null,
        string[]? notes = null,
        string? videoUrl = null,
        RecipeSnapshotNutrition? nutrition = null)
    {
        var id = 0;
        Guid Next() => new($"00000000-0000-4000-8000-{++id:D12}");

        return new RecipeSnapshot(
            new RecipeId(Next()),
            title,
            summary,
            yield,
            yieldUnit,
            prep,
            cook,
            additional,
            additional is null ? null : "Chill",
            videoUrl,
            nutrition ?? Nutrition(),
            [.. (ingredients ?? []).Select(g => new RecipeSnapshotIngredientGroup(
                new RecipeIngredientGroupId(Next()),
                g.Heading,
                [.. g.Lines.Select(text => Line(new RecipeIngredientId(Next()), text))]))],
            [.. (steps ?? []).Select(g => new RecipeSnapshotStepGroup(
                new RecipeStepGroupId(Next()),
                g.Heading,
                [.. g.Steps.Select(text => new RecipeSnapshotStep(new RecipeStepId(Next()), text))]))],
            equipment ?? [],
            notes ?? [],
            keywords ?? [],
            courses ?? [],
            cuisines ?? [],
            diets ?? [],
            allergens ?? []);
    }

    public static RecipeSnapshotNutrition Nutrition(
        string? servingSize = null,
        long? calories = null,
        long? fat = null,
        long? saturatedFat = null,
        long? transFat = null,
        long? unsaturatedFat = null,
        long? cholesterol = null,
        long? sodium = null,
        long? carbohydrate = null,
        long? fiber = null,
        long? sugar = null,
        long? protein = null) =>
        new(servingSize, calories, fat, saturatedFat, transFat, unsaturatedFat, cholesterol, sodium, carbohydrate, fiber, sugar, protein);

    // A stored line, as the parser read it (PP4.5); an unreadable one keeps only its text.
    public static RecipeSnapshotIngredient Line(RecipeIngredientId id, string text)
    {
        var parse = IngredientLineParser.Parse(text);
        return new RecipeSnapshotIngredient(
            id,
            text,
            parse.Status,
            parse.Quantity,
            parse.QuantitySpan is { } q ? new RecipeSnapshotSpan(q.Start, q.Length) : null,
            parse.UnitCode,
            parse.UnitSpan is { } u ? new RecipeSnapshotSpan(u.Start, u.Length) : null,
            parse.Item,
            parse.Note);
    }
}
