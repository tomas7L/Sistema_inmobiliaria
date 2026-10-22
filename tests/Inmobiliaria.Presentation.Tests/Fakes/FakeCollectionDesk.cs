using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Collection;

namespace Inmobiliaria.Presentation.Tests.Fakes;

/// <summary>
/// A scriptable <see cref="ICollectionDesk"/>: each member runs the delegate the test assigned, and
/// throws when a test calls a member it never scripted, so an unexpected call fails loudly.
/// </summary>
internal sealed class FakeCollectionDesk : ICollectionDesk
{
    public Func<CollectionRequest, Task<CollectionPreview>>? OnPreview { get; set; }

    public Func<CollectionPreview, CollectionRequest, Task<CollectionResult>>? OnCommit { get; set; }

    public int CommitCalls { get; private set; }

    public Task<CollectionPreview> PreviewAsync(CollectionRequest request, CancellationToken ct = default) =>
        (OnPreview ?? throw new InvalidOperationException("PreviewAsync was not scripted."))(request);

    public Task<CollectionResult> CommitAsync(
        CollectionPreview preview, CollectionRequest request, CancellationToken ct = default)
    {
        CommitCalls++;
        return (OnCommit ?? throw new InvalidOperationException("CommitAsync was not scripted."))(preview, request);
    }

    public Task<VoidingPreview> PreviewVoidAsync(
        Guid reciboId, string reason, UserRole actingRole, CancellationToken ct = default) =>
        throw new InvalidOperationException("PreviewVoidAsync was not scripted.");

    public Task<VoidingResult> VoidAsync(VoidingPreview preview, CancellationToken ct = default) =>
        throw new InvalidOperationException("VoidAsync was not scripted.");
}
