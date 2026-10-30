namespace PantryPress.Domain.Modules.Recipes.Contracts;

/// <summary>The id of one recipe revision (RCP-007; PP4.4); backend.md: strongly typed ids.</summary>
public readonly record struct RecipeRevisionId(Guid Value)
{
    public override string ToString() => Value.ToString("D");
}
