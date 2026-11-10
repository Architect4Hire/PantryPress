using FluentValidation;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Validation;

/// <summary>
/// A paste to read (PP4.6): something to read, and no more than one request reads at once. Only the size is checked here;
/// what each line holds is the reader's to flag, line by line, never a reason to refuse the paste.
/// </summary>
internal sealed class PasteListRequestValidator : AbstractValidator<PasteListRequest>
{
    public PasteListRequestValidator()
    {
        RuleFor(r => r.Text)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("Paste your list first.")
            .Must(text => text!.Length <= RecipeLimits.MaxPastedTextLength)
            .WithMessage("That's more than we can read at once. Paste a shorter list.")
            .Custom((text, context) =>
            {
                // Counted once: splitting reads every character of the paste.
                var lines = PastedListReader.Lines(text!).Count;
                if (lines == 0)
                {
                    context.AddFailure("Paste your list first.");
                }
                else if (lines > RecipeLimits.MaxPastedLines)
                {
                    context.AddFailure($"That's more than we can read at once. Paste up to {RecipeLimits.MaxPastedLines} lines.");
                }
            });
    }
}

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
