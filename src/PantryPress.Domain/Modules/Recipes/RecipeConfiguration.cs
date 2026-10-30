using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// The recipes table (PP4.3). The site leads the key, and the filter and stamp come from <c>ISiteOwned</c> by convention.
/// No foreign key to the sites table: a module never names another's entities, and a site is never deleted, only marked
/// deleted (PP2.2), so the stamp from a resolved site is what keeps a recipe's site real.
/// </summary>
internal sealed class RecipeConfiguration : IEntityTypeConfiguration<Recipe>
{
    public const string TableName = "Recipes";
    public const string YieldPrefix = "Yield";
    public const string TitleConstraintName = "CK_Recipes_Title";
    public const string TextConstraintName = "CK_Recipes_Text";
    public const string YieldUnitConstraintName = "CK_Recipes_YieldUnit";
    public const string TimesConstraintName = "CK_Recipes_Times";
    public const string AdditionalTimeLabelConstraintName = "CK_Recipes_AdditionalTimeLabel";
    public const string VideoUrlConstraintName = "CK_Recipes_VideoUrl";
    public const string NutritionConstraintName = "CK_Recipes_Nutrition";
    public const string UpdatedAtConstraintName = "CK_Recipes_UpdatedAt";

    private const string Https = "https://";

    private static readonly string[] Times = [nameof(Recipe.PrepMinutes), nameof(Recipe.CookMinutes), nameof(Recipe.AdditionalMinutes)];

    private static readonly string[] Nutrients =
    [
        nameof(Recipe.Calories),
        nameof(Recipe.FatGrams),
        nameof(Recipe.SaturatedFatGrams),
        nameof(Recipe.TransFatGrams),
        nameof(Recipe.UnsaturatedFatGrams),
        nameof(Recipe.CholesterolMilligrams),
        nameof(Recipe.SodiumMilligrams),
        nameof(Recipe.CarbohydrateGrams),
        nameof(Recipe.FiberGrams),
        nameof(Recipe.SugarGrams),
        nameof(Recipe.ProteinGrams),
    ];

    public void Configure(EntityTypeBuilder<Recipe> builder)
    {
        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(TitleConstraintName, RecipeColumnConfiguration.NotBlank(nameof(Recipe.Title)));
            table.HasCheckConstraint(
                TextConstraintName,
                string.Join(
                    " AND ",
                    new[] { nameof(Recipe.Summary), nameof(Recipe.ServingSize) }
                        .Select(c => $"({RecipeColumnConfiguration.NotBlank(c, nullable: true)})")));

            RecipeColumnConfiguration.Quantity(
                builder,
                table,
                TableName,
                YieldPrefix,
                [QuantityKind.Amount, QuantityKind.Range]);

            // The blogger's word for what the yield counts goes only with a yield.
            table.HasCheckConstraint(
                YieldUnitConstraintName,
                $"[{nameof(Recipe.YieldUnit)}] IS NULL OR ([{YieldPrefix}Kind] IS NOT NULL AND LEN([{nameof(Recipe.YieldUnit)}]) > 0)");

            table.HasCheckConstraint(
                TimesConstraintName,
                string.Join(" AND ", Times.Select(c => $"([{c}] IS NULL OR [{c}] BETWEEN 0 AND {RecipeLimits.MaxMinutes})")));
            table.HasCheckConstraint(
                AdditionalTimeLabelConstraintName,
                $"[{nameof(Recipe.AdditionalTimeLabel)}] IS NULL "
                + $"OR ([{nameof(Recipe.AdditionalMinutes)}] IS NOT NULL AND LEN([{nameof(Recipe.AdditionalTimeLabel)}]) > 0)");

            // HTTPS only, spelled exactly, with something after it. Which hosts may be embedded is the facade's (PP4.11).
            table.HasCheckConstraint(
                VideoUrlConstraintName,
                $"[{nameof(Recipe.VideoUrl)}] IS NULL OR (LEN([{nameof(Recipe.VideoUrl)}]) > {Https.Length} "
                + $"AND LEFT([{nameof(Recipe.VideoUrl)}], {Https.Length}) = '{Https}' COLLATE {VocabularyColumnConfiguration.BinaryCollation})");

            table.HasCheckConstraint(
                NutritionConstraintName,
                string.Join(" AND ", Nutrients.Select(c => $"([{c}] IS NULL OR [{c}] >= 0)")));
            table.HasCheckConstraint(
                UpdatedAtConstraintName,
                $"[{nameof(Recipe.UpdatedAt)}] >= [{nameof(Recipe.CreatedAt)}]");
        });

        builder.HasKey(r => new { r.SiteId, r.Id });
        builder.Property(r => r.Id).HasConversion(RecipeColumnConfiguration.RecipeIdConverter).ValueGeneratedNever();
        builder.Ignore(r => r.Yield);

        builder.Property(r => r.Title).HasMaxLength(RecipeLimits.MaxTitleLength);
        builder.Property(r => r.Summary).HasMaxLength(RecipeLimits.MaxSummaryLength);
        builder.Property(r => r.YieldUnit).HasMaxLength(RecipeLimits.MaxYieldUnitLength);
        builder.Property(r => r.AdditionalTimeLabel).HasMaxLength(RecipeLimits.MaxAdditionalTimeLabelLength);
        builder.Property(r => r.VideoUrl).HasMaxLength(RecipeLimits.MaxVideoUrlLength);
        builder.Property(r => r.ServingSize).HasMaxLength(RecipeLimits.MaxServingSizeLength);

        foreach (var nutrient in Nutrients)
        {
            builder.Property(nutrient).HasPrecision(RecipeLimits.NutritionPrecision, RecipeLimits.NutritionScale);
        }

        builder.Property(r => r.Version).IsRowVersion();
    }
}
