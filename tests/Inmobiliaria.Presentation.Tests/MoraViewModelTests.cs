using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Presentation.Common;
using Inmobiliaria.Presentation.Mora;
using Inmobiliaria.Presentation.Tests.Fakes;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>mora spec Requirements 6 and 7, Tests 10 to 13: the Mora screen's view-model.</summary>
public class MoraViewModelTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly RecargoTerms Terms = new(0.02m, graceDays: 0);

    private static MoraAccount Row(string tenant, int days, decimal owed = 600_000m, decimal recargo = 100_000m, DateOnly? frozenOn = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "ab12cd34", tenant, "San Martín 45, Sunchales", ContractStatus.Active,
            new DateOnly(2026, 8, 1), Today.AddDays(-days), days, owed - recargo, owed, recargo, frozenOn);

    private sealed class FakeMoraWorklist : IMoraWorklistQuery
    {
        public List<MoraAccount> Rows { get; set; } = [];

        public List<(DateOnly AsOf, RecargoTerms Terms)> Calls { get; } = [];

        public Exception? Failure { get; set; }

        public Task<IReadOnlyList<MoraAccount>> GetAccountsInMoraAsync(DateOnly asOf, RecargoTerms terms, CancellationToken ct = default)
        {
            Calls.Add((asOf, terms));
            return Failure is not null
                ? Task.FromException<IReadOnlyList<MoraAccount>>(Failure)
                : Task.FromResult<IReadOnlyList<MoraAccount>>(Rows.ToList());
        }
    }

    private static (MoraViewModel Mora, FakeMoraWorklist Query, FakeAccountDirectory Directory, List<AccountListing> Opened) Build()
    {
        var query = new FakeMoraWorklist();
        var directory = new FakeAccountDirectory();
        var opened = new List<AccountListing>();
        var mora = new MoraViewModel(query, Terms, new FakeClock(Today), directory, listing =>
        {
            opened.Add(listing);
            return Task.CompletedTask;
        });

        return (mora, query, directory, opened);
    }

    /// <summary>
    /// Test 10: the list is asked about today with the desk's terms, keeps the query's order (design F7), names who owes,
    /// shows the frozen date when there is one, shows amount owed and recargo apart, never their sum, and says what the
    /// figures cover.
    /// </summary>
    [Fact]
    public async Task TheList_KeepsTheQueryOrder_NamesWhoOwes_AndNeverAddsTheTwoFigures()
    {
        var (mora, query, _, _) = Build();
        query.Rows = [Row("Zeta, Ana", 200), Row("Álvarez, Juan", 38, frozenOn: new DateOnly(2026, 10, 1)), Row("Beta, Rosa", 5)];

        await mora.LoadCommand.ExecuteAsync(null);

        Assert.Equal([(Today, Terms)], query.Calls);
        Assert.Equal(["Zeta, Ana", "Álvarez, Juan", "Beta, Rosa"], mora.Rows.Select(r => r.Tenants));

        var first = mora.Rows[0];
        Assert.Equal(
            ("San Martín 45, Sunchales", "ab12cd34", "Vigente", 200, "600.000", "100.000", null),
            (first.Units, first.ShortId, first.StatusText, first.DaysOverdue, first.AmountOwedText, first.RecargoText, first.FrozenText));
        Assert.Equal("Recargo congelado el 01/10/2026", mora.Rows[1].FrozenText);

        var shown = typeof(MoraRow).GetProperties().Where(p => p.PropertyType == typeof(string)).Select(p => (string?)p.GetValue(first));
        Assert.DoesNotContain("700.000", shown);

        Assert.Equal(MoraTexts.Coverage, mora.CoverageText);
        Assert.Contains("tasa municipal", mora.CoverageText);
        Assert.False(mora.IsEmpty);
    }

    /// <summary>Test 11: nobody in mora is said in words, never a blank table.</summary>
    [Fact]
    public async Task NobodyInMora_SetsTheEmptyState()
    {
        var (mora, _, _, _) = Build();

        await mora.LoadCommand.ExecuteAsync(null);

        Assert.Empty(mora.Rows);
        Assert.True(mora.IsEmpty);
        Assert.Null(mora.Runner.ErrorMessage);
    }

    /// <summary>Test 11: a failing read shows a message, throws nothing and is not mistaken for an empty list.</summary>
    [Fact]
    public async Task AFailingRead_ShowsAMessage_AndIsNotAnEmptyList()
    {
        var (mora, query, _, _) = Build();
        query.Failure = new InvalidOperationException("Host=secret;Password=hunter2");

        await mora.LoadCommand.ExecuteAsync(null);

        Assert.Equal(ErrorMessages.Generic, mora.Runner.ErrorMessage);
        Assert.False(mora.IsEmpty);
        Assert.False(mora.Runner.IsBusy);
    }

    /// <summary>
    /// Test 11, after an earlier read (verifier advisory, 2026-11-11): a failed refresh clears the list and the empty state,
    /// so the screen never says "nobody in mora", nor shows figures from an earlier read, beside the error.
    /// </summary>
    [Fact]
    public async Task AFailedRefresh_ClearsTheEarlierRowsAndTheEmptyState()
    {
        var (mora, query, _, _) = Build();
        await mora.LoadCommand.ExecuteAsync(null);
        Assert.True(mora.IsEmpty);

        query.Failure = new InvalidOperationException("down");
        await mora.LoadCommand.ExecuteAsync(null);
        Assert.False(mora.IsEmpty);
        Assert.Equal(ErrorMessages.Generic, mora.Runner.ErrorMessage);

        query.Failure = null;
        query.Rows = [Row("Zeta, Ana", 200)];
        await mora.LoadCommand.ExecuteAsync(null);
        query.Failure = new InvalidOperationException("down");
        await mora.LoadCommand.ExecuteAsync(null);
        Assert.Empty(mora.Rows);
    }

    /// <summary>Test 12: refreshing reads the port again and replaces every row, never patching the old list.</summary>
    [Fact]
    public async Task Refreshing_ReadsAgain_AndReplacesEveryRow()
    {
        var (mora, query, _, _) = Build();
        query.Rows = [Row("Zeta, Ana", 200), Row("Beta, Rosa", 5)];
        await mora.LoadCommand.ExecuteAsync(null);

        query.Rows = [Row("Nueva, Eva", 1)];
        await mora.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, query.Calls.Count);
        Assert.Equal(["Nueva, Eva"], mora.Rows.Select(r => r.Tenants));

        query.Rows = [];
        await mora.LoadCommand.ExecuteAsync(null);
        Assert.Empty(mora.Rows);
        Assert.True(mora.IsEmpty);
    }

    /// <summary>
    /// Test 13: choosing a row reads that account's listing fresh and hands it to the desk; one that can no longer be
    /// opened says so and opens nothing.
    /// </summary>
    [Fact]
    public async Task ChoosingARow_OpensItsAccountAtTheDesk()
    {
        var (mora, query, directory, opened) = Build();
        var owing = Row("Zeta, Ana", 200);
        var gone = Row("Beta, Rosa", 5);
        query.Rows = [owing, gone];
        var listing = CashDeskTestKit.Account() with { AccountId = owing.AccountId };
        directory.Listings = [listing];
        await mora.LoadCommand.ExecuteAsync(null);

        await mora.OpenCommand.ExecuteAsync(mora.Rows[0]);

        Assert.Equal([owing.AccountId], directory.Finds);
        Assert.Equal([listing], opened);
        Assert.Null(mora.OpenMessage);

        await mora.OpenCommand.ExecuteAsync(mora.Rows[1]);

        Assert.Equal([listing], opened);
        Assert.Equal(MoraTexts.AccountGone, mora.OpenMessage);
    }
}
