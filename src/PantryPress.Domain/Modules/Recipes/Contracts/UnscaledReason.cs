namespace PantryPress.Domain.Modules.Recipes.Contracts;

/// <summary>
/// Why an ingredient line is shown exactly as written at a scale other than 1× (RCP-004; PP4.7), so a reader can be told
/// the line was not scaled. A line with nothing to scale - "salt", "to taste", a temperature - is shown as written with no
/// reason at all. Never stored; page data will carry it (PP7.11), and enums are written as kebab-case names
/// (api-contract.md), so from then on renaming a member is a breaking change.
/// </summary>
public enum UnscaledReason
{
    /// <summary>The parser could not read the line (<see cref="IngredientParseStatus.Unparsed"/>).</summary>
    Unparsed,

    /// <summary>
    /// The line has a number outside its quantity that scaling cannot account for - an equivalent measure
    /// (<c>1 cup (120 g) flour</c>), a count in the item (<c>Juice of 2 lemons</c>), or a second amount in the note - so
    /// scaling the quantity alone would be wrong. A size between the quantity and the unit or item
    /// (<c>1 (14 oz) can</c>, <c>1 9-inch pie crust</c>) is per piece, and is not one of these, unless its fraction would
    /// join the new amount into another number (<c>a ½-inch piece</c> doubled reads <c>2 ½-inch</c>, which is 2½).
    /// </summary>
    OtherAmounts,
}
