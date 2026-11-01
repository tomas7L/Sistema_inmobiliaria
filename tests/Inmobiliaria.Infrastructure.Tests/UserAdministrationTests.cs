using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Indices;
using Inmobiliaria.Domain.Leasing;
using Inmobiliaria.Domain.Shared;
using Inmobiliaria.Domain.Units;
using Inmobiliaria.Infrastructure.Access;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Proves <see cref="PostgresUserAdministration"/> (gestion-de-usuarios spec Tests 1 to 3, 5 to 10, 14, 15, 22
/// and 23, and user-registry tests 9 to 12, 14 to 16 and 31) against a real Postgres engine, as an Admin with the
/// bootstrap-runbook grant shape (<see cref="AccessTestSupport.GrantProvisioningCapabilityAsync"/>) and as an
/// Empleado. Without Supavisor in front of this database, a role name carries the project suffix itself, so the
/// adapter is given <see cref="Composed"/>; the typed username, its validation and the login stay as in
/// production. Also holds the self-service password change tests (user-registry 17 and 18).
///
/// Every created user lands in <c>inmobiliaria_empleado</c> and never in <c>inmobiliaria_admin</c>: an Admin
/// created by <c>app_create_login_role</c> could never provision anyone, so creating an Admin stays the runbook's
/// job (<c>docs/runbooks/bootstrap-first-admin.md</c>).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UserAdministrationTests
{
    private static readonly Func<string, string> Composed =
        username => SupavisorUsername.For(username, AccessTestSupport.TestProjectRef);

    private readonly PostgresFixture _fixture;

    public UserAdministrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>gestion-de-usuarios Test 1: every user, with role, state and pending change; a role-less user has no role.</summary>
    [SkippableFact]
    public async Task List_ShowsEveryUser_WithRoleStateAndPendingChange()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await using var admin = await AdminAsync(superuserContext, "list-admin1");

        var sofia = await admin.Users.CreateEmpleadoAsync("sofia1", "Sofía Uno");
        var maria = await admin.Users.CreateEmpleadoAsync("maria1", "María Uno");
        var mariaId = await IdOfAsync(superuserContext, "maria1");
        await admin.Users.DeactivateAsync(mariaId);
        var orphanId = await ProvisionWithNoGroupAsync(superuserContext, "nogroup1");

        var listing = await admin.Users.ListAsync();

        Assert.True(sofia.Succeeded && maria.Succeeded);
        var sofiaRow = Assert.Single(listing, u => u.Username == Composed("sofia1"));
        Assert.Equal((UserRole?)UserRole.Empleado, sofiaRow.Role);
        Assert.True(sofiaRow.IsActive);
        Assert.True(sofiaRow.MustChangePassword);
        Assert.Equal("Sofía Uno", sofiaRow.DisplayName);

        var mariaRow = Assert.Single(listing, u => u.Id == mariaId);
        Assert.Equal((UserRole?)UserRole.Empleado, mariaRow.Role);
        Assert.False(mariaRow.IsActive);

        Assert.Equal((UserRole?)UserRole.Admin, Assert.Single(listing, u => u.Id == admin.Id).Role);
        Assert.Null(Assert.Single(listing, u => u.Id == orphanId).Role);
    }

    /// <summary>
    /// gestion-de-usuarios Test 2, user-registry tests 9 and 15: the role and the row land together, as an Empleado
    /// with a pending change, and the generated password logs in. A forced failure orphans neither half.
    /// </summary>
    [SkippableFact]
    public async Task Create_MakesAnEmpleadoAsOneUnit_WhoseGeneratedPasswordLogsIn()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await using var admin = await AdminAsync(superuserContext, "prov-admin2");

        var result = await admin.Users.CreateEmpleadoAsync("Sofia2 ", "  Sofía Pérez ");

        var credential = Assert.IsType<ProvisionalCredential>(result.Credential);
        Assert.Equal(Composed("sofia2"), credential.Username);
        Assert.Matches("^[a-z]+-[a-z]+-[a-z]+-[0-9]{5}$", credential.Password);

        var row = await superuserContext.AppUsers.AsNoTracking().SingleAsync(u => u.Username == Composed("sofia2"));
        Assert.True(row.IsActive);
        Assert.True(row.MustChangePassword);
        Assert.Equal("Sofía Pérez", row.DisplayName);
        Assert.True(await HasRoleAsync(superuserContext, Composed("sofia2"), "inmobiliaria_empleado"));
        Assert.False(await HasRoleAsync(superuserContext, Composed("sofia2"), "inmobiliaria_admin"));

        var authenticator = new NpgsqlAuthenticator(AccessTestSupport.BuildEndpoint(superuserContext));
        var login = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync("sofia2", credential.Password));
        Assert.True(login.Session.MustChangePassword);
        await login.Factory.DisposeAsync();

        // A PostgreSQL role that already exists with no app_users row: CREATE ROLE fails inside the same
        // transaction (42710), the name is reported as taken, and no row is left behind.
        string preCreateRoleSql = $"CREATE ROLE \"{Composed("collide2")}\" LOGIN PASSWORD 'Whatever123!';";
        await superuserContext.Database.ExecuteSqlRawAsync(preCreateRoleSql);

        var collision = await admin.Users.CreateEmpleadoAsync("collide2", "Should Not Land");
        Assert.Equal(UserAdminRefusal.UsernameTaken, collision.Refusal);
        Assert.False(await superuserContext.AppUsers.AnyAsync(u => u.Username == Composed("collide2")));
    }

    /// <summary>gestion-de-usuarios Test 3, user-registry test 12: a taken username is refused before any password exists.</summary>
    [SkippableFact]
    public async Task Create_WithATakenUsername_IsRefusedBeforeAnyPasswordIsMade()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        var generated = 0;
        await using var admin = await AdminAsync(superuserContext, "prov-admin3", () =>
        {
            generated++;
            return ProvisionalPassword.Generate();
        });
        await AccessTestSupport.ProvisionUserAsync(superuserContext, "maria3", UserRole.Empleado, isActive: false);
        await admin.Users.CreateEmpleadoAsync("ana3", "Ana Tres");
        generated = 0;

        var deactivatedTaken = await admin.Users.CreateEmpleadoAsync("maria3", "Otra María");
        var activeTaken = await admin.Users.CreateEmpleadoAsync("ANA3", "Otra Ana");

        Assert.Equal(UserAdminRefusal.UsernameTaken, deactivatedTaken.Refusal);
        Assert.Equal(UserAdminRefusal.UsernameTaken, activeTaken.Refusal);
        Assert.Equal(0, generated);
        var mariaRow = Assert.Single(await superuserContext.AppUsers.AsNoTracking()
            .Where(u => u.Username == Composed("maria3")).ToListAsync());
        Assert.False(mariaRow.IsActive);
    }

    /// <summary>gestion-de-usuarios Test 4's database half: invalid input is refused before any database work.</summary>
    [SkippableTheory]
    [InlineData("josé pérez", "José Pérez", UserAdminRefusal.UsernameInvalid)]
    [InlineData("2juan", "Juan", UserAdminRefusal.UsernameInvalid)]
    [InlineData("juan4", "   ", UserAdminRefusal.DisplayNameMissing)]
    public async Task Create_WithInvalidInput_IsRefused(string username, string displayName, UserAdminRefusal refusal)
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await using var admin = await AdminAsync(superuserContext, $"prov-admin4{Guid.NewGuid():N}"[..20]);

        var result = await admin.Users.CreateEmpleadoAsync(username, displayName);

        Assert.Equal(refusal, result.Refusal);
        Assert.Null(result.Credential);
    }

    /// <summary>
    /// gestion-de-usuarios Test 5, user-registry tests 14 and 16: a reset sets a new password and the pending change;
    /// the old password stops working; an open session of that user keeps working.
    /// </summary>
    [SkippableFact]
    public async Task Reset_GivesANewPassword_AndLeavesAnOpenSessionAlone()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await using var admin = await AdminAsync(
            superuserContext, "prov-admin5", Passwords("first-pass-11111", "second-pass-22222"));
        await admin.Users.CreateEmpleadoAsync("reset5", "Reset Cinco");
        var authenticator = new NpgsqlAuthenticator(AccessTestSupport.BuildEndpoint(superuserContext));
        var openSession = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync("reset5", "first-pass-11111"));
        await new PostgresPasswordService(openSession.Factory, openSession.Session)
            .ChangeOwnPasswordAsync("first-pass-11111", "chosen-by-her");

        var result = await admin.Users.ResetPasswordAsync(await IdOfAsync(superuserContext, "reset5"));

        Assert.Equal("second-pass-22222", result.Credential?.Password);
        await using (var context = openSession.Factory.Create())
        {
            Assert.True(await context.AppUsers.AnyAsync());
        }
        await openSession.Factory.DisposeAsync();

        Assert.IsType<AuthenticationResult.Rejected>(await authenticator.AuthenticateAsync("reset5", "chosen-by-her"));
        var newLogin = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync("reset5", "second-pass-22222"));
        Assert.True(newLogin.Session.MustChangePassword);
        await newLogin.Factory.DisposeAsync();
    }

    /// <summary>gestion-de-usuarios Test 6: a deactivated user is not reset.</summary>
    [SkippableFact]
    public async Task Reset_OfADeactivatedUser_IsRefused_AndChangesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await using var admin = await AdminAsync(superuserContext, "prov-admin6");
        await admin.Users.CreateEmpleadoAsync("reset6", "Reset Seis");
        var id = await IdOfAsync(superuserContext, "reset6");
        await admin.Users.DeactivateAsync(id);

        var result = await admin.Users.ResetPasswordAsync(id);

        Assert.Equal(UserAdminRefusal.AlreadyDeactivated, result.Refusal);
        Assert.False(await CanLoginAsync(superuserContext, Composed("reset6")));
    }

    /// <summary>
    /// gestion-de-usuarios Test 7, user-registry test 10: deactivation sets NOLOGIN and IsActive = false, the next
    /// login fails, and a second deactivation succeeds and changes nothing.
    /// </summary>
    [SkippableFact]
    public async Task Deactivate_StopsTheNextLogin_AndASecondTimeIsANoOp()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await using var admin = await AdminAsync(superuserContext, "prov-admin7");
        var created = await admin.Users.CreateEmpleadoAsync("deact7", "Deact Siete");
        var id = await IdOfAsync(superuserContext, "deact7");

        Assert.True((await admin.Users.DeactivateAsync(id)).Succeeded);
        Assert.True((await admin.Users.DeactivateAsync(id)).Succeeded);

        Assert.False(await CanLoginAsync(superuserContext, Composed("deact7")));
        Assert.False((await superuserContext.AppUsers.AsNoTracking().SingleAsync(u => u.Id == id)).IsActive);
        var authenticator = new NpgsqlAuthenticator(AccessTestSupport.BuildEndpoint(superuserContext));
        Assert.IsType<AuthenticationResult.Rejected>(
            await authenticator.AuthenticateAsync("deact7", created.Credential!.Password));
    }

    /// <summary>user-registry test 11: a deactivated user's ContractDocument and RentAdjustment rows stay readable and unchanged.</summary>
    [SkippableFact]
    public async Task Deactivate_LeavesHistoricalContractDocumentAndRentAdjustmentRowsUnchangedAndReadable()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await using var admin = await AdminAsync(superuserContext, "prov-admin11");
        await admin.Users.CreateEmpleadoAsync("maria11", "Maria Eleven");
        var maria11Id = await IdOfAsync(superuserContext, "maria11");

        var (seededContract, index) = SchemaConstraintTests.SeedContractWithClause(superuserContext);
        superuserContext.IndexValues.Add(
            new IndexValue(Guid.NewGuid(), index.Id, new IndexPeriod(2026, 7), 9_440m));
        await superuserContext.SaveChangesAsync();

        var document = new ContractDocument(
            Guid.NewGuid(), seededContract.Id, "storage/path11", "lease11.pdf", "application/pdf",
            DateTimeOffset.UtcNow, maria11Id, DocumentKind.Original);
        superuserContext.ContractDocuments.Add(document);

        var adjustment = SchemaConstraintTests.ConfirmAdjustment(
            seededContract, index.Id,
            new IndexPeriod(2026, 1), 8_000m,
            new IndexPeriod(2026, 7), 9_440m,
            new DateOnly(2026, 7, 1), DateTimeOffset.UtcNow, maria11Id);
        superuserContext.RentAdjustments.Add(adjustment);
        await superuserContext.SaveChangesAsync();

        await admin.Users.DeactivateAsync(maria11Id);

        await using var readContext = _fixture.CreateDbContext();

        var reloadedDocument = await readContext.ContractDocuments.SingleAsync(d => d.Id == document.Id);
        Assert.Equal(maria11Id, reloadedDocument.UploadedByUserId);

        var reloadedAdjustment = await readContext.RentAdjustments.SingleAsync(a => a.Id == adjustment.Id);
        Assert.Equal((Guid?)maria11Id, reloadedAdjustment.ConfirmedBy);

        var reloadedAppUser = await readContext.AppUsers.SingleAsync(u => u.Id == maria11Id);
        Assert.False(reloadedAppUser.IsActive);
    }

    /// <summary>user-registry test 31: a ContractDocument's uploader still resolves to the display name after deactivation.</summary>
    [SkippableFact]
    public async Task Deactivate_ContractDocumentUploaderReference_StillResolvesToDisplayName()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await using var admin = await AdminAsync(superuserContext, "prov-admin31");
        await admin.Users.CreateEmpleadoAsync("maria31", "maria31");
        var uploaderId = await IdOfAsync(superuserContext, "maria31");

        var unit = new PropertyUnit(Guid.NewGuid(), new Address("Fake Street", "1", "Springfield", "Buenos Aires", "1000"));
        superuserContext.Units.Add(unit);
        var contract = new Contract(
            Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31),
            100_000m, [new UnitShare(unit.Id, 100m)]);
        superuserContext.Contracts.Add(contract);
        var document = new ContractDocument(
            Guid.NewGuid(), contract.Id, "storage/path31", "lease31.pdf", "application/pdf",
            DateTimeOffset.UtcNow, uploaderId, DocumentKind.Original);
        superuserContext.ContractDocuments.Add(document);
        await superuserContext.SaveChangesAsync();

        await admin.Users.DeactivateAsync(uploaderId);

        await using var readContext = _fixture.CreateDbContext();
        var reloadedDocument = await readContext.ContractDocuments.SingleAsync(d => d.Id == document.Id);
        var uploader = await readContext.AppUsers.SingleAsync(u => u.Id == reloadedDocument.UploadedByUserId);

        Assert.Equal(uploaderId, reloadedDocument.UploadedByUserId);
        Assert.Equal("maria31", uploader.DisplayName);
        Assert.False(uploader.IsActive);
    }

    /// <summary>
    /// gestion-de-usuarios Test 8: reactivation restores the login with a new password and a pending change; the
    /// password from before is gone; the row and username are the same.
    /// </summary>
    [SkippableFact]
    public async Task Reactivate_RestoresTheSamePerson_WithANewPassword()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await using var admin = await AdminAsync(
            superuserContext, "prov-admin8", Passwords("before-left-11111", "came-back-22222"));
        await admin.Users.CreateEmpleadoAsync("maria8", "María Ocho");
        var id = await IdOfAsync(superuserContext, "maria8");
        await admin.Users.DeactivateAsync(id);

        var result = await admin.Users.ReactivateAsync(id);

        Assert.Equal(new ProvisionalCredential(Composed("maria8"), "came-back-22222"), result.Credential);
        var row = await superuserContext.AppUsers.AsNoTracking().SingleAsync(u => u.Id == id);
        Assert.True(row.IsActive);
        Assert.True(row.MustChangePassword);
        Assert.Equal(Composed("maria8"), row.Username);

        var authenticator = new NpgsqlAuthenticator(AccessTestSupport.BuildEndpoint(superuserContext));
        Assert.IsType<AuthenticationResult.Rejected>(await authenticator.AuthenticateAsync("maria8", "before-left-11111"));
        var login = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync("maria8", "came-back-22222"));
        Assert.True(login.Session.MustChangePassword);
        await login.Factory.DisposeAsync();
    }

    /// <summary>gestion-de-usuarios Test 9: a reactivation whose password step fails leaves the user deactivated.</summary>
    [SkippableFact]
    public async Task Reactivate_IsAllOrNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        // A NUL character can never be sent as text, so the password statement fails after LOGIN was restored
        // inside the same transaction.
        await using var admin = await AdminAsync(
            superuserContext, "prov-admin9", Passwords("before-left-11111", "broken\0password"));
        await admin.Users.CreateEmpleadoAsync("maria9", "María Nueve");
        var id = await IdOfAsync(superuserContext, "maria9");
        await admin.Users.DeactivateAsync(id);

        await Assert.ThrowsAnyAsync<Exception>(() => admin.Users.ReactivateAsync(id));

        Assert.False(await CanLoginAsync(superuserContext, Composed("maria9")));
        Assert.False((await superuserContext.AppUsers.AsNoTracking().SingleAsync(u => u.Id == id)).IsActive);
    }

    /// <summary>gestion-de-usuarios Test 10: reactivating an active user is refused.</summary>
    [SkippableFact]
    public async Task Reactivate_OfAnActiveUser_IsRefused()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await using var admin = await AdminAsync(superuserContext, "prov-admin10");
        await admin.Users.CreateEmpleadoAsync("ana10", "Ana Diez");

        var result = await admin.Users.ReactivateAsync(await IdOfAsync(superuserContext, "ana10"));

        Assert.Equal(UserAdminRefusal.AlreadyActive, result.Refusal);
    }

    /// <summary>gestion-de-usuarios Test 14's port half: an Admin, the Admin herself or a role-less user is never acted on.</summary>
    [SkippableFact]
    public async Task ActionsOnANonEmpleado_AreRefused_AndChangeNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await using var admin = await AdminAsync(superuserContext, "prov-admin14");
        var otherAdminId = await AccessTestSupport.ProvisionUserAsync(superuserContext, "other-admin14", UserRole.Admin);
        var orphanId = await ProvisionWithNoGroupAsync(superuserContext, "nogroup14");

        foreach (var id in new[] { admin.Id, otherAdminId, orphanId })
        {
            Assert.Equal(UserAdminRefusal.NotAnEmpleado, (await admin.Users.ResetPasswordAsync(id)).Refusal);
            Assert.Equal(UserAdminRefusal.NotAnEmpleado, (await admin.Users.DeactivateAsync(id)).Refusal);
            Assert.Equal(UserAdminRefusal.NotAnEmpleado, (await admin.Users.ReactivateAsync(id)).Refusal);
            Assert.True((await superuserContext.AppUsers.AsNoTracking().SingleAsync(u => u.Id == id)).IsActive);
        }

        Assert.Equal(UserAdminRefusal.UserNotFound, (await admin.Users.ResetPasswordAsync(Guid.NewGuid())).Refusal);
        Assert.Equal(UserAdminRefusal.UserNotFound, (await admin.Users.DeactivateAsync(Guid.NewGuid())).Refusal);
        Assert.Equal(UserAdminRefusal.UserNotFound, (await admin.Users.ReactivateAsync(Guid.NewGuid())).Refusal);
    }

    /// <summary>gestion-de-usuarios Test 15: a role another Admin created is refused by name, and nothing changes.</summary>
    [SkippableFact]
    public async Task ActionsOnARoleAnotherAdminCreated_AreRefusedByName()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await using var creator = await AdminAsync(superuserContext, "creator15", Passwords("first-pass-11111", "back-again-22222"));
        await using var other = await AdminAsync(superuserContext, "other15");
        await creator.Users.CreateEmpleadoAsync("sofia15", "Sofía Quince");
        var id = await IdOfAsync(superuserContext, "sofia15");

        Assert.Equal(UserAdminRefusal.CreatedByAnotherAdmin, (await other.Users.ResetPasswordAsync(id)).Refusal);
        Assert.Equal(UserAdminRefusal.CreatedByAnotherAdmin, (await other.Users.DeactivateAsync(id)).Refusal);
        await creator.Users.DeactivateAsync(id);
        Assert.Equal(UserAdminRefusal.CreatedByAnotherAdmin, (await other.Users.ReactivateAsync(id)).Refusal);
        Assert.False(await CanLoginAsync(superuserContext, Composed("sofia15")));
        await creator.Users.ReactivateAsync(id);

        var row = await superuserContext.AppUsers.AsNoTracking().SingleAsync(u => u.Id == id);
        Assert.True(row.IsActive);
        Assert.True(await CanLoginAsync(superuserContext, Composed("sofia15")));
    }

    /// <summary>An Admin login without the runbook's grants cannot create roles; that is said, not blamed on another Admin.</summary>
    [SkippableFact]
    public async Task Create_ByAnAdminWithoutTheRunbookGrants_SaysSo()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        var id = await AccessTestSupport.ProvisionUserAsync(superuserContext, "bare-admin", UserRole.Admin);
        await using var factory = AccessTestSupport.BuildSessionFactoryAs(superuserContext, "bare-admin");
        var session = new UserSession(new AppUser(id, Composed("bare-admin"), "bare-admin"), UserRole.Admin);
        var users = new PostgresUserAdministration(factory, session, roleNameFor: Composed);

        var result = await users.CreateEmpleadoAsync("nadie", "Nadie");

        Assert.Equal(UserAdminRefusal.AdminGrantsMissing, result.Refusal);
        Assert.False(await superuserContext.AppUsers.AnyAsync(u => u.Username == Composed("nadie")));
    }

    /// <summary>gestion-de-usuarios Test 22: an Empleado session is refused by the database, and nothing changes.</summary>
    [SkippableFact]
    public async Task AnEmpleadoSession_IsRefusedByTheDatabase()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await using var admin = await AdminAsync(superuserContext, "prov-admin22");
        await admin.Users.CreateEmpleadoAsync("active22", "Activa");
        await admin.Users.CreateEmpleadoAsync("gone22", "Desactivada");
        var activeId = await IdOfAsync(superuserContext, "active22");
        var goneId = await IdOfAsync(superuserContext, "gone22");
        await admin.Users.DeactivateAsync(goneId);

        var empleadoId = await AccessTestSupport.ProvisionUserAsync(superuserContext, "empleado22", UserRole.Empleado);
        await using var empleadoFactory = AccessTestSupport.BuildSessionFactoryAs(superuserContext, "empleado22");
        var empleadoSession = new UserSession(
            new AppUser(empleadoId, Composed("empleado22"), "empleado22"), UserRole.Empleado);
        var asEmpleado = new PostgresUserAdministration(empleadoFactory, empleadoSession, roleNameFor: Composed);

        Assert.Equal(UserAdminRefusal.AdminOnly, (await asEmpleado.CreateEmpleadoAsync("intruso22", "Intruso")).Refusal);
        Assert.Equal(UserAdminRefusal.AdminOnly, (await asEmpleado.ResetPasswordAsync(activeId)).Refusal);
        Assert.Equal(UserAdminRefusal.AdminOnly, (await asEmpleado.DeactivateAsync(activeId)).Refusal);
        Assert.Equal(UserAdminRefusal.AdminOnly, (await asEmpleado.ReactivateAsync(goneId)).Refusal);
        Assert.Equal(UserAdminRefusal.AdminOnly, (await asEmpleado.DeactivateAsync(goneId)).Refusal);

        Assert.False(await superuserContext.AppUsers.AnyAsync(u => u.Username == Composed("intruso22")));
        Assert.True(await CanLoginAsync(superuserContext, Composed("active22")));
        Assert.False(await CanLoginAsync(superuserContext, Composed("gone22")));
        var rows = await superuserContext.AppUsers.AsNoTracking()
            .Where(u => u.Id == activeId || u.Id == goneId).ToListAsync();
        Assert.True(rows.Single(u => u.Id == activeId).IsActive);
        Assert.False(rows.Single(u => u.Id == goneId).IsActive);
    }

    /// <summary>gestion-de-usuarios Test 23: a username with "ñ" is created and logs in against real PostgreSQL.</summary>
    [SkippableFact]
    public async Task AUsernameWithEnye_IsCreatedAndLogsIn()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await using var admin = await AdminAsync(superuserContext, "prov-admin23");

        var result = await admin.Users.CreateEmpleadoAsync("Nuñez23", "Lucía Núñez");

        Assert.Equal(Composed("nuñez23"), result.Credential?.Username);
        var authenticator = new NpgsqlAuthenticator(AccessTestSupport.BuildEndpoint(superuserContext));
        var login = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync("NUÑEZ23", result.Credential!.Password));
        Assert.Equal("Lucía Núñez", login.Session.DisplayName);
        await login.Factory.DisposeAsync();
    }

    /// <summary>user-registry test 17: re-submitting the just-authenticated password as the "new" one is rejected; the pending requirement remains set.</summary>
    [SkippableFact]
    public async Task ChangeOwnPassword_RejectsResubmittingTheJustAuthenticatedPassword_RequirementStaysPending()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await AccessTestSupport.ProvisionUserAsync(
            superuserContext, "sofia17", UserRole.Empleado, mustChangePassword: true);

        var authenticator = new NpgsqlAuthenticator(AccessTestSupport.BuildEndpoint(superuserContext));
        var login = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync("sofia17", AccessTestSupport.DefaultPassword));
        Assert.True(login.Session.MustChangePassword);

        var passwordService = new PostgresPasswordService(login.Factory, login.Session);

        var exception = await Record.ExceptionAsync(
            () => passwordService.ChangeOwnPasswordAsync(
                AccessTestSupport.DefaultPassword, AccessTestSupport.DefaultPassword));
        var refused = Assert.IsType<PasswordChangeRefusedException>(exception);
        Assert.Equal(PasswordRefusal.SameAsCurrent, refused.Refusal);

        await login.Factory.DisposeAsync();

        // The requirement remains pending — proven by re-authenticating and reading the flag
        // back, not merely by the absence of a successful change.
        var reloadedLogin = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync("sofia17", AccessTestSupport.DefaultPassword));
        Assert.True(reloadedLogin.Session.MustChangePassword);
        await reloadedLogin.Factory.DisposeAsync();
    }

    /// <summary>
    /// Spec "Setting It MUST Clear The Requirement", read back from the database. Live smoke H.5 (2026-10-24):
    /// the forced change was asked again at every login, because the clearing function matched nobody.
    /// </summary>
    [SkippableFact]
    public async Task ChangeOwnPassword_CompletingAForcedChange_ClearsTheRequirementForTheNextLogin()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await AccessTestSupport.ProvisionUserAsync(
            superuserContext, "sofia19", UserRole.Empleado, mustChangePassword: true);

        var authenticator = new NpgsqlAuthenticator(AccessTestSupport.BuildEndpoint(superuserContext));
        var login = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync("sofia19", AccessTestSupport.DefaultPassword));
        Assert.True(login.Session.MustChangePassword);

        const string newPassword = "ForcedChange123!";
        await new PostgresPasswordService(login.Factory, login.Session)
            .ChangeOwnPasswordAsync(AccessTestSupport.DefaultPassword, newPassword);
        await login.Factory.DisposeAsync();

        var reloadedLogin = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync("sofia19", newPassword));
        Assert.False(reloadedLogin.Session.MustChangePassword);
        await reloadedLogin.Factory.DisposeAsync();
    }

    /// <summary>user-registry test 18: a voluntary change with nothing pending does not set MustChangePassword.</summary>
    [SkippableFact]
    public async Task ChangeOwnPassword_VoluntaryChangeWithNothingPending_RaisesNoRequirement()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await AccessTestSupport.ProvisionUserAsync(superuserContext, "lucia18", UserRole.Empleado);

        var authenticator = new NpgsqlAuthenticator(AccessTestSupport.BuildEndpoint(superuserContext));
        var login = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync("lucia18", AccessTestSupport.DefaultPassword));
        Assert.False(login.Session.MustChangePassword);

        var passwordService = new PostgresPasswordService(login.Factory, login.Session);
        const string newPassword = "VoluntaryChange123!";
        await passwordService.ChangeOwnPasswordAsync(AccessTestSupport.DefaultPassword, newPassword);
        await login.Factory.DisposeAsync();

        var reloadedLogin = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync("lucia18", newPassword));
        Assert.False(reloadedLogin.Session.MustChangePassword);
        await reloadedLogin.Factory.DisposeAsync();
    }

    /// <summary>An Admin with the bootstrap-runbook grant shape, and the adapter as that Admin's session would build it.</summary>
    private static async Task<AdminUnderTest> AdminAsync(
        InmobiliariaDbContext superuserContext, string rawAdminUsername, Func<string>? generatePassword = null)
    {
        var id = await AccessTestSupport.ProvisionUserAsync(superuserContext, rawAdminUsername, UserRole.Admin);
        await AccessTestSupport.GrantProvisioningCapabilityAsync(superuserContext, rawAdminUsername);
        var factory = AccessTestSupport.BuildSessionFactoryAs(superuserContext, rawAdminUsername);
        var session = new UserSession(new AppUser(id, Composed(rawAdminUsername), rawAdminUsername), UserRole.Admin);
        return new AdminUnderTest(id, factory, new PostgresUserAdministration(factory, session, generatePassword, Composed));
    }

    private static Func<string> Passwords(params string[] passwords)
    {
        var queue = new Queue<string>(passwords);
        return queue.Dequeue;
    }

    private static Task<Guid> IdOfAsync(InmobiliariaDbContext superuserContext, string rawUsername) =>
        superuserContext.AppUsers.AsNoTracking()
            .Where(u => u.Username == Composed(rawUsername))
            .Select(u => u.Id)
            .SingleAsync();

    /// <summary>A login role in neither group role, with its app_users row: listed, never actionable.</summary>
    private static async Task<Guid> ProvisionWithNoGroupAsync(InmobiliariaDbContext superuserContext, string rawUsername)
    {
        string createRoleSql = $"CREATE ROLE \"{Composed(rawUsername)}\" LOGIN PASSWORD '{AccessTestSupport.DefaultPassword}';";
        await superuserContext.Database.ExecuteSqlRawAsync(createRoleSql);
        var user = new AppUser(Guid.NewGuid(), Composed(rawUsername), rawUsername);
        superuserContext.AppUsers.Add(user);
        await superuserContext.SaveChangesAsync();
        return user.Id;
    }

    private static Task<bool> HasRoleAsync(InmobiliariaDbContext superuserContext, string roleName, string groupRole) =>
        superuserContext.Database
            .SqlQueryRaw<bool>("SELECT pg_has_role({0}, {1}, 'MEMBER') AS \"Value\"", roleName, groupRole)
            .SingleAsync();

    private static Task<bool> CanLoginAsync(InmobiliariaDbContext superuserContext, string roleName) =>
        superuserContext.Database
            .SqlQueryRaw<bool>("SELECT rolcanlogin AS \"Value\" FROM pg_roles WHERE rolname = {0}", roleName)
            .SingleAsync();

    private sealed record AdminUnderTest(Guid Id, ISessionDbContextFactory Factory, PostgresUserAdministration Users)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }
}
