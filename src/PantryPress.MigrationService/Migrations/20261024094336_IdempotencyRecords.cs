using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PantryPress.MigrationService.Migrations
{
    /// <inheritdoc />
    public partial class IdempotencyRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    CallerScope = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Key = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Fingerprint = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResponseStatus = table.Column<int>(type: "int", nullable: true),
                    ResponseHeaders = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ResponseBody = table.Column<byte[]>(type: "varbinary(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => new { x.CallerScope, x.Key });
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_ExpiresAt",
                table: "IdempotencyRecords",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdempotencyRecords");
        }
    }
}
