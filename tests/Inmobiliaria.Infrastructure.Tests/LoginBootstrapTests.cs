using Inmobiliaria.Domain.Access;
using Inmobiliaria.Infrastructure.Access;
using Inmobiliaria.Infrastructure.Persistence;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// Spec test 15's construction-order half (users-and-roles task 6.7): with
/// <c>MustChangePassword = true</c>, the bootstrap path cannot reach
/// <see cref="BootstrapStage.ReadyForMainWindow"/> until the forced change succeeds, and a
/// cancelled forced change lands on <see cref="BootstrapStage.Aborted"/>, never on
/// <see cref="BootstrapStage.ReadyForMainWindow"/>. Asserted over <see cref="LoginBootstrap"/>'s
/// pure state machine — no WPF window is involved anywhere in this file, deliberately:
/// <c>Inmobiliaria.Desktop</c> is excluded from <c>Inmobiliaria.Core.slnf</c> and a test placed
/// there cannot gate the Linux <c>core</c> job (same rule task 4.25's guardrail already applies
/// to every other test in this project).
/// </summary>
public sealed class LoginBootstrapTests
{
    private static AuthenticationResult.Success SuccessWith(bool mustChangePassword)
    {
        var user = new AppUser(
            Guid.NewGuid(), "sofia", "Sofía Test", isActive: true, mustChangePassword: mustChangePassword);
        var session = new UserSession(user, UserRole.Empleado);
        return new AuthenticationResult.Success(session, new NeverOpenedSessionFactory());
    }

    [Fact]
    public void MustChangePasswordPending_CannotReachMainWindow_UntilTheChangeSucceeds()
    {
        var result = SuccessWith(mustChangePassword: true);

        var stage = LoginBootstrap.AfterAuthentication(result);

        Assert.Equal(BootstrapStage.AwaitingForcedPasswordChange, stage);
        Assert.NotEqual(BootstrapStage.ReadyForMainWindow, stage);

        var afterSuccess = LoginBootstrap.AfterForcedPasswordChange(succeeded: true);

        Assert.Equal(BootstrapStage.ReadyForMainWindow, afterSuccess);
    }

    [Fact]
    public void CancellingAForcedChange_AbortsInsteadOfFallingThroughToMainWindow()
    {
        var result = SuccessWith(mustChangePassword: true);
        Assert.Equal(BootstrapStage.AwaitingForcedPasswordChange, LoginBootstrap.AfterAuthentication(result));

        var afterCancel = LoginBootstrap.AfterForcedPasswordChange(succeeded: false);

        Assert.Equal(BootstrapStage.Aborted, afterCancel);
        Assert.NotEqual(BootstrapStage.ReadyForMainWindow, afterCancel);
    }

    [Fact]
    public void NothingPending_GoesStraightToMainWindow_WithNoForcedChangeStage()
    {
        var result = SuccessWith(mustChangePassword: false);

        var stage = LoginBootstrap.AfterAuthentication(result);

        Assert.Equal(BootstrapStage.ReadyForMainWindow, stage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectedOrNotProvisioned_StaysAtAwaitingLogin(bool notProvisioned)
    {
        AuthenticationResult result = notProvisioned
            ? new AuthenticationResult.NotProvisioned("no app_users row")
            : new AuthenticationResult.Rejected();

        var stage = LoginBootstrap.AfterAuthentication(result);

        Assert.Equal(BootstrapStage.AwaitingLogin, stage);
    }

    /// <summary>Test-only stand-in. Never exercised — this test never asks it to open anything.</summary>
    private sealed class NeverOpenedSessionFactory : ISessionDbContextFactory
    {
        public InmobiliariaDbContext Create() =>
            throw new NotSupportedException("Not exercised by LoginBootstrapTests.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
