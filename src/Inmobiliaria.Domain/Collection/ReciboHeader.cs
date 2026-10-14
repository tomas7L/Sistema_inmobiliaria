namespace Inmobiliaria.Domain.Collection;

/// <summary>
/// The text a recibo prints above its lines: tenant, lessors and units, taken as a snapshot when the
/// recibo is issued (design D12). It holds plain text on purpose, so a later rename of a party never
/// rewrites a document that was already handed over. Several lessors are normal. Value equality
/// compares the lessors in order.
/// </summary>
public sealed class ReciboHeader : IEquatable<ReciboHeader>
{
    public string Tenant { get; }

    public IReadOnlyList<string> Lessors { get; }

    public string Units { get; }

    public ReciboHeader(string tenant, IReadOnlyList<string> lessors, string units)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        ArgumentNullException.ThrowIfNull(lessors);
        ArgumentException.ThrowIfNullOrWhiteSpace(units);

        var copied = lessors.ToArray();

        if (copied.Length == 0 || copied.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A recibo names at least one lessor, and no name is blank.", nameof(lessors));
        }

        Tenant = tenant;
        Lessors = Array.AsReadOnly(copied);
        Units = units;
    }

    public bool Equals(ReciboHeader? other) =>
        other is not null &&
        Tenant == other.Tenant &&
        Units == other.Units &&
        Lessors.SequenceEqual(other.Lessors);

    public override bool Equals(object? obj) => Equals(obj as ReciboHeader);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Tenant);
        hash.Add(Units);

        foreach (var lessor in Lessors)
        {
            hash.Add(lessor);
        }

        return hash.ToHashCode();
    }
}
