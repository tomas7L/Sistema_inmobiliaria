using Inmobiliaria.Domain.Collection;
using Microsoft.EntityFrameworkCore;

namespace Inmobiliaria.Infrastructure.Persistence;

/// <summary>
/// The only implementation of <see cref="IReciboNumbering"/>. It touches <c>starting_number</c> and nothing
/// else: the database's trigger copies it into the live counter, refuses it once a recibo exists, and the
/// column-level grant lets only an Admin write it. None of those refusals is caught here; the operator who
/// asks for a go-live number should be told when the database says no.
///
/// No Npgsql type is needed: it is one parameterised statement through the context.
/// </summary>
public sealed class ReciboNumbering : IReciboNumbering
{
    private readonly InmobiliariaDbContext _context;

    public ReciboNumbering(InmobiliariaDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task SetStartingNumberAsync(int startingNumber, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(startingNumber, 1);

        var affected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE recibo_numbering SET starting_number = {startingNumber} WHERE id = 1", ct);

        // The counter is a singleton row the migration seeds. Zero rows means it is gone, and a starting
        // number nobody stored must not look like one that was.
        if (affected != 1)
        {
            throw new InvalidOperationException(
                $"The starting number was not stored: the singleton row of recibo_numbering is missing ({affected} rows updated).");
        }
    }
}
