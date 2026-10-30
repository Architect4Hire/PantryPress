using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Recipes;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.MigrationService;

namespace PantryPress.Tests.Recipes;

// PP4.4: the recipe revisions table in the design-time model. Its site, key, checks, and parent are in RecipeModelTests'
// shared cases; these are what a revision adds - written once, with a trigger behind it, numbered one by one, and holding
// the whole snapshot - so no database is needed. The migrated schema's refusals are RecipeRevisionSchemaTests'.
public sealed class RecipeRevisionModelTests
{
    [Fact]
    public void A_revision_is_site_owned_and_append_only_with_a_trigger_behind_it()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var entity = Entity(db);

        Assert.True(typeof(IAppendOnly).IsAssignableFrom(typeof(RecipeRevision)));
        Assert.Equal(RecipeRevisionConfiguration.ImmutableTriggerName, Assert.Single(entity.GetDeclaredTriggers()).GetDatabaseName());
    }

    // One of each number per recipe, and a recipe's latest of a kind read backwards along the second; both led by the site.
    [Fact]
    public void Revisions_are_numbered_once_each_per_recipe()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var indexes = Entity(db).GetIndexes().ToDictionary(i => i.GetDatabaseName()!);

        var number = indexes[RecipeRevisionConfiguration.NumberIndexName];
        Assert.True(number.IsUnique);
        Assert.Equal(["SiteId", "RecipeId", "Number"], number.Properties.Select(p => p.Name));

        var kind = indexes[RecipeRevisionConfiguration.KindIndexName];
        Assert.False(kind.IsUnique);
        Assert.Equal(["SiteId", "RecipeId", "Kind", "Number"], kind.Properties.Select(p => p.Name));
        Assert.Equal(2, indexes.Count);
    }

    [Fact]
    public void The_snapshot_is_whole_and_the_hash_is_its_sha256()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var entity = Entity(db);

        var snapshot = entity.FindProperty(nameof(RecipeRevision.Snapshot))!;
        Assert.Equal("nvarchar(max)", snapshot.GetColumnType());
        Assert.False(snapshot.IsNullable);

        var hash = entity.FindProperty(nameof(RecipeRevision.ContentHash))!;
        Assert.Equal("binary(32)", hash.GetColumnType());
        Assert.Equal(32, hash.GetMaxLength());
        Assert.True(hash.IsFixedLength());
        Assert.False(hash.IsNullable);
    }

    // Nothing about a revision is generated or moved by the database: it has no row version, because it never changes, and
    // its id is minted by the facade before the save, so a retried save meets its own row.
    [Fact]
    public void Nothing_is_generated_and_nothing_is_a_concurrency_token_but_the_site()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var entity = Entity(db);

        Assert.All(entity.GetProperties(), p => Assert.Equal(ValueGenerated.Never, p.ValueGenerated));
        Assert.Equal(["SiteId"], entity.GetProperties().Where(p => p.IsConcurrencyToken).Select(p => p.Name));
    }

    // What the working copy holds that a revision does not: the site, the keys that make the nesting, the positions that
    // make the order, and the recipe's own bookkeeping.
    private static readonly string[] NotFrozen = ["SiteId", "RecipeId", "GroupId", "Position", "CreatedAt", "UpdatedAt", "Version"];

    // Every column the snapshot freezes, by table. A quantity's are its five field-mapped columns.
    public static TheoryData<Type, string[]> Frozen => new()
    {
        {
            typeof(Recipe),
            [
                "Id", "Title", "Summary", .. QuantityFields("_yield"), "YieldUnit", "PrepMinutes", "CookMinutes", "AdditionalMinutes",
                "AdditionalTimeLabel", "VideoUrl", "ServingSize", "Calories", "FatGrams", "SaturatedFatGrams", "TransFatGrams",
                "UnsaturatedFatGrams", "CholesterolMilligrams", "SodiumMilligrams", "CarbohydrateGrams", "FiberGrams", "SugarGrams",
                "ProteinGrams",
            ]
        },
        { typeof(RecipeIngredientGroup), ["Id", "Heading"] },
        {
            typeof(RecipeIngredient),
            [
                "Id", "OriginalText", "ParseStatus", .. QuantityFields("_quantity"), "QuantityStart", "QuantityLength", "UnitCode",
                "UnitStart", "UnitLength", "Item", "Note",
            ]
        },
        { typeof(RecipeStepGroup), ["Id", "Heading"] },
        { typeof(RecipeStep), ["Id", "Text"] },
        { typeof(RecipeEquipment), ["Text"] },
        { typeof(RecipeNote), ["Text"] },
        { typeof(RecipeKeyword), ["Text"] },
        { typeof(RecipeCourse), ["CourseCode"] },
        { typeof(RecipeCuisine), ["CuisineCode"] },
        { typeof(RecipeDiet), ["DietCode"] },
        { typeof(RecipeAllergen), ["AllergenCode"] },
    };

    // From review (test-gap-analyzer): a revision freezes the whole recipe, and is never rewritten, so a column added to the
    // working copy and left out of the snapshot would be lost from every revision for good. Each table's columns are either
    // frozen or not frozen on purpose: a new one fails here until it is put in one list - and, if frozen, in the snapshot,
    // its writer, its reader, and the working copy's mapping - on purpose.
    [Theory]
    [MemberData(nameof(Frozen))]
    public void Every_working_copy_column_is_frozen_or_left_out_on_purpose(Type type, string[] frozen)
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var columns = db.GetService<IDesignTimeModel>().Model.FindEntityType(type)!.GetProperties().Select(p => p.Name);

        Assert.Equal(frozen.Order(StringComparer.Ordinal), columns.Except(NotFrozen).Order(StringComparer.Ordinal));
    }

    // Every table of the working copy is in the list above: the recipe and the eleven tables keyed to it.
    [Fact]
    public void Every_working_copy_table_is_listed()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var tables = db.GetService<IDesignTimeModel>().Model.GetEntityTypes()
            .Where(e => e.ClrType.Namespace == typeof(Recipe).Namespace)
            .Where(e => typeof(PantryPress.Domain.Managers.Tenancy.ISiteOwned).IsAssignableFrom(e.ClrType) && e.ClrType != typeof(RecipeRevision))
            .Select(e => e.ClrType.Name);

        Assert.Equal(
            Frozen.Select(row => ((Type)row[0]).Name).Order(StringComparer.Ordinal),
            tables.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_kind_numbers_are_pinned()
    {
        Assert.Equal(1, (int)RecipeRevisionKind.Draft);
        Assert.Equal(2, (int)RecipeRevisionKind.Published);
        Assert.Equal(2, Enum.GetValues<RecipeRevisionKind>().Length);
    }

    private static IEnumerable<string> QuantityFields(string field) =>
        new[] { "Kind", "LowNumerator", "LowDenominator", "HighNumerator", "HighDenominator" }.Select(part => field + part);

    private static IEntityType Entity(DbContext db) => db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(RecipeRevision))!;
}
