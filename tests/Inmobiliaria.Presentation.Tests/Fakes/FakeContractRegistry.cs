using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Presentation.Tests.Fakes;

/// <summary>
/// A scriptable <see cref="IContractRegistry"/>: each call is recorded and answered by a delegate the test assigned
/// (success when none is), so a test can prove a call was never made and what it carried.
/// </summary>
internal sealed class FakeContractRegistry : IContractRegistry
{
    public static readonly ContractListing Saved = new(
        Guid.NewGuid(), "Pérez, Juan", "Gómez, Ana", "Alem 1258, Sunchales", ContractStatus.Active, new DateOnly(2026, 12, 1), new DateOnly(2028, 11, 30), false);

    public List<ContractListing> Contracts { get; set; } = [];

    public Func<ContractDraft, bool, ContractResult>? OnCreate { get; set; }

    public Dictionary<Guid, decimal> Rates { get; } = [];

    /// <summary>When set, a creation waits for this, so a test can act while it is in flight.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public List<(ContractDraft Draft, bool Accepted)> Created { get; } = [];

    public List<(string? Text, ContractStatus? Status)> Lists { get; } = [];

    public Task<IReadOnlyList<ContractListing>> ListAsync(string? text, ContractStatus? status, CancellationToken ct = default)
    {
        Lists.Add((text, status));
        return Task.FromResult<IReadOnlyList<ContractListing>>(Contracts.ToList());
    }

    public Task<ContractDetail?> DetailAsync(Guid contractId, CancellationToken ct = default) => Task.FromResult<ContractDetail?>(null);

    public async Task<ContractResult> CreateAsync(ContractDraft draft, bool acceptWarnings, CancellationToken ct = default)
    {
        Created.Add((draft, acceptWarnings));
        if (Gate is { } gate)
        {
            await gate.Task;
        }

        return OnCreate?.Invoke(draft, acceptWarnings) ?? ContractResult.Saved(Saved);
    }

    public Task<decimal?> SuggestRateAsync(Guid lessorId, CancellationToken ct = default) =>
        Task.FromResult<decimal?>(Rates.TryGetValue(lessorId, out var rate) ? rate : null);
}
