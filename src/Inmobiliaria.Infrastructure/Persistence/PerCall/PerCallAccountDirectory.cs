using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Infrastructure.Access;

namespace Inmobiliaria.Infrastructure.Persistence.PerCall;

/// <summary>
/// <see cref="IAccountDirectory"/> for the screens: every call builds an <see cref="AccountDirectory"/> over
/// its own context (design D2).
/// </summary>
internal sealed class PerCallAccountDirectory : IAccountDirectory
{
    private readonly ISessionDbContextFactory _factory;
    private readonly IAgencyClock _clock;

    public PerCallAccountDirectory(ISessionDbContextFactory factory, IAgencyClock clock)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(clock);

        _factory = factory;
        _clock = clock;
    }

    public Task<IReadOnlyList<AccountListing>> SearchAsync(string text, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new AccountDirectory(context, _clock).SearchAsync(text, ct));
}
