using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PantryPress.MigrationService.Migrations
{
    /// <inheritdoc />
    public partial class RecipeRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecipeRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    SnapshotVersion = table.Column<int>(type: "int", nullable: false),
                    Snapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeRevisions", x => new { x.SiteId, x.Id });
                    table.CheckConstraint("CK_RecipeRevisions_Kind", "[Kind] IN (1, 2)");
                    table.CheckConstraint("CK_RecipeRevisions_Number", "[Number] >= 1");
                    table.CheckConstraint("CK_RecipeRevisions_Snapshot", "ISJSON([Snapshot]) = 1");
                    table.CheckConstraint("CK_RecipeRevisions_SnapshotVersion", "[SnapshotVersion] >= 1");
                    table.ForeignKey(
                        name: "FK_RecipeRevisions_Recipes_SiteId_RecipeId",
                        columns: x => new { x.SiteId, x.RecipeId },
                        principalTable: "Recipes",
                        principalColumns: new[] { "SiteId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeRevisions_Kind",
                table: "RecipeRevisions",
                columns: new[] { "SiteId", "RecipeId", "Kind", "Number" });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeRevisions_Number",
                table: "RecipeRevisions",
                columns: new[] { "SiteId", "RecipeId", "Number" },
                unique: true);

            // A revision is written once and never changed or removed (RCP-007). The context refuses a change, a removal,
            // and a bulk write before any of them is sent; this refuses everything that never passes through the context,
            // raw SQL included. THROW inside a trigger rolls back the whole transaction. Erasing a site or an account (ADR 0003,
            // PP19.11a) changes this deliberately, in a migration of its own.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER [TR_RecipeRevisions_Immutable] ON [RecipeRevisions]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51013, N'A recipe revision is never changed or removed once written.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // DROP TABLE fires no delete trigger, so rolling back would erase what readers were shown and what a post pins
            // (PP-D04). Refuse while there is any, holding the table so none is added between the check and the drop.
            // Dropping the table drops its trigger with it.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [RecipeRevisions] WITH (TABLOCKX, HOLDLOCK))
                    THROW 51014, N'RecipeRevisions has rows; rolling back the RecipeRevisions migration would erase every recipe revision.', 1;
                """);
            migrationBuilder.DropTable(
                name: "RecipeRevisions");
        }
    }
}
