using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Presentation.Tests.Fakes;

/// <summary>A scriptable <see cref="IAccountDirectory"/> that records every search text.</summary>
internal sealed class FakeAccountDirectory : IAccountDirectory
{
    public List<AccountListing> Listings { get; set; } = [];

    public List<string> Searches { get; } = [];

    public Task<IReadOnlyList<AccountListing>> SearchAsync(string text, CancellationToken ct = default)
    {
        Searches.Add(text);
        return Task.FromResult<IReadOnlyList<AccountListing>>(Listings.ToList());
    }
}

/// <summary>A scriptable <see cref="IDueAdjustmentQuery"/> that records the dates it was asked for.</summary>
internal sealed class FakeDueAdjustmentQuery : IDueAdjustmentQuery
{
    public List<DueAdjustment> Due { get; set; } = [];

    public List<DateOnly> Calls { get; } = [];

    /// <summary>When set, every call waits for it, so a test can act while the desk awaits the adjustments.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public async Task<IReadOnlyList<DueAdjustment>> GetDueAsync(DateOnly asOf, CancellationToken ct = default)
    {
        Calls.Add(asOf);
        if (Gate is { } gate)
        {
            await gate.Task;
        }

        return Due.ToList();
    }
}

/// <summary>A scriptable <see cref="IReciboReader"/>: payments by collection id.</summary>
internal sealed class FakeReciboReader : IReciboReader
{
    public Dictionary<Guid, IssuedPayment> Payments { get; } = [];

    public List<Guid> CollectionReads { get; } = [];

    public Task<IssuedPayment?> FindByNumberAsync(long reciboNumber, CancellationToken ct = default) =>
        Task.FromResult(Payments.Values.FirstOrDefault(p =>
            p.Locatario.Recibo.Number == reciboNumber || p.Propietario.Recibo.Number == reciboNumber));

    public Task<IssuedPayment?> GetByCollectionAsync(Guid collectionId, CancellationToken ct = default)
    {
        CollectionReads.Add(collectionId);
        return Task.FromResult(Payments.GetValueOrDefault(collectionId));
    }

    public Task<IReadOnlyList<RecentCollection>> RecentAsync(int limit, CancellationToken ct = default) =>
        throw new InvalidOperationException("RecentAsync was not scripted.");
}

/// <summary>A clock the test sets, so "today" is never the machine's.</summary>
internal sealed class FakeClock(DateOnly today) : IAgencyClock
{
    public DateOnly Today { get; set; } = today;
}
