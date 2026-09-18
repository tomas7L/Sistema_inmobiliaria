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
    /// Plain identifier string — no user entity exists yet, so this is not a
    /// relational reference (see contract-documents: Uploader as Plain Identifier).
    /// </summary>
    public string UploadedBy { get; }

    public DocumentKind Kind { get; }

    public ContractDocument(
        Guid id,
        Guid contractId,
        string storagePath,
        string fileName,
        string contentType,
        DateTimeOffset uploadedAt,
        string uploadedBy,
        DocumentKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadedBy);

        Id = id;
        ContractId = contractId;
        StoragePath = storagePath;
        FileName = fileName;
        ContentType = contentType;
        UploadedAt = uploadedAt;
        UploadedBy = uploadedBy;
        Kind = kind;
    }
}
