using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// The recipe equipment table (PP4.3), keyed by <c>(SiteId, RecipeId, Position)</c>: the key refers to the recipe by site and id, so
/// the database refuses a row under another site's recipe, and the row goes with the recipe. Text is Unicode and stored
/// exactly.
/// </summary>
internal sealed class RecipeEquipmentConfiguration : IEntityTypeConfiguration<RecipeEquipment>
{
    public const string TableName = "RecipeEquipment";
    public const string PositionConstraintName = "CK_RecipeEquipment_Position";
    public const string TextConstraintName = "CK_RecipeEquipment_Text";

    public void Configure(EntityTypeBuilder<RecipeEquipment> builder)
    {
        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(PositionConstraintName, RecipeColumnConfiguration.NonNegative(nameof(RecipeEquipment.Position)));
            table.HasCheckConstraint(TextConstraintName, RecipeColumnConfiguration.NotBlank(nameof(RecipeEquipment.Text)));
        });

        builder.HasKey(e => new { e.SiteId, e.RecipeId, e.Position });
        builder.Property(e => e.RecipeId).HasConversion(RecipeColumnConfiguration.RecipeIdConverter);
        builder.Property(e => e.Position).ValueGeneratedNever();
        builder.Property(e => e.Text).HasMaxLength(RecipeLimits.MaxEquipmentLength);

        builder.HasOne<Recipe>()
            .WithMany()
            .HasForeignKey(e => new { e.SiteId, e.RecipeId })
            .HasPrincipalKey(r => new { r.SiteId, r.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
