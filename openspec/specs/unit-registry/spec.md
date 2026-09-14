# Unit Registry Specification

## Purpose

A `Unit` is the leasable physical asset — a property or a **cochera** (parking space) — classified
by `UnitType`. It holds address and physical identity only. Nothing tenancy-related (tenant,
price, dates, availability) is stored on it.

## Requirements

### Requirement: Unit Is Physical Asset Only

The system MUST store on `Unit` only address, physical identity, and `UnitType`. It MUST NOT
store any tenancy-derived field (current tenant, contract dates, price/canon, or an availability
flag).

#### Scenario: Unit record has no availability column

- GIVEN a `Unit` record
- WHEN its schema/fields are inspected
- THEN no stored availability field MUST exist
- AND no tenant or contract reference MUST exist on the `Unit` itself

### Requirement: UnitType Classification

The system MUST classify every `Unit` by a `UnitType` of either Property or Parking space
(**cochera**), without requiring separate top-level entities per type.

#### Scenario: Create a property unit

- GIVEN a new leasable asset is a house or apartment
- WHEN it is registered
- THEN it MUST be stored as a `Unit` with `UnitType = Property`

#### Scenario: Create a cochera unit

- GIVEN a new leasable asset is a parking space
- WHEN it is registered
- THEN it MUST be stored as a `Unit` with `UnitType = ParkingSpace`

### Requirement: Availability Is Derived, Never Stored

A `Unit` MUST be considered available when, and only when, no `Contract` in `Active` or
`PendingTermination` state covers it. Availability MUST NOT be persisted as a column on `Unit`
or anywhere else.

#### Scenario: Unit becomes available when its contract ends

- GIVEN a `Unit` is covered by a `Contract` in `Active` state
- WHEN that `Contract` transitions to `Ended`
- THEN the `Unit` MUST be computed as available
- AND no field on the `Unit` row MUST be written as part of that transition

#### Scenario: Unit under an active contract is not available

- GIVEN a `Unit` is covered by a `Contract` in `Active` or `PendingTermination` state
- WHEN its availability is computed
- THEN the `Unit` MUST be reported as not available

#### Scenario: Unit with prior ended contract is available

- GIVEN a `Unit` previously had a `Contract` that is now `Ended`
- AND no other `Contract` in `Active` or `PendingTermination` state covers it
- WHEN its availability is computed
- THEN the `Unit` MUST be reported as available
