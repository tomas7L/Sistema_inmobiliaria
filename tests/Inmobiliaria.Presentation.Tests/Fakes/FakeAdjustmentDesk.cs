using Inmobiliaria.Domain.Leasing;

namespace Inmobiliaria.Presentation.Tests.Fakes;

/// <summary>A scriptable <see cref="IAdjustmentDesk"/> that records every call. Previews are answered in order, the last repeating.</summary>
internal sealed class FakeAdjustmentDesk : IAdjustmentDesk
{
    public Queue<AdjustmentPreviewResult> Previews { get; } = new();

    public AdjustmentConfirmResult? NextConfirm { get; set; }

    public List<AdjustmentHistoryEntry> History { get; set; } = [];

    public List<Guid> Previewed { get; } = [];

    public List<AdjustmentPreview> Confirmed { get; } = [];

    public List<Guid> HistoryAsked { get; } = [];

    private AdjustmentPreviewResult? _last;

    public Task<AdjustmentPreviewResult> PreviewAsync(Guid contractId, CancellationToken ct = default)
    {
        Previewed.Add(contractId);
        if (Previews.Count > 0)
        {
            _last = Previews.Dequeue();
        }

        return Task.FromResult(_last ?? throw new InvalidOperationException("No preview scripted."));
    }

    public Task<AdjustmentConfirmResult> ConfirmAsync(AdjustmentPreview preview, CancellationToken ct = default)
    {
        Confirmed.Add(preview);
        return Task.FromResult(NextConfirm ?? throw new InvalidOperationException("No confirmation scripted."));
    }

    public Task<IReadOnlyList<AdjustmentHistoryEntry>> HistoryAsync(Guid contractId, CancellationToken ct = default)
    {
        HistoryAsked.Add(contractId);
        return Task.FromResult<IReadOnlyList<AdjustmentHistoryEntry>>(History.ToList());
    }
}
