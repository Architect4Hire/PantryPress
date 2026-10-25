using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PantryPress.MigrationService.Migrations
{
    /// <inheritdoc />
    public partial class Sites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Tagline = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TimeZone = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Locale = table.Column<string>(type: "varchar(35)", unicode: false, maxLength: 35, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => x.Id);
                    table.CheckConstraint("CK_Sites_Slug_Shape", "LEN([Slug]) BETWEEN 3 AND 40 AND [Slug] NOT LIKE '%[^a-z0-9-]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_Sites_Status", "[Status] IN (0, 1, 2, 3, 4, 5)");
                });

            migrationBuilder.CreateIndex(
                name: "UX_Sites_Slug",
                table: "Sites",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Every site-owned row hangs off a site, so rolling back would erase every tenant at once. Refuse while there
            // is anything in it: removing real sites is an operator decision, never a side effect of a rollback.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [Sites])
                    THROW 51002, N'Sites has rows; rolling back the Sites migration would erase every site.', 1;
                """);
            migrationBuilder.DropTable(
                name: "Sites");
        }
    }
}
