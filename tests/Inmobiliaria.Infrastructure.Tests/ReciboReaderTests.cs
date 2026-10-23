using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// ui-de-cobranza spec tests 21, 22, 23 and 41 (reader half), design section 5.2. Recibos are issued and
/// voided through the real desk, and read back as each real login role.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReciboReaderTests
{
    private readonly PostgresFixture _fixture;

    public ReciboReaderTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<(InmobiliariaDbContext Context, string Username)> ContextAsAsync(DeskWorld world, UserRole role)
    {
        var username = $"r{Guid.NewGuid():N}"[..20];
        await AccessTestSupport.ProvisionUserAsync(world.Database.Context, username, role);
        return (AccessTestSupport.BuildDbContextAs(world.Database.Context, username), username);
    }

    private static async Task<CollectionResult> PayAsync(DeskWorld world, Guid accountId)
    {
        var result = await DeskWorld.PayAsync(world.NewDesk(), accountId);
        Assert.True(result.Succeeded, $"Refused: {result.Refusal}");
        return result;
    }

    private static async Task VoidAsync(ICollectionDesk desk, Guid reciboId, string reason)
    {
        var preview = await desk.PreviewVoidAsync(reciboId, reason, UserRole.Admin);
        Assert.True(preview.CanCommit, $"The voiding was refused: {preview.Refusal}.");
        Assert.True((await desk.VoidAsync(preview)).Succeeded);
    }

    /// <summary>Spec test 21: either number finds the whole pair; an unknown number finds nothing.</summary>
    [SkippableTheory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Empleado)]
    public async Task EitherNumber_FindsTheWholePair_AndAnUnknownNumberFindsNothing(UserRole role)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture);
        var paid = await PayAsync(world, await world.SeedAccountAsync());
        var (context, _) = await ContextAsAsync(world, role);
        await using var _context = context;
        var reader = new ReciboReader(context);

        foreach (var number in new[] { paid.LocatarioNumber!.Value, paid.PropietarioNumber!.Value })
        {
            var payment = await reader.FindByNumberAsync(number);

            Assert.NotNull(payment);
            Assert.Equal(paid.CollectionId, payment.CollectionId);
            Assert.Equal((paid.LocatarioNumber, ReciboCopy.Locatario), (payment.Locatario.Recibo.Number, payment.Locatario.Recibo.Copy));
            Assert.Equal((paid.PropietarioNumber, ReciboCopy.Propietario), (payment.Propietario.Recibo.Number, payment.Propietario.Recibo.Copy));
            Assert.Equal(DeskWorld.Today, payment.RecordedOn);
            Assert.False(payment.IsAnnulled);
        }

        Assert.Null(await reader.FindByNumberAsync(paid.PropietarioNumber!.Value + 1));
        Assert.Null(await reader.GetByCollectionAsync(Guid.NewGuid()));
    }

    /// <summary>Spec test 21: a number outside 1 to int.MaxValue returns nothing, without a query.</summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(-5L)]
    [InlineData(2_147_483_648L)]
    public async Task ANumberOutsideTheColumnsRange_ReturnsNothing_WithoutAQuery(long number)
    {
        // Nothing listens on port 1: any query would throw instead of returning null.
        var options = new DbContextOptionsBuilder<InmobiliariaDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1")
            .Options;
        await using var unreachable = new InmobiliariaDbContext(options);

        Assert.Null(await new ReciboReader(unreachable).FindByNumberAsync(number));
    }

    /// <summary>
    /// Spec test 21: the recent list, newest first, shows both numbers, the tenant, the period, the received
    /// and recorded dates and the annulled flag.
    /// </summary>
    [SkippableTheory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Empleado)]
    public async Task TheRecentList_ShowsNumbersTenantDatesAndTheAnnulledFlag_NewestFirst(UserRole role)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture);
        var first = await PayAsync(world, await world.SeedAccountAsync());
        var secondAccount = await world.SeedAccountAsync();
        var second = await PayAsync(world, secondAccount);
        await VoidAsync(world.NewDesk(), await world.LatestReciboIdAsync(secondAccount), "Cobrado por error");

        var (context, _) = await ContextAsAsync(world, role);
        await using var _context = context;
        var recent = await new ReciboReader(context).RecentAsync(50);

        Assert.Equal([second.CollectionId!.Value, first.CollectionId!.Value], recent.Select(r => r.CollectionId));
        var newest = recent[0];
        Assert.Equal(
            (second.LocatarioNumber!.Value, second.PropietarioNumber!.Value, "Perez, Maria", DeskWorld.OldestPeriod, DeskWorld.Today, true),
            (newest.LocatarioNumber, newest.PropietarioNumber, newest.Tenant, newest.Period, newest.ReceivedOn, newest.IsAnnulled));
        Assert.False(recent[1].IsAnnulled);
        Assert.True(recent[0].RecordedAt >= recent[1].RecordedAt);
        Assert.Equal(DeskWorld.Today, DateOnly.FromDateTime(newest.RecordedAt.DateTime));

        Assert.Single(await new ReciboReader(context).RecentAsync(1));
    }

    /// <summary>
    /// Spec test 22: an annulled pair reads with its reason, the voiding user's display name and the time,
    /// read at the moment of the call: a read before the voiding was live, the next reads annulled.
    /// </summary>
    [SkippableFact]
    public async Task AnAnnulledPair_ReadsItsReasonUserAndTime_AtTheMomentOfTheCall()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture);
        var accountId = await world.SeedAccountAsync();
        var paid = await PayAsync(world, accountId);
        var reader = new ReciboReader(world.NewContext());

        Assert.False((await reader.FindByNumberAsync(paid.LocatarioNumber!.Value))!.IsAnnulled);

        var (admin, username) = await ContextAsAsync(world, UserRole.Admin);
        await using var _admin = admin;
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);
        await VoidAsync(new CollectionDesk(admin, new AgencyClock(), DeskWorld.Terms), await world.LatestReciboIdAsync(accountId), "Error de carga");

        var annulled = await reader.FindByNumberAsync(paid.LocatarioNumber!.Value);

        Assert.NotNull(annulled!.Annulment);
        Assert.Equal(("Error de carga", username), (annulled.Annulment.Reason, annulled.Annulment.AnnulledBy));
        Assert.InRange(annulled.Annulment.AnnulledAt, before, DateTimeOffset.UtcNow.AddMinutes(1));
    }

    /// <summary>Design D26: a role with no application user row is shown by its role name.</summary>
    [SkippableFact]
    public async Task AVoidingByARoleWithNoUserRow_ShowsTheRoleName()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture);
        var accountId = await world.SeedAccountAsync();
        var paid = await PayAsync(world, accountId);
        await VoidAsync(world.NewDesk(), await world.LatestReciboIdAsync(accountId), "Duplicado");

        await using var connection = await ReciboTestSupport.OpenAsync(world.Database.Context);
        var superuser = await ReciboTestSupport.ScalarAsync<string>(connection, "SELECT current_user::text");

        var annulled = await new ReciboReader(world.NewContext()).FindByNumberAsync(paid.PropietarioNumber!.Value);

        Assert.Equal(superuser, annulled!.Annulment!.AnnulledBy);
    }

    /// <summary>
    /// Spec tests 23 and 41, reader half: after the contract's rate and the tenant's name change, the
    /// honorarios line, every amount, the total (the sum of the signed lines, equal to the stored total) and
    /// the header read as issued.
    /// </summary>
    [SkippableFact]
    public async Task ARecibo_ReadsAsIssued_AfterTheContractAndTheTenantChange()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var world = await DeskWorld.CreateAsync(_fixture);
        var accountId = await world.SeedAccountAsync();
        var paid = await PayAsync(world, accountId);
        var issued = (await new ReciboReader(world.NewContext()).GetByCollectionAsync(paid.CollectionId!.Value))!;

        await using (var connection = await ReciboTestSupport.OpenAsync(world.Database.Context))
        {
            await ReciboTestSupport.ExecAsync(
                connection,
                """
                UPDATE contracts SET honorarios_percentage = 20
                 WHERE id = (SELECT contract_id FROM contract_accounts WHERE id = @account)
                """,
                ("account", accountId));
            await ReciboTestSupport.ExecAsync(connection, "UPDATE parties SET last_name = 'Otra' WHERE last_name = 'Perez'");
        }

        var later = (await new ReciboReader(world.NewContext()).GetByCollectionAsync(paid.CollectionId!.Value))!;

        foreach (var (before, after) in new[] { (issued.Locatario, later.Locatario), (issued.Propietario, later.Propietario) })
        {
            Assert.Equal(before.ReciboId, after.ReciboId);
            Assert.Equal(before.Recibo.Lines, after.Recibo.Lines);
            Assert.Equal(before.Recibo.Total, after.Recibo.Total);
            Assert.Equal(after.Recibo.Lines.Sum(l => l.Amount), after.Recibo.Total);
            Assert.Equal("Perez, Maria", after.Recibo.Header.Tenant);
            Assert.Equal(before.Recibo.Header.Lessors, after.Recibo.Header.Lessors);
            Assert.Equal(before.Recibo.Header.Units, after.Recibo.Header.Units);
        }

        Assert.Contains(later.Propietario.Recibo.Lines, l => l.Kind == ReciboLineKind.HonorariosAdministracion);

        await using var check = await ReciboTestSupport.OpenAsync(world.Database.Context);
        Assert.Equal(
            later.Propietario.Recibo.Total,
            await ReciboTestSupport.ScalarAsync<decimal>(check, "SELECT total FROM recibos WHERE id = @id", ("id", later.Propietario.ReciboId)));
    }
}
