using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PantryPress.MigrationService.Migrations
{
    /// <inheritdoc />
    public partial class AuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Action = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ActorKind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TargetKind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TargetId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Reason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    TraceId = table.Column<string>(type: "char(32)", unicode: false, fixedLength: true, maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Actor",
                table: "AuditEntries",
                columns: new[] { "ActorId", "Id" },
                filter: "[ActorId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Site",
                table: "AuditEntries",
                columns: new[] { "SiteId", "Id" },
                filter: "[SiteId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Target",
                table: "AuditEntries",
                columns: new[] { "TargetKind", "TargetId", "Id" });

            // Append-only in the database too (PP1.11): no update or delete reaches a row, including the bulk ones that
            // never pass through the context's save. THROW inside a trigger rolls the whole statement back. OPS-011's
            // erasure, when it comes, changes this deliberately in a migration of its own.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER [TR_AuditEntries_AppendOnly] ON [AuditEntries]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, N'The audit log is append-only: an entry is never changed or removed.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // DROP TABLE fires no delete trigger, so rolling back would erase the trail the trigger exists to keep. Refuse
            // while there is anything in it: emptying a real audit log is an operator decision, never a side effect of a
            // rollback. Dropping the table drops its trigger with it.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [AuditEntries])
                    THROW 51001, N'The audit log has entries; rolling back the AuditLog migration would erase them.', 1;
                """);
            migrationBuilder.DropTable(
                name: "AuditEntries");
        }
    }
}
