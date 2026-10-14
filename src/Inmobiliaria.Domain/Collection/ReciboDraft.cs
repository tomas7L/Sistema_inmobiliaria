namespace Inmobiliaria.Domain.Collection;

/// <summary>
/// The lines of one copy of a recibo before it has a number. Validated on construction so a draft
/// that exists is a draft that could be printed: every line belongs on its copy, kinds appear once in
/// print order, positions run 1..n, and no amount has a sign its kind forbids. Value equality compares
/// the lines in order, which the collection preview relies on (design D21).
/// </summary>
public sealed class ReciboDraft : IEquatable<ReciboDraft>
{
    public ReciboCopy Copy { get; }

    public IReadOnlyList<ReciboLine> Lines { get; }

    /// <summary>The sum of the signed lines. This is what the paper says; it is not the ledger figure.</summary>
    public decimal Total { get; }

    public ReciboDraft(ReciboCopy copy, IReadOnlyList<ReciboLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var copied = lines.ToArray();
        Validate(copy, copied);

        Copy = copy;
        Lines = Array.AsReadOnly(copied);
        Total = copied.Sum(l => l.Amount);
    }

    private static void Validate(ReciboCopy copy, ReciboLine[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (!ReciboLineCatalog.AppearsOn(line.Kind, copy))
            {
                throw new ArgumentException($"A {line.Kind} line does not appear on the {copy} recibo.", nameof(lines));
            }

            if (line.Position != i + 1)
            {
                throw new ArgumentException("Line positions must run 1..n in order.", nameof(lines));
            }

            // Strictly ascending kinds: print order, and a kind never twice.
            if (i > 0 && line.Kind <= lines[i - 1].Kind)
            {
                throw new ArgumentException("Lines must be in print order, one per kind.", nameof(lines));
            }

            if (line.Amount != decimal.Truncate(line.Amount))
            {
                throw new ArgumentException($"A {line.Kind} amount must be whole pesos.", nameof(lines));
            }

            if (line.Amount < 0 && !ReciboLineCatalog.MayBeNegative(line.Kind))
            {
                throw new ArgumentException($"A {line.Kind} line may not be negative.", nameof(lines));
            }

            if (line.Kind == ReciboLineKind.HonorariosAdministracion && line.Amount > 0)
            {
                throw new ArgumentException("The honorarios line is never positive.", nameof(lines));
            }
        }
    }

    public bool Equals(ReciboDraft? other) =>
        other is not null && Copy == other.Copy && Lines.SequenceEqual(other.Lines);

    public override bool Equals(object? obj) => Equals(obj as ReciboDraft);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Copy);

        foreach (var line in Lines)
        {
            hash.Add(line);
        }

        return hash.ToHashCode();
    }
}

/// <summary>The two drafts of one payment, the tenant's and the lessor's.</summary>
public sealed record ReciboDraftPair
{
    public ReciboDraft Locatario { get; }

    public ReciboDraft Propietario { get; }

    public ReciboDraftPair(ReciboDraft locatario, ReciboDraft propietario)
    {
        ArgumentNullException.ThrowIfNull(locatario);
        ArgumentNullException.ThrowIfNull(propietario);

        Locatario = locatario;
        Propietario = propietario;
    }
}
