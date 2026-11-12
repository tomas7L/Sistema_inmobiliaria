using Inmobiliaria.Domain.Leasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>tenant_notices</c> (notificaciones design §3.1, D4). The append-only and stamp triggers, the grants and the API-role revoke
/// are raw SQL in the <c>AddTenantNotices</c> migration; this is what EF can say.
/// </summary>
public sealed class TenantNoticeEntryConfiguration : IEntityTypeConfiguration<TenantNoticeEntry>
{
    public void Configure(EntityTypeBuilder<TenantNoticeEntry> builder)
    {
        builder.ToTable("tenant_notices");

        builder.HasKey(n => n.Id);

        // 16: amount_owed is 11 characters, with room for the kinds spec N6 may add.
        builder.Property(n => n.Kind).HasMaxLength(16).IsRequired();
        builder.Property(n => n.Recipient).HasColumnType("text").IsRequired();
        builder.Property(n => n.Total).HasPrecision(14, 2);
        builder.Property(n => n.Recargo).HasPrecision(14, 2);

        // Stamped by the tenant_notices_stamp trigger, which overwrites whatever an INSERT supplies (spec Requirement 7).
        builder.Property(n => n.RecordedBy).HasColumnType("name").ValueGeneratedOnAdd().IsRequired();
        builder.Property(n => n.RecordedAt).HasColumnType("timestamp with time zone").ValueGeneratedOnAdd();

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(n => n.ContractId)
            .OnDelete(DeleteBehavior.Restrict);

        // No notice twice (spec Requirement 4): a second PC's insert waits on the first's open transaction, then fails here
        // as 23505 (design D6).
        builder.HasIndex(n => new { n.ContractId, n.Kind, n.NoticeMonth })
            .IsUnique()
            .HasDatabaseName("ux_tenant_notices_contract_kind_month");

        AppendOnly.Apply(builder);
    }
}
