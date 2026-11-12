using System.Data.Common;
using Inmobiliaria.Domain.Notices;
using Inmobiliaria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Inmobiliaria.Infrastructure.Tests;

/// <summary>
/// notificaciones spec Tests 8 to 11 (Requirements 4 and 5): sending, through the desk, against a real engine and a fake mail
/// port. A row is committed only once the email was accepted; the unique index is what stops a second email, even from a
/// second PC at the same moment.
/// </summary>
public sealed partial class TenantNoticeDeskTests
{
    private const string Agency = "Del Lago Servicios Inmobiliarios";

    private static readonly MailboxCredentials Mailbox = new("dellago.avisos@example.com", "abcd efgh ijkl mnop");

    private static Task<long> SentRowsForAsync(DeskWorld world, Guid contractId) =>
        world.CountAsync("SELECT count(*) FROM tenant_notices WHERE contract_id = @c", ("c", contractId));

    // ---- Happy path and the login -----------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task ASentNotice_IsRecordedWithItsFigures_AndItsEmailIsTheComposedOne()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        await SeedAsync(world, "maria.perez@example.com");
        var sender = new FakeNoticeSender();
        var notice = Assert.Single((await DeskOver(world).ReadAsync()).ToSend);

        var report = await DeskOver(world, sender).SendAsync([notice], Mailbox, Agency);

        Assert.Equal(NoticeSendStatus.Sent, Assert.Single(report.Outcomes).Status);
        Assert.Equal(1, report.SentCount);
        Assert.Equal(NoticeComposer.Compose(notice, Agency), Assert.Single(sender.Accepted));
        Assert.Equal(1, sender.Opens);

        Assert.Equal(1L, await world.CountAsync(
            """
            SELECT count(*) FROM tenant_notices
            WHERE contract_id = @c AND kind = 'amount_owed' AND notice_month = @m AND recipient = @r
              AND figures_on = @d AND total = @t AND recargo = @g
            """,
            ("c", notice.ContractId), ("m", notice.NoticeMonth), ("r", notice.Recipient), ("d", notice.FiguresOn),
            ("t", notice.Total), ("g", notice.Recargo)));

        var board = await DeskOver(world).ReadAsync();
        Assert.Empty(board.ToSend);
        Assert.Single(board.AlreadySent);
    }

    [SkippableFact]
    public async Task ARefusedLogin_FailsEveryNoticeWithItsReason_AndWritesNothing()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        await SeedAsync(world, "a@example.com", "Alvarez");
        await SeedAsync(world, "b@example.com", "Benitez");
        var sender = new FakeNoticeSender { LoginFailure = "Gmail rechazó la contraseña de aplicación." };
        var notices = (await DeskOver(world).ReadAsync()).ToSend;

        var report = await DeskOver(world, sender).SendAsync(notices, Mailbox, Agency);

        Assert.Equal(2, report.Failures.Count);
        Assert.All(report.Outcomes, o => Assert.Equal("Gmail rechazó la contraseña de aplicación.", o.Reason));
        Assert.Equal(0, report.SentCount);
        Assert.Equal(1, sender.Opens);
        Assert.Equal(0, sender.Attempts);
        Assert.Equal(0L, await SentRowsAsync(world));
        Assert.Equal(2, (await DeskOver(world).ReadAsync()).ToSend.Count);
    }

    // ---- Test 8 ---------------------------------------------------------------------------------------------------------

    /// <summary>The database's refusal of a second row reaches the operator as "already sent", and no email goes out.</summary>
    [SkippableFact]
    public async Task ANoticeAlreadyRecorded_IsReportedAsAlreadySent_AndNoEmailGoesOut()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var seeded = await SeedAsync(world, "maria.perez@example.com");
        var notice = Assert.Single((await DeskOver(world).ReadAsync()).ToSend);
        await InsertSentAsync(world, seeded.ContractId, notice.NoticeMonth);
        var sender = new FakeNoticeSender();

        var report = await DeskOver(world, sender).SendAsync([notice], Mailbox, Agency);

        var outcome = Assert.Single(report.Outcomes);
        Assert.Equal(NoticeSendStatus.AlreadySent, outcome.Status);
        Assert.Null(outcome.Reason);
        Assert.Equal(1, report.AlreadySentCount);
        Assert.Equal(0, sender.Attempts);
        Assert.Equal(1L, await SentRowsForAsync(world, seeded.ContractId));
    }

    // ---- Test 9 ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Two PCs press send for the same notice at once. The first to insert holds its email until the second is seen waiting on
    /// the unique index's lock, so the two are truly in flight together; then exactly one email and one row exist.
    /// </summary>
    [SkippableFact]
    public async Task TwoConcurrentSendsOfOneNotice_SendOneEmail_WriteOneRow_AndTheOtherReportsAlreadySent()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var seeded = await SeedAsync(world, "maria.perez@example.com");
        var notice = Assert.Single((await DeskOver(world).ReadAsync()).ToSend);

        var sender = new FakeNoticeSender
        {
            OnSend = async (_, _) =>
            {
                await WaitForALockWaiterAsync(world);
                return new MailDelivery(true, null);
            },
        };

        // Two desks over two sessions, as two PCs; built first, since the world's context list is not thread-safe.
        var first = DeskOver(world, sender);
        var second = DeskOver(world, sender);

        var reports = await Task.WhenAll(
            Task.Run(() => first.SendAsync([notice], Mailbox, Agency)),
            Task.Run(() => second.SendAsync([notice], Mailbox, Agency)));

        var statuses = reports.Select(r => Assert.Single(r.Outcomes).Status).OrderBy(s => s).ToList();
        Assert.Equal([NoticeSendStatus.Sent, NoticeSendStatus.AlreadySent], statuses);
        Assert.Equal(1, sender.Attempts);
        Assert.Single(sender.Accepted);
        Assert.Equal(1L, await SentRowsForAsync(world, seeded.ContractId));
    }

    /// <summary>Until another session of this database waits on a lock; fails the test after ten seconds.</summary>
    private static async Task WaitForALockWaiterAsync(DeskWorld world)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (await world.CountAsync(
                   "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'") == 0)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The second send never waited on the first's insert.");
            }

            await Task.Delay(50);
        }
    }

    // ---- Test 10 --------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task ANoticeSentLastMonth_DoesNotStopThisMonthsBeingSent()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var seeded = await SeedAsync(world, "maria.perez@example.com");
        var notice = Assert.Single((await DeskOver(world).ReadAsync()).ToSend);
        await InsertSentAsync(world, seeded.ContractId, notice.NoticeMonth.AddMonths(-1));
        var sender = new FakeNoticeSender();

        var report = await DeskOver(world, sender).SendAsync([notice], Mailbox, Agency);

        Assert.Equal(NoticeSendStatus.Sent, Assert.Single(report.Outcomes).Status);
        Assert.Single(sender.Accepted);
        Assert.Equal(2L, await SentRowsForAsync(world, seeded.ContractId));
    }

    // ---- Test 11 --------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task WhenTheMailPortRefusesOneOfThree_TheOtherTwoAreRecorded_AndTheRefusedOneIsListedAgain()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        await SeedAsync(world, "a@example.com", "Alvarez");
        await SeedAsync(world, "b@example.com", "Benitez");
        await SeedAsync(world, "c@example.com", "Castro");
        var notices = (await DeskOver(world).ReadAsync()).ToSend.OrderBy(n => n.Recipient, StringComparer.Ordinal).ToList();
        Assert.Equal(3, notices.Count);

        var sender = new FakeNoticeSender
        {
            OnSend = (mail, _) => Task.FromResult(mail.To == "b@example.com"
                ? new MailDelivery(false, "Gmail no aceptó el destinatario.")
                : new MailDelivery(true, null)),
        };

        var report = await DeskOver(world, sender).SendAsync(notices, Mailbox, Agency);

        Assert.Equal(
            [NoticeSendStatus.Sent, NoticeSendStatus.Failed, NoticeSendStatus.Sent],
            report.Outcomes.Select(o => o.Status).ToList());
        Assert.Equal(2, report.SentCount);
        Assert.Equal(1, sender.Opens);
        var failure = Assert.Single(report.Failures);
        Assert.Equal(notices[1], failure.Notice);
        Assert.Equal("Gmail no aceptó el destinatario.", failure.Reason);

        Assert.Equal(1L, await SentRowsForAsync(world, notices[0].ContractId));
        Assert.Equal(0L, await SentRowsForAsync(world, notices[1].ContractId));
        Assert.Equal(1L, await SentRowsForAsync(world, notices[2].ContractId));
        Assert.Equal(["a@example.com", "c@example.com"], sender.Accepted.Select(m => m.To).ToList());

        var again = Assert.Single((await DeskOver(world).ReadAsync()).ToSend);
        Assert.Equal(notices[1].ContractId, again.ContractId);
    }

    /// <summary>
    /// A press cancelled after Gmail accepted an email still records it: the email is out, so a missing row would let the next
    /// press send it again.
    /// </summary>
    [SkippableFact]
    public async Task APressCancelledAfterGmailAcceptedTheEmail_StillRecordsIt()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var seeded = await SeedAsync(world, "maria.perez@example.com");
        var notice = Assert.Single((await DeskOver(world).ReadAsync()).ToSend);
        using var press = new CancellationTokenSource();

        var sender = new FakeNoticeSender
        {
            OnSend = async (_, _) =>
            {
                await press.CancelAsync();
                return new MailDelivery(true, null);
            },
        };

        var report = await DeskOver(world, sender).SendAsync([notice], Mailbox, Agency, press.Token);

        Assert.Equal(NoticeSendStatus.Sent, Assert.Single(report.Outcomes).Status);
        Assert.Single(sender.Accepted);
        Assert.Equal(1L, await SentRowsForAsync(world, seeded.ContractId));
    }

    /// <summary>
    /// An email Gmail accepted whose row then cannot be committed is reported as failed, saying the email went out, so nobody
    /// resends it blind (gentle-ai review, R3-unrecorded-accept-untested).
    /// </summary>
    [SkippableFact]
    public async Task AnAcceptedEmailWhoseRowCannotBeCommitted_IsReportedAsFailed_SayingItWentOut()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        var seeded = await SeedAsync(world, "maria.perez@example.com");
        var notice = Assert.Single((await DeskOver(world).ReadAsync()).ToSend);
        var sender = new FakeNoticeSender();
        var desk = new TenantNoticeDesk(
            world.NewContext(new FailingCommit()), new AgencyClock(), DeskWorld.Terms, sender);

        var report = await desk.SendAsync([notice], Mailbox, Agency);

        var outcome = Assert.Single(report.Outcomes);
        Assert.Equal(NoticeSendStatus.Failed, outcome.Status);
        Assert.Equal(
            "El correo salió, pero no se pudo registrar el envío (commit refused by the test). Revise antes de volver a "
            + "enviarlo.",
            outcome.Reason);
        Assert.Single(sender.Accepted);
        Assert.Equal(0L, await SentRowsForAsync(world, seeded.ContractId));
    }

    /// <summary>A failure that quotes the app password reaches the operator without it (gentle-ai review, R1-001).</summary>
    [SkippableFact]
    public async Task AFailureQuotingTheAppPassword_ReachesTheOperatorWithoutIt()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        await SeedAsync(world, "maria.perez@example.com");
        var notice = Assert.Single((await DeskOver(world).ReadAsync()).ToSend);
        var sender = new FakeNoticeSender
        {
            OnSend = (_, _) => throw new InvalidOperationException($"session for {Mailbox.AppPassword} dropped"),
        };

        var report = await DeskOver(world, sender).SendAsync([notice], Mailbox, Agency);

        Assert.Equal("No se pudo enviar: session for *** dropped", Assert.Single(report.Outcomes).Reason);
    }

    /// <summary>Refuses every commit, as a connection lost between Gmail's acceptance and the commit would.</summary>
    private sealed class FailingCommit : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("commit refused by the test");
    }

    /// <summary>A send that throws is a failure of that notice only, with no row, and the next one still goes.</summary>
    [SkippableFact]
    public async Task ASendThatThrows_FailsThatNoticeOnly_AndLeavesNoRow()
    {
        Skip.If(!_fixture.IsDockerAvailable, _fixture.SkipReason);
        await using var world = await DeskWorld.CreateAsync(_fixture);
        await SeedAsync(world, "a@example.com", "Alvarez");
        await SeedAsync(world, "b@example.com", "Benitez");
        var notices = (await DeskOver(world).ReadAsync()).ToSend.OrderBy(n => n.Recipient, StringComparer.Ordinal).ToList();

        var sender = new FakeNoticeSender
        {
            OnSend = (mail, _) => mail.To == "a@example.com"
                ? throw new InvalidOperationException("La conexión se cortó.")
                : Task.FromResult(new MailDelivery(true, null)),
        };

        var report = await DeskOver(world, sender).SendAsync(notices, Mailbox, Agency);

        Assert.Equal([NoticeSendStatus.Failed, NoticeSendStatus.Sent], report.Outcomes.Select(o => o.Status).ToList());
        Assert.Equal("No se pudo enviar: La conexión se cortó.", report.Outcomes[0].Reason);
        Assert.Equal(0L, await SentRowsForAsync(world, notices[0].ContractId));
        Assert.Equal(1L, await SentRowsForAsync(world, notices[1].ContractId));
    }
}
