using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Presentation.CashDesk;
using Inmobiliaria.Presentation.Tests.Fakes;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>ui-de-cobranza spec test 9 (display half) and Requirement 6, with the directory faked.</summary>
public class AccountPickerViewModelTests
{
    private static AccountListing Listing(
        AccountReadiness readiness, ContractStatus status = ContractStatus.Active, DateOnly? cutover = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "1a2b3c4d", "Demaría, Ana", "Gómez, Juan", "San Martín 45, Sunchales",
            status, cutover, readiness);

    private static readonly AccountReadiness Ready = new(true, false, true);

    private static async Task<AccountPickerViewModel> SearchedAsync(FakeAccountDirectory directory, List<AccountListing>? chosen = null)
    {
        var picker = new AccountPickerViewModel(directory, listing =>
        {
            chosen?.Add(listing);
            return Task.CompletedTask;
        });
        picker.SearchText = "dem";
        await picker.SearchCommand.ExecuteAsync(null);
        return picker;
    }

    /// <summary>Spec test 9, display half: tenant, units, status, short id and readiness.</summary>
    [Fact]
    public async Task AListing_ShowsTenantUnitsStatusShortIdAndReadiness()
    {
        var directory = new FakeAccountDirectory { Listings = [Listing(Ready, ContractStatus.Ended, new DateOnly(2026, 6, 30))] };

        var row = Assert.Single((await SearchedAsync(directory)).Results);

        Assert.Equal(
            ("Demaría, Ana", "San Martín 45, Sunchales", "Finalizado", "1a2b3c4d", true, "Lista para cobrar"),
            (row.Tenants, row.Units, row.StatusText, row.ShortId, row.IsReady, row.ReadinessText));
        Assert.Empty(row.Problems);
        Assert.Equal("Fecha de corte: 30/06/2026", row.CutoverText);
    }

    /// <summary>Each missing gate is named with what to do, in the domain's own refusal text.</summary>
    [Fact]
    public async Task EachMissingGate_IsNamedWithWhatToDo()
    {
        var directory = new FakeAccountDirectory { Listings = [Listing(new AccountReadiness(false, true, false))] };

        var row = Assert.Single((await SearchedAsync(directory)).Results);

        Assert.False(row.IsReady);
        Assert.Equal("No está lista para cobrar", row.ReadinessText);
        Assert.Equal(
            [
                CollectionRefusal.RateNeverEntered.OperatorText(),
                CollectionRefusal.AccountFrozen.OperatorText(),
                CollectionRefusal.CutoverNotSet.OperatorText(),
            ],
            row.Problems);
    }

    /// <summary>A missing cutover date is information only, never a problem.</summary>
    [Fact]
    public async Task AMissingCutoverDate_IsInformationOnly()
    {
        var directory = new FakeAccountDirectory { Listings = [Listing(Ready)] };

        var row = Assert.Single((await SearchedAsync(directory)).Results);

        Assert.True(row.IsReady);
        Assert.Empty(row.Problems);
        Assert.Equal("Sin fecha de corte (informativo)", row.CutoverText);
    }

    /// <summary>A not-ready account stays selectable: the commit's own refusal still decides.</summary>
    [Fact]
    public async Task ANotReadyAccount_StaysSelectable()
    {
        var notReady = Listing(new AccountReadiness(false, false, true));
        var directory = new FakeAccountDirectory { Listings = [notReady] };
        var chosen = new List<AccountListing>();
        var picker = await SearchedAsync(directory, chosen);
        var row = picker.Results.Single();

        Assert.True(picker.ChooseCommand.CanExecute(row));
        await picker.ChooseCommand.ExecuteAsync(row);

        Assert.Equal([notReady], chosen);
    }

    /// <summary>Search starts from two characters; shorter text never reaches the directory.</summary>
    [Theory]
    [InlineData("", false)]
    [InlineData("d", false)]
    [InlineData(" d ", false)]
    [InlineData("de", true)]
    public async Task Search_StartsFromTwoCharacters(string text, bool searches)
    {
        var directory = new FakeAccountDirectory();
        var picker = new AccountPickerViewModel(directory, _ => Task.CompletedTask) { SearchText = text };

        Assert.Equal(searches, picker.SearchCommand.CanExecute(null));
        await picker.SearchCommand.ExecuteAsync(null);

        Assert.Equal(searches ? [text] : [], directory.Searches);
    }

    [Fact]
    public async Task ASearchWithNoResult_SaysSo()
    {
        var picker = await SearchedAsync(new FakeAccountDirectory());

        Assert.Empty(picker.Results);
        Assert.Equal("No se encontró ninguna cuenta.", picker.NoResultsMessage);
    }
}
