namespace PantryPress.Domain.Modules.Recipes.Contracts;

/// <summary>
/// The nutrients a blogger may enter per serving (RCP-006; PP4.3), in the order a US nutrition label lists them, which is
/// the order they are shown in (PP4.9). Each is only ever what the blogger entered: nothing here is estimated or derived.
/// Never stored by this name - each has its own column - but page data will carry it (PP7.11), and enums are written as
/// kebab-case names (api-contract.md), so from then on renaming a member is a breaking change.
/// </summary>
public enum Nutrient
{
    /// <summary>Kilocalories.</summary>
    Calories,

    /// <summary>Total fat, in grams.</summary>
    Fat,

    /// <summary>Grams.</summary>
    SaturatedFat,

    /// <summary>Grams.</summary>
    TransFat,

    /// <summary>Grams, as entered; never worked out from fat less saturated and trans fat.</summary>
    UnsaturatedFat,

    /// <summary>Milligrams.</summary>
    Cholesterol,

    /// <summary>Milligrams.</summary>
    Sodium,

    /// <summary>Total carbohydrate, in grams.</summary>
    Carbohydrate,

    /// <summary>Grams.</summary>
    Fiber,

    /// <summary>Grams.</summary>
    Sugar,

    /// <summary>Grams.</summary>
    Protein,
}
