using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Presentation.Common;
using Inmobiliaria.Presentation.Shell;
using Inmobiliaria.Presentation.Tests.Fakes;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>
/// Spec tests 5 and 6 (design D24). The runner is the one place a screen operation's failure is
/// caught; a refusal is a value and never gets there.
/// </summary>
public sealed class OperationRunnerTests
{
    private const string SecretExceptionText = "Host=db.internal;Username=nicolas;Password=hunter2 timed out";

    private static readonly CollectionRequest AnyRequest = new(Guid.NewGuid(), null, null, 0m);

    /// <summary>What a screen keeps on display while an operation fails (the operator's typed values).</summary>
    private sealed class TypedValues
    {
        public string HandedAmount { get; set; } = "";
    }

    private static FakeCollectionDesk DeskThatThrows() => new()
    {
        OnPreview = _ => throw new InvalidOperationException(SecretExceptionText),
        OnCommit = (_, _) => throw new InvalidOperationException(SecretExceptionText),
    };

    [Fact]
    public async Task AnException_BecomesTheGenericMessage_WithoutTheExceptionText()
    {
        var runner = new OperationRunner();
        var desk = DeskThatThrows();

        var outcome = await runner.RunAsync(() => desk.PreviewAsync(AnyRequest));

        Assert.False(outcome.Completed);
        Assert.Equal(ErrorMessages.Generic, outcome.ErrorMessage);
        Assert.Equal(ErrorMessages.Generic, runner.ErrorMessage);
        Assert.DoesNotContain("hunter2", runner.ErrorMessage);
        Assert.DoesNotContain("Host=", runner.ErrorMessage);
        Assert.DoesNotContain("timed out", runner.ErrorMessage);
    }

    [Fact]
    public async Task AnException_LeavesTheTypedValuesInPlace_AndDoesNotThrow()
    {
        var runner = new OperationRunner();
        var desk = DeskThatThrows();
        var typed = new TypedValues { HandedAmount = "47860" };

        var outcome = await runner.RunAsync(() => desk.PreviewAsync(AnyRequest));

        Assert.False(outcome.Completed);
        Assert.Equal("47860", typed.HandedAmount);
    }

    [Fact]
    public async Task IsBusy_IsTrueWhileRunning_AndResetsAfterSuccessAndAfterFailure()
    {
        var runner = new OperationRunner();
        var gate = new TaskCompletionSource<int>();

        var running = runner.RunAsync(() => gate.Task);
        Assert.True(runner.IsBusy);
        gate.SetResult(1);
        await running;
        Assert.False(runner.IsBusy);

        await runner.RunAsync<int>(() => throw new InvalidOperationException(SecretExceptionText));
        Assert.False(runner.IsBusy);
    }

    [Fact]
    public async Task ACommandRunsAgainAfterAFailure_AndTheOldMessageIsCleared()
    {
        var runner = new OperationRunner();

        await runner.RunAsync<int>(() => throw new InvalidOperationException(SecretExceptionText));
        Assert.NotNull(runner.ErrorMessage);

        var outcome = await runner.RunAsync(() => Task.FromResult(7));

        Assert.True(outcome.Completed);
        Assert.Equal(7, outcome.Value);
        Assert.Null(runner.ErrorMessage);
    }

    [Fact]
    public async Task AFailedCommit_SaysItMayOrMayNotHaveBeenRecorded_AndPointsToTheRecentCollections()
    {
        var runner = new OperationRunner();
        var desk = DeskThatThrows();
        var preview = new CollectionPreview(null, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1),
            false, 0m, 0m, 0m, null, null, [], null);

        var outcome = await runner.RunAsync(() => desk.CommitAsync(preview, AnyRequest), OperationKind.Commit);

        Assert.Equal(ErrorMessages.CommitFailed, outcome.ErrorMessage);
        Assert.Contains(ErrorMessages.Generic, outcome.ErrorMessage);
        Assert.Contains("puede que el cobro se haya registrado o no", outcome.ErrorMessage);
        Assert.Contains(NavigationTitles.RecentCollections, outcome.ErrorMessage);
        Assert.DoesNotContain("hunter2", outcome.ErrorMessage);
    }

    [Fact]
    public async Task AFailedRead_DoesNotClaimThatAPaymentMayHaveBeenRecorded()
    {
        var runner = new OperationRunner();
        var desk = DeskThatThrows();

        await runner.RunAsync(() => desk.PreviewAsync(AnyRequest));

        Assert.DoesNotContain("registrado", runner.ErrorMessage);
    }

    [Fact]
    public async Task ARefusalReturnedAsAValue_IsNotAnError_AndKeepsItsOwnOperatorText()
    {
        var runner = new OperationRunner();
        var desk = new FakeCollectionDesk
        {
            OnCommit = (_, _) => Task.FromResult(CollectionResult.Refused(CollectionRefusal.RateNeverEntered)),
        };
        var preview = new CollectionPreview(null, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1),
            false, 0m, 0m, 0m, null, null, [], null);

        var outcome = await runner.RunAsync(() => desk.CommitAsync(preview, AnyRequest), OperationKind.Commit);

        Assert.True(outcome.Completed);
        Assert.Null(outcome.ErrorMessage);
        Assert.Null(runner.ErrorMessage);
        var refusal = Assert.IsType<CollectionRefusal>(outcome.Value!.Refusal);
        Assert.Equal(CollectionRefusal.RateNeverEntered, refusal);
        Assert.NotEqual(ErrorMessages.Generic, refusal.OperatorText());
        Assert.NotEqual(ErrorMessages.CommitFailed, refusal.OperatorText());
    }

    [Fact]
    public async Task ACancelledOperation_IsNeitherAnErrorNorACompletion()
    {
        var runner = new OperationRunner();

        var outcome = await runner.RunAsync<int>(() => throw new OperationCanceledException());

        Assert.False(outcome.Completed);
        Assert.True(outcome.IsCancelled);
        Assert.Null(outcome.ErrorMessage);
        Assert.Null(runner.ErrorMessage);
        Assert.False(runner.IsBusy);
    }
}
