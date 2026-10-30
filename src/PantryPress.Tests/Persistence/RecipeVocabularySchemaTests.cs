using Aspire.Hosting.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Recipes;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.MigrationService;

namespace PantryPress.Tests.Persistence;

// PP4.1: the recipe vocabulary against the schema the migration service applies. The rows are the lists in code, and
// beneath the .NET rules the tables refuse a malformed code, an enum value nobody defined, an alias in the wrong case or
// with a stray space, an alias of no unit, and a schema.org diet that does not exist. Each refused insert writes nothing.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class RecipeVocabularySchemaTests(MigratedDatabase database) : IAsyncLifetime
{
    private const int CheckConstraintViolation = 547;

    private ServiceProvider _services = null!;

    private static CancellationToken Timeout => CancellationToken.None;

    public async Task InitializeAsync()
    {
        var connectionString = await database.App.GetConnectionStringAsync(MigrationServiceExtensions.ConnectionName, Timeout)
            ?? throw new InvalidOperationException("The test AppHost has no pantrypress connection string.");

        var services = new ServiceCollection();
        services.AddAccountsIdentitySchema();
        services.AddPantryPressDbContext((_, options) => options.UseSqlServer(
            connectionString,
            MigrationServiceExtensions.UseMigrationsAssembly));
        _services = services.BuildServiceProvider();
    }

    public Task DisposeAsync() => _services.DisposeAsync().AsTask();

    [Fact]
    public async Task The_migrated_units_are_the_list_in_code()
    {
        await using var scope = _services.CreateAsyncScope();
        var stored = await Db(scope).Set<Unit>().AsNoTracking().ToListAsync(Timeout);

        Assert.Equal(
            Units.All.OrderBy(u => u.Code, StringComparer.Ordinal),
            stored.Select(u => new UnitEntry(u.Code, u.System, u.Dimension, u.Singular, u.Plural, u.Abbreviation, u.Since))
                .OrderBy(u => u.Code, StringComparer.Ordinal));
    }

    // T and t are both there, and the degree sign came through as written.
    [Fact]
    public async Task The_migrated_aliases_are_the_list_in_code()
    {
        await using var scope = _services.CreateAsyncScope();
        var stored = await Db(scope).Set<UnitAlias>().AsNoTracking().ToListAsync(Timeout);

        Assert.Equal(
            Units.Aliases.OrderBy(a => a.Alias, StringComparer.Ordinal),
            stored.Select(a => new UnitAliasEntry(a.Alias, a.UnitCode, a.CaseSensitive, a.Since)).OrderBy(a => a.Alias, StringComparer.Ordinal));
        Assert.Contains(stored, a => a is { Alias: "T", UnitCode: "tablespoon" });
        Assert.Contains(stored, a => a is { Alias: "t", UnitCode: "teaspoon" });
        Assert.Contains(stored, a => a is { Alias: "°f", UnitCode: "fahrenheit" });
    }

    [Fact]
    public async Task The_migrated_diets_are_the_list_in_code()
    {
        await using var scope = _services.CreateAsyncScope();
        var stored = await Db(scope).Set<Diet>().AsNoTracking().ToListAsync(Timeout);

        Assert.Equal(
            Diets.All.OrderBy(d => d.Code, StringComparer.Ordinal),
            stored.Select(d => new DietEntry(d.Code, d.Name, d.SchemaOrgDiet, d.Since)).OrderBy(d => d.Code, StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_migrated_word_lists_are_the_lists_in_code()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = Db(scope);

        Assert.Equal(Ordered(Courses.All), Ordered((await db.Set<Course>().AsNoTracking().ToListAsync(Timeout)).Select(c => new VocabularyEntry(c.Code, c.Name, c.Since))));
        Assert.Equal(Ordered(Cuisines.All), Ordered((await db.Set<Cuisine>().AsNoTracking().ToListAsync(Timeout)).Select(c => new VocabularyEntry(c.Code, c.Name, c.Since))));
        Assert.Equal(Ordered(Allergens.All), Ordered((await db.Set<Allergen>().AsNoTracking().ToListAsync(Timeout)).Select(a => new VocabularyEntry(a.Code, a.Name, a.Since))));
    }

    // The same shape the .NET rule checks, on every table that has a code.
    [Theory]
    [InlineData("Courses", "Main-course")]
    [InlineData("Cuisines", "tex mex")]
    [InlineData("Allergens", "-nuts")]
    [InlineData("Courses", "side--dish")]
    [InlineData("Cuisines", "x")]
    [InlineData("Units", "Cup")]
    [InlineData("Diets", "low_fat")]
    public async Task A_malformed_code_is_refused(string table, string code)
    {
        await using var scope = _services.CreateAsyncScope();
        var database = Db(scope).Database;
        var name = $"Refused {Guid.NewGuid():N}";

        var error = await Assert.ThrowsAsync<SqlException>(() => table switch
        {
            "Courses" => database.ExecuteSqlAsync($"INSERT INTO [Courses] ([Code], [Name], [Since]) VALUES ({code}, {name}, 1)", Timeout),
            "Cuisines" => database.ExecuteSqlAsync($"INSERT INTO [Cuisines] ([Code], [Name], [Since]) VALUES ({code}, {name}, 1)", Timeout),
            "Allergens" => database.ExecuteSqlAsync($"INSERT INTO [Allergens] ([Code], [Name], [Since]) VALUES ({code}, {name}, 1)", Timeout),
            "Diets" => database.ExecuteSqlAsync($"INSERT INTO [Diets] ([Code], [Name], [Since]) VALUES ({code}, {name}, 1)", Timeout),
            "Units" => database.ExecuteSqlAsync(
                $"INSERT INTO [Units] ([Code], [System], [Dimension], [Singular], [Plural], [Since]) VALUES ({code}, 2, 3, {name}, {name}, 1)",
                Timeout),
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        });

        Assert.Equal(CheckConstraintViolation, error.Number);
        Assert.Contains($"CK_{table}_Code_Shape", error.Message, StringComparison.Ordinal);
    }

    // A name already taken is refused in any case, as the .NET rule compares them: the column's collation ignores case.
    [Theory]
    [InlineData("Dessert")]
    [InlineData("dessert")]
    public async Task A_name_already_taken_is_refused(string name)
    {
        await using var scope = _services.CreateAsyncScope();
        var code = UniqueCode("course");

        var error = await Assert.ThrowsAsync<SqlException>(() => Db(scope).Database.ExecuteSqlAsync(
            $"INSERT INTO [Courses] ([Code], [Name], [Since]) VALUES ({code}, {name}, 1)",
            Timeout));

        Assert.Equal(2601, error.Number);
    }

    [Theory]
    [InlineData(3, 0, UnitConfiguration.SystemConstraintName)]
    [InlineData(-1, 0, UnitConfiguration.SystemConstraintName)]
    [InlineData(0, 4, UnitConfiguration.DimensionConstraintName)]
    public async Task A_system_or_dimension_outside_the_enums_is_refused(int system, int dimension, string constraint)
    {
        await using var scope = _services.CreateAsyncScope();
        var code = UniqueCode("unit");

        var error = await Assert.ThrowsAsync<SqlException>(() => Db(scope).Database.ExecuteSqlAsync(
            $"INSERT INTO [Units] ([Code], [System], [Dimension], [Singular], [Plural], [Since]) VALUES ({code}, {system}, {dimension}, {code}, {code}, 1)",
            Timeout));

        Assert.Equal(CheckConstraintViolation, error.Number);
        Assert.Contains(constraint, error.Message, StringComparison.Ordinal);
    }

    // A case-insensitive alias in capitals would match nothing once the parser folds what the blogger wrote.
    [Theory]
    [InlineData("Cupful")]
    [InlineData("°F-ish")]
    public async Task A_case_insensitive_alias_with_capitals_is_refused(string alias)
    {
        var error = await Assert.ThrowsAsync<SqlException>(() => AddAliasAsync(alias + Guid.NewGuid().ToString("N")[..8], "cup", caseSensitive: false));

        Assert.Equal(CheckConstraintViolation, error.Number);
        Assert.Contains(UnitAliasConfiguration.CaseConstraintName, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(" cupx")]
    [InlineData("cupx ")]
    [InlineData("")]
    public async Task An_alias_with_a_space_at_either_end_or_none_at_all_is_refused(string alias)
    {
        var error = await Assert.ThrowsAsync<SqlException>(() => AddAliasAsync(alias, "cup", caseSensitive: true));

        Assert.Equal(CheckConstraintViolation, error.Number);
        Assert.Contains(UnitAliasConfiguration.TrimmedConstraintName, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_alias_of_no_unit_is_refused()
    {
        var error = await Assert.ThrowsAsync<SqlException>(() => AddAliasAsync(UniqueCode("alias"), UniqueCode("unit"), caseSensitive: false));

        Assert.Equal(CheckConstraintViolation, error.Number);
        Assert.Contains("FK_UnitAliases_Units_UnitCode", error.Message, StringComparison.Ordinal);
    }

    // A spelling already taken stays taken: the key is binary, so only an exact repeat collides.
    [Fact]
    public async Task A_spelling_already_seeded_is_refused()
    {
        var error = await Assert.ThrowsAsync<SqlException>(() => AddAliasAsync("T", "teaspoon", caseSensitive: true));

        Assert.Equal(2627, error.Number);
    }

    [Theory]
    [InlineData("KetoDiet")]
    [InlineData("vegandiet")]
    public async Task A_schema_org_diet_nobody_defined_is_refused(string member)
    {
        await using var scope = _services.CreateAsyncScope();
        var code = UniqueCode("diet");

        var error = await Assert.ThrowsAsync<SqlException>(() => Db(scope).Database.ExecuteSqlAsync(
            $"INSERT INTO [Diets] ([Code], [Name], [SchemaOrgDiet], [Since]) VALUES ({code}, {code}, {member}, 1)",
            Timeout));

        Assert.Equal(CheckConstraintViolation, error.Number);
        Assert.Contains(DietConfiguration.SchemaOrgDietConstraintName, error.Message, StringComparison.Ordinal);
    }

    private async Task AddAliasAsync(string alias, string unit, bool caseSensitive)
    {
        await using var scope = _services.CreateAsyncScope();
        await Db(scope).Database.ExecuteSqlAsync(
            $"INSERT INTO [UnitAliases] ([Alias], [UnitCode], [CaseSensitive], [Since]) VALUES ({alias}, {unit}, {caseSensitive}, 1)",
            Timeout);
    }

    private static IEnumerable<VocabularyEntry> Ordered(IEnumerable<VocabularyEntry> entries) =>
        entries.OrderBy(e => e.Code, StringComparer.Ordinal);

    // Lower-case hex keeps every code inside the shape and unique to this run, since the database is shared.
    private static string UniqueCode(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..30];

    private static PantryPressDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
}
