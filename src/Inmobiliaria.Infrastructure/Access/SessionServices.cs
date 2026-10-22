using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Infrastructure.Persistence;
using Inmobiliaria.Infrastructure.Persistence.PerCall;
using Microsoft.Extensions.DependencyInjection;

namespace Inmobiliaria.Infrastructure.Access;

/// <summary>
/// The post-login composition root (design D3), beside <see cref="ApplicationServices"/>. Everything a
/// screen uses after login is built here from the session and its database factory, and the container
/// holds no <see cref="Microsoft.EntityFrameworkCore.DbContext"/>: every port is a per-call wrapper that
/// creates and disposes its own context for each call (design D2). Proven by <c>SessionServicesTests</c>
/// in the Linux `core` job, since <c>Inmobiliaria.Desktop</c> cannot gate it.
/// </summary>
public static class SessionServices
{
    /// <summary>
    /// Registers the clock, the session and one per-call wrapper for each port a screen needs. The desk is
    /// registered only when <paramref name="recargo"/> is valid: invalid terms close the desk and voiding
    /// instead of falling back to a default (design D16); the shell reads the same settings to say why.
    /// </summary>
    public static IServiceCollection AddSessionServices(
        this IServiceCollection services,
        ISessionDbContextFactory factory,
        IUserSession session,
        RecargoSettings recargo)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(recargo);

        services.AddSingleton(session);
        services.AddSingleton<IAgencyClock>(_ => new AgencyClock());
        services.AddSingleton<IDueAdjustmentQuery>(_ => new PerCallDueAdjustmentQuery(factory));

        if (recargo.Terms is { } terms)
        {
            services.AddSingleton<ICollectionDesk>(provider =>
                new PerCallCollectionDesk(factory, provider.GetRequiredService<IAgencyClock>(), terms));
        }

        return services;
    }
}
