using Inmobiliaria.Domain.Leasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>
/// contract-documents: One-to-Many, Document Metadata, Uploader as Plain Identifier.
/// <c>uploaded_by</c> is a plain text column — no FK, since no user entity exists yet.
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

        builder.Property(d => d.UploadedBy)
            .HasColumnName("uploaded_by")
            .IsRequired();

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(d => d.ContractId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
