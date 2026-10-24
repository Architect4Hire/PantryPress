using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.Domain.Modules.Accounts;

internal sealed class RevokedSessionConfiguration : IEntityTypeConfiguration<RevokedSession>
{
    public const string TableName = "RevokedSessions";
    public const string ExpiresAtIndexName = "IX_RevokedSessions_ExpiresAt";

    /// <summary>Session ids are compared byte for byte, as base64url means them.</summary>
    public const string BinaryCollation = "Latin1_General_100_BIN2";

    public void Configure(EntityTypeBuilder<RevokedSession> builder)
    {
        builder.ToTable(TableName);

        // Every request a signed-in caller makes looks its session id up here, so the id is the clustered key.
        builder.HasKey(r => r.SessionId);
        builder.Property(r => r.SessionId)
            .HasMaxLength(SessionLimits.MaxSessionIdLength)
            .IsUnicode(false)
            .UseCollation(BinaryCollation);

        // The purge reads expired rows oldest first.
        builder.HasIndex(r => r.ExpiresAt).HasDatabaseName(ExpiresAtIndexName);
    }
}
