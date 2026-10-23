using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Presentation.Recibos;
using Inmobiliaria.Presentation.Tests.Fakes;
using static Inmobiliaria.Presentation.Tests.CashDeskTestKit;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>ui-de-cobranza spec Requirement 13 and design D19: the newest collections, and a pair by number.</summary>
public class RecentCollectionsViewModelTests
{
    private static RecentCollection Entry(long number, bool annulled, int minutesAgo = 0) =>
        new(Guid.NewGuid(), number, number + 1, "Perez, Maria", Period, Today,
            new DateTimeOffset(2026, 1, 15, 10, 30 - minutesAgo, 0, TimeSpan.FromHours(-3)), annulled);

    [Fact]
    public async Task TheList_ShowsTheNewest50_WithNumbersTenantDatesAndTheAnnulledFlag()
    {
        var reader = new FakeReciboReader { Recent = [Entry(31003, annulled: true), Entry(31001, annulled: false, minutesAgo: 5)] };
        var recent = new RecentCollectionsViewModel(reader, _ => Task.CompletedTask);

        await recent.LoadCommand.ExecuteAsync(null);

        Assert.Equal([RecentCollectionsViewModel.Limit], reader.RecentLimits);
        Assert.Equal(50, RecentCollectionsViewModel.Limit);
        Assert.Equal(
            [
                ("N° 31003 / 31004", "Perez, Maria", "01/2026", "15/01/2026", "15/01/2026 10:30", "ANULADO"),
                ("N° 31001 / 31002", "Perez, Maria", "01/2026", "15/01/2026", "15/01/2026 10:25", ""),
            ],
            recent.Entries.Select(e => (e.NumbersText, e.Tenant, e.PeriodText, e.ReceivedOnText, e.RecordedText, e.AnnulledText)));
    }

    [Fact]
    public async Task OpeningAnEntry_ShowsBothRecibosAsIssued()
    {
        var reader = new FakeReciboReader();
        var entry = Entry(31001, annulled: false);
        reader.Payments[entry.CollectionId] = Issued(entry.CollectionId, 31001);
        reader.Recent = [entry];
        var recent = new RecentCollectionsViewModel(reader, _ => Task.CompletedTask);
        await recent.LoadCommand.ExecuteAsync(null);

        await recent.OpenCommand.ExecuteAsync(recent.Entries.Single());

        Assert.Equal(["N° 31001", "N° 31002"], recent.Recibo!.Copies.Select(c => c.NumberText));
    }

    [Theory]
    [InlineData("31002", null)]
    [InlineData("99999", "No hay ningún recibo con el número 99999.")]
    public async Task FindingByNumber_ShowsThePairOrSaysNothingHasThatNumber(string number, string? message)
    {
        var reader = new FakeReciboReader();
        var id = Guid.NewGuid();
        reader.Payments[id] = Issued(id, 31001);
        var recent = new RecentCollectionsViewModel(reader, _ => Task.CompletedTask) { NumberText = number };

        await recent.FindCommand.ExecuteAsync(null);

        Assert.Equal(message, recent.FindMessage);
        Assert.Equal(message is null, recent.Recibo is not null);
    }

    [Fact]
    public async Task VoidingAnEntry_HandsItsCollectionToTheVoidingScreen()
    {
        var entry = Entry(31001, annulled: false);
        var handed = new List<Guid>();
        var recent = new RecentCollectionsViewModel(new FakeReciboReader { Recent = [entry] }, id =>
        {
            handed.Add(id);
            return Task.CompletedTask;
        });
        await recent.LoadCommand.ExecuteAsync(null);

        await recent.VoidCommand.ExecuteAsync(recent.Entries.Single());

        Assert.Equal([entry.CollectionId], handed);
    }

    /// <summary>
    /// Task 6.0a (e), S4a review R3-anular-silently-noop-without-desk: with no voiding screen (invalid recargo
    /// terms, D16) the list does not offer "Anular".
    /// </summary>
    [Fact]
    public async Task WithoutAVoidingScreen_NoEntryOffersAnular()
    {
        var entry = Entry(31001, annulled: false);
        var recent = new RecentCollectionsViewModel(new FakeReciboReader { Recent = [entry] }, _ => Task.CompletedTask, canVoid: false);
        await recent.LoadCommand.ExecuteAsync(null);

        Assert.False(recent.VoidCommand.CanExecute(recent.Entries.Single()));
        Assert.False(recent.CanVoid);
    }
}
