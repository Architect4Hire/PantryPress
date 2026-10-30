using FluentValidation;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Validation;

internal sealed class CreateRecipeRevisionRequestValidator : AbstractValidator<CreateRecipeRevisionRequest>
{
    public CreateRecipeRevisionRequestValidator()
    {
        RuleFor(r => r.RecipeId)
            .Must(id => id.Value != Guid.Empty)
            .WithMessage("Choose a recipe.");
        RuleFor(r => r.Kind)
            .Must(kind => Enum.IsDefined(kind))
            .WithMessage("Choose whether this is a draft or what readers will see.");
        RuleFor(r => r.ExpectedVersion)
            .Must(version => version is { Length: RecipeRevisionRules.VersionLength })
            .WithMessage("Reload the recipe and try again.");
    }
}
