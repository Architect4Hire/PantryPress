using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PantryPress.MigrationService.Migrations
{
    /// <inheritdoc />
    public partial class RecipeVocabulary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Allergens",
                columns: table => new
                {
                    Code = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Since = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Allergens", x => x.Code);
                    table.CheckConstraint("CK_Allergens_Code_Shape", "LEN([Code]) BETWEEN 2 AND 40 AND [Code] NOT LIKE '%[^a-z0-9-]%' COLLATE Latin1_General_100_BIN2 AND [Code] NOT LIKE '-%' AND [Code] NOT LIKE '%-' AND [Code] NOT LIKE '%--%'");
                });

            migrationBuilder.CreateTable(
                name: "Courses",
                columns: table => new
                {
                    Code = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Since = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Courses", x => x.Code);
                    table.CheckConstraint("CK_Courses_Code_Shape", "LEN([Code]) BETWEEN 2 AND 40 AND [Code] NOT LIKE '%[^a-z0-9-]%' COLLATE Latin1_General_100_BIN2 AND [Code] NOT LIKE '-%' AND [Code] NOT LIKE '%-' AND [Code] NOT LIKE '%--%'");
                });

            migrationBuilder.CreateTable(
                name: "Cuisines",
                columns: table => new
                {
                    Code = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Since = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cuisines", x => x.Code);
                    table.CheckConstraint("CK_Cuisines_Code_Shape", "LEN([Code]) BETWEEN 2 AND 40 AND [Code] NOT LIKE '%[^a-z0-9-]%' COLLATE Latin1_General_100_BIN2 AND [Code] NOT LIKE '-%' AND [Code] NOT LIKE '%-' AND [Code] NOT LIKE '%--%'");
                });

            migrationBuilder.CreateTable(
                name: "Diets",
                columns: table => new
                {
                    Code = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    SchemaOrgDiet = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Since = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Diets", x => x.Code);
                    table.CheckConstraint("CK_Diets_Code_Shape", "LEN([Code]) BETWEEN 2 AND 40 AND [Code] NOT LIKE '%[^a-z0-9-]%' COLLATE Latin1_General_100_BIN2 AND [Code] NOT LIKE '-%' AND [Code] NOT LIKE '%-' AND [Code] NOT LIKE '%--%'");
                    table.CheckConstraint("CK_Diets_SchemaOrgDiet", "[SchemaOrgDiet] IS NULL OR [SchemaOrgDiet] IN ('DiabeticDiet', 'GlutenFreeDiet', 'HalalDiet', 'HinduDiet', 'KosherDiet', 'LowCalorieDiet', 'LowFatDiet', 'LowLactoseDiet', 'LowSaltDiet', 'VeganDiet', 'VegetarianDiet')");
                });

            migrationBuilder.CreateTable(
                name: "Units",
                columns: table => new
                {
                    Code = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false, collation: "Latin1_General_100_BIN2"),
                    System = table.Column<int>(type: "int", nullable: false),
                    Dimension = table.Column<int>(type: "int", nullable: false),
                    Singular = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Plural = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Abbreviation = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Since = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Units", x => x.Code);
                    table.CheckConstraint("CK_Units_Code_Shape", "LEN([Code]) BETWEEN 2 AND 40 AND [Code] NOT LIKE '%[^a-z0-9-]%' COLLATE Latin1_General_100_BIN2 AND [Code] NOT LIKE '-%' AND [Code] NOT LIKE '%-' AND [Code] NOT LIKE '%--%'");
                    table.CheckConstraint("CK_Units_Dimension", "[Dimension] IN (0, 1, 2, 3)");
                    table.CheckConstraint("CK_Units_System", "[System] IN (0, 1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "UnitAliases",
                columns: table => new
                {
                    Alias = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, collation: "Latin1_General_100_BIN2"),
                    UnitCode = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CaseSensitive = table.Column<bool>(type: "bit", nullable: false),
                    Since = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitAliases", x => x.Alias);
                    table.CheckConstraint("CK_UnitAliases_Case", "[CaseSensitive] = 1 OR [Alias] = LOWER([Alias]) COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_UnitAliases_Trimmed", "LEN([Alias]) > 0 AND DATALENGTH([Alias]) = DATALENGTH(LTRIM(RTRIM([Alias])))");
                    table.ForeignKey(
                        name: "FK_UnitAliases_Units_UnitCode",
                        column: x => x.UnitCode,
                        principalTable: "Units",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Allergens",
                columns: new[] { "Code", "Name", "Since" },
                values: new object[,]
                {
                    { "celery", "Celery", 1 },
                    { "crustacean-shellfish", "Crustacean shellfish", 1 },
                    { "eggs", "Eggs", 1 },
                    { "fish", "Fish", 1 },
                    { "gluten", "Gluten", 1 },
                    { "lupin", "Lupin", 1 },
                    { "milk", "Milk", 1 },
                    { "mollusks", "Mollusks", 1 },
                    { "mustard", "Mustard", 1 },
                    { "peanuts", "Peanuts", 1 },
                    { "sesame", "Sesame", 1 },
                    { "soy", "Soy", 1 },
                    { "sulfites", "Sulfites", 1 },
                    { "tree-nuts", "Tree nuts", 1 },
                    { "wheat", "Wheat", 1 }
                });

            migrationBuilder.InsertData(
                table: "Courses",
                columns: new[] { "Code", "Name", "Since" },
                values: new object[,]
                {
                    { "appetizer", "Appetizer", 1 },
                    { "bread", "Bread", 1 },
                    { "breakfast", "Breakfast", 1 },
                    { "brunch", "Brunch", 1 },
                    { "dessert", "Dessert", 1 },
                    { "drink", "Drink", 1 },
                    { "main-course", "Main course", 1 },
                    { "salad", "Salad", 1 },
                    { "sauce", "Sauce", 1 },
                    { "side-dish", "Side dish", 1 },
                    { "snack", "Snack", 1 },
                    { "soup", "Soup", 1 }
                });

            migrationBuilder.InsertData(
                table: "Cuisines",
                columns: new[] { "Code", "Name", "Since" },
                values: new object[,]
                {
                    { "american", "American", 1 },
                    { "british", "British", 1 },
                    { "cajun-creole", "Cajun and Creole", 1 },
                    { "caribbean", "Caribbean", 1 },
                    { "chinese", "Chinese", 1 },
                    { "filipino", "Filipino", 1 },
                    { "french", "French", 1 },
                    { "german", "German", 1 },
                    { "greek", "Greek", 1 },
                    { "indian", "Indian", 1 },
                    { "irish", "Irish", 1 },
                    { "italian", "Italian", 1 },
                    { "japanese", "Japanese", 1 },
                    { "korean", "Korean", 1 },
                    { "latin-american", "Latin American", 1 },
                    { "mediterranean", "Mediterranean", 1 },
                    { "mexican", "Mexican", 1 },
                    { "middle-eastern", "Middle Eastern", 1 },
                    { "north-african", "North African", 1 },
                    { "portuguese", "Portuguese", 1 },
                    { "southern", "Southern", 1 },
                    { "spanish", "Spanish", 1 },
                    { "tex-mex", "Tex-Mex", 1 },
                    { "thai", "Thai", 1 },
                    { "vietnamese", "Vietnamese", 1 }
                });

            migrationBuilder.InsertData(
                table: "Diets",
                columns: new[] { "Code", "Name", "SchemaOrgDiet", "Since" },
                values: new object[,]
                {
                    { "dairy-free", "Dairy-free", null, 1 },
                    { "diabetic", "Diabetic", "DiabeticDiet", 1 },
                    { "gluten-free", "Gluten-free", "GlutenFreeDiet", 1 },
                    { "halal", "Halal", "HalalDiet", 1 },
                    { "hindu", "Hindu", "HinduDiet", 1 },
                    { "keto", "Keto", null, 1 },
                    { "kosher", "Kosher", "KosherDiet", 1 },
                    { "low-calorie", "Low-calorie", "LowCalorieDiet", 1 },
                    { "low-fat", "Low-fat", "LowFatDiet", 1 },
                    { "low-lactose", "Low-lactose", "LowLactoseDiet", 1 },
                    { "low-salt", "Low-salt", "LowSaltDiet", 1 },
                    { "paleo", "Paleo", null, 1 },
                    { "pescatarian", "Pescatarian", null, 1 },
                    { "vegan", "Vegan", "VeganDiet", 1 },
                    { "vegetarian", "Vegetarian", "VegetarianDiet", 1 }
                });

            migrationBuilder.InsertData(
                table: "Units",
                columns: new[] { "Code", "Abbreviation", "Dimension", "Plural", "Since", "Singular", "System" },
                values: new object[,]
                {
                    { "bunch", null, 3, "bunches", 1, "bunch", 2 },
                    { "can", null, 3, "cans", 1, "can", 2 },
                    { "celsius", "°C", 2, "degrees Celsius", 1, "degree Celsius", 1 },
                    { "clove", null, 3, "cloves", 1, "clove", 2 },
                    { "cup", null, 0, "cups", 1, "cup", 0 },
                    { "dash", null, 3, "dashes", 1, "dash", 2 },
                    { "deciliter", "dl", 0, "deciliters", 1, "deciliter", 1 },
                    { "drop", null, 3, "drops", 1, "drop", 2 },
                    { "fahrenheit", "°F", 2, "degrees Fahrenheit", 1, "degree Fahrenheit", 0 },
                    { "fluid-ounce", "fl oz", 0, "fluid ounces", 1, "fluid ounce", 0 },
                    { "gallon", "gal", 0, "gallons", 1, "gallon", 0 },
                    { "gram", "g", 1, "grams", 1, "gram", 1 },
                    { "handful", null, 3, "handfuls", 1, "handful", 2 },
                    { "head", null, 3, "heads", 1, "head", 2 },
                    { "jar", null, 3, "jars", 1, "jar", 2 },
                    { "kilogram", "kg", 1, "kilograms", 1, "kilogram", 1 },
                    { "liter", "L", 0, "liters", 1, "liter", 1 },
                    { "milliliter", "ml", 0, "milliliters", 1, "milliliter", 1 },
                    { "ounce", "oz", 1, "ounces", 1, "ounce", 0 },
                    { "package", "pkg", 3, "packages", 1, "package", 2 },
                    { "piece", "pc", 3, "pieces", 1, "piece", 2 },
                    { "pinch", null, 3, "pinches", 1, "pinch", 2 },
                    { "pint", "pt", 0, "pints", 1, "pint", 0 },
                    { "pound", "lb", 1, "pounds", 1, "pound", 0 },
                    { "quart", "qt", 0, "quarts", 1, "quart", 0 },
                    { "slice", null, 3, "slices", 1, "slice", 2 },
                    { "sprig", null, 3, "sprigs", 1, "sprig", 2 },
                    { "stick", null, 3, "sticks", 1, "stick", 2 },
                    { "tablespoon", "tbsp", 0, "tablespoons", 1, "tablespoon", 0 },
                    { "teaspoon", "tsp", 0, "teaspoons", 1, "teaspoon", 0 }
                });

            migrationBuilder.InsertData(
                table: "UnitAliases",
                columns: new[] { "Alias", "CaseSensitive", "Since", "UnitCode" },
                values: new object[,]
                {
                    { "° c", false, 1, "celsius" },
                    { "° f", false, 1, "fahrenheit" },
                    { "°c", false, 1, "celsius" },
                    { "°f", false, 1, "fahrenheit" },
                    { "bunch", false, 1, "bunch" },
                    { "bunches", false, 1, "bunch" },
                    { "c", false, 1, "cup" },
                    { "c.", false, 1, "cup" },
                    { "can", false, 1, "can" },
                    { "cans", false, 1, "can" },
                    { "celsius", false, 1, "celsius" },
                    { "centigrade", false, 1, "celsius" },
                    { "clove", false, 1, "clove" },
                    { "cloves", false, 1, "clove" },
                    { "cup", false, 1, "cup" },
                    { "cups", false, 1, "cup" },
                    { "dash", false, 1, "dash" },
                    { "dashes", false, 1, "dash" },
                    { "deciliter", false, 1, "deciliter" },
                    { "deciliters", false, 1, "deciliter" },
                    { "decilitre", false, 1, "deciliter" },
                    { "decilitres", false, 1, "deciliter" },
                    { "deg c", false, 1, "celsius" },
                    { "deg f", false, 1, "fahrenheit" },
                    { "degree c", false, 1, "celsius" },
                    { "degree celsius", false, 1, "celsius" },
                    { "degree f", false, 1, "fahrenheit" },
                    { "degree fahrenheit", false, 1, "fahrenheit" },
                    { "degrees c", false, 1, "celsius" },
                    { "degrees celsius", false, 1, "celsius" },
                    { "degrees f", false, 1, "fahrenheit" },
                    { "degrees fahrenheit", false, 1, "fahrenheit" },
                    { "dl", false, 1, "deciliter" },
                    { "dl.", false, 1, "deciliter" },
                    { "drop", false, 1, "drop" },
                    { "drops", false, 1, "drop" },
                    { "fahrenheit", false, 1, "fahrenheit" },
                    { "fl oz", false, 1, "fluid-ounce" },
                    { "fl oz.", false, 1, "fluid-ounce" },
                    { "fl. oz", false, 1, "fluid-ounce" },
                    { "fl. oz.", false, 1, "fluid-ounce" },
                    { "fl.oz.", false, 1, "fluid-ounce" },
                    { "fluid ounce", false, 1, "fluid-ounce" },
                    { "fluid ounces", false, 1, "fluid-ounce" },
                    { "g", false, 1, "gram" },
                    { "g.", false, 1, "gram" },
                    { "gal", false, 1, "gallon" },
                    { "gal.", false, 1, "gallon" },
                    { "gallon", false, 1, "gallon" },
                    { "gallons", false, 1, "gallon" },
                    { "gals", false, 1, "gallon" },
                    { "gals.", false, 1, "gallon" },
                    { "gram", false, 1, "gram" },
                    { "gramme", false, 1, "gram" },
                    { "grammes", false, 1, "gram" },
                    { "grams", false, 1, "gram" },
                    { "handful", false, 1, "handful" },
                    { "handfuls", false, 1, "handful" },
                    { "head", false, 1, "head" },
                    { "heads", false, 1, "head" },
                    { "jar", false, 1, "jar" },
                    { "jars", false, 1, "jar" },
                    { "kg", false, 1, "kilogram" },
                    { "kg.", false, 1, "kilogram" },
                    { "kgs", false, 1, "kilogram" },
                    { "kgs.", false, 1, "kilogram" },
                    { "kilo", false, 1, "kilogram" },
                    { "kilogram", false, 1, "kilogram" },
                    { "kilogramme", false, 1, "kilogram" },
                    { "kilogrammes", false, 1, "kilogram" },
                    { "kilograms", false, 1, "kilogram" },
                    { "kilos", false, 1, "kilogram" },
                    { "l", false, 1, "liter" },
                    { "l.", false, 1, "liter" },
                    { "lb", false, 1, "pound" },
                    { "lb.", false, 1, "pound" },
                    { "lbs", false, 1, "pound" },
                    { "lbs.", false, 1, "pound" },
                    { "liter", false, 1, "liter" },
                    { "liters", false, 1, "liter" },
                    { "litre", false, 1, "liter" },
                    { "litres", false, 1, "liter" },
                    { "milliliter", false, 1, "milliliter" },
                    { "milliliters", false, 1, "milliliter" },
                    { "millilitre", false, 1, "milliliter" },
                    { "millilitres", false, 1, "milliliter" },
                    { "ml", false, 1, "milliliter" },
                    { "ml.", false, 1, "milliliter" },
                    { "mls", false, 1, "milliliter" },
                    { "ºc", false, 1, "celsius" },
                    { "ºf", false, 1, "fahrenheit" },
                    { "ounce", false, 1, "ounce" },
                    { "ounces", false, 1, "ounce" },
                    { "oz", false, 1, "ounce" },
                    { "oz.", false, 1, "ounce" },
                    { "ozs", false, 1, "ounce" },
                    { "ozs.", false, 1, "ounce" },
                    { "package", false, 1, "package" },
                    { "packages", false, 1, "package" },
                    { "pc", false, 1, "piece" },
                    { "pc.", false, 1, "piece" },
                    { "pcs", false, 1, "piece" },
                    { "pcs.", false, 1, "piece" },
                    { "piece", false, 1, "piece" },
                    { "pieces", false, 1, "piece" },
                    { "pinch", false, 1, "pinch" },
                    { "pinches", false, 1, "pinch" },
                    { "pint", false, 1, "pint" },
                    { "pints", false, 1, "pint" },
                    { "pkg", false, 1, "package" },
                    { "pkg.", false, 1, "package" },
                    { "pkgs", false, 1, "package" },
                    { "pkgs.", false, 1, "package" },
                    { "pound", false, 1, "pound" },
                    { "pounds", false, 1, "pound" },
                    { "pt", false, 1, "pint" },
                    { "pt.", false, 1, "pint" },
                    { "pts", false, 1, "pint" },
                    { "pts.", false, 1, "pint" },
                    { "qt", false, 1, "quart" },
                    { "qt.", false, 1, "quart" },
                    { "qts", false, 1, "quart" },
                    { "qts.", false, 1, "quart" },
                    { "quart", false, 1, "quart" },
                    { "quarts", false, 1, "quart" },
                    { "slice", false, 1, "slice" },
                    { "slices", false, 1, "slice" },
                    { "sprig", false, 1, "sprig" },
                    { "sprigs", false, 1, "sprig" },
                    { "stick", false, 1, "stick" },
                    { "sticks", false, 1, "stick" },
                    { "t", true, 1, "teaspoon" },
                    { "T", true, 1, "tablespoon" },
                    { "tablespoon", false, 1, "tablespoon" },
                    { "tablespoonful", false, 1, "tablespoon" },
                    { "tablespoonfuls", false, 1, "tablespoon" },
                    { "tablespoons", false, 1, "tablespoon" },
                    { "tbl", false, 1, "tablespoon" },
                    { "tbl.", false, 1, "tablespoon" },
                    { "tbs", false, 1, "tablespoon" },
                    { "tbs.", false, 1, "tablespoon" },
                    { "tbsp", false, 1, "tablespoon" },
                    { "tbsp.", false, 1, "tablespoon" },
                    { "tbsps", false, 1, "tablespoon" },
                    { "tbsps.", false, 1, "tablespoon" },
                    { "teaspoon", false, 1, "teaspoon" },
                    { "teaspoonful", false, 1, "teaspoon" },
                    { "teaspoonfuls", false, 1, "teaspoon" },
                    { "teaspoons", false, 1, "teaspoon" },
                    { "tsp", false, 1, "teaspoon" },
                    { "tsp.", false, 1, "teaspoon" },
                    { "tsps", false, 1, "teaspoon" },
                    { "tsps.", false, 1, "teaspoon" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Allergens_Name",
                table: "Allergens",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Courses_Name",
                table: "Courses",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cuisines_Name",
                table: "Cuisines",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Diets_Name",
                table: "Diets",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitAliases_UnitCode",
                table: "UnitAliases",
                column: "UnitCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Allergens");

            migrationBuilder.DropTable(
                name: "Courses");

            migrationBuilder.DropTable(
                name: "Cuisines");

            migrationBuilder.DropTable(
                name: "Diets");

            migrationBuilder.DropTable(
                name: "UnitAliases");

            migrationBuilder.DropTable(
                name: "Units");
        }
    }
}
