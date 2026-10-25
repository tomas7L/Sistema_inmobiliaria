using System.Collections.Concurrent;
using Inmobiliaria.Presentation.Common;

namespace Inmobiliaria.Presentation.Tests.Fakes;

/// <summary>
/// An <see cref="IErrorLog"/> that keeps what it was given, or throws when told to. Safe to share: installed
/// as <see cref="OperationRunner.DefaultErrorLog"/> it receives records from test classes running in parallel.
/// </summary>
internal sealed class RecordingErrorLog : IErrorLog
{
    private readonly ConcurrentQueue<(string Context, Exception Exception)> _entries = new();

    public IReadOnlyCollection<(string Context, Exception Exception)> Entries => _entries.ToArray();

    public bool Throws { get; init; }

    public void Record(string context, Exception exception)
    {
        if (Throws)
        {
            throw new IOException("The disk is full.");
        }

        _entries.Enqueue((context, exception));
    }
}
