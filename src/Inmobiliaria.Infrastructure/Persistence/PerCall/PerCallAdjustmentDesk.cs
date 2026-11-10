using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Infrastructure.Access;

namespace Inmobiliaria.Infrastructure.Persistence.PerCall;

/// <summary>
/// <see cref="IAdjustmentDesk"/> for the screens: every call builds an <see cref="AdjustmentDesk"/> over its own context
/// (design D2), recording the signed-in user as the one who confirms.
/// </summary>
internal sealed class PerCallAdjustmentDesk : IAdjustmentDesk
{
    private readonly ISessionDbContextFactory _factory;
    private readonly IAgencyClock _clock;
    private readonly Guid _userId;

    public PerCallAdjustmentDesk(ISessionDbContextFactory factory, IAgencyClock clock, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(clock);

        _factory = factory;
        _clock = clock;
        _userId = userId;
    }

    public Task<AdjustmentPreviewResult> PreviewAsync(Guid contractId, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new AdjustmentDesk(context, _clock, _userId).PreviewAsync(contractId, ct));

    public Task<AdjustmentConfirmResult> ConfirmAsync(AdjustmentPreview preview, bool chargeDifference = false, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new AdjustmentDesk(context, _clock, _userId).ConfirmAsync(preview, chargeDifference, ct));

    public Task<IReadOnlyList<AdjustmentHistoryEntry>> HistoryAsync(Guid contractId, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new AdjustmentDesk(context, _clock, _userId).HistoryAsync(contractId, ct));
}
