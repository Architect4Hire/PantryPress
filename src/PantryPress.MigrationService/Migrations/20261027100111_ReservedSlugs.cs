using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PantryPress.MigrationService.Migrations
{
    /// <inheritdoc />
    public partial class ReservedSlugs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Sites_Slug_Shape",
                table: "Sites");

            migrationBuilder.CreateTable(
                name: "ReservedSlugs",
                columns: table => new
                {
                    Slug = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    Since = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservedSlugs", x => x.Slug);
                    table.CheckConstraint("CK_ReservedSlugs_Reason", "[Reason] IN (0, 1, 2, 3)");
                });

            // A slug a site already holds is never reserved by a list edit (PP2.8): the migration that tries fails, so taking
            // an address from a site is decided on purpose, with a plan for that site. A deleted site counts, since it still
            // holds the unique index. Created before the seed, so the first list is checked too. The read takes shared locks
            // even under committed snapshots, as in TR_SiteMembers_KeepAnOwner, so a site claiming the slug at the same
            // moment is seen or waited for. The message names the slug for the operator whose migration it stopped; it never
            // reaches a client.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER [TR_ReservedSlugs_NotHeld] ON [ReservedSlugs]
                AFTER INSERT, UPDATE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    DECLARE @held varchar(40) = (
                        SELECT TOP (1) i.[Slug]
                        FROM inserted AS i
                        JOIN [Sites] AS s WITH (READCOMMITTEDLOCK) ON s.[Slug] = i.[Slug]
                        ORDER BY i.[Slug]);
                    IF @held IS NOT NULL
                    BEGIN
                        DECLARE @message nvarchar(200) =
                            CONCAT(N'A site holds the slug ''', @held, N'''; reserving it needs a plan for that site first.');
                        THROW 51006, @message, 1;
                    END
                END
                """);

            migrationBuilder.InsertData(
                table: "ReservedSlugs",
                columns: new[] { "Slug", "Reason", "Since" },
                values: new object[,]
                {
                    { "about", 3, 1 },
                    { "abuse", 2, 1 },
                    { "account", 2, 1 },
                    { "accounts", 2, 1 },
                    { "admin", 0, 1 },
                    { "administrator", 2, 1 },
                    { "api", 0, 1 },
                    { "app", 0, 1 },
                    { "assets", 1, 1 },
                    { "auth", 2, 1 },
                    { "autoconfig", 1, 1 },
                    { "autodiscover", 1, 1 },
                    { "beta", 0, 1 },
                    { "billing", 2, 1 },
                    { "blog", 3, 1 },
                    { "cache", 1, 1 },
                    { "careers", 3, 1 },
                    { "cdn", 1, 1 },
                    { "checkout", 2, 1 },
                    { "community", 3, 1 },
                    { "contact", 3, 1 },
                    { "cookies", 3, 1 },
                    { "creator-pantry", 2, 1 },
                    { "creatorpantry", 2, 1 },
                    { "dashboard", 3, 1 },
                    { "dev", 0, 1 },
                    { "developer", 3, 1 },
                    { "developers", 3, 1 },
                    { "dns", 1, 1 },
                    { "docs", 3, 1 },
                    { "edge", 1, 1 },
                    { "email", 1, 1 },
                    { "files", 1, 1 },
                    { "forum", 3, 1 },
                    { "ftp", 1, 1 },
                    { "health", 1, 1 },
                    { "help", 2, 1 },
                    { "helpdesk", 2, 1 },
                    { "hostmaster", 2, 1 },
                    { "images", 1, 1 },
                    { "imap", 1, 1 },
                    { "img", 1, 1 },
                    { "import", 3, 1 },
                    { "isatap", 1, 1 },
                    { "jobs", 3, 1 },
                    { "legal", 3, 1 },
                    { "localhost", 1, 1 },
                    { "login", 2, 1 },
                    { "logout", 2, 1 },
                    { "mail", 0, 1 },
                    { "media", 1, 1 },
                    { "moderator", 2, 1 },
                    { "mta-sts", 1, 1 },
                    { "news", 3, 1 },
                    { "no-reply", 2, 1 },
                    { "noreply", 2, 1 },
                    { "ns1", 1, 1 },
                    { "ns2", 1, 1 },
                    { "ns3", 1, 1 },
                    { "ns4", 1, 1 },
                    { "oauth", 2, 1 },
                    { "official", 2, 1 },
                    { "openpgpkey", 1, 1 },
                    { "operator", 2, 1 },
                    { "origin", 1, 1 },
                    { "pantry-press", 2, 1 },
                    { "pantrypress", 2, 1 },
                    { "password", 2, 1 },
                    { "payments", 2, 1 },
                    { "platform", 2, 1 },
                    { "pop3", 1, 1 },
                    { "postmaster", 2, 1 },
                    { "press", 3, 1 },
                    { "preview", 0, 1 },
                    { "pricing", 3, 1 },
                    { "privacy", 3, 1 },
                    { "prod", 0, 1 },
                    { "proxy", 1, 1 },
                    { "publish", 3, 1 },
                    { "register", 2, 1 },
                    { "root", 2, 1 },
                    { "sandbox", 0, 1 },
                    { "security", 2, 1 },
                    { "settings", 3, 1 },
                    { "sftp", 1, 1 },
                    { "shop", 3, 1 },
                    { "sign-in", 2, 1 },
                    { "sign-up", 2, 1 },
                    { "signin", 2, 1 },
                    { "signup", 2, 1 },
                    { "smtp", 1, 1 },
                    { "sso", 2, 1 },
                    { "staff", 2, 1 },
                    { "staging", 0, 1 },
                    { "static", 1, 1 },
                    { "status", 0, 1 },
                    { "store", 3, 1 },
                    { "studio", 3, 1 },
                    { "support", 2, 1 },
                    { "system", 2, 1 },
                    { "team", 2, 1 },
                    { "terms", 3, 1 },
                    { "uploads", 1, 1 },
                    { "verify", 2, 1 },
                    { "vpn", 1, 1 },
                    { "webmail", 1, 1 },
                    { "webmaster", 2, 1 },
                    { "wpad", 1, 1 },
                    { "www", 0, 1 }
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sites_Slug_Shape",
                table: "Sites",
                sql: "LEN([Slug]) BETWEEN 3 AND 40 AND [Slug] NOT LIKE '%[^a-z0-9-]%' COLLATE Latin1_General_100_BIN2 AND [Slug] NOT LIKE '-%' AND [Slug] NOT LIKE '%-' AND [Slug] NOT LIKE '%--%'");

            // The backstop under the slug policy (PP2.8): no new or changed slug may be a reserved one, whichever insert or
            // update wrote it. A bulk load must ask for triggers and checks (FireTriggers, CheckConstraints), and only an
            // operator can disable a trigger. A site whose slug is unchanged is let through. Both columns use the binary collation, so the comparison is
            // byte for byte, and the shape check already admits only lower-case. THROW inside a trigger ends the whole
            // transaction.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER [TR_Sites_SlugNotReserved] ON [Sites]
                AFTER INSERT, UPDATE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1
                        FROM inserted AS i
                        JOIN [ReservedSlugs] AS r WITH (READCOMMITTEDLOCK) ON r.[Slug] = i.[Slug]
                        WHERE NOT EXISTS (SELECT 1 FROM deleted AS d WHERE d.[Id] = i.[Id] AND d.[Slug] = i.[Slug]))
                        THROW 51005, N'That slug is reserved for PantryPress.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reference data only, so nothing is lost. Dropping the table drops its own trigger; the one on Sites goes first.
            migrationBuilder.Sql("DROP TRIGGER [TR_Sites_SlugNotReserved];");

            migrationBuilder.DropTable(
                name: "ReservedSlugs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Sites_Slug_Shape",
                table: "Sites");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sites_Slug_Shape",
                table: "Sites",
                sql: "LEN([Slug]) BETWEEN 3 AND 40 AND [Slug] NOT LIKE '%[^a-z0-9-]%' COLLATE Latin1_General_100_BIN2");
        }
    }
}
