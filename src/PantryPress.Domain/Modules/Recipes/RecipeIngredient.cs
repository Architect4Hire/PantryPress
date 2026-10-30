using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// One ingredient line (RCP-002; PP-D05; PP4.3). <see cref="OriginalText"/> is the blogger's wording, kept exactly as
/// written - never trimmed, normalized, or made from the parsed parts - and it is what a reader sees. The parsed parts sit
/// beside it and only enrich it: the quantity, the unit's code from the platform's vocabulary (PP4.1), the item, and the
/// note, with where the quantity and the unit are in the text, so that scaling replaces only the quantity's span (PP4.7)
/// and conversion the quantity's and the unit's (PP4.8). A line that cannot be read is kept with none of them and flagged
/// (<see cref="IngredientParseStatus.Unparsed"/>). Site-owned, under its group by <c>(SiteId, GroupId)</c>.
/// </summary>
public sealed class RecipeIngredient : ISiteOwned
{
    private QuantityKind? _quantityKind;
    private long? _quantityLowNumerator;
    private long? _quantityLowDenominator;
    private long? _quantityHighNumerator;
    private long? _quantityHighDenominator;

    public RecipeIngredientId Id { get; init; }

    /// <summary>Stamped by the save from the request's site; never set in code (tenancy.md rule 3).</summary>
    public SiteId SiteId { get; private set; }

    public RecipeIngredientGroupId GroupId { get; init; }

    /// <summary>Where the line comes in its group, from 0.</summary>
    public int Position { get; set; }

    /// <summary>The line exactly as the blogger wrote it.</summary>
    public string OriginalText { get; set; } = string.Empty;

    public IngredientParseStatus ParseStatus { get; set; }

    /// <summary>
    /// The parsed quantity, or none ("salt and pepper"). Its columns are written and read only through
    /// <see cref="StoredQuantity"/>.
    /// </summary>
    public Quantity? Quantity
    {
        get => new StoredQuantity(_quantityKind, _quantityLowNumerator, _quantityLowDenominator, _quantityHighNumerator, _quantityHighDenominator)
            .ToQuantity();
        set => (_quantityKind, _quantityLowNumerator, _quantityLowDenominator, _quantityHighNumerator, _quantityHighDenominator) =
            StoredQuantity.From(value);
    }

    /// <summary>Where the quantity is in <see cref="OriginalText"/>, in UTF-16 code units, as .NET indexes a string.</summary>
    public int? QuantityStart { get; set; }

    public int? QuantityLength { get; set; }

    /// <summary>The unit's vocabulary code (<c>cup</c>, <c>gram</c>), or none ("2 eggs").</summary>
    public string? UnitCode { get; set; }

    /// <summary>Where the unit is in <see cref="OriginalText"/>; every line with a unit has one.</summary>
    public int? UnitStart { get; set; }

    public int? UnitLength { get; set; }

    /// <summary>What the line names ("all-purpose flour"); every parsed line has one.</summary>
    public string? Item { get; set; }

    /// <summary>The line's note ("sifted").</summary>
    public string? Note { get; set; }
}
