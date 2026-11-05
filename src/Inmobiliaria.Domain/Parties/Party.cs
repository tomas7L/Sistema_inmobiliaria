namespace Inmobiliaria.Domain.Parties;

/// <summary>
/// One identity record per natural person, independent of any role they hold on any
/// contract. DNI and CUIL are natural keys, both optional (a foreign tenant may have
/// neither an Argentine DNI on file yet). No role or contract data is stored here.
/// </summary>
public sealed class Party
{
    public Guid Id { get; }
    public string FirstName { get; private set; }
    public string LastName { get; private set; }
    public string? Dni { get; private set; }
    public string? Cuil { get; private set; }
    public string? Email { get; private set; }
    public string? PhoneNumber { get; private set; }
    public string? Domicile { get; private set; }

    public Party(
        Guid id,
        string firstName,
        string lastName,
        string? dni = null,
        string? cuil = null,
        string? email = null,
        string? phoneNumber = null,
        string? domicile = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);

        Id = id;
        FirstName = firstName;
        LastName = lastName;
        Dni = dni;
        Cuil = cuil;
        Email = email;
        PhoneNumber = phoneNumber;
        Domicile = domicile;
    }

    public void UpdateContactInfo(string? email, string? phoneNumber, string? domicile)
    {
        Email = email;
        PhoneNumber = phoneNumber;
        Domicile = domicile;
    }

    /// <summary>
    /// Corrects name, DNI and CUIL in place (gestion-de-alquileres spec Decision 29). The constructor keeps storing
    /// what it is given; this path stores identifiers normalized and refuses an invalid one, as the registry has
    /// already checked them by then.
    /// </summary>
    public void CorrectIdentity(string firstName, string lastName, string? dni, string? cuil)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);

        var normalizedDni = PartyIdentifiers.NormalizeDni(dni);
        if (normalizedDni is not null && !PartyIdentifiers.IsValidDni(normalizedDni))
        {
            throw new ArgumentException("Not a valid DNI.", nameof(dni));
        }

        var normalizedCuil = PartyIdentifiers.NormalizeCuil(cuil);
        if (normalizedCuil is not null && !PartyIdentifiers.IsValidCuil(normalizedCuil))
        {
            throw new ArgumentException("Not a valid CUIL.", nameof(cuil));
        }

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Dni = normalizedDni;
        Cuil = normalizedCuil;
    }
}
