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
    /// <see cref="IRecipePasteFacade"/>, which touches no store, and creates, reads, lists, saves, archives, restores, and
    /// duplicates recipes (PP4.11) through <see cref="IRecipesFacade"/>, which asks Sites the caller's role. The host also
    /// registers <c>PantryPressDbContext</c>, the clock, and the Sites module. ApiService is the first host to call this
    /// (PP4.6); no route freezes a revision until PP5.14.
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
        services.AddScoped<RecipesDataLayer>();
        services.AddScoped<IValidator<RecipeContent>, RecipeContentValidator>();
        services.AddScoped<IValidator<UpdateRecipeRequest>, UpdateRecipeRequestValidator>();
        services.AddScoped<IValidator<ListRecipesQuery>, ListRecipesQueryValidator>();
        services.AddScoped<IRecipesFacade, RecipesFacade>();
        return services;
    }
}
