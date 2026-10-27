using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PantryPress.MigrationService.Migrations
{
    /// <inheritdoc />
    public partial class SiteHosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SiteHosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Host = table.Column<string>(type: "varchar(253)", unicode: false, maxLength: 253, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    IsCanonical = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteHosts", x => x.Id);
                    table.CheckConstraint("CK_SiteHosts_Canonical", "[IsCanonical] = 0 OR ([Status] = 0 AND [Kind] <> 2)");
                    table.CheckConstraint("CK_SiteHosts_Host_Shape", "DATALENGTH([Host]) BETWEEN 3 AND 253 AND [Host] NOT LIKE '%[^a-z0-9.-]%' COLLATE Latin1_General_100_BIN2 AND [Host] LIKE '%_._%' AND [Host] NOT LIKE '.%' AND [Host] NOT LIKE '%.' AND [Host] NOT LIKE '%..%' AND [Host] NOT LIKE '-%' AND [Host] NOT LIKE '%-' AND [Host] NOT LIKE '%.-%' AND [Host] NOT LIKE '%-.%' AND [Host] NOT LIKE '%[^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.][^.]%' AND SUBSTRING(REVERSE([Host]), CHARINDEX('.', REVERSE([Host])) - 1, 1) LIKE '[a-z]'");
                    table.CheckConstraint("CK_SiteHosts_Kind", "[Kind] IN (0, 1, 2)");
                    table.CheckConstraint("CK_SiteHosts_Status", "[Status] IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_SiteHosts_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SiteHosts_SiteId_Kind",
                table: "SiteHosts",
                columns: new[] { "SiteId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "UX_SiteHosts_Host",
                table: "SiteHosts",
                column: "Host",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SiteHosts_SiteId_Canonical",
                table: "SiteHosts",
                column: "SiteId",
                unique: true,
                filter: "[IsCanonical] = 1");

            // A host never changes site or spelling, so which site a host names changes only by a row coming or going, and the
            // host index alone decides who holds a host. Its status, kind, and whether it is canonical change in place, so
            // anything cached about those must be refreshed on every write to the site's hosts (PP2.12). A host never becomes
            // or stops being a subdomain, the platform's own address, which needs no verifying. Each site that is not deleted keeps a canonical host: the
            // filtered index allows at most one, and this refuses losing it, so moving it is one statement that sets the new
            // and clears the old together. It guards losing one, not having one; creating a site writes its subdomain with it
            // (PP2.10). As with TR_SiteMembers_KeepAnOwner, the read takes locks, so it judges what is committed now and holds
            // it to the end of the transaction rather than reading a snapshot. Either refusal ends the whole transaction.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER [TR_SiteHosts_Guard] ON [SiteHosts]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM inserted)
                       AND (UPDATE([Id]) OR UPDATE([SiteId]) OR UPDATE([Host]) OR UPDATE([Kind]))
                       AND EXISTS (
                           SELECT 1
                           FROM inserted AS i
                           FULL JOIN deleted AS d ON d.[Id] = i.[Id]
                           WHERE i.[Id] IS NULL OR d.[Id] IS NULL OR i.[SiteId] <> d.[SiteId] OR i.[Host] <> d.[Host]
                              OR (i.[Kind] <> d.[Kind] AND (i.[Kind] = 0 OR d.[Kind] = 0)))
                        THROW 51008, N'A site host never changes its site or its host name, or becomes or stops being a subdomain.', 1;
                    IF EXISTS (
                        SELECT 1
                        FROM (SELECT DISTINCT [SiteId] FROM deleted WHERE [IsCanonical] = 1) AS changed
                        JOIN [Sites] AS s ON s.[Id] = changed.[SiteId]
                        WHERE s.[Status] <> 5
                          AND NOT EXISTS (
                              SELECT 1
                              FROM [SiteHosts] AS h WITH (READCOMMITTEDLOCK)
                              WHERE h.[SiteId] = changed.[SiteId] AND h.[IsCanonical] = 1))
                        THROW 51007, N'A site needs a canonical host.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back would take every site's hosts, and with them every public address. Refuse while there are any.
            // Dropping the table drops its trigger with it.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [SiteHosts])
                    THROW 51009, N'SiteHosts has rows; rolling back the SiteHosts migration would erase every site host.', 1;
                """);
            migrationBuilder.DropTable(
                name: "SiteHosts");
        }
    }
}
