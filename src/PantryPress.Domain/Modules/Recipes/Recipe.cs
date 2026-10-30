using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// A structured recipe as its blogger edits it (RCP-001, RCP-006; PP-D05; PP4.3). Site-owned and private (PP-D06): the
/// global filter and the save's stamp keep it to its site. These are the live editing tables, which page data never reads
/// (ARCH-004); what a reader sees comes from an immutable published revision (RCP-007, PP4.4).
/// <para>
/// Its parts are rows of their own, each keyed by the site: ingredient groups and their lines, step groups and their steps,
/// equipment, notes, keywords, and the courses, cuisines, diets, and allergens the blogger chose from the platform's
/// vocabulary (PP4.1), held by code under a foreign key. Diets and allergens are only ever what the blogger set (RCP-006);
/// nothing here infers one. Nutrition is per serving, as the blogger entered it, and never estimated. Total time is not
/// stored: it is the sum of the others (PP4.9).
/// </para>
/// </summary>
public sealed class Recipe : ISiteOwned
{
    private QuantityKind? _yieldKind;
    private long? _yieldLowNumerator;
    private long? _yieldLowDenominator;
    private long? _yieldHighNumerator;
    private long? _yieldHighDenominator;

    public RecipeId Id { get; init; }

    /// <summary>Stamped by the save from the request's site; never set in code (tenancy.md rule 3).</summary>
    public SiteId SiteId { get; private set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Plain text, as the blogger wrote it.</summary>
    public string? Summary { get; set; }

    /// <summary>
    /// How much the recipe makes - an amount or a range ("Serves 4–6"), never "to taste" - which scaling starts from
    /// (RCP-004). Its columns are written and read only through <see cref="StoredQuantity"/>.
    /// </summary>
    public Quantity? Yield
    {
        get => new StoredQuantity(_yieldKind, _yieldLowNumerator, _yieldLowDenominator, _yieldHighNumerator, _yieldHighDenominator)
            .ToQuantity();
        set
        {
            if (value is { HasAmount: false })
            {
                throw new ArgumentException("A yield is an amount or a range, never \"to taste\".", nameof(value));
            }

            (_yieldKind, _yieldLowNumerator, _yieldLowDenominator, _yieldHighNumerator, _yieldHighDenominator) = StoredQuantity.From(value);
        }
    }

    /// <summary>The blogger's own word for what the yield counts ("servings", "cookies"), not a unit code.</summary>
    public string? YieldUnit { get; set; }

    public int? PrepMinutes { get; set; }

    public int? CookMinutes { get; set; }

    public int? AdditionalMinutes { get; set; }

    /// <summary>What the additional time is for ("Chill"); shown only beside an additional time.</summary>
    public string? AdditionalTimeLabel { get; set; }

    /// <summary>An HTTPS address of the recipe's video. Which hosts may be embedded is the facade's to check (PP4.11).</summary>
    public string? VideoUrl { get; set; }

    /// <summary>The serving the nutrition is for, in the blogger's words ("1 slice").</summary>
    public string? ServingSize { get; set; }

    /// <summary>Kilocalories per serving.</summary>
    public decimal? Calories { get; set; }

    public decimal? FatGrams { get; set; }

    public decimal? SaturatedFatGrams { get; set; }

    public decimal? TransFatGrams { get; set; }

    public decimal? UnsaturatedFatGrams { get; set; }

    public decimal? CholesterolMilligrams { get; set; }

    public decimal? SodiumMilligrams { get; set; }

    public decimal? CarbohydrateGrams { get; set; }

    public decimal? FiberGrams { get; set; }

    public decimal? SugarGrams { get; set; }

    public decimal? ProteinGrams { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// The concurrency token: the database moves it on every update, and a save from a stale read fails. A change to any
    /// part of the recipe also updates <see cref="UpdatedAt"/>, so this one token guards the whole recipe (PP4.11).
    /// </summary>
    public byte[] Version { get; set; } = [];
}
