namespace Inmobiliaria.Domain.Leasing;

/// <summary>
/// Metadata for one uploaded document (original lease, addendum, or termination notice)
/// belonging to a <see cref="Contract"/>. One-to-many: uploading a new document never
/// overwrites a previous one. No content is parsed, OCR'd, or generated here — only the
/// Supabase Storage pointer and metadata are stored.
/// </summary>
public sealed class ContractDocument
{
    public Guid Id { get; }
    public Guid ContractId { get; }
    public string StoragePath { get; }
    public string FileName { get; }
    public string ContentType { get; }
    public DateTimeOffset UploadedAt { get; }

    /// <summary>
    /// Relational reference to <see cref="Inmobiliaria.Domain.Access.AppUser"/> — the FK target every table
    /// points at when it needs to name a person (design Decision 8). Replaces the plain
    /// identifier string this column used to be before `Domain/Access` existed.
    /// </summary>
    public Guid UploadedByUserId { get; }

    public DocumentKind Kind { get; }

    public ContractDocument(
        Guid id,
        Guid contractId,
        string storagePath,
        string fileName,
        string contentType,
        DateTimeOffset uploadedAt,
        Guid uploadedByUserId,
        DocumentKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        if (uploadedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Uploaded-by user id must be a real user.", nameof(uploadedByUserId));
        }

        Id = id;
        ContractId = contractId;
        StoragePath = storagePath;
        FileName = fileName;
        ContentType = contentType;
        UploadedAt = uploadedAt;
        UploadedByUserId = uploadedByUserId;
        Kind = kind;
    }
}
