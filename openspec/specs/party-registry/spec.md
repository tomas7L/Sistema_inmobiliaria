# Party Registry Specification

## Purpose

A `Party` is one identity record per natural person, independent of any role (lessor/**locador**,
tenant/**locatario**, co-debtor/**codeudor**) they hold on any contract. DNI (national ID number)
and CUIL (tax ID) are natural keys. No role, contract, or commercial data is stored here.

## Requirements

### Requirement: Single Party Identity Per Person

The system MUST represent each natural person as exactly one `Party` record, regardless of how
many roles or contracts that person is associated with.

#### Scenario: Same person as owner and later tenant

- GIVEN a person already exists as a `Party` because they are a lessor on Contract A
- WHEN that same person becomes a tenant on Contract B
- THEN the system MUST reuse the existing `Party` record
- AND MUST NOT create a second `Party` for the same person

#### Scenario: Condominio does not duplicate identity

- GIVEN two owners jointly lease one unit (e.g. "VICO LESLIE" and "VICO ALEJANDRA")
- WHEN both are recorded as lessors on the same contract
- THEN each MUST exist as its own single `Party` record
- AND role assignment is external to `Party` (see `lease-contract`)

### Requirement: Natural Key Uniqueness

The system MUST NOT allow two `Party` records to share the same DNI value when both are present,
nor the same CUIL value when both are present.

#### Scenario: Duplicate DNI rejected

- GIVEN a `Party` already exists with a given DNI
- WHEN a new `Party` is created with that same DNI
- THEN the system MUST reject the creation

#### Scenario: Duplicate CUIL rejected

- GIVEN a `Party` already exists with a given CUIL
- WHEN a new `Party` is created with that same CUIL
- THEN the system MUST reject the creation

Note (not resolved here): whether DNI or CUIL may be absent — e.g. a foreign tenant without an
Argentine DNI — is unsettled and flagged as a risk, not decided by this requirement.

### Requirement: Role-Agnostic Storage

`Party` MUST NOT store role-specific or contract-specific data (role, contract reference,
honorarios rate, canon). All such data belongs to `ContractParty` or `Contract` (see
`lease-contract`).

#### Scenario: Querying identity returns no role data

- GIVEN a `Party` record
- WHEN it is read independent of any contract
- THEN it MUST expose only person-identity fields (name, DNI, CUIL, contact, domicile)
- AND MUST NOT expose any role or contract attribute
