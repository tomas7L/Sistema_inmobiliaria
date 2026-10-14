using System.Reflection;
using Inmobiliaria.Domain.Collection;

namespace Inmobiliaria.Domain.Tests;

/// <summary>
/// The recibo document's derived lines and totals (cobranza-y-recibos spec Tests 36, 37, 49, 50;
/// design D11, section 7.4). Pure domain: nothing here touches an account, a database or the model.
/// </summary>
public class ReciboLinesTests
{
    private static readonly DateOnly Period = new(2026, 9, 1);
    private static readonly DateOnly ReceivedOn = new(2026, 9, 12);

    private static ReciboHeader Header() =>
        new("Maria Gomez", ["Juan Perez"], "Departamento 2B");

    private static ReciboDraftPair Drafts(TypedReciboLines? typed = null) =>
        ReciboBuilder.Build(
            ReciboLedgerFigures.ForRentPeriod(rent: 507_000m, contractualFeeInstalment: 50_000m, recargo: 91_260m),
            typed ?? TypedReciboLines.None,
            honorariosPercentage: 8m);

    private static ReciboLineKind[] KindsOf(ReciboDraft draft) => [.. draft.Lines.Select(l => l.Kind)];

    // Test 36
    [Fact]
    public void TheLocatarioRecibo_CarriesRentInstalmentAndRecargo()
    {
        var locatario = Drafts().Locatario;

        Assert.Equal(ReciboCopy.Locatario, locatario.Copy);
        Assert.Equal(
            [ReciboLineKind.Rent, ReciboLineKind.ContractualFeeInstalment, ReciboLineKind.Recargo],
            KindsOf(locatario));
        Assert.Equal(507_000m, locatario.Lines.Single(l => l.Kind == ReciboLineKind.Rent).Amount);
        Assert.Equal(50_000m, locatario.Lines.Single(l => l.Kind == ReciboLineKind.ContractualFeeInstalment).Amount);
        Assert.Equal(91_260m, locatario.Lines.Single(l => l.Kind == ReciboLineKind.Recargo).Amount);
    }

    // Test 36
    [Fact]
    public void ThePropietarioRecibo_CarriesRentAndRecargo_ButNoInstalment()
    {
        var propietario = Drafts().Propietario;

        Assert.Equal(ReciboCopy.Propietario, propietario.Copy);
        Assert.DoesNotContain(ReciboLineKind.ContractualFeeInstalment, KindsOf(propietario));
        Assert.Contains(ReciboLineKind.Rent, KindsOf(propietario));
        Assert.Contains(ReciboLineKind.Recargo, KindsOf(propietario));
    }

    [Fact]
    public void AZeroRecargo_PrintsNoRecargoLine_OnEitherCopy()
    {
        var drafts = ReciboBuilder.Build(
            ReciboLedgerFigures.ForRentPeriod(507_000m, contractualFeeInstalment: null, recargo: 0m),
            TypedReciboLines.None,
            honorariosPercentage: 8m);

        Assert.DoesNotContain(ReciboLineKind.Recargo, KindsOf(drafts.Locatario));
        Assert.DoesNotContain(ReciboLineKind.Recargo, KindsOf(drafts.Propietario));
    }

    [Fact]
    public void LinesCarryPositionsFromOneInPrintOrder()
    {
        foreach (var draft in new[] { Drafts().Locatario, Drafts().Propietario })
        {
            Assert.Equal(Enumerable.Range(1, draft.Lines.Count), draft.Lines.Select(l => l.Position));
            Assert.Equal(draft.Lines.Select(l => l.Kind).OrderBy(k => (int)k), draft.Lines.Select(l => l.Kind));
        }
    }

    // Test 37
    [Fact]
    public void TheOperatorsTypedLines_HaveNoMemberForADerivedLine()
    {
        var typedMembers = typeof(TypedReciboLines)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.Name)
            .Order();

        Assert.Equal(["OtrosConceptos", "Seguro", "TasaMunicipal"], typedMembers);
    }

    // Test 37
    [Fact]
    public void TheCatalog_NamesExactlyFourDerivedKinds()
    {
        var derived = Enum.GetValues<ReciboLineKind>().Where(ReciboLineCatalog.IsLedgerDerived);

        Assert.Equal(
            [
                ReciboLineKind.Rent,
                ReciboLineKind.ContractualFeeInstalment,
                ReciboLineKind.Recargo,
                ReciboLineKind.SaldoDeudor,
            ],
            derived);
    }

    // Test 37
    [Fact]
    public void TypingLines_NeverChangesTheDerivedOnes()
    {
        var plain = Drafts();
        var typed = Drafts(new TypedReciboLines(TasaMunicipal: 1_000m, Seguro: 2_000m, OtrosConceptos: -500m));

        foreach (var kind in Enum.GetValues<ReciboLineKind>().Where(ReciboLineCatalog.IsLedgerDerived))
        {
            Assert.Equal(AmountOf(plain.Locatario, kind), AmountOf(typed.Locatario, kind));
            Assert.Equal(AmountOf(plain.Propietario, kind), AmountOf(typed.Propietario, kind));
        }
    }

    // Test 37
    [Fact]
    public void ADraft_RefusesALineThatDoesNotBelongOnItsCopy()
    {
        Assert.Throws<ArgumentException>(() => new ReciboDraft(
            ReciboCopy.Propietario,
            [new ReciboLine(ReciboLineKind.ContractualFeeInstalment, 1, 50_000m)]));

        Assert.Throws<ArgumentException>(() => new ReciboDraft(
            ReciboCopy.Locatario,
            [new ReciboLine(ReciboLineKind.HonorariosAdministracion, 1, -100m)]));
    }

    [Fact]
    public void ADraft_RefusesDuplicateKindsAndBrokenPositions()
    {
        Assert.Throws<ArgumentException>(() => new ReciboDraft(
            ReciboCopy.Locatario,
            [
                new ReciboLine(ReciboLineKind.Rent, 1, 100m),
                new ReciboLine(ReciboLineKind.Rent, 2, 100m),
            ]));

        Assert.Throws<ArgumentException>(() => new ReciboDraft(
            ReciboCopy.Locatario,
            [
                new ReciboLine(ReciboLineKind.Recargo, 1, 100m),
                new ReciboLine(ReciboLineKind.Rent, 2, 100m),
            ]));

        Assert.Throws<ArgumentException>(() => new ReciboDraft(
            ReciboCopy.Locatario,
            [new ReciboLine(ReciboLineKind.Rent, 3, 100m)]));
    }

    [Fact]
    public void ADraft_RefusesANegativeAmountOnAKindThatMayNotBeNegative()
    {
        Assert.Throws<ArgumentException>(() => new ReciboDraft(
            ReciboCopy.Locatario,
            [new ReciboLine(ReciboLineKind.Rent, 1, -100m)]));

        Assert.Throws<ArgumentException>(() => new ReciboDraft(
            ReciboCopy.Propietario,
            [new ReciboLine(ReciboLineKind.HonorariosAdministracion, 1, 100m)]));
    }

    [Fact]
    public void TwoDraftsWithTheSameLines_AreEqual()
    {
        Assert.Equal(Drafts(), Drafts());
        Assert.Equal(Drafts().Locatario.GetHashCode(), Drafts().Locatario.GetHashCode());
    }

    // Test 49
    [Fact]
    public void BothKindsOfRecibo_CarryTheNonFiscalLegend()
    {
        var pair = ReciboPair.Issue(31_001, Period, ReceivedOn, Header(), Drafts());

        Assert.Equal("Documento no válido como factura", Recibo.NonFiscalLegend);
        Assert.Equal(Recibo.NonFiscalLegend, pair.Locatario.Legend);
        Assert.Equal(Recibo.NonFiscalLegend, pair.Propietario.Legend);
    }

    // Test 50
    [Fact]
    public void TheTotal_IsTheSumOfTheSignedLines()
    {
        var drafts = Drafts(new TypedReciboLines(TasaMunicipal: 1_000m, Seguro: -2_000m, OtrosConceptos: -500m));

        foreach (var draft in new[] { drafts.Locatario, drafts.Propietario })
        {
            Assert.Equal(draft.Lines.Sum(l => l.Amount), draft.Total);
        }

        // 507,000 + 50,000 + 91,260 + 1,000 - 2,000 - 500
        Assert.Equal(646_760m, drafts.Locatario.Total);
        // 507,000 + 91,260 + 1,000 - 2,000 - 500 - 47,860
        Assert.Equal(548_900m, drafts.Propietario.Total);

        var recibo = ReciboPair.Issue(31_001, Period, ReceivedOn, Header(), drafts).Propietario;
        Assert.Equal(recibo.Lines.Sum(l => l.Amount), recibo.Total);
    }

    // Test 50
    [Fact]
    public void NothingInCollection_UsesFloatingPoint()
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        var types = typeof(Recibo).Assembly.GetTypes()
            .Where(t => t.Namespace == "Inmobiliaria.Domain.Collection")
            .Where(t => !t.IsEnum) // an enum's explicit IConvertible.ToDouble() is not a money member
            .ToList();

        Assert.NotEmpty(types);

        var offenders = new List<string>();

        foreach (var type in types)
        {
            offenders.AddRange(type.GetProperties(all)
                .Where(p => IsFloatingPoint(p.PropertyType)).Select(p => $"{type.Name}.{p.Name}"));
            offenders.AddRange(type.GetFields(all)
                .Where(f => IsFloatingPoint(f.FieldType)).Select(f => $"{type.Name}.{f.Name}"));
            offenders.AddRange(type.GetMethods(all)
                .Where(m => IsFloatingPoint(m.ReturnType) || m.GetParameters().Any(p => IsFloatingPoint(p.ParameterType)))
                .Select(m => $"{type.Name}.{m.Name}()"));
            offenders.AddRange(type.GetConstructors(all)
                .Where(c => c.GetParameters().Any(p => IsFloatingPoint(p.ParameterType)))
                .Select(c => $"{type.Name}..ctor"));
        }

        Assert.Empty(offenders);
    }

    private static decimal? AmountOf(ReciboDraft draft, ReciboLineKind kind) =>
        draft.Lines.SingleOrDefault(l => l.Kind == kind)?.Amount;

    private static bool IsFloatingPoint(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(double) || type == typeof(float);
    }

    // R3-whole-peso-unenforced: ledger figures are whole pesos upstream, so a fraction is refused, not cut.
    [Theory]
    [InlineData(507_000.5, null, 0)]
    [InlineData(507_000, 50_000.25, 0)]
    [InlineData(507_000, null, 91_260.75)]
    public void AFractionalLedgerFigure_IsRefused_NotTruncated(double rent, double? instalment, double recargo)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReciboLedgerFigures.ForRentPeriod(
            (decimal)rent, instalment is { } i ? (decimal)i : null, (decimal)recargo));
    }

    [Fact]
    public void AFractionalOpeningBalance_IsRefused_NotTruncated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReciboLedgerFigures.ForOpeningBalance(160_000.5m));
    }

    [Fact]
    public void ADraftWithAFractionalAmount_IsRefused()
    {
        var lines = new[] { new ReciboLine(ReciboLineKind.Rent, 1, 100.5m) };

        Assert.Throws<ArgumentException>(() => new ReciboDraft(ReciboCopy.Locatario, lines));
    }
}
