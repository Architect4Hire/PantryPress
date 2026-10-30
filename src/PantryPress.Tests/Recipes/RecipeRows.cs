using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Recipes;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

/// <summary>One recipe with one of every part, as the tests add it.</summary>
internal sealed record RecipeRowSet(
    Recipe Recipe,
    RecipeIngredientGroup IngredientGroup,
    RecipeIngredient Ingredient,
    RecipeStepGroup StepGroup,
    RecipeStep Step,
    RecipeEquipment Equipment,
    RecipeNote Note,
    RecipeKeyword Keyword,
    RecipeCourse Course,
    RecipeCuisine Cuisine,
    RecipeDiet Diet,
    RecipeAllergen Allergen);

/// <summary>
/// A whole, valid recipe for the recipe tables' tests (PP4.3): every part filled, every check met, the site left to the
/// save's stamp. Ids are fresh unless given, so two sites can hold the same ids and still be told apart.
/// </summary>
internal static class RecipeRows
{
    public static readonly DateTimeOffset Created = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);

    public static Recipe NewRecipe(RecipeId? id = null) => new()
    {
        Id = id ?? new RecipeId(Guid.NewGuid()),
        Title = "Apple pie",
        Summary = "A deep-dish pie with a flaky crust.",
        Yield = Quantity.Range(6, 8),
        YieldUnit = "servings",
        PrepMinutes = 45,
        CookMinutes = 60,
        AdditionalMinutes = 120,
        AdditionalTimeLabel = "Cool",
        VideoUrl = "https://www.youtube.com/watch?v=abc123",
        ServingSize = "1 slice",
        Calories = 410.5m,
        FatGrams = 19.25m,
        SodiumMilligrams = 230m,
        ProteinGrams = 0m,
        CreatedAt = Created,
        UpdatedAt = Created,
    };

    public static RecipeIngredientGroup NewIngredientGroup(RecipeId recipe, RecipeIngredientGroupId? id = null) => new()
    {
        Id = id ?? new RecipeIngredientGroupId(Guid.NewGuid()),
        RecipeId = recipe,
        Position = 0,
        Heading = "For the crust",
    };

    /// <summary>"2½ cups all-purpose flour, sifted": the quantity at 0 for 2, the unit at 3 for 4.</summary>
    public static RecipeIngredient NewIngredient(RecipeIngredientGroupId group, RecipeIngredientId? id = null) => new()
    {
        Id = id ?? new RecipeIngredientId(Guid.NewGuid()),
        GroupId = group,
        Position = 0,
        OriginalText = "2½ cups all-purpose flour, sifted",
        ParseStatus = IngredientParseStatus.Parsed,
        Quantity = Quantity.Amount(Rational.Create(5, 2)),
        QuantityStart = 0,
        QuantityLength = 2,
        UnitCode = "cup",
        UnitStart = 3,
        UnitLength = 4,
        Item = "all-purpose flour",
        Note = "sifted",
    };

    public static RecipeStepGroup NewStepGroup(RecipeId recipe, RecipeStepGroupId? id = null) => new()
    {
        Id = id ?? new RecipeStepGroupId(Guid.NewGuid()),
        RecipeId = recipe,
        Position = 0,
        Heading = "Make the crust",
    };

    public static RecipeStep NewStep(RecipeStepGroupId group, RecipeStepId? id = null) => new()
    {
        Id = id ?? new RecipeStepId(Guid.NewGuid()),
        GroupId = group,
        Position = 0,
        Text = "Whisk the flour and salt, then cut in the cold butter.",
    };

    /// <summary>Adds a whole recipe to <paramref name="db"/>; the caller saves, in a scope working on the site.</summary>
    public static RecipeRowSet Add(
        PantryPressDbContext db,
        RecipeId? recipeId = null,
        RecipeIngredientGroupId? ingredientGroupId = null,
        RecipeIngredientId? ingredientId = null,
        RecipeStepGroupId? stepGroupId = null,
        RecipeStepId? stepId = null)
    {
        var recipe = NewRecipe(recipeId);
        var ingredientGroup = NewIngredientGroup(recipe.Id, ingredientGroupId);
        var stepGroup = NewStepGroup(recipe.Id, stepGroupId);
        var rows = new RecipeRowSet(
            recipe,
            ingredientGroup,
            NewIngredient(ingredientGroup.Id, ingredientId),
            stepGroup,
            NewStep(stepGroup.Id, stepId),
            new RecipeEquipment { RecipeId = recipe.Id, Position = 0, Text = "9-inch deep-dish pie plate" },
            new RecipeNote { RecipeId = recipe.Id, Position = 0, Text = "Keeps two days at room temperature." },
            new RecipeKeyword { RecipeId = recipe.Id, Position = 0, Text = "fall baking" },
            new RecipeCourse { RecipeId = recipe.Id, CourseCode = "dessert", Position = 0 },
            new RecipeCuisine { RecipeId = recipe.Id, CuisineCode = "american", Position = 0 },
            new RecipeDiet { RecipeId = recipe.Id, DietCode = "vegetarian", Position = 0 },
            new RecipeAllergen { RecipeId = recipe.Id, AllergenCode = "milk", Position = 0 });

        db.AddRange(
            rows.Recipe, rows.IngredientGroup, rows.Ingredient, rows.StepGroup, rows.Step, rows.Equipment, rows.Note,
            rows.Keyword, rows.Course, rows.Cuisine, rows.Diet, rows.Allergen);
        return rows;
    }
}
