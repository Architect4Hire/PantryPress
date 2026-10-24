using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PantryPress.Domain.Managers.Outbox;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public const string TableName = "OutboxMessages";
    public const string ReadyIndexName = "IX_OutboxMessages_Ready";
    public const string FinishedIndexName = "IX_OutboxMessages_Finished";

    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable(TableName);
        builder.HasKey(m => m.Id);

        // Type names are compared exactly, as handlers are registered.
        builder.Property(m => m.Type)
            .HasMaxLength(OutboxRules.MaxTypeLength)
            .IsUnicode(false)
            .UseCollation("Latin1_General_100_BIN2");
        builder.Property(m => m.LastError).HasMaxLength(OutboxRules.MaxErrorLength);
        builder.Property(m => m.TraceParent).HasMaxLength(OutboxRules.MaxTraceParentLength).IsUnicode(false);

        // The claim reads pending messages that are due, oldest first, of the types one Worker handles. Filtered, so the
        // index holds only the backlog however many delivered rows are waiting for the purge.
        builder.HasIndex(m => new { m.AvailableAt, m.Type })
            .HasDatabaseName(ReadyIndexName)
            .HasFilter("[State] = 1");

        // The purge reads finished messages by state, oldest first.
        builder.HasIndex(m => new { m.State, m.CompletedAt })
            .HasDatabaseName(FinishedIndexName)
            .HasFilter("[CompletedAt] IS NOT NULL");
    }
}
