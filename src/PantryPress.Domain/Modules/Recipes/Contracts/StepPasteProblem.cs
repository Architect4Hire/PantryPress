namespace PantryPress.Domain.Modules.Recipes.Contracts;

/// <summary>
/// Why a step read from a paste (RCP-003; PP4.6) cannot be kept as it is, so the blogger can fix or remove it before saving.
/// The step is still proposed, exactly as pasted, never dropped. Returned as kebab-case names (api-contract.md), so renaming a
/// member is a breaking change; never stored.
/// </summary>
public enum StepPasteProblem
{
    /// <summary>The line is only a step's number or bullet (<c>Step 1</c>, <c>2.</c>), with nothing to do after it.</summary>
    NoText,

    /// <summary>The step is longer than <see cref="RecipeLimits.MaxStepTextLength"/>.</summary>
    TooLong,

    /// <summary>
    /// The line holds a control character, or a broken character (half of a surrogate pair). Over the API only the first
    /// arrives: a broken character is refused with the request, before anything is read.
    /// </summary>
    IllFormedText,
}
