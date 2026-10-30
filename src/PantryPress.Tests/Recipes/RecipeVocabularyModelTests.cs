using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Recipes;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.MigrationService;

namespace PantryPress.Tests.Recipes;

// PP4.1: the recipe vocabulary's tables are platform reference data (PP-D06, ADR 0006) - no SiteId, no filter, read-only to
// the app - seeded from the lists in code, so the two cannot drift. No database is needed.
public sealed class RecipeVocabularyModelTests
{
    public static TheoryData<Type, string, string[]> Tables => new()
    {
        { typeof(Unit), UnitConfiguration.TableName, [UnitConfiguration.CodeShapeConstraintName, UnitConfiguration.DimensionConstraintName, UnitConfiguration.SystemConstraintName] },
        { typeof(UnitAlias), UnitAliasConfiguration.TableName, [UnitAliasConfiguration.CaseConstraintName, UnitAliasConfiguration.TrimmedConstraintName] },
        { typeof(Course), CourseConfiguration.TableName, [CourseConfiguration.CodeShapeConstraintName] },
        { typeof(Cuisine), CuisineConfiguration.TableName, [CuisineConfiguration.CodeShapeConstraintName] },
        { typeof(Diet), DietConfiguration.TableName, [DietConfiguration.CodeShapeConstraintName, DietConfiguration.SchemaOrgDietConstraintName] },
        { typeof(Allergen), AllergenConfiguration.TableName, [AllergenConfiguration.CodeShapeConstraintName] },
    };

    // The design-time model: the runtime one drops collations, check constraints, and seed data, which only migrations need.
    private static IEntityType Entity(DbContext db, Type type) => db.GetService<IDesignTimeModel>().Model.FindEntityType(type)!;

    [Theory]
    [MemberData(nameof(Tables))]
    public void Each_table_is_read_only_platform_reference_data(Type type, string table, string[] constraints)
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var entity = Entity(db, type);

        Assert.Equal(table, entity.GetTableName());
        Assert.True(typeof(IReferenceData).IsAssignableFrom(type));
        Assert.False(typeof(ISiteOwned).IsAssignableFrom(type));
        Assert.DoesNotContain(entity.GetProperties(), p => p.Name == "SiteId" || p.ClrType == typeof(SiteId));
        Assert.Empty(entity.GetDeclaredQueryFilters());
        Assert.Equal(constraints.Order(StringComparer.Ordinal), entity.GetCheckConstraints().Select(c => c.Name!).Order(StringComparer.Ordinal));
    }

    // Every code is ASCII under a binary collation, so a recipe's foreign key to it matches byte for byte.
    [Theory]
    [InlineData(typeof(Unit), nameof(Unit.Code))]
    [InlineData(typeof(UnitAlias), nameof(UnitAlias.UnitCode))]
    [InlineData(typeof(Course), nameof(Course.Code))]
    [InlineData(typeof(Cuisine), nameof(Cuisine.Code))]
    [InlineData(typeof(Diet), nameof(Diet.Code))]
    [InlineData(typeof(Allergen), nameof(Allergen.Code))]
    public void Codes_are_binary_ascii(Type type, string property)
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var code = Entity(db, type).FindProperty(property)!;

        Assert.Equal(VocabularyCodes.MaxLength, code.GetMaxLength());
        Assert.False(code.IsUnicode());
        Assert.Equal(VocabularyColumnConfiguration.BinaryCollation, code.GetCollation());
    }

    // Exactly as schema.org spells it, which JSON-LD carries and the column's check compares byte for byte.
    [Fact]
    public void A_diets_schema_org_member_is_binary_ascii()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var member = Entity(db, typeof(Diet)).FindProperty(nameof(Diet.SchemaOrgDiet))!;

        Assert.Equal(Diets.SchemaOrgDietMaxLength, member.GetMaxLength());
        Assert.True(member.IsNullable);
        Assert.False(member.IsUnicode());
        Assert.Equal(VocabularyColumnConfiguration.BinaryCollation, member.GetCollation());
    }

    // Unicode for the degree sign, binary so T and t are two keys, and compared the same way by EF Core.
    [Fact]
    public void An_alias_is_a_binary_unicode_key_that_points_at_its_unit()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var alias = Entity(db, typeof(UnitAlias));
        var key = alias.FindPrimaryKey()!.Properties.Single();

        Assert.Equal(nameof(UnitAlias.Alias), key.Name);
        Assert.NotEqual(false, key.IsUnicode());
        Assert.Equal(VocabularyColumnConfiguration.BinaryCollation, key.GetCollation());
        Assert.False(key.GetKeyValueComparer().Equals("T", "t"));

        var foreignKey = Assert.Single(alias.GetForeignKeys());
        Assert.Equal(typeof(Unit), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal([nameof(UnitAlias.UnitCode)], foreignKey.Properties.Select(p => p.Name));
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void The_units_seed_is_the_list_exactly()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        var seeded = Entity(db, typeof(Unit)).GetSeedData()
            .Select(row => new UnitEntry(
                (string)row[nameof(Unit.Code)]!,
                (UnitSystem)row[nameof(Unit.System)]!,
                (UnitDimension)row[nameof(Unit.Dimension)]!,
                (string)row[nameof(Unit.Singular)]!,
                (string)row[nameof(Unit.Plural)]!,
                (string?)row[nameof(Unit.Abbreviation)],
                (int)row[nameof(Unit.Since)]!));

        Assert.Equal(Units.All.OrderBy(u => u.Code, StringComparer.Ordinal), seeded.OrderBy(u => u.Code, StringComparer.Ordinal));
    }

    [Fact]
    public void The_aliases_seed_is_the_list_exactly()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        var seeded = Entity(db, typeof(UnitAlias)).GetSeedData()
            .Select(row => new UnitAliasEntry(
                (string)row[nameof(UnitAlias.Alias)]!,
                (string)row[nameof(UnitAlias.UnitCode)]!,
                (bool)row[nameof(UnitAlias.CaseSensitive)]!,
                (int)row[nameof(UnitAlias.Since)]!));

        Assert.Equal(
            Units.Aliases.OrderBy(a => a.Alias, StringComparer.Ordinal),
            seeded.OrderBy(a => a.Alias, StringComparer.Ordinal));
    }

    [Fact]
    public void The_diets_seed_is_the_list_exactly()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        var seeded = Entity(db, typeof(Diet)).GetSeedData()
            .Select(row => new DietEntry(
                (string)row[nameof(Diet.Code)]!,
                (string)row[nameof(Diet.Name)]!,
                (string?)row[nameof(Diet.SchemaOrgDiet)],
                (int)row[nameof(Diet.Since)]!));

        Assert.Equal(Diets.All.OrderBy(d => d.Code, StringComparer.Ordinal), seeded.OrderBy(d => d.Code, StringComparer.Ordinal));
    }

    public static TheoryData<Type, IReadOnlyList<VocabularyEntry>> Words => new()
    {
        { typeof(Course), Courses.All },
        { typeof(Cuisine), Cuisines.All },
        { typeof(Allergen), Allergens.All },
    };

    [Theory]
    [MemberData(nameof(Words))]
    public void A_word_lists_seed_is_the_list_exactly(Type type, IReadOnlyList<VocabularyEntry> list)
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        var seeded = Entity(db, type).GetSeedData()
            .Select(row => new VocabularyEntry((string)row["Code"]!, (string)row["Name"]!, (int)row["Since"]!));

        Assert.Equal(list.OrderBy(e => e.Code, StringComparer.Ordinal), seeded.OrderBy(e => e.Code, StringComparer.Ordinal));
    }

    // A name is unique too, so two words a blogger could not tell apart cannot both be stored.
    [Theory]
    [InlineData(typeof(Course))]
    [InlineData(typeof(Cuisine))]
    [InlineData(typeof(Diet))]
    [InlineData(typeof(Allergen))]
    public void A_name_is_unique(Type type)
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        var index = Assert.Single(Entity(db, type).GetIndexes());
        Assert.True(index.IsUnique);
        Assert.Equal(["Name"], index.Properties.Select(p => p.Name));
    }
}
