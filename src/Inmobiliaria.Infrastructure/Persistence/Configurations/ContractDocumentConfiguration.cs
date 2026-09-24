using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Leasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>
/// contract-documents: One-to-Many, Document Metadata, Uploader as a Relational Reference.
/// <c>uploaded_by_user_id</c> is a real FK to <see cref="AppUser"/> now that
/// <c>Domain/Access</c> exists (design Decision 8) — the plain-text placeholder this used to
/// be is gone. <c>ON DELETE RESTRICT</c>: deleting a user must never cascade into documents,
/// and users are never deleted anyway (deactivated only).
/// </summary>
public sealed class ContractDocumentConfiguration : IEntityTypeConfiguration<ContractDocument>
{
    public void Configure(EntityTypeBuilder<ContractDocument> builder)
    {
        builder.ToTable("contract_documents", t => t.HasCheckConstraint(
            "ck_contract_documents_kind",
            "kind IN ('Original', 'Addendum', 'TerminationNotice')"));

        builder.HasKey(d => d.Id);

        // Explicit mapping for the read-only (no-setter) properties: without it, EF's
        // constructor-binding discovery does not recognize them as mapped model properties
        // and migration generation fails with "no suitable constructor was found".
        builder.Property(d => d.StoragePath).IsRequired();
        builder.Property(d => d.FileName).IsRequired();
        builder.Property(d => d.ContentType).IsRequired();
        builder.Property(d => d.UploadedAt).IsRequired();

        builder.Property(d => d.Kind)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(d => d.UploadedByUserId)
            .HasColumnName("uploaded_by_user_id")
            .IsRequired();

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(d => d.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(d => d.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
