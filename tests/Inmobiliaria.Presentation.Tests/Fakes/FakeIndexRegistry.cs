using Inmobiliaria.Domain.Indices;

namespace Inmobiliaria.Presentation.Tests.Fakes;

/// <summary>
/// A scriptable <see cref="IIndexRegistry"/>: what it reads is set by the test, each write is recorded and answered by a
/// delegate the test assigned (success when none is), so a test can prove a call was never made.
/// </summary>
internal sealed class FakeIndexRegistry : IIndexRegistry
{
    public List<IndexListing> Indices { get; set; } = [];

    public List<AffectedAdjustment> Affected { get; set; } = [];

    public List<IndexCorrectionRecord> Corrections { get; set; } = [];

    public Func<Guid, IndexPeriod, decimal, bool, IndexEntryResult>? OnAdd { get; set; }

    public Func<Guid, decimal, bool, IndexEntryResult>? OnCorrect { get; set; }

    public int Lists { get; private set; }

    public List<(Guid Index, IndexPeriod Period, decimal Level, bool Accepted)> Added { get; } = [];

    public List<(Guid Value, decimal Level, bool Accepted)> Corrected { get; } = [];

    public List<(Guid Value, decimal Level)> AffectedAsked { get; } = [];

    public List<Guid> CorrectionsAsked { get; } = [];

    public Task<IReadOnlyList<IndexListing>> ListAsync(CancellationToken ct = default)
    {
        Lists++;
        return Task.FromResult<IReadOnlyList<IndexListing>>(Indices.ToList());
    }

    public Task<IndexEntryResult> AddAsync(Guid indexId, IndexPeriod period, decimal level, bool acceptFarLevel, CancellationToken ct = default)
    {
        Added.Add((indexId, period, level, acceptFarLevel));
        return Task.FromResult(OnAdd?.Invoke(indexId, period, level, acceptFarLevel)
            ?? IndexEntryResult.Saved(new IndexValueListing(Guid.NewGuid(), period, level)));
    }

    public Task<IndexEntryResult> CorrectAsync(Guid valueId, decimal newLevel, bool acceptFarLevel, CancellationToken ct = default)
    {
        Corrected.Add((valueId, newLevel, acceptFarLevel));
        return Task.FromResult(OnCorrect?.Invoke(valueId, newLevel, acceptFarLevel)
            ?? IndexEntryResult.Saved(new IndexValueListing(valueId, new IndexPeriod(2026, 8), newLevel), 9_440m));
    }

    public Task<IReadOnlyList<AffectedAdjustment>> ListAffectedAdjustmentsAsync(Guid valueId, decimal newLevel, CancellationToken ct = default)
    {
        AffectedAsked.Add((valueId, newLevel));
        return Task.FromResult<IReadOnlyList<AffectedAdjustment>>(Affected.ToList());
    }

    public Task<IReadOnlyList<IndexCorrectionRecord>> ListCorrectionsAsync(Guid valueId, CancellationToken ct = default)
    {
        CorrectionsAsked.Add(valueId);
        return Task.FromResult<IReadOnlyList<IndexCorrectionRecord>>(Corrections.ToList());
    }
}
