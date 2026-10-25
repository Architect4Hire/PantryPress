using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PantryPress.MigrationService.Migrations
{
    /// <inheritdoc />
    public partial class SiteMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SiteMembers",
                columns: table => new
                {
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteMembers", x => new { x.SiteId, x.AccountId });
                    table.CheckConstraint("CK_SiteMembers_Role", "[Role] IN (0, 1, 2)");
                    table.CheckConstraint("CK_SiteMembers_Status", "[Status] IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_SiteMembers_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SiteMembers_AccountId",
                table: "SiteMembers",
                column: "AccountId");

            // Every site keeps an active Owner (SITE-010; PP2.3). A check before the save cannot see a concurrent one: two
            // owners demoting each other would each still count the other. Only a row that was an active Owner can take a
            // site's last one away, so only those sites are checked. The read takes shared locks even where the database
            // reads committed snapshots, as Azure SQL does by default: a change made after another has locked its row waits
            // for it and then sees it, and two made at the same instant deadlock, so one is rolled back (1205) - either way
            // the site keeps an owner. THROW inside a trigger ends the whole transaction, not just the statement, with
            // anything staged beside it. A site marked Deleted (5) is exempt, so erasure can remove its members; Owner and
            // Active are both 0. This guards losing an owner, not having one: a site and its first Owner are written
            // together by the facade that creates it (PP2.10).
            migrationBuilder.Sql(
                """
                CREATE TRIGGER [TR_SiteMembers_KeepAnOwner] ON [SiteMembers]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1
                        FROM (SELECT DISTINCT [SiteId] FROM deleted WHERE [Role] = 0 AND [Status] = 0) AS changed
                        JOIN [Sites] AS s ON s.[Id] = changed.[SiteId]
                        WHERE s.[Status] <> 5
                          AND NOT EXISTS (
                              SELECT 1
                              FROM [SiteMembers] AS m WITH (READCOMMITTEDLOCK)
                              WHERE m.[SiteId] = changed.[SiteId] AND m.[Role] = 0 AND m.[Status] = 0))
                        THROW 51003, N'A site needs at least one active owner.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back would take every site's members, and with them who may reach it. Refuse while there are any.
            // Dropping the table drops its trigger with it.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [SiteMembers])
                    THROW 51004, N'SiteMembers has rows; rolling back the SiteMembers migration would erase every membership.', 1;
                """);
            migrationBuilder.DropTable(
                name: "SiteMembers");
        }
    }
}
