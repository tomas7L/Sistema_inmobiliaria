# Contract Documents Specification

## Purpose

`ContractDocument` records the uploaded signed lease and any later addenda or termination
notices for a `Contract`, one-to-many, so no prior document is ever overwritten. The system
never generates these documents — it stores an uploaded file's pointer and metadata.

## Requirements

### Requirement: One-to-Many Document Storage

A `Contract` MUST support multiple `ContractDocument` records. Uploading a new document MUST NOT
overwrite or remove a previously stored document pointer.

#### Scenario: Original lease and later addendum coexist

- GIVEN a `Contract` has an uploaded original signed lease
- WHEN a later addendum is uploaded for the same contract
- THEN both documents MUST remain stored and retrievable as separate `ContractDocument` rows

### Requirement: Document Metadata

Each `ContractDocument` MUST store a storage pointer (Supabase Storage path/key), original
filename, content type, uploaded-at timestamp, a reference to the `AppUser` who uploaded it, and a
`DocumentKind` (Original, Addendum, or TerminationNotice).

(Previously: recorded an "uploader identifier" as a plain string, not a relational reference.)

#### Scenario: Upload records full metadata with a real uploader reference

- GIVEN an original signed lease file is uploaded for a contract by an authenticated user "maria"
- WHEN the `ContractDocument` is created
- THEN it MUST record the storage pointer, filename, content type, uploaded-at, a foreign-key
  reference to "maria"'s `AppUser` row, and `DocumentKind = Original`

#### Scenario: The uploader reference survives the uploader's later deactivation

- GIVEN a `ContractDocument` was uploaded by "maria"
- WHEN "maria" is later deactivated
- THEN the `ContractDocument` MUST still resolve its uploader reference to "maria"'s `AppUser` row
  and MUST still display her name

### Requirement: No Content Processing

The system MUST NOT parse, OCR, or generate PDF content from an uploaded document. It stores the
pointer and metadata only.

#### Scenario: Uploading a PDF stores pointer only

- GIVEN a signed lease PDF is uploaded
- WHEN the `ContractDocument` is created
- THEN only the storage pointer and metadata MUST be persisted
- AND no extracted text or generated content MUST be stored

## Tests

Derived from the requirements above, not generic CRUD coverage. Each names the requirement it proves. Integration tests require a real Postgres (`postgres:17.6` via Testcontainers); constraints cannot be validated against a fake.

30. **`UploadedBy` is a real foreign key.** `ContractDocument.UploadedBy` resolves to an `AppUser` row; the "plain identifier" requirement is absent from the living specification.
31. **A `ContractDocument`'s uploader reference survives the uploader's deactivation.** The FK still resolves and displays the uploader's name after they are deactivated.
