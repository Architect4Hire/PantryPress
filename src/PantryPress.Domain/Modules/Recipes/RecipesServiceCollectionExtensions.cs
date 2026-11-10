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
    /// site the request entered. It reads pasted ingredient and step lists for review (PP4.6) through
    /// <see cref="IRecipePasteFacade"/>, which touches no store. The host also registers <c>PantryPressDbContext</c> and the
    /// clock. ApiService is the first host to call this (PP4.6); no route freezes a revision until PP4.11 and PP5.14.
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
        services.AddScoped<IValidator<PasteListRequest>, PasteListRequestValidator>();
        services.AddScoped<IRecipePasteFacade, RecipePasteFacade>();
        return services;
    }
}
