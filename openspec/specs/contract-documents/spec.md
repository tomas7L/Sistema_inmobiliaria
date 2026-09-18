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
filename, content type, uploaded-at timestamp, an uploader identifier, and a `DocumentKind`
(Original, Addendum, or TerminationNotice).

#### Scenario: Upload records full metadata

- GIVEN an original signed lease file is uploaded for a contract
- WHEN the `ContractDocument` is created
- THEN it MUST record the storage pointer, filename, content type, uploaded-at, uploader
  identifier, and `DocumentKind = Original`

### Requirement: Uploader as Plain Identifier

Because no user entity exists yet, `UploadedBy` MUST be stored as a plain identifier string, not
as a foreign key to a user record.

#### Scenario: Upload records a string uploader

- GIVEN a document is uploaded by an agency staff member
- WHEN the `ContractDocument` is created
- THEN `UploadedBy` MUST be stored as a string, not a relational reference

### Requirement: No Content Processing

The system MUST NOT parse, OCR, or generate PDF content from an uploaded document. It stores the
pointer and metadata only.

#### Scenario: Uploading a PDF stores pointer only

- GIVEN a signed lease PDF is uploaded
- WHEN the `ContractDocument` is created
- THEN only the storage pointer and metadata MUST be persisted
- AND no extracted text or generated content MUST be stored
