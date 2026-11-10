using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Recipes;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.Tests.Recipes;
using PantryPress.Tests.TwoSite;

namespace PantryPress.Tests.Persistence;

// PP4.5: what the parser reads is what the ingredient table accepts. Every line of the golden corpus is stored as the
// parser read it - parsed or flagged - against the schema the migration service applies, and comes back exactly: the
// blogger's words byte for byte, and every part where the parser put it. Two kinds of line are left out, both refused
// before they are stored: an empty one, which the table refuses too, and one with a broken character, which SqlClient reads
// back as U+FFFD (recipes.md, PP4.4), so the recipe routes refuse ill-formed text.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class IngredientParseStorageTests(MigratedDatabase database) : IAsyncLifetime
{
    private TwoSiteHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await TwoSiteHarness.CreateAsync(database);

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task Every_corpus_parse_is_stored_and_read_back_exactly()
    {
        var lines = IngredientCorpus.Load()
            .Select(c => c.Input)
            .Where(line => IngredientLineParser.Parse(line).Problem is not (IngredientParseProblem.Empty or IngredientParseProblem.IllFormedText))
            .ToList();
        var world = await _harness.SeedAsync("rcp-parse");
        var recipe = RecipeRows.NewRecipe();
        var group = RecipeRows.NewIngredientGroup(recipe.Id);
        await _harness.ChangeAsync(world.A.Id, db =>
        {
            db.AddRange(recipe, group);
            for (var i = 0; i < lines.Count; i++)
            {
                db.Add(Row(group.Id, i, lines[i]));
            }
        });

        await using var scope = _harness.In(world.A.Id);
        var stored = await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>()
            .Set<RecipeIngredient>()
            .AsNoTracking()
            .Where(l => l.GroupId == group.Id)
            .OrderBy(l => l.Position)
            .ToListAsync();

        Assert.Equal(lines.Count, stored.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var expected = Row(group.Id, i, lines[i]);
            var actual = stored[i];
            Assert.Equal(lines[i], actual.OriginalText, StringComparer.Ordinal);
            Assert.Equal(
                (expected.ParseStatus, expected.Quantity, expected.QuantityStart, expected.QuantityLength, expected.UnitCode,
                    expected.UnitStart, expected.UnitLength, expected.Item, expected.Note),
                (actual.ParseStatus, actual.Quantity, actual.QuantityStart, actual.QuantityLength, actual.UnitCode,
                    actual.UnitStart, actual.UnitLength, actual.Item, actual.Note));
        }
    }

    // The row a writer of ingredient lines (PP4.6, PP4.11) will make from a parse.
    private static RecipeIngredient Row(RecipeIngredientGroupId group, int position, string line)
    {
        var parse = IngredientLineParser.Parse(line);
        return new RecipeIngredient
        {
            Id = new RecipeIngredientId(Guid.NewGuid()),
            GroupId = group,
            Position = position,
            OriginalText = line,
            ParseStatus = parse.Status,
            Quantity = parse.Quantity,
            QuantityStart = parse.QuantitySpan?.Start,
            QuantityLength = parse.QuantitySpan?.Length,
            UnitCode = parse.UnitCode,
            UnitStart = parse.UnitSpan?.Start,
            UnitLength = parse.UnitSpan?.Length,
            Item = parse.Item,
            Note = parse.Note,
        };
    }
}
