namespace PantryPress.Domain.Modules.Recipes.Contracts;

/// <summary>The id of one group of a recipe's ingredients (RCP-001); backend.md: strongly typed ids.</summary>
public readonly record struct RecipeIngredientGroupId(Guid Value)
{
    public override string ToString() => Value.ToString("D");
}
