using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>
/// contract-account: An Account Belongs to Exactly One Contract.
///
/// The unique index on <c>contract_id</c> is the database half of the specification's "each
/// contract MUST have exactly one account". The domain cannot enforce it — a second
/// <see cref="ContractAccount"/> for the same contract is a perfectly valid object in isolation,
/// and only the table can see that one already exists.
/// </summary>
public sealed class ContractAccountConfiguration : IEntityTypeConfiguration<ContractAccount>
{
    public void Configure(EntityTypeBuilder<ContractAccount> builder)
    {
        builder.ToTable("contract_accounts");

        builder.HasKey(a => a.Id);

        // Get-only, like RentAdjustment's properties: mapped explicitly rather than left to
        // convention, which skips a property with no setter.
        builder.Property(a => a.ContractId).HasColumnName("contract_id").IsRequired();

        builder.HasIndex(a => a.ContractId)
            .IsUnique()
            .HasDatabaseName("ix_contract_accounts_contract_id");

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(a => a.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        // The movements belong to the account and are reached only through it. The foreign key is
        // a shadow property: AccountMovement deliberately carries no AccountId, because a movement
        // is never handled apart from the account that owns it.
        builder.HasMany(a => a.Movements)
            .WithOne()
            .HasForeignKey("AccountId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(a => a.Movements).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Neither column is ever reassigned: an account's identity and its contract are fixed at
        // birth. Throwing here reports which rule was broken, instead of letting a stray change
        // reach the database and come back as an opaque error — or worse, succeed.
        foreach (var property in builder.Metadata.GetProperties())
        {
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }
    }
}
