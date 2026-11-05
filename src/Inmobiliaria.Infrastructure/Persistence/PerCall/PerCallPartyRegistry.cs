using Inmobiliaria.Domain.Parties;
using Inmobiliaria.Infrastructure.Access;

namespace Inmobiliaria.Infrastructure.Persistence.PerCall;

/// <summary>
/// <see cref="IPartyRegistry"/> for the screens: every call builds a <see cref="PartyRegistry"/> over its own
/// context (design D2).
/// </summary>
internal sealed class PerCallPartyRegistry : IPartyRegistry
{
    private readonly ISessionDbContextFactory _factory;

    public PerCallPartyRegistry(ISessionDbContextFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        _factory = factory;
    }

    public Task<IReadOnlyList<PartyListing>> SearchAsync(string? text, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new PartyRegistry(context).SearchAsync(text, ct));

    public Task<PartyResult> CreateAsync(PartyDraft draft, bool acceptNoIdentifier, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new PartyRegistry(context).CreateAsync(draft, acceptNoIdentifier, ct));

    public Task<PartyResult> CorrectAsync(Guid partyId, PartyDraft draft, bool acceptNoIdentifier, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new PartyRegistry(context).CorrectAsync(partyId, draft, acceptNoIdentifier, ct));
}
