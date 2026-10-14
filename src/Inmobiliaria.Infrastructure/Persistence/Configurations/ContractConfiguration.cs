using Inmobiliaria.Domain.Leasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>lease-contract: Lifecycle Dates, Honorarios Percentage.</summary>
public sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> builder)
    {
        builder.ToTable("contracts", t =>
        {
            t.HasCheckConstraint(
                "ck_contracts_honorarios_percentage_range",
                "honorarios_percentage IS NULL OR (honorarios_percentage >= 0 AND honorarios_percentage <= 100)");
            t.HasCheckConstraint(
                "ck_contracts_status",
                "status IN ('Active', 'PendingTermination', 'Ended')");
            t.HasCheckConstraint(
                "ck_contracts_end_reason",
                "end_reason IS NULL OR end_reason IN " +
                "('Expiry', 'EarlyTerminationByTenant', 'TerminationForCause', 'MutualAgreement')");

            // Any day of the month, including the 29th through the 31st: a lease that says the
            // 31st is a lease the agency must be able to record. A month too short for it clamps
            // when the due date is computed, in AccrualSchedule.DueDateFor.
            t.HasCheckConstraint(
                "ck_contracts_due_day_range",
                "due_day BETWEEN 1 AND 31");

            // The honorarios contractuales plan is an amount AND a count, never one of them
            // (cobranza-y-recibos design section 5). Three checks rather than one so a refusal
            // names the rule that was broken.
            t.HasCheckConstraint(
                "ck_contracts_contractual_fee_both_or_neither",
                "(contractual_fee_amount IS NULL) = (contractual_fee_instalments IS NULL)");
            t.HasCheckConstraint(
                "ck_contracts_contractual_fee_amount_positive",
                "contractual_fee_amount IS NULL OR contractual_fee_amount > 0");
            t.HasCheckConstraint(
                "ck_contracts_contractual_fee_instalments_range",
                "contractual_fee_instalments IS NULL OR contractual_fee_instalments BETWEEN 1 AND 6");
        });

        builder.HasKey(c => c.Id);

        // Explicit mapping for the read-only (no-setter) date properties: without it, EF's
        // constructor-binding discovery does not recognize them as mapped model properties
        // and migration generation fails with "no suitable constructor was found".
        builder.Property(c => c.StartDate).IsRequired();
        builder.Property(c => c.NominalEndDate).IsRequired();
        builder.Property(c => c.NoticeGivenDate);
        builder.Property(c => c.PlannedMoveOutDate);
        builder.Property(c => c.ActualEndDate);

        // The day rent falls due, and the day mora is counted from. Get-only on the domain type
        // on purpose: that is what kept it out of the EF model until this configuration named it,
        // so the property could ship in one change and the column with its migration in this one.
        // Smallint because a day of the month needs two bytes, not four.
        builder.Property(c => c.DueDay)
            .HasColumnName("due_day")
            .HasColumnType("smallint")
            .HasDefaultValue(Contract.DefaultDueDay)
            .IsRequired();

        // The go-live date of a contract that predates the system, null for one created here. The
        // domain exposes it get-only and sets it through Contract.SetCutoverDate, so the column is
        // bound to the private field behind it. Nullable, and existing rows take null: nothing about
        // them moves (spec Test 64).
        builder.Property(c => c.CutoverDate)
            .HasField("_cutoverDate")
            .HasColumnName("cutover_date");

        // The fee plan is two columns. Contract.ContractualFeePlan is a value assembled from them,
        // so it is ignored and the two private fields behind it are mapped by name instead. That is
        // also what keeps a plan that is "an amount without a count" out of the domain: the type
        // cannot be built from half a plan, and the checks above refuse it in the table.
        builder.Property<decimal?>("_contractualFeeAmount")
            .HasColumnName("contractual_fee_amount")
            .HasPrecision(14, 2);
        builder.Property<short?>("_contractualFeeInstalments")
            .HasColumnName("contractual_fee_instalments")
            .HasColumnType("smallint");
        builder.Ignore(c => c.ContractualFeePlan);

        builder.Property(c => c.MonthlyRent)
            .HasColumnName("monthly_rent")
            .HasPrecision(14, 2)
            .IsRequired();

        builder.Property(c => c.HonorariosPercentage)
            .HasColumnName("honorarios_percentage")
            .HasPrecision(5, 2);

        builder.Property(c => c.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(c => c.EndReason)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Navigation(c => c.Parties).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(c => c.Units).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(c => c.Adjustments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
