using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Presentation.CashDesk;
using Inmobiliaria.Presentation.Tests.Fakes;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>
/// A cash desk over faked ports, with a desk whose previews follow the domain's one rule the screen depends
/// on: the handed amount must equal the ledger figure. Everything else is scripted per test.
/// </summary>
internal sealed class CashDeskTestKit
{
    public static readonly DateOnly Today = new(2026, 1, 15);
    public static readonly DateOnly Period = new(2026, 1, 1);
    public const decimal Ledger = 598_260m;

    public FakeCollectionDesk Desk { get; } = new();
    public FakeAccountDirectory Directory { get; } = new();
    public FakeDueAdjustmentQuery Adjustments { get; } = new();
    public FakeReciboReader Reader { get; } = new();
    public FakeClock Clock { get; } = new(Today);
    public List<CollectionRequest> PreviewRequests { get; } = [];

    /// <summary>The ledger figure the scripted desk answers with; a test changes it to simulate another payment.</summary>
    public decimal LedgerFigure { get; set; } = Ledger;

    public CashDeskTestKit()
    {
        Desk.OnPreview = request =>
        {
            PreviewRequests.Add(request);
            return Task.FromResult(PreviewFor(request, LedgerFigure, request.ReceivedOn ?? Clock.Today));
        };
    }

    public CashDeskViewModel Build() => new(Desk, Directory, Adjustments, Reader, Clock);

    public static AccountListing Account(AccountReadiness? readiness = null, Guid? contractId = null) =>
        new(Guid.NewGuid(), contractId ?? Guid.NewGuid(), "1a2b3c4d", "Perez, Maria", "Gomez, Juan",
            "Bv. Belgrano 123, Sunchales", ContractStatus.Active, null, readiness ?? new AccountReadiness(true, false, true));

    /// <summary>What the domain would draft: rent plus recargo to the ledger figure, the typed lines on the locatario copy.</summary>
    public static CollectionPreview PreviewFor(CollectionRequest request, decimal ledger, DateOnly receivedOn)
    {
        var locatario = new List<ReciboLine>
        {
            new(ReciboLineKind.Rent, 1, 500_000m),
            new(ReciboLineKind.Recargo, 2, ledger - 500_000m),
        };
        if (request.Seguro is { } seguro)
        {
            locatario.Add(new ReciboLine(ReciboLineKind.Seguro, 3, decimal.Truncate(seguro)));
        }

        var propietario = new List<ReciboLine>
        {
            new(ReciboLineKind.Rent, 1, 500_000m),
            new(ReciboLineKind.HonorariosAdministracion, 2, -47_860m),
        };

        return new CollectionPreview(
            request.NamedPeriod ?? Period, receivedOn, receivedOn, false, ledger, ledger, 0m,
            new ReciboDraft(ReciboCopy.Locatario, locatario), new ReciboDraft(ReciboCopy.Propietario, propietario),
            [], request.HandedAmount == ledger ? null : CollectionRefusal.AmountNotTotal);
    }

    public static IssuedPayment Issued(Guid collectionId, long locatarioNumber, AnnulmentRecord? annulment = null)
    {
        var header = new ReciboHeader("Perez, Maria", ["Gomez, Juan", "Ibáñez, Rosa"], "Bv. Belgrano 123, Sunchales");
        Recibo Recibo(ReciboCopy copy, long number, params ReciboLine[] lines) =>
            new(number, Period, Today, header, new ReciboDraft(copy, lines));

        return new IssuedPayment(
            collectionId, Today, new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.FromHours(-3)),
            new IssuedRecibo(Guid.NewGuid(), Recibo(ReciboCopy.Locatario, locatarioNumber,
                new ReciboLine(ReciboLineKind.Rent, 1, 500_000m), new ReciboLine(ReciboLineKind.Recargo, 2, 98_260m))),
            new IssuedRecibo(Guid.NewGuid(), Recibo(ReciboCopy.Propietario, locatarioNumber + 1,
                new ReciboLine(ReciboLineKind.Rent, 1, 500_000m), new ReciboLine(ReciboLineKind.HonorariosAdministracion, 2, -47_860m))),
            annulment);
    }

    public static DueAdjustment Due(Guid contractId, params MissingIndexValue[] missing) =>
        new(contractId, new DateOnly(2026, 1, 10), 500_000m, new IndexPeriod(2025, 6), new IndexPeriod(2025, 12),
            missing, missing.Length == 0 ? 1.1m : null, missing.Length == 0 ? 550_000m : null);

    /// <summary>A desk with an account chosen and its owed-step preview on screen.</summary>
    public async Task<CashDeskViewModel> ChosenAsync(AccountListing? account = null)
    {
        var desk = Build();
        await desk.ChooseAccountAsync(account ?? Account());
        return desk;
    }

    /// <summary>Types the handed amount and waits for the preview it triggers.</summary>
    public static async Task TypeHandedAsync(CashDeskViewModel desk, string text)
    {
        desk.HandedAmountText = text;
        await desk.PreviewInFlight;
    }
}
