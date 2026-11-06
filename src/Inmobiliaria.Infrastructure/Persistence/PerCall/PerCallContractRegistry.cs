using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Infrastructure.Access;

namespace Inmobiliaria.Infrastructure.Persistence.PerCall;

/// <summary>
/// <see cref="IContractRegistry"/> for the screens: every call builds a <see cref="ContractRegistry"/> over its own
/// context (design D2).
/// </summary>
internal sealed class PerCallContractRegistry : IContractRegistry
{
    private readonly ISessionDbContextFactory _factory;
    private readonly IAgencyClock _clock;
    private readonly RecargoTerms? _terms;

    public PerCallContractRegistry(ISessionDbContextFactory factory, IAgencyClock clock, RecargoTerms? terms)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(clock);

        _factory = factory;
        _clock = clock;
        _terms = terms;
    }

    private ContractRegistry Over(InmobiliariaDbContext context) => new(context, _clock, _terms);

    public Task<IReadOnlyList<ContractListing>> ListAsync(string? text, ContractStatus? status, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new ContractRegistry(context, _clock).ListAsync(text, status, ct));

    public Task<ContractDetail?> DetailAsync(Guid contractId, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new ContractRegistry(context, _clock).DetailAsync(contractId, ct));

    public Task<ContractResult> CreateAsync(ContractDraft draft, bool acceptWarnings, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new ContractRegistry(context, _clock).CreateAsync(draft, acceptWarnings, ct));

    public Task<decimal?> SuggestRateAsync(Guid lessorId, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new ContractRegistry(context, _clock).SuggestRateAsync(lessorId, ct));

    public Task<ContractResult> CorrectAsync(Guid contractId, ContractCorrection correction, bool acceptWarnings, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => Over(context).CorrectAsync(contractId, correction, acceptWarnings, ct));

    public Task<ContractResult> SetRateAsync(Guid contractId, decimal rate, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => Over(context).SetRateAsync(contractId, rate, ct));

    public Task<ContractResult> SetCodebtorsAsync(Guid contractId, IReadOnlyList<Guid> codebtorIds, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => Over(context).SetCodebtorsAsync(contractId, codebtorIds, ct));

    public Task<ContractResult> GiveNoticeAsync(Guid contractId, DateOnly noticeDate, DateOnly moveOutDate, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => Over(context).GiveNoticeAsync(contractId, noticeDate, moveOutDate, ct));

    public Task<ContractEndSummary> SummarizeEndAsync(Guid contractId, EndReason? reason, DateOnly endDate, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => Over(context).SummarizeEndAsync(contractId, reason, endDate, ct));

    public Task<ContractResult> EndAsync(Guid contractId, EndReason? reason, DateOnly endDate, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => Over(context).EndAsync(contractId, reason, endDate, ct));

    public Task<ContractCancellationSummary> SummarizeCancelAsync(Guid contractId, string? reason, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => Over(context).SummarizeCancelAsync(contractId, reason, ct));

    public Task<ContractResult> CancelAsync(Guid contractId, string? reason, ContractCancellationSummary shown, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => Over(context).CancelAsync(contractId, reason, shown, ct));

    public Task<ContractResult> UndoNoticeAsync(Guid contractId, string? reason, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => Over(context).UndoNoticeAsync(contractId, reason, ct));

    public Task<ContractUndoEndSummary> SummarizeUndoEndAsync(Guid contractId, string? reason, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => Over(context).SummarizeUndoEndAsync(contractId, reason, ct));

    public Task<ContractResult> UndoEndAsync(Guid contractId, string? reason, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => Over(context).UndoEndAsync(contractId, reason, ct));
}
