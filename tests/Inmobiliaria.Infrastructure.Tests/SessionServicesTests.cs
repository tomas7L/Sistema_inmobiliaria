using System.Data.Common;
using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Domain.GoLive;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Infrastructure.Access;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Spec tests 3 and 4 (design D2, D3, D16): the post-login wiring resolves for each role without opening a
/// connection or holding a context, and every port call obtains its own context and disposes it. No
/// container: the connection is never reachable, and a spy factory and an interceptor that refuses to open
/// it prove what is asked of the factory.
/// </summary>
public sealed class SessionServicesTests
{
    private sealed class ConnectionRefusedException : Exception
    {
    }

    /// <summary>Refuses to open any connection, so a query fails at the first point it would touch the database.</summary>
    private sealed class RefuseConnectionInterceptor : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
            throw new ConnectionRefusedException();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            throw new ConnectionRefusedException();
    }

    /// <summary>Counts and keeps every context it creates; each one refuses to open its connection.</summary>
    private sealed class SpySessionFactory : ISessionDbContextFactory
    {
        public List<InmobiliariaDbContext> Created { get; } = [];

        public InmobiliariaDbContext Create()
        {
            var options = new DbContextOptionsBuilder<InmobiliariaDbContext>()
                .UseNpgsql("Host=localhost;Database=never;Username=never;Password=never")
                .AddInterceptors(new RefuseConnectionInterceptor())
                .Options;

            var context = new InmobiliariaDbContext(options);
            Created.Add(context);
            return context;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static IUserSession SessionOf(UserRole role) =>
        new UserSession(new AppUser(Guid.NewGuid(), role == UserRole.Admin ? "nicolas" : "maria", "Test"), role);

    private static RecargoSettings ValidSettings()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""{ "Recargo": { "DailyRate": 0.02, "GraceDays": 0 } }""");
        return RecargoSettings.Read(document.RootElement);
    }

    private static ServiceCollection Collect(SpySessionFactory factory, UserRole role, RecargoSettings settings)
    {
        var services = new ServiceCollection();
        services.AddSessionServices(factory, SessionOf(role), settings);
        return services;
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Empleado)]
    public void EveryPortAScreenNeeds_ResolvesForEachRole_WithoutCreatingAContext(UserRole role)
    {
        var factory = new SpySessionFactory();
        using var provider = Collect(factory, role, ValidSettings()).BuildServiceProvider();

        Assert.IsAssignableFrom<ICollectionDesk>(provider.GetRequiredService<ICollectionDesk>());
        Assert.IsAssignableFrom<IDueAdjustmentQuery>(provider.GetRequiredService<IDueAdjustmentQuery>());
        Assert.IsAssignableFrom<IGoLiveSetup>(provider.GetRequiredService<IGoLiveSetup>());
        Assert.IsAssignableFrom<IAccountDirectory>(provider.GetRequiredService<IAccountDirectory>());
        Assert.IsAssignableFrom<IReciboReader>(provider.GetRequiredService<IReciboReader>());
        Assert.IsAssignableFrom<IAgencyClock>(provider.GetRequiredService<IAgencyClock>());
        Assert.Equal(role, provider.GetRequiredService<IUserSession>().Role);
        Assert.Empty(factory.Created);
    }

    [Fact]
    public void TheSessionContainer_RegistersNoDbContext_AndHoldsNoneAfterResolvingEverything()
    {
        var factory = new SpySessionFactory();
        var services = Collect(factory, UserRole.Admin, ValidSettings());

        foreach (var descriptor in services)
        {
            Assert.False(
                typeof(DbContext).IsAssignableFrom(descriptor.ServiceType),
                $"Service type {descriptor.ServiceType} is a DbContext.");
            Assert.False(
                typeof(ISessionDbContextFactory).IsAssignableFrom(descriptor.ServiceType),
                $"Service type {descriptor.ServiceType} exposes the session factory.");

            if (descriptor.ImplementationType is not null)
            {
                Assert.False(
                    typeof(DbContext).IsAssignableFrom(descriptor.ImplementationType),
                    $"Implementation type {descriptor.ImplementationType} is a DbContext.");
            }
        }

        using var provider = services.BuildServiceProvider();
        foreach (var descriptor in services)
        {
            Assert.IsNotAssignableFrom<DbContext>(provider.GetRequiredService(descriptor.ServiceType));
        }

        Assert.Empty(factory.Created);
    }

    [Fact]
    public void InvalidRecargoTerms_LeaveTheDeskUnregistered_AndTheOtherPortsResolvable()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""{ "Recargo": { "DailyRate": 2, "GraceDays": 0 } }""");
        var invalid = RecargoSettings.Read(document.RootElement);
        var factory = new SpySessionFactory();

        var services = Collect(factory, UserRole.Empleado, invalid);
        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<ICollectionDesk>());
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ICollectionDesk));
        Assert.NotNull(provider.GetRequiredService<IDueAdjustmentQuery>());
        Assert.NotNull(provider.GetRequiredService<IGoLiveSetup>());
        Assert.NotNull(provider.GetRequiredService<IAccountDirectory>());
        Assert.NotNull(provider.GetRequiredService<IReciboReader>());
        Assert.NotNull(provider.GetRequiredService<IAgencyClock>());
    }

    [Fact]
    public async Task TwoCallsOnOneWrapper_ObtainTwoDistinctContexts_EachDisposed()
    {
        var factory = new SpySessionFactory();
        using var provider = Collect(factory, UserRole.Admin, ValidSettings()).BuildServiceProvider();
        var query = provider.GetRequiredService<IDueAdjustmentQuery>();

        await Assert.ThrowsAsync<ConnectionRefusedException>(() => query.GetDueAsync(new DateOnly(2026, 10, 1)));
        await Assert.ThrowsAsync<ConnectionRefusedException>(() => query.GetDueAsync(new DateOnly(2026, 10, 1)));

        Assert.Equal(2, factory.Created.Count);
        Assert.NotSame(factory.Created[0], factory.Created[1]);
        Assert.All(factory.Created, context => Assert.Throws<ObjectDisposedException>(() => context.ChangeTracker));
    }

    [Fact]
    public async Task TheDeskWrapper_AlsoObtainsOneDisposedContextPerCall()
    {
        var factory = new SpySessionFactory();
        using var provider = Collect(factory, UserRole.Empleado, ValidSettings()).BuildServiceProvider();
        var desk = provider.GetRequiredService<ICollectionDesk>();
        var request = new CollectionRequest(Guid.NewGuid(), null, null, 0m);

        await Assert.ThrowsAsync<ConnectionRefusedException>(() => desk.PreviewAsync(request));
        await Assert.ThrowsAsync<ConnectionRefusedException>(() => desk.PreviewAsync(request));

        Assert.Equal(2, factory.Created.Count);
        Assert.NotSame(factory.Created[0], factory.Created[1]);
        Assert.All(factory.Created, context => Assert.Throws<ObjectDisposedException>(() => context.ChangeTracker));
    }

    [Fact]
    public async Task TheGoLiveWrapper_AlsoObtainsOneDisposedContextPerCall()
    {
        var factory = new SpySessionFactory();
        using var provider = Collect(factory, UserRole.Admin, ValidSettings()).BuildServiceProvider();
        var setup = provider.GetRequiredService<IGoLiveSetup>();

        await Assert.ThrowsAsync<ConnectionRefusedException>(() => setup.ListContractsAsync());
        await Assert.ThrowsAsync<ConnectionRefusedException>(() => setup.GetStartingNumberStateAsync());

        Assert.Equal(2, factory.Created.Count);
        Assert.NotSame(factory.Created[0], factory.Created[1]);
        Assert.All(factory.Created, context => Assert.Throws<ObjectDisposedException>(() => context.ChangeTracker));
    }

    [Fact]
    public async Task TheDirectoryWrapper_AlsoObtainsOneDisposedContextPerCall()
    {
        var factory = new SpySessionFactory();
        using var provider = Collect(factory, UserRole.Empleado, ValidSettings()).BuildServiceProvider();
        var directory = provider.GetRequiredService<IAccountDirectory>();

        await Assert.ThrowsAsync<ConnectionRefusedException>(() => directory.SearchAsync("perez"));
        await Assert.ThrowsAsync<ConnectionRefusedException>(() => directory.SearchAsync("perez"));

        Assert.Equal(2, factory.Created.Count);
        Assert.NotSame(factory.Created[0], factory.Created[1]);
        Assert.All(factory.Created, context => Assert.Throws<ObjectDisposedException>(() => context.ChangeTracker));
    }

    [Fact]
    public async Task TheReaderWrapper_AlsoObtainsOneDisposedContextPerCall()
    {
        var factory = new SpySessionFactory();
        using var provider = Collect(factory, UserRole.Empleado, ValidSettings()).BuildServiceProvider();
        var reader = provider.GetRequiredService<IReciboReader>();

        await Assert.ThrowsAsync<ConnectionRefusedException>(() => reader.FindByNumberAsync(31001));
        await Assert.ThrowsAsync<ConnectionRefusedException>(() => reader.RecentAsync(50));

        Assert.Equal(2, factory.Created.Count);
        Assert.NotSame(factory.Created[0], factory.Created[1]);
        Assert.All(factory.Created, context => Assert.Throws<ObjectDisposedException>(() => context.ChangeTracker));
    }
}
