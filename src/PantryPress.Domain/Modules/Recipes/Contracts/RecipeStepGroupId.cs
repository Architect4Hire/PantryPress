namespace PantryPress.Domain.Modules.Recipes.Contracts;

/// <summary>The id of one group of a recipe's steps (RCP-001); backend.md: strongly typed ids.</summary>
public readonly record struct RecipeStepGroupId(Guid Value)
{
    public override string ToString() => Value.ToString("D");
}
