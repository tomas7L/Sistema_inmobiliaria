using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Infrastructure.Access;

namespace Inmobiliaria.Infrastructure.Persistence.PerCall;

/// <summary>
/// <see cref="IMoraWorklistQuery"/> for the screens: every call builds a <see cref="MoraWorklistQuery"/> and its
/// materialiser over its own context (design D2). Registered only with valid recargo terms (mora design D6).
/// </summary>
internal sealed class PerCallMoraWorklistQuery : IMoraWorklistQuery
{
    private readonly ISessionDbContextFactory _factory;

    public PerCallMoraWorklistQuery(ISessionDbContextFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        _factory = factory;
    }

    public Task<IReadOnlyList<MoraAccount>> GetAccountsInMoraAsync(DateOnly asOf, RecargoTerms terms, CancellationToken ct = default) =>
        PerCall.RunAsync(
            _factory,
            context => new MoraWorklistQuery(context, new AccountMaterialiser(context)).GetAccountsInMoraAsync(asOf, terms, ct));
}
