using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PantryPress.Domain.Managers.Tenancy;

namespace PantryPress.Tests.Persistence;

// PP2.5: nothing in the product is site-owned yet, so these tests plant two site-owned types in the real
// PantryPressDbContext through a replacement model customizer: the production filter convention, interceptors, and
// registration run unchanged over them. Replacing a service gives the context its own internal services, so the planted
// model never mixes with the one the hosts build. SiteId has a public setter here only so a test can play the bug that
// sets it by hand.
internal sealed class ProbeRecipe : ISiteOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public SiteId SiteId { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<ProbeStep> Steps { get; } = [];
}

internal sealed class ProbeStep : ISiteOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public SiteId SiteId { get; set; }

    public Guid RecipeId { get; set; }

    public string Text { get; set; } = string.Empty;
}

internal sealed class SiteOwnedProbeCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.Entity<ProbeRecipe>(recipe =>
        {
            recipe.ToTable("ProbeRecipes", "probe");
            recipe.Property(r => r.Name).HasMaxLength(100);
            recipe.HasIndex(r => new { r.SiteId, r.Name });

            // A reference between site-owned rows is keyed by site, so the database refuses a step under another site's
            // recipe (PP2.5).
            recipe.HasMany(r => r.Steps).WithOne()
                .HasForeignKey(s => new { s.SiteId, s.RecipeId })
                .HasPrincipalKey(r => new { r.SiteId, r.Id });
        });
        modelBuilder.Entity<ProbeStep>(step =>
        {
            step.ToTable("ProbeSteps", "probe");
            step.Property(s => s.Text).HasMaxLength(100);
            step.HasIndex(s => new { s.SiteId, s.RecipeId });
        });
    }
}
