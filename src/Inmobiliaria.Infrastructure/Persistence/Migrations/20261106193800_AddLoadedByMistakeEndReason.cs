using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inmobiliaria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoadedByMistakeEndReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_contracts_end_reason",
                table: "contracts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_contracts_end_reason",
                table: "contracts",
                sql: "end_reason IS NULL OR end_reason IN ('Expiry', 'EarlyTerminationByTenant', 'TerminationForCause', 'MutualAgreement', 'LoadedByMistake')");
        }

        /// <summary>
        /// Fails with 23514 once any contract has been cancelled, and rolls back cleanly. That is deliberate: cancelling is final, so
        /// no data step here could undo one honestly, and an older version cannot read the value (design §12, tasks H.5). From the
        /// first cancellation on, recovery means fixing forward or restoring a backup.
        /// </summary>
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_contracts_end_reason",
                table: "contracts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_contracts_end_reason",
                table: "contracts",
                sql: "end_reason IS NULL OR end_reason IN ('Expiry', 'EarlyTerminationByTenant', 'TerminationForCause', 'MutualAgreement')");
        }
    }
}
