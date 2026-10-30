using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Recipes;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.MigrationService;

namespace PantryPress.Tests.Recipes;

// PP4.3: the recipe tables are site-owned and private (PP-D06). These read the design-time model - which keeps collations,
// check constraints, and keys - so no database is needed; the migrated schema's refusals are RecipeSchemaTests'.
public sealed class RecipeModelTests
{
    public static TheoryData<Type, string, string[]> Tables => new()
    {
        {
            typeof(Recipe), RecipeConfiguration.TableName,
            [
                RecipeConfiguration.TitleConstraintName, RecipeConfiguration.TextConstraintName,
                RecipeConfiguration.YieldUnitConstraintName, RecipeConfiguration.TimesConstraintName,
                RecipeConfiguration.AdditionalTimeLabelConstraintName, RecipeConfiguration.VideoUrlConstraintName,
                RecipeConfiguration.NutritionConstraintName, RecipeConfiguration.UpdatedAtConstraintName,
                .. QuantityConstraints(RecipeConfiguration.TableName, RecipeConfiguration.YieldPrefix),
            ]
        },
        {
            typeof(RecipeIngredientGroup), RecipeIngredientGroupConfiguration.TableName,
            [RecipeIngredientGroupConfiguration.PositionConstraintName, RecipeIngredientGroupConfiguration.HeadingConstraintName]
        },
        {
            typeof(RecipeIngredient), RecipeIngredientConfiguration.TableName,
            [
                RecipeIngredientConfiguration.PositionConstraintName, RecipeIngredientConfiguration.OriginalTextConstraintName,
                RecipeIngredientConfiguration.ParseStatusConstraintName, RecipeIngredientConfiguration.UnparsedConstraintName,
                RecipeIngredientConfiguration.ParsedConstraintName, RecipeIngredientConfiguration.UnitConstraintName,
                RecipeIngredientConfiguration.QuantitySpanConstraintName, RecipeIngredientConfiguration.UnitSpanConstraintName,
                RecipeIngredientConfiguration.SpansApartConstraintName,
                .. QuantityConstraints(RecipeIngredientConfiguration.TableName, RecipeIngredientConfiguration.QuantityPrefix),
            ]
        },
        {
            typeof(RecipeStepGroup), RecipeStepGroupConfiguration.TableName,
            [RecipeStepGroupConfiguration.PositionConstraintName, RecipeStepGroupConfiguration.HeadingConstraintName]
        },
        {
            typeof(RecipeStep), RecipeStepConfiguration.TableName,
            [RecipeStepConfiguration.PositionConstraintName, RecipeStepConfiguration.TextConstraintName]
        },
        {
            typeof(RecipeEquipment), RecipeEquipmentConfiguration.TableName,
            [RecipeEquipmentConfiguration.PositionConstraintName, RecipeEquipmentConfiguration.TextConstraintName]
        },
        {
            typeof(RecipeNote), RecipeNoteConfiguration.TableName,
            [RecipeNoteConfiguration.PositionConstraintName, RecipeNoteConfiguration.TextConstraintName]
        },
        {
            typeof(RecipeKeyword), RecipeKeywordConfiguration.TableName,
            [RecipeKeywordConfiguration.PositionConstraintName, RecipeKeywordConfiguration.TextConstraintName]
        },
        { typeof(RecipeCourse), RecipeCourseConfiguration.TableName, [RecipeCourseConfiguration.PositionConstraintName] },
        { typeof(RecipeCuisine), RecipeCuisineConfiguration.TableName, [RecipeCuisineConfiguration.PositionConstraintName] },
        { typeof(RecipeDiet), RecipeDietConfiguration.TableName, [RecipeDietConfiguration.PositionConstraintName] },
        { typeof(RecipeAllergen), RecipeAllergenConfiguration.TableName, [RecipeAllergenConfiguration.PositionConstraintName] },
    };

    // Each part's key, and the parent it hangs off by (SiteId, id), so the database refuses a part under another site's row.
    public static TheoryData<Type, string[], Type, DeleteBehavior> Parents => new()
    {
        { typeof(RecipeIngredientGroup), ["SiteId", "RecipeId"], typeof(Recipe), DeleteBehavior.Cascade },
        { typeof(RecipeIngredient), ["SiteId", "GroupId"], typeof(RecipeIngredientGroup), DeleteBehavior.Cascade },
        { typeof(RecipeStepGroup), ["SiteId", "RecipeId"], typeof(Recipe), DeleteBehavior.Cascade },
        { typeof(RecipeStep), ["SiteId", "GroupId"], typeof(RecipeStepGroup), DeleteBehavior.Cascade },
        { typeof(RecipeEquipment), ["SiteId", "RecipeId"], typeof(Recipe), DeleteBehavior.Cascade },
        { typeof(RecipeNote), ["SiteId", "RecipeId"], typeof(Recipe), DeleteBehavior.Cascade },
        { typeof(RecipeKeyword), ["SiteId", "RecipeId"], typeof(Recipe), DeleteBehavior.Cascade },
        { typeof(RecipeCourse), ["SiteId", "RecipeId"], typeof(Recipe), DeleteBehavior.Cascade },
        { typeof(RecipeCuisine), ["SiteId", "RecipeId"], typeof(Recipe), DeleteBehavior.Cascade },
        { typeof(RecipeDiet), ["SiteId", "RecipeId"], typeof(Recipe), DeleteBehavior.Cascade },
        { typeof(RecipeAllergen), ["SiteId", "RecipeId"], typeof(Recipe), DeleteBehavior.Cascade },
    };

    // A code that refers to the platform's vocabulary (PP4.1), and the site-led index that serves a read by it, if any.
    public static TheoryData<Type, string, Type, string[]?> VocabularyReferences => new()
    {
        { typeof(RecipeIngredient), nameof(RecipeIngredient.UnitCode), typeof(Unit), null },
        { typeof(RecipeCourse), nameof(RecipeCourse.CourseCode), typeof(Course), ["SiteId", nameof(RecipeCourse.CourseCode)] },
        { typeof(RecipeCuisine), nameof(RecipeCuisine.CuisineCode), typeof(Cuisine), ["SiteId", nameof(RecipeCuisine.CuisineCode)] },
        { typeof(RecipeDiet), nameof(RecipeDiet.DietCode), typeof(Diet), ["SiteId", nameof(RecipeDiet.DietCode)] },
        { typeof(RecipeAllergen), nameof(RecipeAllergen.AllergenCode), typeof(Allergen), ["SiteId", nameof(RecipeAllergen.AllergenCode)] },
    };

    [Theory]
    [MemberData(nameof(Tables))]
    public void Each_table_is_site_owned_and_led_by_the_site(Type type, string table, string[] constraints)
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var entity = Entity(db, type);

        Assert.Equal(table, entity.GetTableName());
        Assert.True(typeof(ISiteOwned).IsAssignableFrom(type));
        Assert.NotNull(entity.FindDeclaredQueryFilter(SiteQueryFilterConvention.FilterName));
        Assert.Equal(nameof(ISiteOwned.SiteId), entity.FindPrimaryKey()!.Properties[0].Name);
        Assert.All(entity.GetIndexes(), index => Assert.Equal(nameof(ISiteOwned.SiteId), index.Properties[0].Name));
        Assert.Equal(constraints.Order(StringComparer.Ordinal), entity.GetCheckConstraints().Select(c => c.Name!).Order(StringComparer.Ordinal));

        // The tenant root is the Sites module's: no recipe table names it (PP-D03; owner-approved, PP4.3).
        Assert.DoesNotContain(entity.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType.Name == "Site");
    }

    [Theory]
    [MemberData(nameof(Parents))]
    public void Each_part_hangs_off_its_parent_by_site_and_id(Type type, string[] properties, Type parent, DeleteBehavior delete)
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var reference = Assert.Single(Entity(db, type).GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == parent);

        Assert.Equal(properties, reference.Properties.Select(p => p.Name));
        Assert.Equal(["SiteId", "Id"], reference.PrincipalKey.Properties.Select(p => p.Name));
        Assert.True(reference.PrincipalKey.IsPrimaryKey());
        Assert.Equal(delete, reference.DeleteBehavior);
    }

    // The code matches the vocabulary's column exactly - ASCII, binary - so the foreign key compares byte for byte; it has no
    // navigation, which the reference-data guard would meet; and no index that does not lead with the site.
    [Theory]
    [MemberData(nameof(VocabularyReferences))]
    public void A_vocabulary_code_is_held_by_foreign_key(Type type, string property, Type vocabulary, string[]? covering)
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var entity = Entity(db, type);
        var code = entity.FindProperty(property)!;
        var target = Entity(db, vocabulary).FindPrimaryKey()!.Properties.Single();

        Assert.Equal(target.GetMaxLength(), code.GetMaxLength());
        Assert.Equal(target.IsUnicode(), code.IsUnicode());
        Assert.False(code.IsUnicode());
        Assert.Equal(target.GetCollation(), code.GetCollation());

        var reference = Assert.Single(entity.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == vocabulary);
        Assert.Equal([property], reference.Properties.Select(p => p.Name));
        Assert.Equal(DeleteBehavior.Restrict, reference.DeleteBehavior);
        Assert.Null(reference.DependentToPrincipal);
        Assert.Null(reference.PrincipalToDependent);

        Assert.DoesNotContain(entity.GetIndexes(), index => index.Properties[0].Name == property);
        if (covering is not null)
        {
            Assert.Contains(entity.GetIndexes(), index => index.Properties.Select(p => p.Name).SequenceEqual(covering));
        }
    }

    // Diets and allergens are what the blogger set, and nothing else (RCP-006): a join row per choice, and no column that
    // could derive one.
    [Fact]
    public void A_recipe_holds_no_diet_or_allergen_of_its_own()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var recipe = Entity(db, typeof(Recipe));

        Assert.DoesNotContain(
            recipe.GetProperties(),
            p => p.Name.Contains("Diet", StringComparison.Ordinal) || p.Name.Contains("Allergen", StringComparison.Ordinal));
        Assert.DoesNotContain(recipe.GetProperties(), p => p.GetComputedColumnSql() is not null);
    }

    // The quantity's five columns are the entity's private fields, behind StoredQuantity; the Quantity and Yield properties
    // are not mapped, so EF Core never builds one past Rational.Create (PP4.2's hand-off).
    [Theory]
    [InlineData(typeof(Recipe), "Yield", "_yield")]
    [InlineData(typeof(RecipeIngredient), "Quantity", "_quantity")]
    public void A_quantity_is_stored_as_exact_columns_behind_its_fields(Type type, string prefix, string field)
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var entity = Entity(db, type);

        Assert.Null(entity.FindProperty(prefix));
        foreach (var (part, clr) in new[]
        {
            ("Kind", typeof(QuantityKind?)), ("LowNumerator", typeof(long?)), ("LowDenominator", typeof(long?)),
            ("HighNumerator", typeof(long?)), ("HighDenominator", typeof(long?)),
        })
        {
            var property = entity.FindProperty(field + part)!;
            Assert.Equal(prefix + part, property.GetColumnName());
            Assert.Equal(clr, property.ClrType);
            Assert.True(property.IsNullable);
            Assert.Null(property.PropertyInfo);
            Assert.NotNull(property.FieldInfo);
        }
    }

    [Fact]
    public void Text_is_unicode_and_sized_by_the_limits()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        AssertText(db, typeof(Recipe), nameof(Recipe.Title), RecipeLimits.MaxTitleLength, nullable: false);
        AssertText(db, typeof(Recipe), nameof(Recipe.Summary), RecipeLimits.MaxSummaryLength, nullable: true);
        AssertText(db, typeof(Recipe), nameof(Recipe.YieldUnit), RecipeLimits.MaxYieldUnitLength, nullable: true);
        AssertText(db, typeof(Recipe), nameof(Recipe.AdditionalTimeLabel), RecipeLimits.MaxAdditionalTimeLabelLength, nullable: true);
        AssertText(db, typeof(Recipe), nameof(Recipe.VideoUrl), RecipeLimits.MaxVideoUrlLength, nullable: true);
        AssertText(db, typeof(Recipe), nameof(Recipe.ServingSize), RecipeLimits.MaxServingSizeLength, nullable: true);
        AssertText(db, typeof(RecipeIngredientGroup), nameof(RecipeIngredientGroup.Heading), RecipeLimits.MaxGroupHeadingLength, nullable: true);
        AssertText(db, typeof(RecipeIngredient), nameof(RecipeIngredient.OriginalText), RecipeLimits.MaxIngredientTextLength, nullable: false);
        AssertText(db, typeof(RecipeIngredient), nameof(RecipeIngredient.Item), RecipeLimits.MaxIngredientItemLength, nullable: true);
        AssertText(db, typeof(RecipeIngredient), nameof(RecipeIngredient.Note), RecipeLimits.MaxIngredientNoteLength, nullable: true);
        AssertText(db, typeof(RecipeStepGroup), nameof(RecipeStepGroup.Heading), RecipeLimits.MaxGroupHeadingLength, nullable: true);
        AssertText(db, typeof(RecipeStep), nameof(RecipeStep.Text), RecipeLimits.MaxStepTextLength, nullable: false);
        AssertText(db, typeof(RecipeEquipment), nameof(RecipeEquipment.Text), RecipeLimits.MaxEquipmentLength, nullable: false);
        AssertText(db, typeof(RecipeNote), nameof(RecipeNote.Text), RecipeLimits.MaxNoteLength, nullable: false);
        AssertText(db, typeof(RecipeKeyword), nameof(RecipeKeyword.Text), RecipeLimits.MaxKeywordLength, nullable: false);
    }

    [Fact]
    public void Nutrition_is_exact_decimal_per_serving()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var nutrients = Entity(db, typeof(Recipe)).GetProperties().Where(p => p.ClrType == typeof(decimal?)).ToList();

        Assert.Equal(11, nutrients.Count);
        Assert.All(nutrients, n =>
        {
            Assert.Equal(RecipeLimits.NutritionPrecision, n.GetPrecision());
            Assert.Equal(RecipeLimits.NutritionScale, n.GetScale());
            Assert.True(n.IsNullable);
        });
    }

    // The recipe carries the concurrency token for the whole of it (PP4.11 moves UpdatedAt with every part's change).
    [Fact]
    public void The_recipe_has_a_row_version()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var version = Entity(db, typeof(Recipe)).FindProperty(nameof(Recipe.Version))!;

        Assert.True(version.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
    }

    [Fact]
    public void Ids_are_minted_by_the_facade_not_the_database()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        foreach (var type in new[] { typeof(Recipe), typeof(RecipeIngredientGroup), typeof(RecipeIngredient), typeof(RecipeStepGroup), typeof(RecipeStep) })
        {
            Assert.Equal(ValueGenerated.Never, Entity(db, type).FindProperty("Id")!.ValueGenerated);
        }

        foreach (var type in new[] { typeof(RecipeEquipment), typeof(RecipeNote), typeof(RecipeKeyword) })
        {
            Assert.Equal(ValueGenerated.Never, Entity(db, type).FindProperty("Position")!.ValueGenerated);
        }
    }

    // Stored as these numbers, so they never change.
    [Fact]
    public void The_parse_status_numbers_are_pinned()
    {
        Assert.Equal(0, (int)IngredientParseStatus.Parsed);
        Assert.Equal(1, (int)IngredientParseStatus.Unparsed);
        Assert.Equal(2, Enum.GetValues<IngredientParseStatus>().Length);
    }

    private static IEnumerable<string> QuantityConstraints(string table, string prefix) =>
        new[] { "Kind", "Shape", "Terms", "Range" }.Select(what => $"CK_{table}_{prefix}{what}");

    private static void AssertText(DbContext db, Type type, string name, int length, bool nullable)
    {
        var property = Entity(db, type).FindProperty(name)!;
        Assert.Equal(length, property.GetMaxLength());
        Assert.NotEqual(false, property.IsUnicode());
        Assert.Equal(nullable, property.IsNullable);
        Assert.Null(property.GetCollation());
    }

    // The design-time model: the runtime one drops collations and check constraints, which only migrations need.
    private static IEntityType Entity(DbContext db, Type type) => db.GetService<IDesignTimeModel>().Model.FindEntityType(type)!;
}
