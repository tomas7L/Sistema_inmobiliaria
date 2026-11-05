using Inmobiliaria.Domain.Parties;

namespace Inmobiliaria.Presentation.Tests.Fakes;

/// <summary>
/// A scriptable <see cref="IPartyRegistry"/>: the list it answers with is set by the test, each write is recorded and
/// answered by a delegate the test assigned (success when none is), so a test can prove a call was never made.
/// </summary>
internal sealed class FakePartyRegistry : IPartyRegistry
{
    public List<PartyListing> Persons { get; set; } = [];

    public Func<PartyDraft, bool, PartyResult>? OnCreate { get; set; }

    public Func<Guid, PartyDraft, bool, PartyResult>? OnCorrect { get; set; }

    public List<string?> Searches { get; } = [];

    public List<(PartyDraft Draft, bool Accepted)> Created { get; } = [];

    public List<(Guid Id, PartyDraft Draft, bool Accepted)> Corrected { get; } = [];

    public Task<IReadOnlyList<PartyListing>> SearchAsync(string? text, CancellationToken ct = default)
    {
        Searches.Add(text);
        return Task.FromResult<IReadOnlyList<PartyListing>>(Persons.ToList());
    }

    public Task<PartyResult> CreateAsync(PartyDraft draft, bool acceptNoIdentifier, CancellationToken ct = default)
    {
        Created.Add((draft, acceptNoIdentifier));
        return Task.FromResult(OnCreate?.Invoke(draft, acceptNoIdentifier) ?? PartyResult.Saved(Listing(Guid.NewGuid(), draft)));
    }

    public Task<PartyResult> CorrectAsync(Guid partyId, PartyDraft draft, bool acceptNoIdentifier, CancellationToken ct = default)
    {
        Corrected.Add((partyId, draft, acceptNoIdentifier));
        return Task.FromResult(OnCorrect?.Invoke(partyId, draft, acceptNoIdentifier) ?? PartyResult.Saved(Listing(partyId, draft)));
    }

    private static PartyListing Listing(Guid id, PartyDraft draft) =>
        new(id, draft.FirstName ?? string.Empty, draft.LastName ?? string.Empty, draft.Dni, draft.Cuil, draft.Email, draft.PhoneNumber, draft.Domicile);
}
