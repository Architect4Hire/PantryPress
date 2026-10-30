namespace PantryPress.Domain.Modules.Recipes.Contracts;

/// <summary>The id of one recipe (RCP-001); backend.md: strongly typed ids.</summary>
public readonly record struct RecipeId(Guid Value)
{
    public override string ToString() => Value.ToString("D");
}
