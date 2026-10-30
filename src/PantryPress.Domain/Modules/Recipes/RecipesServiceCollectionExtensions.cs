using Microsoft.Extensions.DependencyInjection;

namespace PantryPress.Domain.Modules.Recipes;

public static class RecipesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Recipes module (PP4.1). So far it holds the recipe vocabulary - units and their spellings, courses,
    /// cuisines, diets, and allergens, platform reference data seeded by migrations - and the recipe tables (PP4.3), all of
    /// them in the context's model and read by nothing yet, so there is nothing to register and no host calls this. The
    /// first repository and facade arrive with recipe revisions and the recipe routes (PP4.4, PP4.11).
    /// </summary>
    public static IServiceCollection AddRecipes(this IServiceCollection services) => services;
}
