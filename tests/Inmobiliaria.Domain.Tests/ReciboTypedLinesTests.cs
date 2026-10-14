using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// The three lines the operator types: tasa municipal, seguro and Otros Conceptos
/// (cobranza-y-recibos spec Tests 21, 38, 39, 40, 41). They live on the recibo and never on the ledger.
/// </summary>
public class ReciboTypedLinesTests
{
    private static ReciboDraftPair Build(TypedReciboLines typed, decimal rate = 8m) =>
        ReciboBuilder.Build(
            ReciboLedgerFigures.ForRentPeriod(507_000m, contractualFeeInstalment: null, recargo: 0m),
            typed,
            rate);

    private static decimal? AmountOf(ReciboDraft draft, ReciboLineKind kind) =>
        draft.Lines.SingleOrDefault(l => l.Kind == kind)?.Amount;

    // Test 21
    [Fact]
    public void AFractionalTypedAmount_IsTruncatedTowardZero_AndShownTruncated()
    {
        var drafts = Build(new TypedReciboLines(TasaMunicipal: 1_234.99m, Seguro: -1_234.99m, OtrosConceptos: 99.5m));

        foreach (var draft in new[] { drafts.Locatario, drafts.Propietario })
        {
            Assert.Equal(1_234m, AmountOf(draft, ReciboLineKind.TasaMunicipal));
            Assert.Equal(-1_234m, AmountOf(draft, ReciboLineKind.Seguro));
            Assert.Equal(99m, AmountOf(draft, ReciboLineKind.OtrosConceptos));
        }
    }

    [Fact]
    public void AnAmountThatTruncatesToZero_PrintsNoLine()
    {
        var drafts = Build(new TypedReciboLines(TasaMunicipal: 0.99m, Seguro: -0.5m, OtrosConceptos: 0m));

        var zeroKinds = new[]
        {
            ReciboLineKind.TasaMunicipal, ReciboLineKind.Seguro, ReciboLineKind.OtrosConceptos,
        };

        // R3-truncate-zero-locatario-only: both copies omit the zero lines, not just the locatario.
        foreach (var draft in new[] { drafts.Locatario, drafts.Propietario })
        {
            Assert.All(draft.Lines, l => Assert.DoesNotContain(l.Kind, zeroKinds));
        }
    }

    // Test 38
    [Fact]
    public void ANegativeOtrosConceptos_IsAccepted_AndAppearsOnTheRecibo()
    {
        var drafts = Build(new TypedReciboLines(OtrosConceptos: -3_000m));

        Assert.Equal(-3_000m, AmountOf(drafts.Locatario, ReciboLineKind.OtrosConceptos));
        Assert.Equal(-3_000m, AmountOf(drafts.Propietario, ReciboLineKind.OtrosConceptos));
        Assert.Equal(507_000m - 3_000m, drafts.Locatario.Total);
    }

    // Test 39
    [Fact]
    public void ANegativeSeguro_IsAccepted_OnItsOwnLine_OnBothRecibos()
    {
        var drafts = Build(new TypedReciboLines(Seguro: -4_500m));

        Assert.Equal(-4_500m, AmountOf(drafts.Locatario, ReciboLineKind.Seguro));
        Assert.Equal(-4_500m, AmountOf(drafts.Propietario, ReciboLineKind.Seguro));
        Assert.Single(drafts.Locatario.Lines, l => l.Kind == ReciboLineKind.Seguro);
    }

    // Test 40
    [Fact]
    public void ANegativeTasaMunicipal_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Build(new TypedReciboLines(TasaMunicipal: -1m)));
    }

    [Fact]
    public void ATasaMunicipalThatOnlyTruncatesToZeroFromBelow_IsStillRefused()
    {
        // -0.5 is negative as typed; the refusal is about what the operator typed, not the truncated result.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Build(new TypedReciboLines(TasaMunicipal: -0.5m)));
    }

    [Fact]
    public void TheCatalog_AllowsNegativesOnlyWhereTheOfficeDoes()
    {
        Assert.True(ReciboLineCatalog.MayBeNegative(ReciboLineKind.Seguro));
        Assert.True(ReciboLineCatalog.MayBeNegative(ReciboLineKind.OtrosConceptos));
        Assert.False(ReciboLineCatalog.MayBeNegative(ReciboLineKind.TasaMunicipal));
        Assert.False(ReciboLineCatalog.MayBeNegative(ReciboLineKind.Rent));
    }

    // Test 41
    [Fact]
    public void TypedLines_CreateNoLedgerMovement_AndLeaveTheBalanceUnchanged()
    {
        var contractId = Guid.NewGuid();
        var account = new ContractAccount(Guid.NewGuid(), contractId);
        var september = new DateOnly(2026, 9, 1);
        account.Append(AccountMovement.RentAccrual(Guid.NewGuid(), 507_000m, september, new DateOnly(2026, 9, 10)));

        var asOf = new DateOnly(2026, 9, 10);
        var balanceBefore = account.LedgerBalance(asOf);
        var movementsBefore = account.Movements.Count;

        var drafts = Build(new TypedReciboLines(TasaMunicipal: 1_000m, Seguro: 2_000m, OtrosConceptos: -500m));

        Assert.Equal(movementsBefore, account.Movements.Count);
        Assert.Equal(balanceBefore, account.LedgerBalance(asOf));

        // The recibo total moves by the typed lines; the ledger figure does not.
        Assert.Equal(balanceBefore + 1_000m + 2_000m - 500m, drafts.Locatario.Total);
    }
}
