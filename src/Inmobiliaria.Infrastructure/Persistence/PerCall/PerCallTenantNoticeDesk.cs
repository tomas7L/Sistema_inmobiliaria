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

    public PerCallTenantNoticeDesk(ISessionDbContextFactory factory, IAgencyClock clock, RecargoTerms terms)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(terms);

        _factory = factory;
        _clock = clock;
        _terms = terms;
    }

    public Task<NoticeBoard> ReadAsync(CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new TenantNoticeDesk(context, _clock, _terms).ReadAsync(ct));

    public Task<NoticeSendReport> SendAsync(
        IReadOnlyList<TenantNotice> notices, MailboxCredentials credentials, CancellationToken ct = default) =>
        PerCall.RunAsync(_factory, context => new TenantNoticeDesk(context, _clock, _terms).SendAsync(notices, credentials, ct));
}
