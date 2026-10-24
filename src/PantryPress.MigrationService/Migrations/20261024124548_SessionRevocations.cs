using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PantryPress.MigrationService.Migrations
{
    /// <inheritdoc />
    public partial class SessionRevocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RevokedSessions",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevokedSessions", x => x.SessionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RevokedSessions_ExpiresAt",
                table: "RevokedSessions",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RevokedSessions");
        }
    }
}
