using System.Collections.Frozen;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// One diet: its stable code, its display name, the schema.org <c>RestrictedDiet</c> member it means where there is one,
/// and the list version that added it.
/// </summary>
public sealed record DietEntry(string Code, string Name, string? SchemaOrgDiet, int Since);

/// <summary>
/// The diets a blogger can label a recipe with (RCP-001, RCP-006; PP4.1), as platform reference data (PP-D06, ADR 0006).
/// A label appears only when the blogger sets it; PantryPress never infers one. A diet with a schema.org member becomes
/// JSON-LD's <c>suitableForDiet</c> (PP4.10); one without stays on the page only. This list is the one source: the
/// <c>Diets</c> table is seeded from it, so a change here changes the model and needs a reviewed migration. To change it,
/// bump <see cref="Version"/> and give each new entry that version; never remove an entry or change a code, because
/// recipes store the code (PP4.3).
/// </summary>
public static class Diets
{
    /// <summary>The list's version: the highest <see cref="DietEntry.Since"/> in it.</summary>
    public const int Version = 1;

    /// <summary>The longest <see cref="SchemaOrgMembers"/> name, which the column is sized to.</summary>
    public const int SchemaOrgDietMaxLength = 40;

    /// <summary>
    /// Every member of schema.org's <c>RestrictedDiet</c> enumeration (https://schema.org/RestrictedDiet). The column is
    /// checked against this list, so a member nobody defined cannot be stored.
    /// </summary>
    public static FrozenSet<string> SchemaOrgMembers { get; } = new[]
    {
        "DiabeticDiet", "GlutenFreeDiet", "HalalDiet", "HinduDiet", "KosherDiet", "LowCalorieDiet", "LowFatDiet",
        "LowLactoseDiet", "LowSaltDiet", "VeganDiet", "VegetarianDiet",
    }.ToFrozenSet(StringComparer.Ordinal);

    public static IReadOnlyList<DietEntry> All { get; } =
    [
        new("vegetarian", "Vegetarian", "VegetarianDiet", Since: 1),
        new("vegan", "Vegan", "VeganDiet", Since: 1),
        new("pescatarian", "Pescatarian", SchemaOrgDiet: null, Since: 1),
        new("gluten-free", "Gluten-free", "GlutenFreeDiet", Since: 1),
        new("dairy-free", "Dairy-free", SchemaOrgDiet: null, Since: 1),
        new("low-lactose", "Low-lactose", "LowLactoseDiet", Since: 1),
        new("low-fat", "Low-fat", "LowFatDiet", Since: 1),
        new("low-calorie", "Low-calorie", "LowCalorieDiet", Since: 1),
        new("low-salt", "Low-salt", "LowSaltDiet", Since: 1),
        new("diabetic", "Diabetic", "DiabeticDiet", Since: 1),
        new("halal", "Halal", "HalalDiet", Since: 1),
        new("kosher", "Kosher", "KosherDiet", Since: 1),
        new("hindu", "Hindu", "HinduDiet", Since: 1),
        new("keto", "Keto", SchemaOrgDiet: null, Since: 1),
        new("paleo", "Paleo", SchemaOrgDiet: null, Since: 1),
    ];
}
