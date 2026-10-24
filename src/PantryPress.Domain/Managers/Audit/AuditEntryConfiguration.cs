using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PantryPress.Domain.Managers.Audit;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public const string TableName = "AuditEntries";
    public const string AppendOnlyTriggerName = "TR_AuditEntries_AppendOnly";
    public const string SiteIndexName = "IX_AuditEntries_Site";
    public const string TargetIndexName = "IX_AuditEntries_Target";
    public const string ActorIndexName = "IX_AuditEntries_Actor";

    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        // The trigger, created by the AuditLog migration, refuses every update and delete in the database. Declaring it
        // keeps EF Core from using an OUTPUT clause that a table with triggers cannot take.
        builder.ToTable(TableName, table => table.HasTrigger(AppendOnlyTriggerName));
        builder.HasKey(e => e.Id);

        // Codes are compared exactly, as they are written.
        builder.Property(e => e.Action)
            .HasMaxLength(AuditRules.MaxActionLength)
            .IsUnicode(false)
            .UseCollation("Latin1_General_100_BIN2");
        builder.Property(e => e.ActorKind)
            .HasMaxLength(AuditRules.MaxActorKindLength)
            .IsUnicode(false)
            .UseCollation("Latin1_General_100_BIN2");
        builder.Property(e => e.TargetKind)
            .HasMaxLength(AuditRules.MaxTargetKindLength)
            .IsUnicode(false)
            .UseCollation("Latin1_General_100_BIN2");
        builder.Property(e => e.TargetId)
            .HasMaxLength(AuditRules.MaxTargetIdLength)
            .IsUnicode(false)
            .UseCollation("Latin1_General_100_BIN2");
        builder.Property(e => e.Reason).HasMaxLength(AuditRules.MaxReasonLength);
        builder.Property(e => e.TraceId)
            .HasMaxLength(AuditRules.TraceIdLength)
            .IsFixedLength()
            .IsUnicode(false);

        // The operator console's three ways in (OPS-006): a site's trail, an account's trail as the thing acted on,
        // and as the one acting. Newest first is a backwards scan of each.
        builder.HasIndex(e => new { e.SiteId, e.Id })
            .HasDatabaseName(SiteIndexName)
            .HasFilter("[SiteId] IS NOT NULL");
        builder.HasIndex(e => new { e.TargetKind, e.TargetId, e.Id })
            .HasDatabaseName(TargetIndexName);
        builder.HasIndex(e => new { e.ActorId, e.Id })
            .HasDatabaseName(ActorIndexName)
            .HasFilter("[ActorId] IS NOT NULL");
    }
}
