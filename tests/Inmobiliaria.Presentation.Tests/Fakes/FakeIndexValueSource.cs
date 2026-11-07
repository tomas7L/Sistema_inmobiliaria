using Inmobiliaria.Domain.Indices;

namespace Inmobiliaria.Presentation.Tests.Fakes;

/// <summary>A scriptable <see cref="IIndexValueSource"/> that records each request, so a test can prove none was made.</summary>
internal sealed class FakeIndexValueSource : IIndexValueSource
{
    public Dictionary<(string Index, IndexPeriod Period), IndexFetch> Answers { get; } = [];

    public IndexFetch Otherwise { get; set; } = new IndexFetch.NotPublishedYet();

    public List<(string Index, IndexPeriod Period)> Asked { get; } = [];

    /// <summary>When set, every request waits for it, so a test can act while the source is slow.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public bool Supports(string indexName) => indexName is "IPC" or "ICL";

    public async Task<IndexFetch> FetchAsync(string indexName, IndexPeriod period, CancellationToken ct = default)
    {
        Asked.Add((indexName, period));
        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }

        return Answers.GetValueOrDefault((indexName, period), Otherwise);
    }
}
