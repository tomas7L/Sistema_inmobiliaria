using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Infrastructure.Access;

namespace Inmobiliaria.Infrastructure.Persistence.PerCall;

/// <summary>
/// <see cref="ICollectionDesk"/> for the screens: every call builds a <see cref="CollectionDesk"/> over its
/// own context (design D2). A commit is one call; it re-previews inside its own transaction.
/// </summary>
internal sealed class PerCallCollectionDesk : ICollectionDesk
{
    private readonly ISessionDbContextFactory _factory;
    private readonly IAgencyClock _clock;
    private readonly RecargoTerms _terms;

    public PerCallCollectionDesk(ISessionDbContextFactory factory, IAgencyClock clock, RecargoTerms terms)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(terms);

        _factory = factory;
        _clock = clock;
        _terms = terms;
    }

    public Task<CollectionPreview> PreviewAsync(CollectionRequest request, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new CollectionDesk(context, _clock, _terms).PreviewAsync(request, ct));

    public Task<CollectionResult> CommitAsync(
        CollectionPreview preview, CollectionRequest request, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new CollectionDesk(context, _clock, _terms).CommitAsync(preview, request, ct));

    public Task<VoidingPreview> PreviewVoidAsync(
        Guid reciboId, string reason, UserRole actingRole, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context =>
            new CollectionDesk(context, _clock, _terms).PreviewVoidAsync(reciboId, reason, actingRole, ct));

    public Task<VoidingResult> VoidAsync(VoidingPreview preview, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new CollectionDesk(context, _clock, _terms).VoidAsync(preview, ct));
}
