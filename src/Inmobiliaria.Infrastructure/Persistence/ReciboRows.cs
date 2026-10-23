using Inmobiliaria.Domain.Collection;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// The recibo-side property bags (tasks.md "Issues found" 9) and the domain types they hold, both ways. Moved
/// out of <see cref="CollectionDesk"/> unchanged (ui-de-cobranza design D20) so the desk and
/// <c>ReciboReader</c> rebuild a recibo from its rows the same way: what is displayed is what was issued.
/// Only these two files know what a bag is.
/// </summary>
internal static class ReciboRows
{
    /// <summary>
    /// Several tenants, lessors or units on one recibo are one text column each. A name containing this
    /// separator would be split in two when a voiding rebuilds the header, which only matters to that
    /// in-memory header, never to the document already issued.
    /// </summary>
    public const string NameSeparator = "; ";

    private static readonly TimeZoneInfo AgencyZone = AgencyClock.ResolveZone();

    /// <summary>An instant the database stamped, in the agency's zone.</summary>
    public static DateTimeOffset InAgencyZone(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, AgencyZone);

    /// <summary>The agency's date of an instant the database stamped: when a payment was recorded.</summary>
    public static DateOnly AgencyDateOf(DateTimeOffset instant) => DateOnly.FromDateTime(InAgencyZone(instant).DateTime);

    public static T Read<T>(Dictionary<string, object> bag, string key) => (T)bag[key];

    public static Dictionary<string, object> CollectionBag(
        Guid id, Guid accountId, DateOnly period, int attempt, DateOnly receivedOn, Guid paymentId, Guid? recargoId) =>
        new()
        {
            ["Id"] = id,
            ["AccountId"] = accountId,
            ["Period"] = period,
            ["Attempt"] = checked((short)attempt),
            ["ReceivedOn"] = receivedOn,
            ["PaymentMovementId"] = paymentId,
            ["RecargoMovementId"] = recargoId!,
        };

    public static Dictionary<string, object> ReciboBag(Recibo recibo, Guid id, Guid collectionId, Guid contractId) =>
        new()
        {
            ["Id"] = id,
            ["CollectionId"] = collectionId,
            ["Number"] = checked((int)recibo.Number),
            ["Copy"] = recibo.Copy.ToString(),
            ["ContractId"] = contractId,
            ["Period"] = recibo.Period,
            ["ReceivedOn"] = recibo.ReceivedOn,
            ["TenantName"] = recibo.Header.Tenant,
            ["LessorNames"] = string.Join(NameSeparator, recibo.Header.Lessors),
            ["UnitsDescription"] = recibo.Header.Units,
            ["Legend"] = recibo.Legend,
            ["Total"] = recibo.Total,
        };

    public static Dictionary<string, object> LineBag(ReciboLine line, Guid reciboId) =>
        new()
        {
            ["Id"] = Guid.NewGuid(),
            ["ReciboId"] = reciboId,
            ["Position"] = checked((short)line.Position),
            ["Kind"] = line.Kind.ToString(),
            ["Amount"] = line.Amount,
        };

    public static Dictionary<string, object> AnnulmentBag(Guid reciboId, string reason) =>
        new()
        {
            ["Id"] = Guid.NewGuid(),
            ["ReciboId"] = reciboId,
            ["Reason"] = reason,
        };

    public static CollectionRecord CollectionFrom(Dictionary<string, object> bag, bool isAnnulled) =>
        new(
            Read<Guid>(bag, "Id"),
            Read<Guid>(bag, "AccountId"),
            Read<DateOnly>(bag, "Period"),
            Read<DateOnly>(bag, "ReceivedOn"),
            Read<Guid>(bag, "PaymentMovementId"),
            bag.TryGetValue("RecargoMovementId", out var recargo) && recargo is Guid recargoId ? recargoId : null,
            isAnnulled);

    public static Recibo ReciboFrom(Dictionary<string, object> row, IEnumerable<Dictionary<string, object>> lines)
    {
        var copy = Enum.Parse<ReciboCopy>(Read<string>(row, "Copy"));
        var header = new ReciboHeader(
            Read<string>(row, "TenantName"),
            Read<string>(row, "LessorNames").Split(NameSeparator),
            Read<string>(row, "UnitsDescription"));
        var draft = new ReciboDraft(
            copy,
            lines
                .OrderBy(l => Read<short>(l, "Position"))
                .Select(l => new ReciboLine(
                    Enum.Parse<ReciboLineKind>(Read<string>(l, "Kind")), Read<short>(l, "Position"), Read<decimal>(l, "Amount")))
                .ToList());

        return new Recibo(Read<int>(row, "Number"), Read<DateOnly>(row, "Period"), Read<DateOnly>(row, "ReceivedOn"), header, draft);
    }
}
