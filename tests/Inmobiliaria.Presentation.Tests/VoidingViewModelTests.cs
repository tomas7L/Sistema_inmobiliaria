using Inmobiliaria.Domain.Access;
using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Collection;
using Inmobiliaria.Presentation.Common;
using Inmobiliaria.Presentation.Tests.Fakes;
using Inmobiliaria.Presentation.Voiding;
using static Inmobiliaria.Presentation.Tests.CashDeskTestKit;

namespace Inmobiliaria.Presentation.Tests;

/// <summary>ui-de-cobranza spec tests 19, 25 and 26, Requirement 16, design section 8, with the ports faked.</summary>
public class VoidingViewModelTests
{
    private sealed class Kit
    {
        public FakeCollectionDesk Desk { get; } = new();
        public FakeReciboReader Reader { get; } = new();
        public FakeClock Clock { get; } = new(Today);
        public Guid CollectionId { get; } = Guid.NewGuid();
        public IssuedPayment Payment { get; private set; }

        public Kit(int daysOld = 0)
        {
            Payment = Issued(CollectionId, 31001) with { RecordedOn = Today.AddDays(-daysOld) };
            Reader.Payments[CollectionId] = Payment;
            Desk.OnPreviewVoid = (_, reason, role) => Task.FromResult(PreviewFor(reason, role));
            Desk.OnVoid = _ =>
            {
                Reader.Payments[CollectionId] = Payment with
                {
                    Annulment = new AnnulmentRecord("Cobrado por error", "Nicolás", new DateTimeOffset(2026, 1, 15, 11, 0, 0, TimeSpan.FromHours(-3))),
                };
                return Task.FromResult(VoidingResult.Done());
            };
        }

        public decimal ResultingOwed { get; set; } = 598_260m;

        public VoidingPreview PreviewFor(string reason, UserRole role) =>
            new(Period, [new ReciboAnnulment(31001, reason), new ReciboAnnulment(31002, reason)],
                [AccountMovement.Correction(Guid.NewGuid(), 500_000m, Today, Guid.NewGuid(), Period),
                 AccountMovement.Correction(Guid.NewGuid(), -98_260m, Today, Guid.NewGuid(), Period)],
                ResultingOwed, null, role);

        public VoidingViewModel Build(UserRole role = UserRole.Admin) => new(Desk, Reader, Clock, role);
    }

    private static async Task<VoidingViewModel> FoundAsync(Kit kit, UserRole role = UserRole.Admin, string number = "31002")
    {
        var voiding = kit.Build(role);
        voiding.NumberText = number;
        await voiding.FindCommand.ExecuteAsync(null);
        return voiding;
    }

    private static async Task<VoidingViewModel> PreviewedAsync(Kit kit, UserRole role = UserRole.Admin)
    {
        var voiding = await FoundAsync(kit, role);
        voiding.ReasonText = "Cobrado por error";
        await voiding.PreviewCommand.ExecuteAsync(null);
        return voiding;
    }

    /// <summary>Spec Requirement 16: either number finds the pair, and the recent list opens the same one.</summary>
    [Theory]
    [InlineData("31001")]
    [InlineData("31002")]
    public async Task EitherNumber_FindsThePair_AndOffersTheVoiding(string number)
    {
        var kit = new Kit();

        var voiding = await FoundAsync(kit, number: number);

        Assert.Equal(["N° 31001", "N° 31002"], voiding.Recibo!.Copies.Select(c => c.NumberText));
        Assert.True(voiding.Gate!.Offered);

        var fromList = kit.Build();
        await fromList.OpenAsync(kit.CollectionId);
        Assert.Equal(voiding.Recibo.Copies.Select(c => c.ReciboId), fromList.Recibo!.Copies.Select(c => c.ReciboId));
    }

    [Theory]
    [InlineData("99999", "No hay ningún recibo con el número 99999.")]
    [InlineData("abc", "Escriba el número de uno de los dos recibos.")]
    public async Task ANumberThatFindsNothing_SaysSo(string number, string message)
    {
        var voiding = await FoundAsync(new Kit(), number: number);

        Assert.Null(voiding.Recibo);
        Assert.Equal(message, voiding.FindMessage);
        Assert.False(voiding.PreviewCommand.CanExecute(null));
    }

    /// <summary>Spec Requirement 16: a blank reason is refused in the domain's text and the port is not called.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankReason_IsRefused_WithoutCallingThePort(string reason)
    {
        var kit = new Kit();
        var voiding = await FoundAsync(kit);
        voiding.ReasonText = reason;

        await voiding.PreviewCommand.ExecuteAsync(null);

        Assert.Empty(kit.Desk.VoidPreviews);
        Assert.Equal(VoidingRefusal.ReasonRequired.OperatorText(), voiding.RefusalMessage);
    }

    /// <summary>Spec test 25: the preview shows the corrections, the resulting amount owed and both recibos to be annulled.</summary>
    [Fact]
    public async Task ThePreview_ShowsWhatWillBeReversed()
    {
        var kit = new Kit();

        var voiding = await PreviewedAsync(kit, UserRole.Empleado);

        Assert.Equal((kit.Payment.Locatario.ReciboId, "Cobrado por error", UserRole.Empleado), kit.Desk.VoidPreviews.Single());
        Assert.Equal(["Corrección: 500.000", "Corrección: -98.260"], voiding.Corrections);
        Assert.Equal("598.260", voiding.ResultingAmountOwedText);
        Assert.Equal("Se anularán los recibos N° 31001 y N° 31002.", voiding.AnnulmentsText);
        Assert.True(voiding.ConfirmCommand.CanExecute(null));
    }

    /// <summary>Changing the reason withdraws the preview: the confirmation is for the reason shown.</summary>
    [Fact]
    public async Task ChangingTheReason_WithdrawsThePreview()
    {
        var voiding = await PreviewedAsync(new Kit());

        voiding.ReasonText = "Otro motivo";

        Assert.Null(voiding.VoidPreview);
        Assert.False(voiding.ConfirmCommand.CanExecute(null));
    }

    /// <summary>Spec test 25: at day 8 an Empleado is not offered the voiding and is told why; an Admin is.</summary>
    [Fact]
    public async Task AtDay8_AnEmpleadoIsPreGated_AndAnAdminIsNot()
    {
        var kit = new Kit(daysOld: 8);

        var empleado = await FoundAsync(kit, UserRole.Empleado);
        var admin = await FoundAsync(kit, UserRole.Admin);

        Assert.False(empleado.Gate!.Offered);
        Assert.Equal(VoidingRefusal.AdminOnly.OperatorText(), empleado.Gate.Explanation);
        Assert.False(empleado.PreviewCommand.CanExecute(null));
        Assert.True(admin.PreviewCommand.CanExecute(null));
    }

    /// <summary>
    /// Spec test 19: a stale voiding previews once more and needs a new confirmation; the voiding is never
    /// repeated on its own.
    /// </summary>
    [Fact]
    public async Task AStaleVoiding_PreviewsOnceMore_AndNeedsANewConfirmation()
    {
        var kit = new Kit();
        kit.Desk.OnVoid = _ =>
        {
            kit.ResultingOwed = 610_000m;
            return Task.FromResult(VoidingResult.Refused(VoidingRefusal.PreviewStale));
        };
        var voiding = await PreviewedAsync(kit);

        await voiding.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(1, kit.Desk.VoidCalls);
        Assert.Equal(2, kit.Desk.VoidPreviews.Count);
        Assert.Equal(VoidingRefusal.PreviewStale.OperatorText(), voiding.StaleMessage);
        Assert.Equal("610.000", voiding.ResultingAmountOwedText);
        Assert.True(voiding.ConfirmCommand.CanExecute(null));
        Assert.Null(voiding.DoneMessage);
    }

    /// <summary>Spec test 26: a refusal the pre-gate did not foresee is shown in its operator text.</summary>
    [Fact]
    public async Task ARefusalTheGateAllowed_IsShownInItsOwnText()
    {
        var kit = new Kit(daysOld: 7);
        kit.Desk.OnVoid = _ => Task.FromResult(VoidingResult.Refused(VoidingRefusal.AdminOnly));
        var voiding = await PreviewedAsync(kit, UserRole.Empleado);

        await voiding.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(VoidingRefusal.AdminOnly.OperatorText(), voiding.RefusalMessage);
        Assert.Null(voiding.VoidPreview);
        Assert.False(voiding.ConfirmCommand.CanExecute(null));
    }

    /// <summary>Spec Requirement 16: after a voiding the payment is read again and shown annulled; nothing prints.</summary>
    [Fact]
    public async Task AfterAVoiding_ThePairIsReadAgain_AndShownAnnulled()
    {
        var kit = new Kit();
        var voiding = await PreviewedAsync(kit);

        await voiding.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal("Cobro anulado.", voiding.DoneMessage);
        Assert.True(voiding.Recibo!.IsAnnulled);
        Assert.False(voiding.Gate!.Offered);
        Assert.Null(voiding.VoidPreview);
    }

    /// <summary>A voiding that raises says it may or may not have been applied, in its own words.</summary>
    [Fact]
    public async Task AFailedVoiding_SaysItMayOrMayNotHaveBeenApplied()
    {
        var kit = new Kit();
        kit.Desk.OnVoid = _ => throw new TimeoutException("Host=db");
        var voiding = await PreviewedAsync(kit);

        await voiding.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(ErrorMessages.VoidFailed, voiding.Runner.ErrorMessage);
        Assert.Contains("anulación", ErrorMessages.VoidFailed);
        Assert.False(voiding.Runner.IsBusy);
    }

    /// <summary>A double click sends one voiding.</summary>
    [Fact]
    public async Task ADoubleClick_SendsOneVoiding()
    {
        var kit = new Kit();
        var gate = new TaskCompletionSource<VoidingResult>();
        kit.Desk.OnVoid = _ => gate.Task;
        var voiding = await PreviewedAsync(kit);

        var first = voiding.ConfirmCommand.ExecuteAsync(null);
        Assert.False(voiding.ConfirmCommand.CanExecute(null));
        var second = voiding.ConfirmCommand.ExecuteAsync(null);
        gate.SetResult(VoidingResult.Refused(VoidingRefusal.AlreadyAnnulled));
        await Task.WhenAll(first, second);

        Assert.Equal(1, kit.Desk.VoidCalls);
    }

    /// <summary>
    /// Task 6.0a (a), S4a review R4-void-reread-failure-generic-retry: when the pair cannot be read back after
    /// a voiding, the screen withdraws it and says the voiding was recorded, never a generic "try again".
    /// </summary>
    [Fact]
    public async Task APairThatCannotBeReadBack_AfterAVoiding_IsWithdrawn_AndTheVoidingIsSaidDone()
    {
        var kit = new Kit();
        kit.Desk.OnVoid = _ =>
        {
            kit.Reader.Throws = true;
            return Task.FromResult(VoidingResult.Done());
        };
        var voiding = await PreviewedAsync(kit);

        await voiding.ConfirmCommand.ExecuteAsync(null);

        Assert.Null(voiding.Recibo);
        Assert.Null(voiding.Gate);
        Assert.Equal(VoidingTexts.DoneButNotShown, voiding.DoneMessage);
        Assert.Null(voiding.Runner.ErrorMessage);
        Assert.False(voiding.PreviewCommand.CanExecute(null));
    }

    /// <summary>
    /// S4b review R3-void-reread-notfound-untested: when the read-back after a voiding finds nothing, the screen
    /// withdraws the pair and says the voiding was recorded, as it does when the read throws.
    /// </summary>
    [Fact]
    public async Task APairThatIsNotFoundOnReadBack_AfterAVoiding_IsWithdrawn_AndTheVoidingIsSaidDone()
    {
        var kit = new Kit();
        kit.Desk.OnVoid = _ =>
        {
            kit.Reader.Payments.Remove(kit.CollectionId);
            return Task.FromResult(VoidingResult.Done());
        };
        var voiding = await PreviewedAsync(kit);

        await voiding.ConfirmCommand.ExecuteAsync(null);

        Assert.Null(voiding.Recibo);
        Assert.Null(voiding.Gate);
        Assert.Equal(VoidingTexts.DoneButNotShown, voiding.DoneMessage);
        Assert.False(voiding.PreviewCommand.CanExecute(null));
    }

    /// <summary>
    /// Task 6.0a (b), S4a review R4-open-during-inflight-preview-mismatched-target: a preview of pair A still in
    /// flight when pair B is opened never becomes confirmable under B.
    /// </summary>
    [Fact]
    public async Task APreviewOfAnotherPair_StillInFlight_NeverLandsOnTheOpenedOne()
    {
        var kit = new Kit();
        var otherId = Guid.NewGuid();
        kit.Reader.Payments[otherId] = Issued(otherId, 31101);
        var slow = new TaskCompletionSource<VoidingPreview>();
        kit.Desk.OnPreviewVoid = (_, _, _) => slow.Task;
        var voiding = await FoundAsync(kit);
        voiding.ReasonText = "Cobrado por error";

        var previewing = voiding.PreviewCommand.ExecuteAsync(null);
        await voiding.OpenAsync(otherId);
        slow.SetResult(kit.PreviewFor("Cobrado por error", UserRole.Admin));
        await previewing;

        Assert.Equal("N° 31101", voiding.Recibo!.Copies[0].NumberText);
        Assert.Null(voiding.VoidPreview);
        Assert.False(voiding.ConfirmCommand.CanExecute(null));
    }

    /// <summary>
    /// Task 6.0a (c), S4a review R3-failed-void-reoffers-preview: a voiding that raises withdraws the pair, so
    /// nothing is offered until the recibo is looked up again, as its text says.
    /// </summary>
    [Fact]
    public async Task AFailedVoiding_WithdrawsThePair()
    {
        var kit = new Kit();
        kit.Desk.OnVoid = _ => throw new TimeoutException("Host=db");
        var voiding = await PreviewedAsync(kit);

        await voiding.ConfirmCommand.ExecuteAsync(null);

        Assert.Null(voiding.Recibo);
        Assert.False(voiding.PreviewCommand.CanExecute(null));
        Assert.Equal(ErrorMessages.VoidFailed, voiding.Runner.ErrorMessage);
    }

    /// <summary>Task 6.0a (d), S4a review R3-open-from-list-keeps-done-message: opening another pair clears the old messages.</summary>
    [Fact]
    public async Task OpeningAnotherPair_AfterAVoiding_ClearsTheDoneMessage()
    {
        var kit = new Kit();
        var otherId = Guid.NewGuid();
        kit.Reader.Payments[otherId] = Issued(otherId, 31101);
        var voiding = await PreviewedAsync(kit);
        await voiding.ConfirmCommand.ExecuteAsync(null);
        Assert.NotNull(voiding.DoneMessage);

        await voiding.OpenAsync(otherId);

        Assert.Null(voiding.DoneMessage);
        Assert.Null(voiding.FindMessage);
        Assert.True(voiding.Gate!.Offered);
    }
}
