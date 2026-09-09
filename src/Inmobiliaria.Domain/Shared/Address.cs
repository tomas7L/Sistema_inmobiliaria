namespace Inmobiliaria.Domain.Shared;

/// <summary>
/// Physical address of a <see cref="Domain.Units.Unit"/>. Owned by its unit — it has no
/// identity of its own and is never queried independently.
/// </summary>
public sealed class Address
{
    public string Street { get; }
    public string Number { get; }
    public string? Floor { get; }
    public string? Apartment { get; }
    public string City { get; }
    public string Province { get; }
    public string PostalCode { get; }

    public Address(
        string street,
        string number,
        string city,
        string province,
        string postalCode,
        string? floor = null,
        string? apartment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(street);
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        ArgumentException.ThrowIfNullOrWhiteSpace(province);
        ArgumentException.ThrowIfNullOrWhiteSpace(postalCode);

        Street = street;
        Number = number;
        City = city;
        Province = province;
        PostalCode = postalCode;
        Floor = floor;
        Apartment = apartment;
    }
}
