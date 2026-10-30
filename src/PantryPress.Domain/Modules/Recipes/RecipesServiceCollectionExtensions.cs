using Microsoft.Extensions.DependencyInjection;

namespace PantryPress.Domain.Modules.Recipes;

public static class RecipesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Recipes module (PP4.1). So far it holds only the recipe vocabulary - units and their spellings,
    /// courses, cuisines, diets, and allergens - which is platform reference data in the context's model, seeded by
    /// migrations and read by nothing yet, so there is nothing to register and no host calls this. The recipe itself, and
    /// the facades a host will need, arrive with PP4.3.
    /// </summary>
    public static IServiceCollection AddRecipes(this IServiceCollection services) => services;
}
