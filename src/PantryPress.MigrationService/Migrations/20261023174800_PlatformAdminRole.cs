using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PantryPress.MigrationService.Migrations
{
    /// <inheritdoc />
    public partial class PlatformAdminRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[] { new Guid("b2f1a6c4-7e5d-4a3b-9c21-5f0d8e4a7b31"), "f0c4d2f4-6a51-4a9a-9c3f-2a1be0d27c6f", "PlatformAdmin", "PLATFORMADMIN" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: new Guid("b2f1a6c4-7e5d-4a3b-9c21-5f0d8e4a7b31"));
        }
    }
}
