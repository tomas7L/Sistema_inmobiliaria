using Microsoft.Extensions.DependencyInjection;

namespace Inmobiliaria.Infrastructure.Access;

/// <summary>
/// The pre-login composition root (design Decision 7). Registers nothing DB-shaped: no
/// <see cref="Microsoft.EntityFrameworkCore.DbContext"/>, no <see cref="Npgsql.NpgsqlDataSource"/>,
/// no <see cref="Npgsql.NpgsqlConnection"/>, no <see cref="ISessionDbContextFactory"/>. Spec
/// "No Database Access Before Authentication" is therefore a property of this container's
/// contents, not a rule anybody has to remember to keep (spec tests 1, 6; see
/// <c>CompositionGuardTests</c>). A plain cross-platform static method rather than WPF code, so
/// the Linux <c>core</c> CI job can prove it is empty even though <c>Inmobiliaria.Desktop</c>
/// itself is excluded from <c>Inmobiliaria.Core.slnf</c>.
/// </summary>
public static class ApplicationServices
{
    public static IServiceCollection AddPreLoginServices(
        this IServiceCollection services, ConnectionEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(endpoint);

        services.AddSingleton(endpoint);
        services.AddSingleton<IAuthenticator, NpgsqlAuthenticator>();

        return services;
    }
}
