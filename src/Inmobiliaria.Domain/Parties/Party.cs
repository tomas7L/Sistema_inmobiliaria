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
}
