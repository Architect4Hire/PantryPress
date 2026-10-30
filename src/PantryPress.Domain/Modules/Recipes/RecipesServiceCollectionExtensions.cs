using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.Domain.Modules.Recipes.DataLayer;
using PantryPress.Domain.Modules.Recipes.Facades;
using PantryPress.Domain.Modules.Recipes.Repositories;
using PantryPress.Domain.Modules.Recipes.Validation;

namespace PantryPress.Domain.Modules.Recipes;

public static class RecipesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Recipes module (PP4.1). It holds the recipe vocabulary - units and their spellings, courses, cuisines,
    /// diets, and allergens, platform reference data seeded by migrations - and the recipe tables (PP4.3), the working copy
    /// a blogger edits, and freezes a recipe into a revision (PP4.4) through <see cref="IRecipeRevisionsFacade"/>, on the
    /// site the request entered. The host also registers <c>PantryPressDbContext</c> and the clock. No host calls this yet:
    /// the recipe routes (PP4.11) and publishing (PP5.14) are the first that will.
    /// </summary>
    public static IServiceCollection AddRecipes(this IServiceCollection services)
    {
        services.AddSiteContext();
        services.AddScoped<RecipeRepository>();
        services.AddScoped<RecipeRevisionRepository>();
        services.AddScoped<RecipesUnitOfWork>();
        services.AddScoped<RecipeRevisionsDataLayer>();
        services.AddScoped<IValidator<CreateRecipeRevisionRequest>, CreateRecipeRevisionRequestValidator>();
        services.AddScoped<IRecipeRevisionsFacade, RecipeRevisionsFacade>();
        return services;
    }
}
