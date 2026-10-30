namespace PantryPress.Domain.Modules.Recipes.Contracts;

/// <summary>The id of one ingredient line of a recipe (RCP-002); backend.md: strongly typed ids.</summary>
public readonly record struct RecipeIngredientId(Guid Value)
{
    public override string ToString() => Value.ToString("D");
}
