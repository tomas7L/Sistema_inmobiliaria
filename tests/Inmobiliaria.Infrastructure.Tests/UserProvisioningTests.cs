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
/// Proves <see cref="PostgresUserProvisioning"/> (spec tests 9–12, 14, 15's database half, 16–18,
/// 31) against a real Postgres engine. Every calling "Admin" here is granted the exact bootstrap-
/// runbook shape (<see cref="AccessTestSupport.GrantProvisioningCapabilityAsync"/>) — task 3.12
/// proved a bare Admin membership cannot successfully call <c>app_create_login_role</c> or
/// <c>app_set_role_password</c> against another role at all, for either group role.
///
/// <b>The provisioning decision this file proves (task the design gate left open):</b> a login
/// role created by <c>app_create_login_role</c> never receives <c>WITH ADMIN OPTION</c> on
/// anything (design Decision 3's function body — not modifiable from this slice, no migration
/// allowed here). An Admin created that way could therefore never itself provision anyone,
/// Empleado or Admin alike — task 3.12's finding applied forward. Rather than ship a role that
/// carries every Admin table grant but is structurally unable to do the one thing that is
/// supposed to set her apart, <see cref="IUserProvisioning.CreateUserAsync"/> creates ONLY
/// Empleado accounts (see its own remarks for the full argument). Every test below that creates a
/// user asserts the new role lands in <c>inmobiliaria_empleado</c> and NEVER in
/// <c>inmobiliaria_admin</c> — the standing proof of that decision. Creating an additional Admin
/// remains <c>docs/runbooks/bootstrap-first-admin.md</c>'s job.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UserProvisioningTests
{
    private readonly PostgresFixture _fixture;

    public UserProvisioningTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Provisions a throwaway Admin test user and grants it the exact bootstrap-runbook shape
    /// (task 3.7 / <c>docs/runbooks/bootstrap-first-admin.md</c>) — the only grant shape task
    /// 3.12 proved actually lets a caller provision another role.
    /// </summary>
    private static async Task<ISessionDbContextFactory> ProvisionAdminWithCapabilityAsync(
        InmobiliariaDbContext superuserContext, string rawAdminUsername)
    {
        await AccessTestSupport.ProvisionUserAsync(superuserContext, rawAdminUsername, UserRole.Admin);
        await AccessTestSupport.GrantProvisioningCapabilityAsync(superuserContext, rawAdminUsername);
        return AccessTestSupport.BuildSessionFactoryAs(superuserContext, rawAdminUsername);
    }

    /// <summary>
    /// Spec test 9: the login role and the <c>app_users</c> row land as one unit, and a forced
    /// failure orphans neither. Also the standing proof of this file's provisioning decision:
    /// the new role is a member of <c>inmobiliaria_empleado</c> only, never
    /// <c>inmobiliaria_admin</c>.
    /// </summary>
    [SkippableFact]
    public async Task CreateUser_CreatesLoginRoleAndAppUsersRowAsOneUnit_AndAForcedFailureOrphansNeither()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await using var adminFactory = await ProvisionAdminWithCapabilityAsync(superuserContext, "prov-admin9");
        IUserProvisioning provisioning = new PostgresUserProvisioning(adminFactory);

        // --- Happy path: both halves land as one unit. ---
        var newUsername = $"sofia9{Guid.NewGuid():N}"[..24];
        await provisioning.CreateUserAsync(newUsername, "ProvisionalPass123!", "Sofia Nueva");

        await using (var readContext = _fixture.CreateDbContext())
        {
            var appUser = await readContext.AppUsers.SingleAsync(u => u.Username == newUsername);
            Assert.True(appUser.IsActive);
            Assert.True(appUser.MustChangePassword);
        }

        // Positional {0}/{1} placeholders — EF parameterizes these itself (this is the safe
        // overload EF1002 asks for), never client-built string concatenation.
        var roleExists = await superuserContext.Database
            .SqlQueryRaw<bool>("SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = {0}) AS \"Value\"", newUsername)
            .SingleAsync();
        Assert.True(roleExists);

        var isEmpleadoMember = await superuserContext.Database
            .SqlQueryRaw<bool>(
                "SELECT pg_has_role({0}, 'inmobiliaria_empleado', 'MEMBER') AS \"Value\"", newUsername)
            .SingleAsync();
        Assert.True(isEmpleadoMember);

        // The provisioning decision this file exists to prove: never Admin.
        var isAdminMember = await superuserContext.Database
            .SqlQueryRaw<bool>(
                "SELECT pg_has_role({0}, 'inmobiliaria_admin', 'MEMBER') AS \"Value\"", newUsername)
            .SingleAsync();
        Assert.False(isAdminMember);

        // --- Forced failure, proving neither half is ever orphaned. A Postgres role
        // pre-exists (created out of band here, standing in for any real-world
        // inconsistency) with no matching app_users row. CreateUser's own pre-check only
        // queries app_users, finds nothing there, and proceeds — so the failure happens one
        // level down, inside the SAME transaction, when app_create_login_role's own CREATE
        // ROLE hits a duplicate object (42710). The transaction wrapping both halves then
        // rolls back before the AppUsers insert is ever reached.
        var collidingUsername = $"collide9{Guid.NewGuid():N}"[..24];
        string preCreateRoleSql = $"CREATE ROLE {collidingUsername} LOGIN PASSWORD 'Whatever123!';";
        await superuserContext.Database.ExecuteSqlRawAsync(preCreateRoleSql);

        var exception = await Record.ExceptionAsync(
            () => provisioning.CreateUserAsync(collidingUsername, "AnotherPass123!", "Should Not Land"));
        Assert.NotNull(exception);

        var orphanedRows = await superuserContext.AppUsers
            .Where(u => u.Username == collidingUsername)
            .ToListAsync();
        Assert.Empty(orphanedRows);
    }

    /// <summary>Spec test 10: <c>ALTER ROLE ... NOLOGIN</c> on an active user fails their next login attempt.</summary>
    [SkippableFact]
    public async Task Deactivate_SetsRoleToNoLogin_AndTheNextLoginAttemptFails()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await using var adminFactory = await ProvisionAdminWithCapabilityAsync(superuserContext, "prov-admin10");
        IUserProvisioning provisioning = new PostgresUserProvisioning(adminFactory);

        // PostgreSQL restricts ALTER ROLE on an EXISTING role to that role's own creator (or a
        // superuser) — CREATEROLE plus ADMIN OPTION is not, by itself, enough for a role this
        // Admin did not create (discovered running this test; see PostgresUserProvisioning.
        // DeactivateAsync's remarks). The SAME provisioning instance must therefore create the
        // target user it will later deactivate.
        var composedTarget = SupavisorUsername.For("deact10", AccessTestSupport.TestProjectRef);
        await provisioning.CreateUserAsync(composedTarget, AccessTestSupport.DefaultPassword, "Deact Ten");

        await provisioning.DeactivateAsync(composedTarget);

        var authenticator = new NpgsqlAuthenticator(AccessTestSupport.BuildEndpoint(superuserContext));
        var result = await authenticator.AuthenticateAsync("deact10", AccessTestSupport.DefaultPassword);

        Assert.IsType<AuthenticationResult.Rejected>(result);
    }

    /// <summary>Spec test 11: a deactivated user's ContractDocument/RentAdjustment rows remain readable and unchanged.</summary>
    [SkippableFact]
    public async Task Deactivate_LeavesHistoricalContractDocumentAndRentAdjustmentRowsUnchangedAndReadable()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await using var adminFactory = await ProvisionAdminWithCapabilityAsync(superuserContext, "prov-admin11");
        IUserProvisioning provisioning = new PostgresUserProvisioning(adminFactory);

        // Same provisioning-instance-must-be-the-creator constraint as test 10 above.
        var composedTarget11 = SupavisorUsername.For("maria11", AccessTestSupport.TestProjectRef);
        var maria11Id = await provisioning.CreateUserAsync(
            composedTarget11, AccessTestSupport.DefaultPassword, "Maria Eleven");

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

        await provisioning.DeactivateAsync(composedTarget11);

        await using var readContext = _fixture.CreateDbContext();

        var reloadedDocument = await readContext.ContractDocuments.SingleAsync(d => d.Id == document.Id);
        Assert.Equal(maria11Id, reloadedDocument.UploadedByUserId);

        var reloadedAdjustment = await readContext.RentAdjustments.SingleAsync(a => a.Id == adjustment.Id);
        Assert.Equal((Guid?)maria11Id, reloadedAdjustment.ConfirmedBy);

        var reloadedAppUser = await readContext.AppUsers.SingleAsync(u => u.Id == maria11Id);
        Assert.False(reloadedAppUser.IsActive);
    }

    /// <summary>Spec test 12: provisioning a username already assigned to a deactivated user is rejected and never produces a shared identity.</summary>
    [SkippableFact]
    public async Task CreateUser_WithADeactivatedUsersUsername_IsRejected()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await using var adminFactory = await ProvisionAdminWithCapabilityAsync(superuserContext, "prov-admin12");
        IUserProvisioning provisioning = new PostgresUserProvisioning(adminFactory);

        await AccessTestSupport.ProvisionUserAsync(
            superuserContext, "maria12", UserRole.Empleado, isActive: false);
        var composedDeactivated = SupavisorUsername.For("maria12", AccessTestSupport.TestProjectRef);

        var exception = await Record.ExceptionAsync(
            () => provisioning.CreateUserAsync(composedDeactivated, "BrandNew123!", "New Maria"));
        Assert.NotNull(exception);

        // No shared identity ever resulted: exactly the one (still deactivated) row exists.
        var rows = await superuserContext.AppUsers
            .Where(u => u.Username == composedDeactivated)
            .ToListAsync();
        var onlyRow = Assert.Single(rows);
        Assert.False(onlyRow.IsActive);
    }

    /// <summary>Spec test 14: an Admin's reset on a different user takes effect only at that user's next login; her already-open session keeps working.</summary>
    [SkippableFact]
    public async Task ResetPassword_TakesEffectOnlyAtNextLogin_PriorSessionStaysAlive()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await using var adminFactory = await ProvisionAdminWithCapabilityAsync(superuserContext, "prov-admin14");
        IUserProvisioning provisioning = new PostgresUserProvisioning(adminFactory);

        // The SAME provisioning instance must create the target it will later reset —
        // PostgreSQL restricts ALTER ROLE ... PASSWORD on an existing role to its own creator
        // (see PostgresUserProvisioning.ResetPasswordAsync's remarks).
        var composedTarget = SupavisorUsername.For("reset14", AccessTestSupport.TestProjectRef);
        await provisioning.CreateUserAsync(composedTarget, AccessTestSupport.DefaultPassword, "Reset Fourteen");

        var authenticator = new NpgsqlAuthenticator(AccessTestSupport.BuildEndpoint(superuserContext));
        var login = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync("reset14", AccessTestSupport.DefaultPassword));

        await provisioning.ResetPasswordAsync(composedTarget, "BrandNewByAdmin456!");

        // The already-open session keeps working, untouched by the reset.
        await using (var context = login.Factory.Create())
        {
            var appUserCount = await context.AppUsers.CountAsync();
            Assert.True(appUserCount >= 1);
        }
        await login.Factory.DisposeAsync();

        // The old password no longer authenticates; the new one does.
        var oldPasswordResult = await authenticator.AuthenticateAsync("reset14", AccessTestSupport.DefaultPassword);
        Assert.IsType<AuthenticationResult.Rejected>(oldPasswordResult);

        var newLogin = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync("reset14", "BrandNewByAdmin456!"));
        await newLogin.Factory.DisposeAsync();
    }

    /// <summary>Spec test 15 (database half): provisioning a user sets AppUser.MustChangePassword = true.</summary>
    [SkippableFact]
    public async Task CreateUser_SetsMustChangePasswordTrue()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await using var adminFactory = await ProvisionAdminWithCapabilityAsync(superuserContext, "prov-admin15");
        IUserProvisioning provisioning = new PostgresUserProvisioning(adminFactory);

        var newUsername = $"sofia15{Guid.NewGuid():N}"[..24];
        await provisioning.CreateUserAsync(newUsername, "ProvisionalPass123!", "Sofia Quince");

        await using var readContext = _fixture.CreateDbContext();
        var appUser = await readContext.AppUsers.SingleAsync(u => u.Username == newUsername);
        Assert.True(appUser.MustChangePassword);
    }

    /// <summary>Spec test 16: after an Admin resets an active user's password, MustChangePassword is set and the next authentication requires a change first.</summary>
    [SkippableFact]
    public async Task ResetPassword_SetsMustChangePassword_AndNextAuthenticationRequiresIt()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await using var adminFactory = await ProvisionAdminWithCapabilityAsync(superuserContext, "prov-admin16");
        IUserProvisioning provisioning = new PostgresUserProvisioning(adminFactory);

        var composedTarget = SupavisorUsername.For("reset16", AccessTestSupport.TestProjectRef);
        await provisioning.CreateUserAsync(composedTarget, AccessTestSupport.DefaultPassword, "Reset Sixteen");

        await provisioning.ResetPasswordAsync(composedTarget, "BrandNewByAdmin789!");

        var authenticator = new NpgsqlAuthenticator(AccessTestSupport.BuildEndpoint(superuserContext));
        var login = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync("reset16", "BrandNewByAdmin789!"));

        Assert.True(login.Session.MustChangePassword);
        await login.Factory.DisposeAsync();
    }

    /// <summary>Spec test 17: re-submitting the just-authenticated password as the "new" one is rejected; the pending requirement remains set.</summary>
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
        Assert.IsType<InvalidOperationException>(exception);

        await login.Factory.DisposeAsync();

        // The requirement remains pending — proven by re-authenticating and reading the flag
        // back, not merely by the absence of a successful change.
        var reloadedLogin = Assert.IsType<AuthenticationResult.Success>(
            await authenticator.AuthenticateAsync("sofia17", AccessTestSupport.DefaultPassword));
        Assert.True(reloadedLogin.Session.MustChangePassword);
        await reloadedLogin.Factory.DisposeAsync();
    }

    /// <summary>Spec test 18: a voluntary change with nothing pending does not set MustChangePassword.</summary>
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

    /// <summary>Spec test 31: a ContractDocument's uploader FK still resolves and displays the uploader's name after deactivation.</summary>
    [SkippableFact]
    public async Task Deactivate_ContractDocumentUploaderReference_StillResolvesToDisplayName()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);

        await using var superuserContext = _fixture.CreateDbContext();
        await using var adminFactory = await ProvisionAdminWithCapabilityAsync(superuserContext, "prov-admin31");
        IUserProvisioning provisioning = new PostgresUserProvisioning(adminFactory);

        // The SAME provisioning instance must create the target it will later deactivate —
        // PostgreSQL restricts ALTER ROLE ... NOLOGIN on an existing role to its own creator.
        var composedTarget = SupavisorUsername.For("maria31", AccessTestSupport.TestProjectRef);
        var uploaderId = await provisioning.CreateUserAsync(
            composedTarget, AccessTestSupport.DefaultPassword, "maria31");

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

        await provisioning.DeactivateAsync(composedTarget);

        await using var readContext = _fixture.CreateDbContext();
        var reloadedDocument = await readContext.ContractDocuments.SingleAsync(d => d.Id == document.Id);
        var uploader = await readContext.AppUsers.SingleAsync(u => u.Id == reloadedDocument.UploadedByUserId);

        Assert.Equal(uploaderId, reloadedDocument.UploadedByUserId);
        Assert.Equal("maria31", uploader.DisplayName);
        Assert.False(uploader.IsActive);
    }
}
