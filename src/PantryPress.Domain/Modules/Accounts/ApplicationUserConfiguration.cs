using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PantryPress.Domain.Modules.Accounts;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public const string EmailIndexName = "EmailIndex";

    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        // Identity's EmailIndex is not unique; its unique-email check runs only in UserManager, so two concurrent
        // sign-ups could create duplicate accounts. The database is the last word. Null emails stay allowed.
        builder.HasIndex(u => u.NormalizedEmail)
            .HasDatabaseName(EmailIndexName)
            .IsUnique()
            .HasFilter("[NormalizedEmail] IS NOT NULL");
    }
}
