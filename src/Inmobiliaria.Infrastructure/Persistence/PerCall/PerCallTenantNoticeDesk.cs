using Inmobiliaria.Domain.Accounts;
using Inmobiliaria.Domain.Notices;
using Inmobiliaria.Infrastructure.Access;

namespace Inmobiliaria.Infrastructure.Persistence.PerCall;

/// <summary>
/// <see cref="ITenantNoticeDesk"/> for the screens: every call builds a <see cref="TenantNoticeDesk"/> over its own context
/// (design D2). Registered only with valid recargo terms, as the desk and Mora are (notificaciones design §3.5).
/// </summary>
internal sealed class PerCallTenantNoticeDesk : ITenantNoticeDesk
{
    private readonly ISessionDbContextFactory _factory;
    private readonly IAgencyClock _clock;
    private readonly RecargoTerms _terms;
    private readonly ITenantNoticeSender _sender;

    public PerCallTenantNoticeDesk(
        ISessionDbContextFactory factory, IAgencyClock clock, RecargoTerms terms, ITenantNoticeSender sender)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(sender);

        _factory = factory;
        _clock = clock;
        _terms = terms;
        _sender = sender;
    }

    public Task<NoticeBoard> ReadAsync(CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => Desk(context).ReadAsync(ct));

    public Task<NoticeSendReport> SendAsync(
        IReadOnlyList<TenantNotice> notices, MailboxCredentials credentials, string agencyName, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => Desk(context).SendAsync(notices, credentials, agencyName, ct));

    private TenantNoticeDesk Desk(InmobiliariaDbContext context) => new(context, _clock, _terms, _sender);
}
