using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PantryPress.MigrationService.Migrations
{
    /// <inheritdoc />
    public partial class RecipeLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                table: "Recipes",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByAccountId",
                table: "Recipes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_Library",
                table: "Recipes",
                columns: new[] { "SiteId", "UpdatedAt", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Recipes_ArchivedAt",
                table: "Recipes",
                sql: "[ArchivedAt] IS NULL OR [ArchivedAt] >= [CreatedAt]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the two columns would put every archived recipe back in its library and forget who made each one, so
            // an Author could change anyone's. Refuse while any recipe holds either, holding the table so none is archived
            // or made between the check and the drop. With none, the columns hold nothing and the drop loses nothing.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [Recipes] WITH (TABLOCKX, HOLDLOCK) WHERE [ArchivedAt] IS NOT NULL OR [CreatedByAccountId] IS NOT NULL)
                    THROW 51015, N'Recipes has archived or attributed rows; rolling back the RecipeLibrary migration would lose them.', 1;
                """);
            migrationBuilder.DropIndex(
                name: "IX_Recipes_Library",
                table: "Recipes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Recipes_ArchivedAt",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "CreatedByAccountId",
                table: "Recipes");
        }
    }
}
