using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PantryPress.Domain.Modules.Recipes.Business;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// The columns every recipe vocabulary table shares (PP4.1): a code stored as ASCII under a binary collation, so codes
/// compare byte for byte and a recipe's foreign key matches exactly, and checked to <see cref="VocabularyCodes"/>' shape
/// beneath the .NET rule; and a display name.
/// </summary>
internal static class VocabularyColumnConfiguration
{
    public const string BinaryCollation = "Latin1_General_100_BIN2";

    public static void Code(PropertyBuilder<string> code) => code
        .HasMaxLength(VocabularyCodes.MaxLength)
        .IsUnicode(false)
        .UseCollation(BinaryCollation);

    public static void Name(PropertyBuilder<string> name) => name.HasMaxLength(VocabularyCodes.NameMaxLength);

    public static string CodeShape(string column = "Code") =>
        $"LEN([{column}]) BETWEEN {VocabularyCodes.MinLength} AND {VocabularyCodes.MaxLength} " +
        $"AND [{column}] NOT LIKE '%[^a-z0-9-]%' COLLATE {BinaryCollation} " +
        $"AND [{column}] NOT LIKE '-%' AND [{column}] NOT LIKE '%-' AND [{column}] NOT LIKE '%--%'";
}
