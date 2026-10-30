using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PantryPress.MigrationService.Migrations
{
    /// <inheritdoc />
    public partial class Recipes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Recipes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    YieldUnit = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    PrepMinutes = table.Column<int>(type: "int", nullable: true),
                    CookMinutes = table.Column<int>(type: "int", nullable: true),
                    AdditionalMinutes = table.Column<int>(type: "int", nullable: true),
                    AdditionalTimeLabel = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    VideoUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ServingSize = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Calories = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    FatGrams = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    SaturatedFatGrams = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    TransFatGrams = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    UnsaturatedFatGrams = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    CholesterolMilligrams = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    SodiumMilligrams = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    CarbohydrateGrams = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    FiberGrams = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    SugarGrams = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    ProteinGrams = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    YieldHighDenominator = table.Column<long>(type: "bigint", nullable: true),
                    YieldHighNumerator = table.Column<long>(type: "bigint", nullable: true),
                    YieldKind = table.Column<int>(type: "int", nullable: true),
                    YieldLowDenominator = table.Column<long>(type: "bigint", nullable: true),
                    YieldLowNumerator = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recipes", x => new { x.SiteId, x.Id });
                    table.CheckConstraint("CK_Recipes_AdditionalTimeLabel", "[AdditionalTimeLabel] IS NULL OR ([AdditionalMinutes] IS NOT NULL AND LEN([AdditionalTimeLabel]) > 0)");
                    table.CheckConstraint("CK_Recipes_Nutrition", "([Calories] IS NULL OR [Calories] >= 0) AND ([FatGrams] IS NULL OR [FatGrams] >= 0) AND ([SaturatedFatGrams] IS NULL OR [SaturatedFatGrams] >= 0) AND ([TransFatGrams] IS NULL OR [TransFatGrams] >= 0) AND ([UnsaturatedFatGrams] IS NULL OR [UnsaturatedFatGrams] >= 0) AND ([CholesterolMilligrams] IS NULL OR [CholesterolMilligrams] >= 0) AND ([SodiumMilligrams] IS NULL OR [SodiumMilligrams] >= 0) AND ([CarbohydrateGrams] IS NULL OR [CarbohydrateGrams] >= 0) AND ([FiberGrams] IS NULL OR [FiberGrams] >= 0) AND ([SugarGrams] IS NULL OR [SugarGrams] >= 0) AND ([ProteinGrams] IS NULL OR [ProteinGrams] >= 0)");
                    table.CheckConstraint("CK_Recipes_Text", "([Summary] IS NULL OR LEN([Summary]) > 0) AND ([ServingSize] IS NULL OR LEN([ServingSize]) > 0)");
                    table.CheckConstraint("CK_Recipes_Times", "([PrepMinutes] IS NULL OR [PrepMinutes] BETWEEN 0 AND 43200) AND ([CookMinutes] IS NULL OR [CookMinutes] BETWEEN 0 AND 43200) AND ([AdditionalMinutes] IS NULL OR [AdditionalMinutes] BETWEEN 0 AND 43200)");
                    table.CheckConstraint("CK_Recipes_Title", "LEN([Title]) > 0");
                    table.CheckConstraint("CK_Recipes_UpdatedAt", "[UpdatedAt] >= [CreatedAt]");
                    table.CheckConstraint("CK_Recipes_VideoUrl", "[VideoUrl] IS NULL OR (LEN([VideoUrl]) > 8 AND LEFT([VideoUrl], 8) = 'https://' COLLATE Latin1_General_100_BIN2)");
                    table.CheckConstraint("CK_Recipes_YieldKind", "[YieldKind] IS NULL OR [YieldKind] IN (0, 1)");
                    table.CheckConstraint("CK_Recipes_YieldRange", "[YieldHighNumerator] IS NULL OR CAST([YieldLowNumerator] AS decimal(38, 0)) * [YieldHighDenominator] < CAST([YieldHighNumerator] AS decimal(38, 0)) * [YieldLowDenominator]");
                    table.CheckConstraint("CK_Recipes_YieldShape", "([YieldKind] IS NULL AND [YieldLowNumerator] IS NULL AND [YieldLowDenominator] IS NULL AND [YieldHighNumerator] IS NULL AND [YieldHighDenominator] IS NULL) OR ([YieldKind] IS NOT NULL AND [YieldKind] = 2 AND [YieldLowNumerator] IS NULL AND [YieldLowDenominator] IS NULL AND [YieldHighNumerator] IS NULL AND [YieldHighDenominator] IS NULL) OR ([YieldKind] IS NOT NULL AND [YieldKind] = 0 AND [YieldLowNumerator] IS NOT NULL AND [YieldLowDenominator] IS NOT NULL AND [YieldHighNumerator] IS NULL AND [YieldHighDenominator] IS NULL) OR ([YieldKind] IS NOT NULL AND [YieldKind] = 1 AND [YieldLowNumerator] IS NOT NULL AND [YieldLowDenominator] IS NOT NULL AND [YieldHighNumerator] IS NOT NULL AND [YieldHighDenominator] IS NOT NULL)");
                    table.CheckConstraint("CK_Recipes_YieldTerms", "([YieldLowNumerator] IS NULL OR [YieldLowNumerator] BETWEEN 1 AND 1000000) AND ([YieldLowDenominator] IS NULL OR [YieldLowDenominator] BETWEEN 1 AND 1000000) AND ([YieldHighNumerator] IS NULL OR [YieldHighNumerator] BETWEEN 1 AND 1000000) AND ([YieldHighDenominator] IS NULL OR [YieldHighDenominator] BETWEEN 1 AND 1000000)");
                    table.CheckConstraint("CK_Recipes_YieldUnit", "[YieldUnit] IS NULL OR ([YieldKind] IS NOT NULL AND LEN([YieldUnit]) > 0)");
                });

            migrationBuilder.CreateTable(
                name: "RecipeAllergens",
                columns: table => new
                {
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AllergenCode = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeAllergens", x => new { x.SiteId, x.RecipeId, x.AllergenCode });
                    table.CheckConstraint("CK_RecipeAllergens_Position", "[Position] >= 0");
                    table.ForeignKey(
                        name: "FK_RecipeAllergens_Allergens_AllergenCode",
                        column: x => x.AllergenCode,
                        principalTable: "Allergens",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecipeAllergens_Recipes_SiteId_RecipeId",
                        columns: x => new { x.SiteId, x.RecipeId },
                        principalTable: "Recipes",
                        principalColumns: new[] { "SiteId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeCourses",
                columns: table => new
                {
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CourseCode = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeCourses", x => new { x.SiteId, x.RecipeId, x.CourseCode });
                    table.CheckConstraint("CK_RecipeCourses_Position", "[Position] >= 0");
                    table.ForeignKey(
                        name: "FK_RecipeCourses_Courses_CourseCode",
                        column: x => x.CourseCode,
                        principalTable: "Courses",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecipeCourses_Recipes_SiteId_RecipeId",
                        columns: x => new { x.SiteId, x.RecipeId },
                        principalTable: "Recipes",
                        principalColumns: new[] { "SiteId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeCuisines",
                columns: table => new
                {
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CuisineCode = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeCuisines", x => new { x.SiteId, x.RecipeId, x.CuisineCode });
                    table.CheckConstraint("CK_RecipeCuisines_Position", "[Position] >= 0");
                    table.ForeignKey(
                        name: "FK_RecipeCuisines_Cuisines_CuisineCode",
                        column: x => x.CuisineCode,
                        principalTable: "Cuisines",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecipeCuisines_Recipes_SiteId_RecipeId",
                        columns: x => new { x.SiteId, x.RecipeId },
                        principalTable: "Recipes",
                        principalColumns: new[] { "SiteId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeDiets",
                columns: table => new
                {
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DietCode = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeDiets", x => new { x.SiteId, x.RecipeId, x.DietCode });
                    table.CheckConstraint("CK_RecipeDiets_Position", "[Position] >= 0");
                    table.ForeignKey(
                        name: "FK_RecipeDiets_Diets_DietCode",
                        column: x => x.DietCode,
                        principalTable: "Diets",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecipeDiets_Recipes_SiteId_RecipeId",
                        columns: x => new { x.SiteId, x.RecipeId },
                        principalTable: "Recipes",
                        principalColumns: new[] { "SiteId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeEquipment",
                columns: table => new
                {
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeEquipment", x => new { x.SiteId, x.RecipeId, x.Position });
                    table.CheckConstraint("CK_RecipeEquipment_Position", "[Position] >= 0");
                    table.CheckConstraint("CK_RecipeEquipment_Text", "LEN([Text]) > 0");
                    table.ForeignKey(
                        name: "FK_RecipeEquipment_Recipes_SiteId_RecipeId",
                        columns: x => new { x.SiteId, x.RecipeId },
                        principalTable: "Recipes",
                        principalColumns: new[] { "SiteId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeIngredientGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Heading = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeIngredientGroups", x => new { x.SiteId, x.Id });
                    table.CheckConstraint("CK_RecipeIngredientGroups_Heading", "[Heading] IS NULL OR LEN([Heading]) > 0");
                    table.CheckConstraint("CK_RecipeIngredientGroups_Position", "[Position] >= 0");
                    table.ForeignKey(
                        name: "FK_RecipeIngredientGroups_Recipes_SiteId_RecipeId",
                        columns: x => new { x.SiteId, x.RecipeId },
                        principalTable: "Recipes",
                        principalColumns: new[] { "SiteId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeKeywords",
                columns: table => new
                {
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeKeywords", x => new { x.SiteId, x.RecipeId, x.Position });
                    table.CheckConstraint("CK_RecipeKeywords_Position", "[Position] >= 0");
                    table.CheckConstraint("CK_RecipeKeywords_Text", "LEN([Text]) > 0");
                    table.ForeignKey(
                        name: "FK_RecipeKeywords_Recipes_SiteId_RecipeId",
                        columns: x => new { x.SiteId, x.RecipeId },
                        principalTable: "Recipes",
                        principalColumns: new[] { "SiteId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeNotes",
                columns: table => new
                {
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeNotes", x => new { x.SiteId, x.RecipeId, x.Position });
                    table.CheckConstraint("CK_RecipeNotes_Position", "[Position] >= 0");
                    table.CheckConstraint("CK_RecipeNotes_Text", "LEN([Text]) > 0");
                    table.ForeignKey(
                        name: "FK_RecipeNotes_Recipes_SiteId_RecipeId",
                        columns: x => new { x.SiteId, x.RecipeId },
                        principalTable: "Recipes",
                        principalColumns: new[] { "SiteId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeStepGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Heading = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeStepGroups", x => new { x.SiteId, x.Id });
                    table.CheckConstraint("CK_RecipeStepGroups_Heading", "[Heading] IS NULL OR LEN([Heading]) > 0");
                    table.CheckConstraint("CK_RecipeStepGroups_Position", "[Position] >= 0");
                    table.ForeignKey(
                        name: "FK_RecipeStepGroups_Recipes_SiteId_RecipeId",
                        columns: x => new { x.SiteId, x.RecipeId },
                        principalTable: "Recipes",
                        principalColumns: new[] { "SiteId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeIngredients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    OriginalText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ParseStatus = table.Column<int>(type: "int", nullable: false),
                    QuantityStart = table.Column<int>(type: "int", nullable: true),
                    QuantityLength = table.Column<int>(type: "int", nullable: true),
                    UnitCode = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true, collation: "Latin1_General_100_BIN2"),
                    UnitStart = table.Column<int>(type: "int", nullable: true),
                    UnitLength = table.Column<int>(type: "int", nullable: true),
                    Item = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    QuantityHighDenominator = table.Column<long>(type: "bigint", nullable: true),
                    QuantityHighNumerator = table.Column<long>(type: "bigint", nullable: true),
                    QuantityKind = table.Column<int>(type: "int", nullable: true),
                    QuantityLowDenominator = table.Column<long>(type: "bigint", nullable: true),
                    QuantityLowNumerator = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeIngredients", x => new { x.SiteId, x.Id });
                    table.CheckConstraint("CK_RecipeIngredients_OriginalText", "LEN([OriginalText]) > 0");
                    table.CheckConstraint("CK_RecipeIngredients_Parsed", "([ParseStatus] <> 0 OR [Item] IS NOT NULL) AND ([Item] IS NULL OR LEN([Item]) > 0) AND ([Note] IS NULL OR LEN([Note]) > 0)");
                    table.CheckConstraint("CK_RecipeIngredients_ParseStatus", "[ParseStatus] IN (0, 1)");
                    table.CheckConstraint("CK_RecipeIngredients_Position", "[Position] >= 0");
                    table.CheckConstraint("CK_RecipeIngredients_QuantityKind", "[QuantityKind] IS NULL OR [QuantityKind] IN (0, 1, 2)");
                    table.CheckConstraint("CK_RecipeIngredients_QuantityRange", "[QuantityHighNumerator] IS NULL OR CAST([QuantityLowNumerator] AS decimal(38, 0)) * [QuantityHighDenominator] < CAST([QuantityHighNumerator] AS decimal(38, 0)) * [QuantityLowDenominator]");
                    table.CheckConstraint("CK_RecipeIngredients_QuantityShape", "([QuantityKind] IS NULL AND [QuantityLowNumerator] IS NULL AND [QuantityLowDenominator] IS NULL AND [QuantityHighNumerator] IS NULL AND [QuantityHighDenominator] IS NULL) OR ([QuantityKind] IS NOT NULL AND [QuantityKind] = 2 AND [QuantityLowNumerator] IS NULL AND [QuantityLowDenominator] IS NULL AND [QuantityHighNumerator] IS NULL AND [QuantityHighDenominator] IS NULL) OR ([QuantityKind] IS NOT NULL AND [QuantityKind] = 0 AND [QuantityLowNumerator] IS NOT NULL AND [QuantityLowDenominator] IS NOT NULL AND [QuantityHighNumerator] IS NULL AND [QuantityHighDenominator] IS NULL) OR ([QuantityKind] IS NOT NULL AND [QuantityKind] = 1 AND [QuantityLowNumerator] IS NOT NULL AND [QuantityLowDenominator] IS NOT NULL AND [QuantityHighNumerator] IS NOT NULL AND [QuantityHighDenominator] IS NOT NULL)");
                    table.CheckConstraint("CK_RecipeIngredients_QuantitySpan", "(([QuantityStart] IS NULL AND [QuantityLength] IS NULL) OR ([QuantityStart] IS NOT NULL AND [QuantityLength] IS NOT NULL AND [QuantityStart] >= 0 AND [QuantityLength] > 0 AND [QuantityStart] <= DATALENGTH([OriginalText]) / 2 AND [QuantityLength] <= DATALENGTH([OriginalText]) / 2 - [QuantityStart])) AND ([QuantityStart] IS NULL OR [QuantityKind] IS NOT NULL) AND ([QuantityStart] IS NOT NULL OR [QuantityKind] IS NULL OR [QuantityKind] = 2)");
                    table.CheckConstraint("CK_RecipeIngredients_QuantityTerms", "([QuantityLowNumerator] IS NULL OR [QuantityLowNumerator] BETWEEN 1 AND 1000000) AND ([QuantityLowDenominator] IS NULL OR [QuantityLowDenominator] BETWEEN 1 AND 1000000) AND ([QuantityHighNumerator] IS NULL OR [QuantityHighNumerator] BETWEEN 1 AND 1000000) AND ([QuantityHighDenominator] IS NULL OR [QuantityHighDenominator] BETWEEN 1 AND 1000000)");
                    table.CheckConstraint("CK_RecipeIngredients_SpansApart", "[QuantityStart] IS NULL OR [UnitStart] IS NULL OR CAST([QuantityStart] AS bigint) + [QuantityLength] <= [UnitStart] OR CAST([UnitStart] AS bigint) + [UnitLength] <= [QuantityStart]");
                    table.CheckConstraint("CK_RecipeIngredients_Unit", "[UnitCode] IS NULL OR ([QuantityKind] IS NOT NULL AND [QuantityKind] IN (0, 1))");
                    table.CheckConstraint("CK_RecipeIngredients_UnitSpan", "(([UnitStart] IS NULL AND [UnitLength] IS NULL) OR ([UnitStart] IS NOT NULL AND [UnitLength] IS NOT NULL AND [UnitStart] >= 0 AND [UnitLength] > 0 AND [UnitStart] <= DATALENGTH([OriginalText]) / 2 AND [UnitLength] <= DATALENGTH([OriginalText]) / 2 - [UnitStart])) AND (([UnitCode] IS NULL AND [UnitStart] IS NULL) OR ([UnitCode] IS NOT NULL AND [UnitStart] IS NOT NULL))");
                    table.CheckConstraint("CK_RecipeIngredients_Unparsed", "[ParseStatus] <> 1 OR ([QuantityKind] IS NULL AND [UnitCode] IS NULL AND [Item] IS NULL AND [Note] IS NULL AND [QuantityStart] IS NULL AND [UnitStart] IS NULL)");
                    table.ForeignKey(
                        name: "FK_RecipeIngredients_RecipeIngredientGroups_SiteId_GroupId",
                        columns: x => new { x.SiteId, x.GroupId },
                        principalTable: "RecipeIngredientGroups",
                        principalColumns: new[] { "SiteId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecipeIngredients_Units_UnitCode",
                        column: x => x.UnitCode,
                        principalTable: "Units",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecipeSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeSteps", x => new { x.SiteId, x.Id });
                    table.CheckConstraint("CK_RecipeSteps_Position", "[Position] >= 0");
                    table.CheckConstraint("CK_RecipeSteps_Text", "LEN([Text]) > 0");
                    table.ForeignKey(
                        name: "FK_RecipeSteps_RecipeStepGroups_SiteId_GroupId",
                        columns: x => new { x.SiteId, x.GroupId },
                        principalTable: "RecipeStepGroups",
                        principalColumns: new[] { "SiteId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeAllergens_SiteId_AllergenCode",
                table: "RecipeAllergens",
                columns: new[] { "SiteId", "AllergenCode" });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeCourses_SiteId_CourseCode",
                table: "RecipeCourses",
                columns: new[] { "SiteId", "CourseCode" });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeCuisines_SiteId_CuisineCode",
                table: "RecipeCuisines",
                columns: new[] { "SiteId", "CuisineCode" });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeDiets_SiteId_DietCode",
                table: "RecipeDiets",
                columns: new[] { "SiteId", "DietCode" });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIngredientGroups_SiteId_RecipeId_Position",
                table: "RecipeIngredientGroups",
                columns: new[] { "SiteId", "RecipeId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIngredients_SiteId_GroupId_Position",
                table: "RecipeIngredients",
                columns: new[] { "SiteId", "GroupId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeStepGroups_SiteId_RecipeId_Position",
                table: "RecipeStepGroups",
                columns: new[] { "SiteId", "RecipeId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeSteps_SiteId_GroupId_Position",
                table: "RecipeSteps",
                columns: new[] { "SiteId", "GroupId", "Position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back would take every site's recipes and every word of them. Refuse while there are any, holding the
            // table so none is added between the check and the drop; the rest of the tables hang off a recipe, so none of them
            // has a row while this one is empty. Rolling back further, PP4.1's RecipeVocabulary stops here too: its tables are
            // what these refer to.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [Recipes] WITH (TABLOCKX, HOLDLOCK))
                    THROW 51012, N'Recipes has rows; rolling back the Recipes migration would erase every recipe.', 1;
                """);
            migrationBuilder.DropTable(
                name: "RecipeAllergens");

            migrationBuilder.DropTable(
                name: "RecipeCourses");

            migrationBuilder.DropTable(
                name: "RecipeCuisines");

            migrationBuilder.DropTable(
                name: "RecipeDiets");

            migrationBuilder.DropTable(
                name: "RecipeEquipment");

            migrationBuilder.DropTable(
                name: "RecipeIngredients");

            migrationBuilder.DropTable(
                name: "RecipeKeywords");

            migrationBuilder.DropTable(
                name: "RecipeNotes");

            migrationBuilder.DropTable(
                name: "RecipeSteps");

            migrationBuilder.DropTable(
                name: "RecipeIngredientGroups");

            migrationBuilder.DropTable(
                name: "RecipeStepGroups");

            migrationBuilder.DropTable(
                name: "Recipes");
        }
    }
}
