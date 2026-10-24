using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PantryPress.Domain.Managers.Idempotency;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public const string TableName = "IdempotencyRecords";
    public const string ExpiresAtIndexName = "IX_IdempotencyRecords_ExpiresAt";

    /// <summary>
    /// Keys are compared byte for byte: "Abc" and "abc" are two keys, as a client means them (the database's default
    /// collation would fold them into one, and replay one caller's answer for the other's request).
    /// </summary>
    public const string BinaryCollation = "Latin1_General_100_BIN2";

    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable(TableName);

        // The caller leads: every lookup is for one caller's key, and the primary key is what makes a concurrent
        // claim of the same key lose rather than duplicate.
        builder.HasKey(r => new { r.CallerScope, r.Key });
        builder.Property(r => r.CallerScope)
            .HasMaxLength(IdempotencyRequest.MaxCallerScopeLength)
            .IsUnicode(false)
            .UseCollation(BinaryCollation);
        builder.Property(r => r.Key)
            .HasMaxLength(IdempotencyRequest.MaxKeyLength)
            .IsUnicode(false)
            .UseCollation(BinaryCollation);
        builder.Property(r => r.Fingerprint)
            .HasMaxLength(IdempotencyRequest.FingerprintLength)
            .IsFixedLength();
        builder.Property(r => r.ResponseHeaders).HasMaxLength(StoredResponse.MaxHeadersLength);

        // The purge reads expired records oldest first.
        builder.HasIndex(r => r.ExpiresAt).HasDatabaseName(ExpiresAtIndexName);
    }
}
