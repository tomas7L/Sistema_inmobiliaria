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

    public Task<ContractDetail?> DetailAsync(Guid contractId, CancellationToken ct = default) => Task.FromResult(Detail);

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

    /// <summary>What <see cref="DetailAsync"/> answers.</summary>
    public ContractDetail? Detail { get; set; }

    public Func<ContractResult>? OnChange { get; set; }

    public ContractEndSummary EndSummary { get; set; } = new(null, 0m, 0m, []);

    /// <summary>Each write and summary, as its name followed by its arguments, in order.</summary>
    public List<object?[]> Calls { get; } = [];

    /// <summary>When set, a write waits for this, so a test can act while it is in flight.</summary>
    public TaskCompletionSource? ChangeGate { get; set; }

    private async Task<ContractResult> Change(params object?[] call)
    {
        Calls.Add(call);
        if (ChangeGate is { } gate)
        {
            await gate.Task;
        }

        return OnChange?.Invoke() ?? ContractResult.Saved(Saved);
    }

    public Task<ContractResult> CorrectAsync(Guid contractId, ContractCorrection correction, bool acceptWarnings, CancellationToken ct = default) =>
        Change("Correct", contractId, correction, acceptWarnings);

    public Task<ContractResult> SetRateAsync(Guid contractId, decimal rate, CancellationToken ct = default) =>
        Change("SetRate", contractId, rate);

    public Task<ContractResult> SetCodebtorsAsync(Guid contractId, IReadOnlyList<Guid> codebtorIds, CancellationToken ct = default) =>
        Change("SetCodebtors", contractId, codebtorIds);

    public Task<ContractResult> GiveNoticeAsync(Guid contractId, DateOnly noticeDate, DateOnly moveOutDate, CancellationToken ct = default) =>
        Change("GiveNotice", contractId, noticeDate, moveOutDate);

    public Task<ContractEndSummary> SummarizeEndAsync(Guid contractId, EndReason? reason, DateOnly endDate, CancellationToken ct = default)
    {
        Calls.Add(["SummarizeEnd", contractId, reason, endDate]);
        return Task.FromResult(EndSummary);
    }

    public Task<ContractResult> EndAsync(Guid contractId, EndReason? reason, DateOnly endDate, CancellationToken ct = default) =>
        Change("End", contractId, reason, endDate);

    public ContractCancellationSummary CancelSummary { get; set; } =
        new(null, "Pérez, Juan", "Alem 1258, Sunchales", 900_000m, [], false, 0m, [], "plan");

    public ContractUndoEndSummary UndoEndSummary { get; set; } = new(null, null, []);

    public Task<ContractCancellationSummary> SummarizeCancelAsync(Guid contractId, string? reason, CancellationToken ct = default)
    {
        Calls.Add(["SummarizeCancel", contractId, reason]);
        return Task.FromResult(CancelSummary);
    }

    public Task<ContractResult> CancelAsync(Guid contractId, string? reason, ContractCancellationSummary shown, CancellationToken ct = default) =>
        Change("Cancel", contractId, reason, shown);

    public Task<ContractResult> UndoNoticeAsync(Guid contractId, string? reason, CancellationToken ct = default) =>
        Change("UndoNotice", contractId, reason);

    public Task<ContractUndoEndSummary> SummarizeUndoEndAsync(Guid contractId, string? reason, CancellationToken ct = default)
    {
        Calls.Add(["SummarizeUndoEnd", contractId, reason]);
        return Task.FromResult(UndoEndSummary);
    }

    public Task<ContractResult> UndoEndAsync(Guid contractId, string? reason, CancellationToken ct = default) =>
        Change("UndoEnd", contractId, reason);
}
