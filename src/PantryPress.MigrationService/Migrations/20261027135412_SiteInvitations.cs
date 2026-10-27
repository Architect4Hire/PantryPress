using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PantryPress.MigrationService.Migrations
{
    /// <inheritdoc />
    public partial class SiteInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SiteInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    InvitedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AcceptedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteInvitations", x => new { x.SiteId, x.Id });
                    table.CheckConstraint("CK_SiteInvitations_Accepted", "([Status] = 1 AND [AcceptedBy] IS NOT NULL AND [AcceptedAt] IS NOT NULL) OR ([Status] <> 1 AND [AcceptedBy] IS NULL AND [AcceptedAt] IS NULL)");
                    table.CheckConstraint("CK_SiteInvitations_Expiry", "[ExpiresAt] > [CreatedAt]");
                    table.CheckConstraint("CK_SiteInvitations_Role", "[Role] IN (0, 1, 2)");
                    table.CheckConstraint("CK_SiteInvitations_Status", "[Status] IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_SiteInvitations_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_SiteInvitations_SiteId_NormalizedEmail_Pending",
                table: "SiteInvitations",
                columns: new[] { "SiteId", "NormalizedEmail" },
                unique: true,
                filter: "[Status] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back would take every site's invitations, open ones included, and the record of who joined through
            // which. Refuse while there are any.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [SiteInvitations])
                    THROW 51010, N'SiteInvitations has rows; rolling back the SiteInvitations migration would erase every invitation.', 1;
                """);
            migrationBuilder.DropTable(
                name: "SiteInvitations");
        }
    }
}
