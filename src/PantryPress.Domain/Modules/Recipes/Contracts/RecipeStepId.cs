namespace PantryPress.Domain.Modules.Recipes.Contracts;

/// <summary>The id of one step of a recipe (RCP-001); backend.md: strongly typed ids.</summary>
public readonly record struct RecipeStepId(Guid Value)
{
    public override string ToString() => Value.ToString("D");
}
