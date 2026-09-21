# Economic Index Specification

## Purpose

`EconomicIndex` is the catalog of named indices (IPC, RIPTE, ICL, ...) a contract's adjustment clause can reference, and `IndexValue` is a published value for that index in a given period. This capability stores index data; it does not compute any contract's coefficient.

Domain vocabulary (Argentine terms): **IPC** = Índice de Precios al Consumidor, INDEC's consumer price index (a dimensionless index number, currently in the thousands); **RIPTE** = Remuneración Imponible Promedio de los Trabajadores Estables, the average taxable wage published by the Ministry of Labour (an amount in pesos, currently over a million); **ICL** = Índice para Contratos de Locación, the BCRA lease index.

## Requirements

### Requirement: Index Identity

The system MUST identify each `EconomicIndex` by a stable name (e.g. "IPC", "RIPTE", "ICL") and MUST reject creating two active indices with the same name.

#### Scenario: IPC and RIPTE coexist as distinct indices

- GIVEN no index named "IPC" exists yet
- WHEN "IPC" and "RIPTE" are each registered as an `EconomicIndex`
- THEN both MUST be stored as distinct, independently referenceable indices

### Requirement: Published Value Stored Per Period

Each `IndexValue` MUST record the `EconomicIndex` it belongs to, the period it was published for, and the published level. The system MUST reject a second value for the same index and period.

#### Scenario: One value per index per period

- GIVEN IPC already has a published value for period 2026-06
- WHEN a second value is entered for IPC, period 2026-06
- THEN the system MUST reject it as a duplicate for that index and period

#### Scenario: Same period, different indices

- GIVEN IPC has a published value for period 2026-06
- WHEN RIPTE is given a published value for the same period 2026-06
- THEN both MUST be accepted as independent rows

### Requirement: Manual Entry Is How the Table Is Filled

Index values MUST be entered manually by an operator. Automatic retrieval from any publishing source MUST NOT be required for this capability to function; automatic retrieval is a later, separate change and, if added, MUST fill the same `IndexValue` table rather than a parallel one.

#### Scenario: Operator enters a published value

- GIVEN IPC's value for period 2026-08 was just published by INDEC
- WHEN the operator types that level and period into the system
- THEN the value MUST be stored and immediately available to any clause referencing IPC

### Requirement: Discontinuation Names a Successor

An `EconomicIndex` MUST support being marked discontinued from a given period, and MUST optionally name a successor `EconomicIndex` of similar characteristics. Discontinuing an index MUST NOT delete or alter its previously published values.

#### Scenario: An index is discontinued with a successor

- GIVEN RIPTE has published values through period 2027-03
- WHEN RIPTE is marked discontinued from period 2027-04 with a named successor index
- THEN RIPTE's existing published values MUST remain stored and readable unchanged
- AND RIPTE MUST be flagged discontinued from 2027-04 onward

#### Scenario: Discontinued index without a successor yet

- GIVEN an index is marked discontinued
- WHEN no successor has been named
- THEN the discontinuation MUST still be accepted, leaving successor resolution to fail explicitly rather than silently, for any period after the discontinuation

### Requirement: Successor Resolution for Later Periods

Resolving an index for a period at or after its discontinuation date MUST follow its successor chain until it finds an index that is active for that period, or MUST report that no value can be resolved if the chain ends without one.

#### Scenario: Resolution follows one successor hop

- GIVEN Index A is discontinued from 2027-01 naming Index B as successor
- AND Index B is active and has a published value for period 2027-02
- WHEN a clause referencing Index A is resolved for period 2027-02
- THEN the system MUST resolve it through Index B's published value

#### Scenario: Resolution before discontinuation uses the original index

- GIVEN Index A is discontinued from 2027-01 naming Index B as successor
- WHEN a clause referencing Index A is resolved for period 2026-11, before the discontinuation date
- THEN the system MUST use Index A's own published value for that period, not Index B's

## Tests

1. **Index identity unique within active indices.** Two active indices cannot share a name; a discontin

ued index can be succeeded by a new index with its former name.
2. **Published value per period enforced.** Duplicate (index, period) rejected.
3. **Successor resolution single hop.** An index discontinued from a period resolves to its successor for later periods.
4. **Successor resolution does not apply before discontinuation.** Before the discontinuation date, the original index is used.
5. **Manual entry is the sole storage method.** Automatic retrieval is not part of this capability; if added later, it fills the same table.
