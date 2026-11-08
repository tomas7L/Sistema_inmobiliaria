# Tasks: gestion-de-alquileres (the missing rental screens)

Turns `design.md` §11 (twelve slices, S1 to S12) into ordered, checkable work. It does not re-plan the change and does
not edit `spec.md` or `design.md`. Where slicing or applying exposed a gap, it is listed under **Issues found**.

Vocabulary: **Admin** and **Empleado** are the two roles; the **canon** is a contract's total monthly rent; a **late
adjustment** applies later than the month it fell due, and the **difference** is what the old canon undercharged in between.

Slice to phase map (one slice = one phase = one PR, S12 splitting per screen): S1 = Phase 1, and so on to S12 = Phase 12.

## What to read (Tomás)

| Read | Skip |
|---|---|
| This header, the **Review Workload Forecast**, the **Human Follow-Ups**, **Issues found** and **Deviations** | Everything inside the phases except the **Verification** table at the end of each. The checkboxes are for the assistant and for the second inspection at the start of each session |

## Review Workload Forecast

| Field | Value |
|---|---|
| Estimated changed lines | About 5,000 authored (additions + deletions, tests included), design §11 |
| Review budget | About 400 authored lines per PR, a planning heuristic and not a cap |
| 400-line budget risk | High: S3, S5, S9, S10 are estimated above it, S12 at about 950, S1 came in at about 1,620, S2 at about 1,410 and S3 at about 4,250, 3,100 of them tests (see Deviations) |
| Chained PRs recommended | Yes |
| Suggested split | One PR per slice; S12 splits per screen |
| Delivery strategy | auto-chain |
| Chain strategy | stacked-to-main, as in `ui-de-cobranza` and `gestion-de-usuarios`: each slice PR targets `develop` once the previous one has merged |

Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: stacked-to-main
400-line budget risk: High

**Order** (design §11). Each slice is merged before the next starts. S1, S2 and S6 are independent of each other. **S7 needs
S6.** S3, S4, S5 chain. S8, S9, S10, S11 chain. **S8 needs S4**, since Test 81 ends a contract and creates another. **S10 needs
S5**, which S10 extends so that cancelling also reverses the new kind. S12 waits for Mateo's mockups.

**Independent verifier.** Every slice whose Risk names an item ends with one: S3, S4, S5, S7, S8, S9, S10, S11. S1, S2, S6 and
S12 name none.

**Every spec test 1 to 92 is mapped to one phase below**, Test 92's correction half closing in S11 and Test 71 being proven per
slice and audited at S12.

| Phase | Slice | Spec tests | Requirements | Est. | Migration | Risk |
|---|---|---|---|---|---|---|
| 1 | S1 Personas | 1-7 | R1-R3 | ~350 | None | None |
| 2 | S2 Propiedades | 8, 9 | R4, R5 | ~250 | None | None |
| 3 | S3 Crear contratos | 10-22, 68, 69 | R6-R8, R21 | ~500 | M1 | Item 2 |
| 4 | S4 Corregir, avisar, terminar | 23-29, 80 | R9, R10 | ~400 | M2 | Item 1 |
| 5 | S5 Cancelar y deshacer | 72-79, 89, 90 | R23, R24 | ~450 | M3 | Item 1 |
| 6 | S6 Índices | 30-34 | R11, R12 | ~300 | M4 | None |
| 7 | S7 Traer índices | 82-88 | R25 | ~250 | None | Item 6 |
| 8 | S8 Cláusula y calendario | 35-38, 40, 56, 81 | R13, R17 | ~350 | M5 | Item 1 |
| 9 | S9 Confirmar ajustes | 39, 41-48 | R14, R15 | ~450 | M6 | Item 1 |
| 10 | S10 La diferencia | 49-55, 61-67, 92 | R16, R19, R20 | ~450 | M7 | Items 1, 3 |
| 11 | S11 Correcciones | 57-60, 91 | R18 | ~300 | None | Item 1 |
| 12 | S12 Pantallas | 70, 71 | R22 | ~950 | None | None |

---

## Phase 1: Personas (S1, PR 1, est. about 350 lines)

**Docker: yes. Supabase: no. Model change: no.** Spec tests: 1 to 7. Requirements 1 to 3, Decisions 28, 29. Design anchors: D18,
D19, D20, §2.1, §4, §6. Needs: nothing.

- [x] 1.1 **[Second inspection, read-only]** Anchors match: `Party.cs:11-14,42`, `PartyConfiguration.cs:16-24`, `SessionServices.cs:39-45`, `PerCallDueAdjustmentQuery.cs:21-22`. Baseline 1013 passed (Domain 433, Presentation 244, Infrastructure 336), 0 failed, 0 skipped
- [x] 1.2 **[RED then GREEN, Tests 3, 5, 6]** `PartyIdentifiers` and `PartyDraft`, `PartyDraftRules` in `src/Inmobiliaria.Domain/Parties/`, tests in `tests/Inmobiliaria.Domain.Tests/PartyIdentifiersTests.cs`. Deviation: see below
- [x] 1.3 **[RED then GREEN, Requirement 3]** `Party.CorrectIdentity` beside `UpdateContactInfo`; `tests/Inmobiliaria.Domain.Tests/PartyTests.cs`, with one test that the constructor and `UpdateContactInfo` behave as before
- [x] 1.4 **[Domain]** `IPartyRegistry`, `PartyListing`, `PartyResult`, `PartyRefusal` and its one `OperatorText`. Deviation: see below
- [x] 1.5 **[RED then GREEN, Tests 1 to 7]** `PartyRegistry` and `PartyRegistryErrorMap` in `src/Inmobiliaria.Infrastructure/Persistence/`, `tests/Inmobiliaria.Infrastructure.Tests/PartyRegistryTests.cs`, against `postgres:17.6`, as real Admin and Empleado logins
- [x] 1.6 **[RED then GREEN, wiring]** `PerCallPartyRegistry`, registered in `SessionServices`; `SessionServicesTests` resolves it for both roles without a connection, one context per call
- [x] 1.7 **[RED then GREEN, Requirements 1 to 3 on the screen]** `PartiesViewModel`, `PartyRow`, `PendingPartyCorrection`, `PartyChange` in `src/Inmobiliaria.Presentation/Parties/`, `FakePartyRegistry`, `tests/Inmobiliaria.Presentation.Tests/PartiesViewModelTests.cs`. No view: S12
- [x] 1.8 **[Prove the guards by making them fail]** Recorded below
- [x] 1.9 **[Differential]** Existing callers of `Party` and `SessionServices` behave as before. Recorded below
- [x] 1.10 **[Guardrails]** Build, suite, `has-pending-model-changes`. Recorded below
- [x] 1.11 **[Handover]** File list, commit Summary and Description, PR title and description to Tomás. No commit by the assistant. Merged as PR #91

### Verification, PR 1 (Docker: yes)

| Command | Expected |
|---|---|
| `dotnet build Inmobiliaria.sln --configuration Release` | exit code 0; **Advertencia(s)** and **Errores** both 0. MSB3027 means the app is open |
| `dotnet test Inmobiliaria.Core.slnf --configuration Release` (Docker up) | exit code 0; 0 failed; 0 skipped; passed = baseline + new tests |
| `dotnet ef migrations has-pending-model-changes -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure` | exit code 0, no pending changes |

---

## Phase 2: Propiedades (S2, PR 2, est. about 250 lines)

**Docker: yes. Supabase: no. Model change: no.** Spec tests: 8, 9. Requirements 4, 5. Design anchors: §2.2, §4, §6. Needs: nothing.

- [x] 2.1 **[Second inspection, read-only]** PR 1 merged intact; `Unit.cs:38`, `PropertyUnit`, `ParkingUnit` as the design cites
- [x] 2.2 **[Domain]** `IUnitRegistry`, `UnitListing` (with covering contract's tenant and move-out date), typed refusals with their `OperatorText`
- [x] 2.3 **[RED then GREEN, Test 8]** `UnitRegistry.ListAsync`: unavailable when a `contract_units` row joins a contract in `Active` or `PendingTermination`, one query, as a real role
- [x] 2.4 **[RED then GREEN, Test 9]** `CreateAsync`, `RelocateAsync`; identical-address warning compared as folded text in memory
- [x] 2.5 **[RED then GREEN, wiring]** `PerCallUnitRegistry`, `SessionServicesTests`
- [x] 2.6 **[RED then GREEN]** `UnitsViewModel`, fake port, tests in Presentation.Tests. No view: S12
- [x] 2.7 **[Guardrails and handover]** as 1.8, 1.10 and 1.11

### Verification, PR 2 (Docker: yes)

Same three commands as PR 1.

---

## Phase 3: Crear contratos (S3, PR 3, est. about 500 lines)

**Docker: yes. Supabase: M1 to the test database, then production, with the preflight query first (H.3, H.4). Model change: yes, M1 in
this PR.** Spec tests: 10 to 22, 68, 69. Requirements 6, 7, 8, 21. Design anchors: D17, D18, D19, §2.3, §4, §10 F2, §12 M1. **Risk: item 2 (who may
set a cutover).** Needs: nothing in this change; starts the S3-S4-S5 chain.

- [x] 3.1 **[Second inspection, read-only]** `Contract.cs:119-169,227-247`, `ContractPartyConfiguration.cs:21`, `20261023131755_HardenCutoverDateToAdmin.cs:18-28`, `CutoverTriggerTests.cs:162` as the design cites
- [x] 3.2 **[RED then GREEN]** `ContractDraft`, `ContractDraftRules.Check`, `CheckCutoverBounds` extracted from `CheckCutoverDate`; start-on-the-1st rule in the rules only (spec F9), Domain.Tests
- [x] 3.3 **[Model and M1]** `ContractParty.RemovedAt`, query filter (D13), `AddContractPartyRemovalAndGuards`: `removed_at`, `ux_contract_parties_one_tenant`, `contracts_cutover_insert_guard`
- [x] 3.4 **[RED then GREEN, Tests 68, 69; Finding F2]** Rewrite `CutoverTriggerTests.AnEmpleadoInsertsAContractWithADate_AndCannotChangeIt` to expect `contracts_cutover_insert_admin_only`; move its "cannot change it" half to a contract inserted as superuser. As real login roles
- [x] 3.5 **[RED then GREEN, Tests 10 to 22]** `IContractRegistry` create, list, detail: one `SaveChangesAsync` in a transaction, availability checked inside it, rate suggestion from the lessor's latest `StartDate` with a rate
- [x] 3.6 **[RED then GREEN, wiring]** `PerCallContractRegistry`, `SessionServicesTests`
- [x] 3.7 **[RED then GREEN]** `ContractFormViewModel`, the cutover section offered only to an Admin, the database still deciding. No view: S12
- [x] 3.8 **[Prove the guards by making them fail]** Tenant index, cutover INSERT trigger
- [x] 3.9 **[Guardrails and handover]** as 1.10 and 1.11, `has-pending-model-changes` proving the model matches M1
- [x] 3.10 **[Independent verifier]** Item 2. Reads the whole spec, runs probes in a fresh scratch copy

### Verification, PR 3 (Docker: yes)

Same three commands as PR 1, then the independent verifier.

---

## Phase 4: Corregir, avisar, terminar (S4, PR 4, est. about 400 lines)

**Docker: yes. Supabase: M2 (H.3). Model change: yes, M2 in this PR.** Spec tests: 23 to 29, 80. Requirements 9, 10. Design anchors: D12, D13, D14, D16, §2.3, §4, §10 F1, §12
M2. **Risk: item 1 (changes stored contract terms).** Needs: S3.

- [x] 4.1 **[Second inspection, read-only]** PR 3 merged intact; `Contract.cs:26-27,49,184,363,414-445`, `ContractConfiguration.cs:51-52,61-65`, `20261014204601_AddFeePlanCutoverAndLedgerConstraints.cs:130-151`
- [x] 4.2 **[RED then GREEN]** `Contract.Correct`, `SetHonorariosPercentage`, `RemoveParty`, `AssignParty` restoring a removed row; `StartDate`, `NominalEndDate`, `DueDay` to `private set`; `ContractLifecycleRules`. `has-pending-model-changes` proves the setters change no model
- [x] 4.3 **[M2]** `AddContractEventsAndTermsLock`: `contract_events` with stamp, append-only and explicit `GRANT SELECT, INSERT`; `contracts_terms_lock` raising `contracts_terms_locked`
- [x] 4.4 **[RED then GREEN, Tests 23 to 29]** Port and adapter: correct (raw `UPDATE`s for units and shares, Finding F1), rate, notice, end, typed refusals, as real roles
- [x] 4.5 **[RED then GREEN, Test 80]** A codeudor removed leaves a trace; neither role holds DELETE; the query filter is pinned
- [x] 4.6 **[RED then GREEN]** `ContractDetailViewModel`: detail, notice, end, each opening a summary. No view: S12
- [x] 4.7 **[Prove the guards by making them fail]** Terms lock, `contract_events` append-only
- [x] 4.8 **[Guardrails and handover]** as 1.10 and 1.11
- [x] 4.9 **[Independent verifier]** Item 1

### Verification, PR 4 (Docker: yes)

Same three commands as PR 1, then the independent verifier.

---

## Phase 5: Cancelar y deshacer (S5, PR 5, est. about 450 lines)

**Docker: yes. Supabase: M3 (H.3). Model change: yes, M3 in this PR.** Spec tests: 72 to 79, 89, 90. Requirements 23, 24. Design anchors: D9, D10, D11, D12, §2.3 to §2.5, §12 M3.
**Risk: item 1 (erases debt, irreversibly).** Needs: S4. **From M3 on, every PC at the agency updates together** (design §12).

- [x] 5.1 **[Second inspection, read-only]** PR 4 merged intact; `VoidingPlanner.cs:128-149`, `AccrualSchedule.cs:31,46`, `AccountDirectory.cs:38-41,68-84`, `GoLiveSetup.cs:48-50`, `AccountMaterialiser.cs:150`
- [x] 5.2 **[Model and M3]** `EndReason.LoadedByMistake`, `ck_contracts_end_reason` widened; `End` newly throws on it
- [x] 5.3 **[RED then GREEN]** `CollectionRules.VoidingWindowOf` extracted, `VoidingPlanner` calling it with its tests unchanged (D11)
- [x] 5.4 **[RED then GREEN]** `AccrualSchedule.IsCancelled`; Test 73 must fail if the end date alone governs accrual
- [x] 5.5 **[RED then GREEN, Tests 72, 75, 76, 77, 90]** `ContractCancellation.Plan`, adapter: account lock, reload, plan again, compare; corrections, contract update and event in one transaction, as each role
- [x] 5.6 **[RED then GREEN, Test 74]** Picker (`AccountDirectory`) and go-live (`GoLiveSetup`) exclude a cancelled contract; worklist, availability, mora, recibos as design §2.5
- [x] 5.7 **[RED then GREEN, Tests 78, 79]** `Contract.UndoNotice`, `UndoEnd`, `Cancel`, refusals
- [x] 5.8 **[RED then GREEN, Test 89]** "Cargado por error" not offered when ending normally; view-model
- [x] 5.9 **[Prove the guards by making them fail]** Accrual guard, voiding block
- [x] 5.10 **[Guardrails and handover]** as 1.10 and 1.11
- [x] 5.11 **[Independent verifier]** Item 1

### Verification, PR 5 (Docker: yes)

Same three commands as PR 1, then the independent verifier.

---

## Phase 6: Índices (S6, PR 6, est. about 300 lines)

**Docker: yes. Supabase: M4 and M4b (H.3). Model change: yes, M4b; M4 is data only. Both ship in this PR.** Spec tests: 30 to 34, 94, 95. Requirements 11, 12. Design anchors: D23,
D26, §4, §6, §12 M4 and M4b. Needs: nothing.

- [x] 6.1 **[Second inspection, read-only]** Latest merge intact; `EconomicIndexConfiguration.cs:34-37`, `IndexValue.cs:32`, `RentAdjustmentIndexValue.cs:15-20`
- [x] 6.2 **[M4]** `SeedEconomicIndices`, raw `INSERT ... WHERE NOT EXISTS`, `Down` removing nothing
- [x] 6.3 **[RED then GREEN, Test 30]** Exactly one IPC, RIPTE and ICL on an empty database and on one already holding "ICL"
- [x] 6.4 **[RED then GREEN, Tests 32, 33]** `IndexEntryRules` in Domain: future month, non-positive level, more than six decimals, plausibility warning (D23)
- [x] 6.5 **[RED then GREEN, Tests 32 to 34]** `IIndexRegistry` (a duplicate refused, a far level storing nothing until confirmed, a typed value clearing the missing-index entry), adapter, per-call wrapper: list, add, correct, affected adjustments
- [x] 6.5b **[M4b, RED then GREEN, Tests 94, 95]** `AddIndexValueCorrections`: table, stamp, append-only, trigger; the port reads the records. Added on 2026-11-06, spec Decision 42
- [x] 6.6 **[RED then GREEN, Test 31]** `IndexLevelParser` (strict `1.915.878,76`, invariant), view-model. No view: S12
- [x] 6.7 **[Guardrails and handover]** as 1.8, 1.10 and 1.11

### Verification, PR 6 (Docker: yes)

Same three commands as PR 1.

---

## Phase 7: Traer índices (S7, PR 7, est. about 250 lines)

**Docker: yes. Supabase: no. Model change: no. Network: one manual query first, and one press of each button at the end.** Spec tests: 82 to 88. Requirement 25. Design anchors: D21,
D22, §5. **Risk: item 6 (no test sees the live API).** Needs: **S6**, because the button stores through `IIndexRegistry`.

- [x] 7.1 **[First step, manual]** One query of the exact IPC combination, untested so far (design §5): `start_date` with `limit` and `format=json`
- [x] 7.2 **[Domain]** `IIndexValueSource`, `IndexFetch` (`Found`, `NotPublishedYet`, `Unreachable`, `SourceFailed`), `Supports`
- [x] 7.3 **[RED then GREEN, Tests 82, 84, 85, 86]** `IndexSources/*`, ICL and IPC, with a fake `HttpMessageHandler` replaying design §5's shapes, malformed ones included. Nothing touches the network
- [x] 7.4 **[RED then GREEN]** `IndexSourceSettings`, `appsettings.json` section `IndexSources`, 10-second timeout; the credential guard stays unaffected
- [x] 7.5 **[RED then GREEN, Tests 83, 87, 88]** Button logic in the view-model: same checks and table as typed, a differing stored level shown beside the source's, RIPTE offering no button, nothing requested unless pressed
- [x] 7.6 **[Guardrails and handover]** as 1.8, 1.10 and 1.11
- [x] 7.7 **[Independent verifier]** Item 6, plus H.6, one manual press of each button against the live source. **The press from a screen moved to 12.3b: there is no view until S12**

### Verification, PR 7 (Docker: yes)

Same three commands as PR 1, then the independent verifier.

---

## Phase 8: Cláusula y calendario (S8, PR 8, est. about 350 lines)

**Docker: yes. Supabase: M5 (H.3). Model change: yes, M5 in this PR.** Spec tests: 35 to 38, 40, 56, 81. Requirements 13, 17. Design anchors: D3, D15, §3.5, §12 M5. **Risk: item 1 (decides when
rent moves).** Needs: **S4**, since Test 81 ends a contract and creates another.

- [x] 8.1 **[Second inspection, read-only]** `AdjustmentSchedule.cs:12-24`, `DueAdjustmentQuery.cs:31,44-49,71,86-88`, `AdjustmentWarning.cs:14-32`, `20260924202549_AddUsersAndRoles.cs:159`
- [x] 8.2 **[Model and M5]** `AddClauseWindowOffset`: `window_offset_months`, default 1, check 0 to 3; `app_replace_clause_indices`; clause lock
- [x] 8.3 **[RED then GREEN, Tests 35 to 38]** Window `end = due − K`, `base = end − N`; existing clauses read as today; a clause cannot change after a confirmed adjustment, through the function, as each role
- [x] 8.4 **[RED then GREEN, Test 56]** `AdjustmentSchedule.NextDueDate` gains the cutover; existing calls unchanged
- [x] 8.5 **[RED then GREEN, Test 40]** `DueAdjustmentQuery`: `Active` or `PendingTermination`, `IndexValues`, the desk's warning following it (replaces `ui-de-cobranza/design.md:578`)
- [x] 8.6 **[RED then GREEN, Test 81]** An agreed new deal goes through
- [x] 8.7 **[Prove the guards by making them fail]** Clause lock, the function's refusal
- [x] 8.8 **[Guardrails and handover]** as 1.10 and 1.11
- [x] 8.9 **[Independent verifier]** Item 1

### Verification, PR 8 (Docker: yes)

Same three commands as PR 1, then the independent verifier.

---

## Phase 9: Confirmar ajustes (S9, PR 9, est. about 450 lines)

**Docker: yes. Supabase: M6, with the preflight query first (H.3, H.4). Model change: yes, M6 in this PR.** Spec tests: 39, 41 to 48. Requirements 14, 15. Design anchors: D1, D2, D8, D18, §3.1, §3.2, §3.4, §12 M6.
**Risk: item 1 (what is charged).** Needs: S8.

- [ ] 9.1 **[Second inspection, read-only]** `AccrualSchedule.cs:198-226`, `RentAdjustment.cs:80-112`, `Contract.cs:315-330`, `ContractAccount.cs:449-452`
- [ ] 9.2 **[Model and M6]** `AddAdjustmentAppliesFrom`: `applies_from`, first-of-month check, both unique indexes
- [ ] 9.3 **[RED then GREEN, Tests 45, 46, 48]** `CanonFor` prices from `applies_from`. **Test 45 must be seen failing on today's `CanonFor`.** `ContractAdjustmentLateConfirmationTests` unchanged
- [ ] 9.4 **[RED then GREEN, Tests 44, 47]** `AdjustmentTiming`, `Contract.ConfirmAdjustment` refusals
- [ ] 9.5 **[RED then GREEN, Tests 39, 41 to 43]** `IAdjustmentDesk` preview, confirm, history; account lock, summary rebuilt and compared (`PreviewStale`, `CorrectsLatest`); two operators at once
- [ ] 9.6 **[RED then GREEN]** `AdjustmentsViewModel`: worklist, summary, the post-confirmation notice reminder. No view: S12
- [ ] 9.7 **[Prove the guards by making them fail]** Both unique indexes
- [ ] 9.8 **[Guardrails and handover]** as 1.10 and 1.11
- [ ] 9.9 **[Independent verifier]** Item 1

### Verification, PR 9 (Docker: yes)

Same three commands as PR 1, then the independent verifier.

---

## Phase 10: La diferencia (S10, PR 10, est. about 450 lines)

**Docker: yes. Supabase: M7 (H.3). Model change: yes, M7 in this PR.** Spec tests: 49 to 55, 61 to 67, 92. Requirements 16, 19, 20. Design anchors: D4, D5, D6, D7, §3.3, §3.6, §10 F4, F5, F8, §12 M7.
**Risk: item 1 (what is owed), item 3 (`PeriodNet` consumers).** Needs: S9, and **S5**, which this slice extends so that cancelling also reverses the new kind.

- [ ] 10.1 **[Second inspection, read-only]** Design §3.6's table against the code: `ContractAccount.cs:226,248-259,278,296-314,391-394,449-452`, `CollectionPlanner.cs:331-333,394-397,415-421`, `ReciboBuilder.cs:27-45,68,105-111`
- [ ] 10.2 **[Model and M7]** `MovementKind.AdjustmentDifference`, `ReciboLineKind.AdjustmentDifference`; `AddAdjustmentDifference`: both kind checks, `ck_account_movements_period_required`, a sign check, `difference_amount`, `difference_movement_id` with its unique index
- [ ] 10.3 **[RED then GREEN, Test 52]** `DifferenceNet`, recargo base subtracting it. **Test 52 must be seen failing if the new kind joins the recargo base**
- [ ] 10.4 **[RED then GREEN, Tests 49 to 51, 53 to 55]** `AdjustmentDifference.Regular`, the choice defaulting to no, D6's links; no double counting
- [ ] 10.5 **[RED then GREEN, Tests 61 to 64]** Ledger: sign, raw update refused, balance on its date, ended contract, voiding leaves it owed
- [ ] 10.6 **[RED then GREEN, Tests 65 to 67]** Planner and recibo: `FiguresFor`, `MovementsToWrite`, `ReciboLedgerFigures`, `HonorariosBase`, line kind, labels, preview, `DeskTexts.cs:19`
- [ ] 10.7 **[RED then GREEN, beyond the spec's Tests]** `AmountOwedForPayment` leaves out a charge whose period has not begun (design F8), one test
- [ ] 10.8 **[RED then GREEN, Test 92, cancellation half]** `ContractCancellation` reverses the new kind; no credit on any date
- [ ] 10.9 **[Prove the guards by making them fail]** Sign check, recargo base
- [ ] 10.10 **[Guardrails and handover]** as 1.10 and 1.11
- [ ] 10.11 **[Independent verifier]** Items 1 and 3

### Verification, PR 10 (Docker: yes)

Same three commands as PR 1, then the independent verifier.

---

## Phase 11: Correcciones (S11, PR 11, est. about 300 lines)

**Docker: yes. Supabase: no. Model change: no.** Spec tests: 57 to 60, 91, and Test 92's correction half. Requirement 18. Design anchors: §3.3, §3.4. **Risk: item 1.** Needs: S10.

- [ ] 11.1 **[Second inspection, read-only]** PR 10 merged intact; `RentAdjustmentIndexValue.cs:6-11`, `LedgerDating.cs:41-42`
- [ ] 11.2 **[RED then GREEN, Test 91]** `AdjustmentDifference.ForCorrection`, the chain formula: 330,000 in the spec's example; a paid one offers no new charge
- [ ] 11.3 **[RED then GREEN, Tests 57 to 60]** `IAdjustmentDesk` `PreviewCorrectionAsync`, `ConfirmCorrectionAsync`, from a corrected index and from the history; appended, reusing the corrected row's `EffectiveDate`
- [ ] 11.4 **[RED then GREEN, Test 92, correction half]** Reversing an unpaid charge leaves no credit on any date
- [ ] 11.5 **[Guardrails and handover]** as 1.8, 1.10 and 1.11
- [ ] 11.6 **[Independent verifier]** Item 1

### Verification, PR 11 (Docker: yes)

Same three commands as PR 1, then the independent verifier.

---

## Phase 12: Pantallas (S12, est. about 950 lines, splits per screen)

**Docker: yes for the full suite. Supabase: no (the live smoke is H.7). Model change: no.** Spec tests: 70, 71. Requirement 22. Design anchors: §6, §7. **Waits for Mateo's mockups** (spec P8, P11). If he runs long, Tomás may choose
plain views for some screens and restyle them later (proposal §7).

- [ ] 12.1 **[Second inspection, read-only]** `NavigationEntry.cs:4-11`, `ShellViewModel.cs:48-52`, `ShellViewModelTests.cs:42,56`, `App.xaml.cs:148-183`, `MainWindow.xaml.cs:18-36`
- [ ] 12.2 **[RED then GREEN, Test 70]** `NavigationTarget` gains `Parties`, `Units`, `Contracts`, `Indices`, `Adjustments`; added **before** the Admin block, so Usuarios stays Admin-only
- [ ] 12.3 **[Desktop]** Theme dictionary and the five views, one PR per screen; build only
- [ ] 12.3b **[H.6, carried from S7]** With the Índices view: `IndicesViewModel` now takes `IIndexRegistry`, `IIndexValueSource`, `IAgencyClock` and `IDueAdjustmentQuery`; it needs a row list for `Fetched` with its confirm and correct actions, and hides the button on `CanBringFromSource`. One press of each button (ICL, IPC) against the live source, from the screen
- [ ] 12.3c **[Carried from S8]** The clause forms: set `ClauseIndexNames` and `IndexNames` from the catalog, or an offset left blank silently reads 1 instead of the suggestion. Show `ClauseWindowText` and `WindowText`
- [ ] 12.4 **[Audit, Test 71]** Every refusal in this spec has its own operator text, none reaching the generic error
- [ ] 12.5 **[Specs]** Merge the deltas into `openspec/specs/` on archiving, as before
- [ ] 12.6 **[Guardrails and handover]** as 1.10 and 1.11

### Verification, PR 12 and its siblings

Same three commands as PR 1.

---

## Human Follow-Ups (Tomás)

| # | When | What |
|---|---|---|
| H.1 | Before each slice | Docker running. A skipped Testcontainers test is an unrun test |
| H.2 | Before anything touching the live database | Supabase restored if paused. Only Tomás connects; the assistant never handles the password |
| H.3 | With S3, S4, S5, S6, S8, S9, S10 | **Apply the slice's migration to the test database, smoke-test it, then to production, before installing a version that needs it** (design §12, D25). M1 `AddContractPartyRemovalAndGuards` (S3), M2 `AddContractEventsAndTermsLock` (S4), M3 `AddLoadedByMistakeEndReason` (S5), M4 `SeedEconomicIndices` (S6), M5 `AddClauseWindowOffset` (S8), M6 `AddAdjustmentAppliesFrom` (S9), M7 `AddAdjustmentDifference` (S10) |
| H.4 | **Before M1 and before M6, on production, expecting no rows** | The two preflight queries below. M1 fails if a contract has two tenants; M6 fails on duplicate Regular rows |
| H.5 | From M3 on | **Every PC at the agency updates together**: once a version writes a new enum value, an older version cannot read that row (design §12) |
| H.6 | S7, then S12 | One manual press of each button (ICL, IPC) against the live source. Done on 2026-11-07 without a screen, from a scratch program; the press from the screen is task 12.3b |
| H.7 | After S12 | Live smoke of the five screens on the test Supabase, restored first |
| H.8 | When Nicolás answers | Questions N1 to N4 below. None blocks anything: N1 and N2 change only form suggestions and which day the ICL button asks for, each a small edit |

**Preflight queries (H.4):**

```sql
SELECT contract_id FROM contract_parties WHERE role = 'Tenant' GROUP BY 1 HAVING count(*) > 1;
SELECT contract_id, effective_date FROM rent_adjustments WHERE kind = 'Regular' GROUP BY 1, 2 HAVING count(*) > 1;
-- Before M1, expecting false: no Empleado login may create objects, which the older cutover UPDATE trigger relies on.
SELECT has_schema_privilege('inmobiliaria_empleado', 'public', 'CREATE');
```

**Questions for Nicolás (`spec.md`, "Questions"):**

- **N1.** On a contract adjusted by IPC on 1 July, which is the last month you use? (June, May?) And on RIPTE and ICL?
- **N2.** For ICL, do you compare the 1st of the month with the 1st of the month?
- **N3.** Do you have any tenant or owner that is a company?
- **N4.** Which address do you send from, and from which program? Input for the notices change, not this one.

## Issues found

1. **`design.md:107` says "§12 gives the check" for dotted identifiers on existing rows, and §12 does not.** Its two queries cover tenants and
   adjustments. S1 tolerates such rows (search normalizes the stored value, and a correction rewrites it), but a dotted row would not collide with its
   digits-only twin. Suggested addition to H.4, not in the design: `SELECT id FROM parties WHERE dni ~ '[^0-9]' OR cuil ~ '[^0-9]';`, expecting no rows.
2. **The spec's Preconditions table names slices S1 to S6 from the proposal** (design F6). The Verification tables here are the current ones.
3. **Test 71 is split**: proven per slice, audited at S12. **Test 92 is split**: its cancellation half closes in S10, its correction half in S11.
4. **Fixed on 2026-11-05, before S2, from S1's review** (lineage `review-0f581f6b7015ea49`, approved; its findings were non-blocking). Editing any form field now withdraws a pending confirmation or an unaccepted no-identifier warning, so the operator saves and confirms again; the warning had the same defect and the review had not named it. Suite 1128 passed, 0 failed, 0 skipped. What the review found:
   - `PartiesViewModel.cs:218-222`, a real defect: confirming a correction writes the form as it stands, not the values shown in the summary. Editing a field after saving does not clear the pending confirmation, so the operator can confirm one thing and store another. Requirement 3 needs what was confirmed to be what is written.
   - `PartiesViewModel.cs:234-255`: the failure path has no test.
   - `PartyRegistry.cs:110-117`: searching rows that still hold dots, and partial identifiers, have no test.

5. **From S3's verifier, for S4:** an Empleado can set `contract_parties.removed_at` by raw SQL and then insert another tenant, with no trace until `contract_events` exists (S4). The index still refuses a second live tenant. This is the raw gap spec F6 accepted.
6. **The older cutover UPDATE trigger does not qualify `pg_has_role`** (`20261023131755_HardenCutoverDateToAdmin.cs`), so it relies on no login holding CREATE on a schema. None does on the test database; H.4 now checks production. Left as it is: it predates this change.
7. **Small wording left as it is:** a unit repeated in the shares gets the "sumar 100%" text, which S12's picker should make unreachable. `UnitListing.AddressText` writes "depto" where the recibo's `DescribeUnit` writes "dpto"; contract screens use the recibo's, except the unit-being-vacated warning, which reuses the unit listing.

8. **From S3's review, for S12:** `PartiesViewModel` and `UnitsViewModel` apply a reply to whatever the form holds by then, as `ContractFormViewModel` did before its fix. An edit made while a save is out can be cleared, or a warning accepted for values it did not describe. S12's views should make the form read-only while busy, or those two get the same edit counter.

9. **To fix before S4, from the review of S3's tests** (lineage `review-00412cafc6e4a762`, approved on a scratch clone holding only the nine new test files; its findings are non-blocking):
   - `ContractFormAdversarialTests.cs:54-61`: `OddCanons_DoNotThrowOutOfTheCommand` asserts nothing, and the runner would catch a throw anyway. Pin the refusal or the port call for each input.
   - `ContractFormViewModelTests.cs:133-162`: warning withdrawal skips end date, due day, cutover, lessors, fee plan, clause and `SharesAsAmounts`.
   - `ContractCreationRoleTests.cs:160-164`: `PeriodOfOnlyAccrualAsync` does not prove the accrual is the only one. Assert the count.
   - `ContractCreationAdversarialTests.cs:491` and `ContractCreationRoleTests.cs:100`: both accept any exception. Assert the probe's failure and the two-tenant violation.
   - `ContractCreationRoleTests.cs:203`: `AClauseOnADiscontinuedIndex_DoesNotThrow` accepts any outcome. Pin it.

   **Done 2026-11-06, tests only, before S4.** Each strengthened assertion was seen failing against a temporary mutation, then restored. The suite is now 1404 (nine new withdrawal cases). **Pinned as a decision, taken 2026-11-06:** `AClauseOnADiscontinuedIndex_IsWritten`. Creating the contract stays allowed: an index with a successor is valid, a window can precede the discontinuation, and an index can be discontinued after the contract exists, so a check here would not remove the case. `economic-index/spec.md` already requires resolution to fail explicitly, and `DueAdjustmentQuery` does: the adjustment stays pending, no canon is guessed and collecting is not blocked. Today nothing reaches this, because `MarkDiscontinued` has no caller in `src/`.

10. **For whoever builds the screen that discontinues an index** (`proposal.md:80`, additive later), from item 9's decision: the warning says "falta el índice X de MM/AAAA", but typing that value cannot clear it, since resolution fails before levels are read, so it needs its own text; the clause's picker should not offer a discontinued index; and `DueAdjustmentQuery` has no test of this path, only `IndexResolver` does.
11. **`CorrectAsync`, `SetRateAsync` and `SetCodebtorsAsync` have a port and no view-model.** Task 4.6 names detail, notice and end only, and design §6 lists no correction form. S12 needs one for Requirement 9 before those three reach an operator.

## Deviations (recorded beside tasks during apply)

**Phase 1 (S1), applied 2026-11-05.**

- 1.1: baseline 1013 passed, 0 failed, 0 skipped, as measured just before. Anchors matched the design.
- 1.2: **`NormalizeDni` and `NormalizeCuil` strip dots, dashes and spaces, not "everything but digits"** (design §2.1 says "digits only"). Keeping digits alone would
  turn "abc" into empty, and empty means absent, so it would be accepted. Anything else stays and `IsValid*` refuses it; what reaches the database is digits only, as the design requires.
- 1.2, 1.3, 1.5, 1.6, 1.7: **RED order.** The Domain bodies were drafted before the tests, so they were stubbed out to observe RED, then restored. With stubs (identity normalizing, always valid, adapter
  throwing `NotImplementedException`, empty view-model commands, wrapper unregistered): Domain 34 of 55 selected failed, Presentation 14 of 15, Infrastructure 30 of 33.
- 1.4: **`PartyRefusal` gains `PartyNotFound`**, beyond design §2.1's six: a correction by an id no longer there would otherwise throw. **`CorrectAsync` also takes `acceptNoIdentifier`**: R3 says R2's checks
  apply, so removing a person's last identifier warns as creating does.
- 1.5: a blank search lists everyone, uncapped, in `AccountSearch`'s order. Requirement 1's "list" needs it, since `AccountSearch.Select` finds nothing under 2 characters and stops at 50. `AccountSearch` itself is unchanged.
- 1.7: "use that person" sets `ChosenParty`; the contract form reads it in S3. It is offered on creating only, since on a correction it has no meaning; the refusal still names the holder there.
- 1.8: guards, each in a scratch mutation of `PartyRegistry.cs`, restored after: no normalization → 5 red, including Tests 3, 4, 6; `ix_parties_dni` mapped to the wrong refusal → 4 red; holder lookup removed → 3 red, including Test 4; accept flag ignored → 2 red, including Test 6.
  **Test 7 has no mutation of its own**: this slice writes nothing to `recibos`, and that table is already append-only. It pins the stored `tenant_name` before and after.
- 1.9: differential. `git diff` on `Party.cs` and `SessionServices.cs` is additions only. The new `PartyTests.TheConstructorAndUpdateContactInfo_BehaveAsBefore`, run unchanged on a copy of the base commit, passes there too. Every existing `SessionServicesTests` assertion is still present and green. `has-pending-model-changes` reports none, so `CorrectIdentity` changed no EF model.
- 1.10: build 0 warnings, 0 errors; full suite **1109 passed**, 0 failed, 0 skipped (Domain 483, Presentation 259, Infrastructure 367), 96 more than the baseline; `has-pending-model-changes`: no changes. `TIME_WAIT` before the suite: 1006.
- Size: about 1,620 authored lines against an estimate of about 350, about 730 of production code, of which 326 are the view-model, and about 890 of tests. Not split: one behaviour with its tests. **This is a warning for the forecast of later slices, whose estimates may be as low.**

**Phase 2 (S2), applied 2026-11-05.**

- 2.1: PR 1 and its review follow-up (#91, #92) merged intact; `Unit.cs:38`, `PropertyUnit`, `ParkingUnit` as the design cites. Baseline 1128 passed, 0 failed, 0 skipped.
- 2.2: **`UnitDraft` and `UnitDraftRules` added**, not named by the design, following `PartyDraft`: the address a form typed, trimmed, with its one rule as a value rather than the `Address` constructor's exception.
- 2.2, 2.3: **`UnitListing.Covering` holds status, tenant and move-out date.** Spec Decision 30 lets a successor start while the previous contract waits for its move-out, so two contracts can cover one unit: the `Active` one is reported, since that is what a new contract is refused for. The status is there because S3 needs it to tell a refusal from a warning.
- 2.2 to 2.6: **RED order.** Port and form rule were drafted first, with the adapter throwing `NotImplementedException` and the view-model's write path stubbed: Presentation 18 of 21 selected failed, Infrastructure 22 of 22. Domain 8 of 8 passed at once, so its rule was proven by mutation below.
- 2.4: The warning also applies when correcting, excluding the unit's own address, since the requirement says "an identical existing address". All seven parts are compared, folded: another floor or apartment is another unit.
- 2.6: Editing any field, or choosing another type, withdraws the warning, the rule S1's review follow-up established. No confirmation on correcting an address: the spec asks for none.
- 2.7: **Guards proven by making them fail**, each restored after: `PendingTermination` no longer covering failed its test; the pending contract reported before the active one failed its test; exact text instead of folded failed 3; a postal code no longer required failed 1.
- 2.7: Build 0 warnings, suite 1179 passed, 0 failed, 0 skipped, `has-pending-model-changes` clean. **About 1,410 lines against an estimate of 250**, nearly all tests, like S1. The list is `UnitRegistryTests.TheList_IsOneQuery`, pinned by counting commands.

**Phase 3 (S3), applied 2026-11-05.**

- 3.1: PR #93 merged intact; every anchor matched. Baseline 1179 passed, 0 failed, 0 skipped.
- 3.2: **`Contract`'s constructor does not validate the cutover** (design §2.3 says it does through `CheckCutoverBounds`). It has no `today`, and Domain has no clock, so it cannot. The bounds are one method, used by `CheckCutoverDate` and by `ContractDraftRules`; the constructor is unchanged.
- 3.2: **`RentSplit.FromAmounts` added**, not named by the design, for the conversion spec R7 asks for: the residue goes to the largest share, ties to the lowest id, like `RentSplit.Equal`.
- 3.3: **The new function names `pg_catalog.pg_has_role`** (design D17 says "the same test"). The verifier showed a login holding CREATE could shadow the unqualified name. See Issues found, 6.
- 3.5: **The units are locked (`FOR UPDATE`, in id order) before their availability is read.** The design says the check runs inside the transaction, which alone would not stop two operators letting one unit at once (spec Decision 30). Removing the lock fails `TwoOperatorsOnOneUnit_TheSecondIsRefused`. Availability reuses `UnitRegistry.ListAsync`.
- 3.5: A cutover date warns of nothing about the first due date, since a cutover carries those months. A rate or fee with more decimals than its column, or too large for it, is refused rather than rounded by the database. A person repeated as lessor or codeudor becomes one row.
- 3.5, 3.7: **Warnings are accepted together with one flag** (`acceptWarnings`), and editing anything on the form withdraws them, as on Personas and Propiedades. Accepting never gets past a refusal.
- 3.7: **`ContractsViewModel` added**, not named by the tasks: spec R6 needs the list and the past-end flag, and no other slice owns them. The detail view-model stays in S4 (4.6), as planned.
- 3.2, 3.5, 3.7: **RED order.** Trigger and index tests failed before M1 (4 of 11). The port's tests failed against an adapter throwing `NotImplementedException` (26 of 26). The rules and the form were drafted first, then stubbed to observe RED, then restored: Domain 23 of 33, Presentation 29 of 33.
- 3.8: **Guards proven by making them fail:** no trigger or index (before M1, and again by the verifier: 4 of 36), no unit lock (1), an empty error map (1), and each of the verifier's findings below failing before its fix.
- 3.9: Build 0 warnings, suite 1395 passed, 0 failed, 0 skipped, `has-pending-model-changes` clean against `AddContractPartyRemovalAndGuards`.
- 3.10: **Independent verifier: every requirement and test PASS, no blockers.** An Empleado got no cutover date stored by any route tried: insert, multi-row, `MERGE`, `COPY`, role switching or trigger evasion. Not verified: `PerCallContractRegistry` against a real login, the view, and Supabase. **Fixed from its advisories:** a 29-digit share threw out of the form with no message; with no canon, typed amounts blamed the shares. Its 114 test cases are committed as `ContractCreationAdversarialTests`, `ContractCreationRoleTests`, `ContractFormAdversarialTests` and `ContractDraftAdversarialTests`, less one that passed only while a known gap stays open.
- 3.10: **The gentle-ai review saw the code, the migration and the two edited test files, not the nine new test files**, because the whole change exceeds its context budget. Those files ship in the same commit and carry most of the 1395 tests stated here: `ContractDraftRulesTests`, `ContractDraftAdversarialTests`, `ContractPartyGuardTests`, `ContractRegistryTests`, `ContractCreationAdversarialTests`, `ContractCreationRoleTests`, `ContractFormViewModelTests`, `ContractFormAdversarialTests`, `FakeContractRegistry`. The suite and the independent verifier cover them; the review does not.
- 3.10: **gentle-ai review `review-96ad9cc3ff60c910` ended `escalated`, with no approval.** Its one CRITICAL finding, `R3-missing-s3-tests`, says the test files are absent, which follows from leaving them out to fit the budget; recording that in this file did not satisfy its validator, and adding them exceeds the 200-line correction budget. Three lenses ran: risk, resilience, reliability.
- 3.10: **Fixed from its warnings, after it closed, so not reviewed by it:** the result is read before committing, so a failed read writes nothing (`AFailureReadingTheResult_LeavesNothing`); an edit made while a save is out discards the warnings that come back and is kept when the contract is written (`AnEditWhileSaving_*`). The three tests failed before the fix. **Accepted:** `R1-removed-at-untraced` is Issues found 5, spec F6.
- 3.9: **M1 applied by Tomás on 2026-11-05**, to the test database and then production, after the H.4 queries came back clean. On both, the trigger, the index and the column were each confirmed present. Smoke test on the test database: the app opened and the desk found the fictitious contract.

**Phase 4 (S4), applied 2026-11-06.**

- 4.1: **Second inspection clean.** PR #95 landed intact, CI green on `efc1720`, and every anchor matched. Two things the design left open: `AccountRowLock` is an advisory lock and not `FOR UPDATE`, and the amount owed needs `RecargoTerms`.
- 4.2: **`SetHonorariosPercentage` takes `decimal`, not `decimal?` as design §2.3 writes it.** The design's next sentence says an entered rate cannot return to null; a non-nullable parameter says it in the signature.
- 4.2: **`RemoveParty` takes the account and a `replacementFollows` flag**, beyond the design's `(partyId, role, at)`. The rule depends on movements, and "unless the same operation adds a replacement" has to be stated by the caller.
- 4.2: **`Contract.Parties` now hides removed rows itself.** A write loads them through `IgnoreQueryFilters`, because `AssignParty` restores a row from them, and every domain rule reading `Parties` must not count them.
- 4.2, 4.4: **`ContractDraftRules.Check` gained `standingStart`.** Without it a contract that waited a month with no movement could never be corrected, since "start before the current month" refused a date nobody changed. A start that moves is still judged in full. Not in the spec, which says only "under the same checks as creation".
- 4.3: **`ck_contract_events_reason_not_blank` says `reason IS NOT NULL` too.** Design D12 asks for a non-blank reason; the column is nullable, and a CHECK passes on NULL. Pinned by `AnEventThatNeedsAReason_IsRefusedWithoutOne`.
- 4.4: **Two refusals the spec does not name.** A contract with a confirmed adjustment cannot be corrected even with no movement (`AdjustmentConfirmed`), since the adjustment priced from the canon being changed. An ended contract cannot be corrected (`ContractEnded`), since its end date is only checked against its start at the moment of ending. Both are easy to reverse if the agency needs otherwise.
- 4.4: **A correction holds the account's lock, then the contract's row.** D16 leaves the canon to the port and the domain, so nothing else stops a correction overwriting the canon of a period being written. Removing the lock fails `ACorrection_WaitsForAWriter_ThenSeesItsMovement`.
- 4.4: **A tenant swap saves twice.** The tenant index is not deferrable, and bringing back a previous tenant is two updates EF orders by key. The test picks ids on each side of the tenant's, because with random ids the same bug was caught only some of the time.
- 4.4: **Warnings on a correction are only what it changes.** No rate warning, since the rate has its own operation, and no "first due date passed" unless the start or due day moved.
- 4.4: **`SummarizeEndAsync` added** for R10's summary. It applies the end in memory and appends what is due without saving, through `DueAccruals.AppendInMemory`, extracted from `CollectionDesk.BuildPreview`, whose tests pass unchanged. `AmountOwed` is null when the recargo settings are invalid: those close the desk, they must not stop an end, and no figure is guessed, so the screen shows the ledger and says recargo is left out.
- 4.5: **`SetCodebtorsAsync` replaces the whole list**, covering add, replace and remove in one call (spec Decision 33, "a replacing operation"). `contract_events.detail` holds the role and party id. Tenant and lessor removals leave the same trace, which D12 allows and the spec does not ask for.
- 4.2, 4.4, 4.6: **RED order.** The port's tests failed against an adapter throwing `NotImplementedException` (73 of 85; the other 12 are guards M2 already satisfied). The domain's failed to compile against members that did not exist. The view-model and its tests were written together, so its RED is the mutations below.
- 4.7: **Guards proven by making them fail, reading why each failed.** Terms lock, its `due_day` branch, append-only, the stamp, the grants, the account lock, the event trace, the restore, the movement and adjustment checks, the query filter, the summary's end date and unwritten periods, and the view-model's summary closing. Three of my own tests were caught passing for the wrong reason and strengthened: the tenant swap (chance, see above), the summary's end date (passed on any day of the month before the 10th) and `ATenantOrLessor_CannotBeRemovedWithMovements` (the "last tenant" rule answered first).
- 4.8: Build 0 warnings, suite 1550 passed, 0 failed, 0 skipped (Domain 573, Presentation 365, Infrastructure 612), `has-pending-model-changes` clean against `AddContractEventsAndTermsLock`. The model diff was the new table alone, so the three private setters changed nothing. Size: see `git diff --stat` at handover.
- 4.9: **Independent verifier: one blocker, fixed.** R9 and Tests 23 to 27, 29 and 80 passed. `SummarizeEndAsync` used the dated `LedgerBalance` and `AmountOwed`, so after a voided early payment, whose rent stays dated ahead of today, the summary said 0 owed and the desk then asked for 450,000. It now uses the desk's figures (`AmountOwedForPayment`). Pinned by `TheEndSummary_CountsARentLeftOwedByAVoidedEarlyPayment`, which fails with the dated figures (expected 450000, actual 0), and `TheEndSummary_IsWhatTheDeskAsksFor_AndWhatPayingItAllComesTo`.
- 4.9: **Its advisory on the unit lock taken up.** Removing it passed every test, and the verifier's race then put three contracts on one unit. `ReplacingWithAUnitBeingLetRightNow_WaitsAndThenRefuses` now fails without it. The foreign key would make the correction wait anyway, so the test asserts the refusal, not the wait.
- 4.9: **Rechecked by the verifier, limited to that blocker: it no longer reproduces.** On the voided early payment the summary and the desk both say 450,000, and its other probes match the desk to the peso (2,922,500 summary, desk and paid, fee plan included).
- 4.9: **Also fixed:** "Queda adeudado $0" now reads "No queda nada adeudado."; mixed line endings in `AccountMaterialiser.cs`.
- 4.9: **Advisories left as they are, recorded for later:**
  - **Lock order for S9 and S10:** confirming an adjustment must take the account's lock before the contract's row, as `ContractRegistry` does, or the two can deadlock. Today `ConfirmAdjustment` has no production caller.
  - **`AdjustmentConfirmed` refuses all of a correction**, also tenant, lessors and units, which do not depend on the canon. Mild and unreachable today; revisit with S9.
  - **Raw SQL as a real Empleado after a movement still changes** `monthly_rent`, `contract_parties.party_id`, `contract_parties.removed_at` on the only lessor, and `contract_units.unit_id`. D16 and spec F6 accept this as reachable only by raw SQL, but `removed_at` widens F6, which reasoned only from the lack of DELETE.
  - **A preview open during a tenant correction** commits and prints the new tenant, since the header is not part of preview equality. It prints current data, not what the screen showed. Canon, rate and end give `PreviewStale` correctly.
  - **A correction can leave notice or move-out dates on the wrong side of a new start**, and notice can predate the start. The spec states no rule.
  - **A contract starting in the future cannot be ended:** every date is refused by R10's bounds. Only cancelling (R23, S5) applies.
  - **`PeriodsAlreadyWritten` counts `RentAccrual` only**, so an opening-balance month after the end date is not named. Read from the code, not reproduced.
- 4.9: **A socket failure in the verifier's first full run** (`SocketException` in `AnEarlyPayment_RacingTheLazyAccrual...`), passing on rerun. TIME_WAIT was 1 before and 453 after, not measured during, and the rerun peaked at 4,353, well below the 15,650 `CLAUDE.md` records. Not caused by this code, but it does not fit the recorded explanation.
- 4.9: **gentle-ai review, in two candidates on a scratch clone,** because the whole change (24 files, 4,517 lines) hit `lens_context_budget_exceeded`, as S3 did. **Code and M2 without tests or documents (18 files, 2,484 lines; lenses risk and resilience): `review-db39865409dd3fb9`, approved, acknowledged.** **Tests over that code, plus the cancel fix (6 files, 2,015 lines; lens reliability): `review-5ef8317a6005412d`, approved, acknowledged.** Leaving the documents out of the first candidate is what kept S3's false "tests are missing" finding away. `tasks.md` and `ESTADO.md` were not in either.
- 4.9: **Its non-blocking findings, fixed after each closed, so not reviewed by it.** `R4-cancel-during-commit`: cancel stayed enabled during a write, so an operator could believe an end was abandoned when it was saved; now gated, and the fix itself went into the second candidate. `R3-cancel-notify-unproved`: the test now asserts the button is told, and fails if the notification is removed. `R3-rate-movement-setup-vacuous`: renamed to what it proves.
- 4.8: **Final guardrails, after everything above:** build 0 warnings, suite 1556 passed, 0 failed, 0 skipped, `has-pending-model-changes` clean against `AddContractEventsAndTermsLock`.
- 4.8: **M2 applied by Tomás on 2026-11-06**, to the test database and then production. On both, the table, the three triggers and the history row were confirmed, with INSERT and no DELETE for an Empleado. Smoke test on the test database: the app opened and the desk found and selected the fictitious contract.

**Phase 5 (S5), applied 2026-11-06.**

- 5.1: **Second inspection clean.** PR #96 landed intact, CI green on `9036aee`, and every anchor matched, except that `AccountRowLock` moved down `AccountMaterialiser.cs` when S4 extracted `DueAccruals`.
- 5.2: **M3 is the widened check alone** (`AddLoadedByMistakeEndReason`). `end_reason` was already `varchar(32)`.
- 5.5: **The amount in the summary is what the desk would ask for, not what the corrections add up to.** Spec R23 asks for "the exact amount that stops being owed". Periods due and never written stop being owed too, and so does projected recargo, yet neither has a row to correct. S4's blocker came from reading a different figure from the desk's. With invalid recargo settings it is the ledger with what is due, and cancelling still works.
- 5.5: **Unwritten periods are not written first in order to correct them.** The spec's scenario expects both accruals on the ledger, which holds when the account had been read; when it had not, nothing is written, the balance is zero and the summary still counted them. Design §2.5 says so too. A reviewer may want to confirm this reading.
- 5.5: **A month is taken to zero, not past it.** Design §2.4 reverses each movement "for its net", which on a part-paid month would leave a credit, so the reversal stops at what the month still owes. Not reachable through the desk today, which pays a month whole; pinned by `APartPaidMonth_IsReversedForWhatIsLeft_AndNoMore`.
- 5.5: **`ContractCancellation.Plan` takes no role**, unlike design §2.4's input list. Neither role differs on cancelling (spec Decision 35), and an Admin is named from the voiding window alone. A payment needing an Admin blocks an Admin too: it has to be voided either way.
- 5.5: **An account that changed since the summary refuses with `CancellationStale`**, compared through the plan's corrections as well as the amount. The amount alone cannot see it: a period written in between changes nothing in what the desk asks for. Proven by removing that half of the comparison.
- 5.5: **S10 must add `AdjustmentDifference` to `ContractCancellation.IsCharge`**, since the kind does not exist yet (tasks order: "S10 needs S5").
- 5.6: **`ContractDetail` gained `Events` and `IsCancelled`, `ContractListing` gained `IsCancelled`**, for "readable, marked as cancelled, with its reason". `ContractRow.StatusText` reads "Cancelado por error de carga". The worklist, availability, mora and recibos needed no change, as design §2.5 says; each now has a test.
- 5.7: **The undo end's unit check locks the units first**, as creating does. An undo writes no row naming the unit, so without the lock no foreign key would make it wait: S4's verifier found this same gap on replacing a unit, so this one has its test from the start.
- 5.7: **`SummarizeUndoEndAsync` names only what will newly accrue**, leaving out what is already on the ledger.
- 5.8: **"Cargado por error" is refused by the port as well as left off the screen's list** (`EndReasonNotOffered`), and `End` throws on it, so a stale list or a test cannot end a contract that way without reversing anything.
- 5.2, 5.5, 5.7: **RED order.** The port's tests failed against an adapter throwing `NotImplementedException` (33 of 35; the other two are satisfied by M3's check and the domain rule). The domain's were written before its code and could only fail to compile. The view-model's were written with it, so their RED is the mutations below, plus `TheList_MarksACancelledContract`, seen failing first.
- 5.9: **Guards proven by making them fail, reading why each failed (30 mutations).** Accrual guard (with the end date alone governing, 3 periods accrued where 0 are expected, which is Test 73), voiding block, annulled payments, Admin named, part-paid reversal, frozen recargo, reversal date, the summary's comparison and its figures, the event trace, corrections, the covering contract, the unit lock, desk and go-live filters, the end reason check, final cancellation and the list.
- 5.11: **Independent verifier: one blocker, fixed.** R24 and Tests 72 to 75, 77 to 79, 89 and 90 passed, and no reachable account shape ended non-zero, double-reversed or corrected a payment. A payment arriving between the summary and the confirm was refused with a generic text, without the recibos R23 asks for, because `ContractResult` could not carry them. `ContractResult.RefusalText` now does, pinned by `APaymentBetweenSummaryAndConfirm_Refuses_NamingItsRecibos`. **Rechecked by the verifier, limited to that blocker: it no longer reproduces** ("Primero hay que anular los recibos 31001, 31002."). Its pass overlapped my mutations, so the final suite below reruns both on the final code.
- 5.11: **Its advisories taken up, each seen failing when its fix is reverted:** the voiding window in agency days near midnight, agreeing with what the desk lets an Admin void; a recent and an old payment naming only the recent, with the Admin notice saying which ones need one; a payment and an accrual arriving mid-cancellation; a voided early payment; a refused summary cannot be confirmed; the event keeps what was reversed, what stopped being owed and the reason replaced; `SetRateAsync` and `SetCodebtorsAsync` refuse a cancelled contract, beyond the spec, since cancelling is final (spec Decision 37); `SuggestRateAsync` skips a cancelled contract, so a mistaken rate does not come back when the right contract is loaded.
- 5.11: **Advisories left as they are, recorded for later:**
  - **A credit month ends non-zero.** A negative opening balance would end at its own amount, and a positive correction with no period too. Nothing in `src/` writes either today. **`carga-inicial` must refuse negative opening balances**, or this reopens.
  - **`SetCutoverDateAsync` by id still accepts a cancelled contract.** No charge results, since the schedule produces nothing, and go-live no longer lists it.
  - **Cancelling a contract starting in the future leaves `actual_end_date` before `start_date`.** No reader broke, and no check ties them.
  - **`AddLoadedByMistakeEndReason`'s `Down` fails with 23514 when a row is already cancelled**, and rolls back cleanly. That is the right failure: rolling back past a cancellation has to be a decision.
  - **Mutations that still survive:** the fingerprint without amount and date, since the account-changed test changes which movements are corrected, not their figures, and the amount is compared beside it; and `PeriodsBegun`'s own guard, which looks redundant with `IsScheduled`.
- 5.11: **Socket data point, for `CLAUDE.md`'s open question:** the verifier's full run went from 6 to 3,142 `TIME_WAIT` with no failure.
- 5.11: **gentle-ai review, in two candidates on a scratch clone,** as in S4, since a slice this size does not fit whole. **Code and M3 without tests or documents (20 files, 2,134 lines; lenses risk and resilience): `review-7d7f19f862597282`, approved, acknowledged.** **Tests over that code, plus M3's comment (6 files, 1,660 lines; lens reliability): `review-6c52c48bdbb32522`, approved, acknowledged.**
- 5.11: **Its non-blocking findings, fixed after each closed, so not reviewed by it.** `R4-migration-down-blocked-after-cancel`: `Down` failing after the first cancellation is deliberate, and the migration now says so. `R3-down-migration-contract-unproved`: `M3sDown_FailsOnceAContractIsCancelled_AndLeavesEverythingInPlace`. `R3-double-confirm-write-path-unproved`: `ASecondConfirm_IsRefused_AndWritesNothing`. `R3-sleep-as-lock-synchronisation`: the test waits on `pg_locks` and no longer on a delay. `R3-undo-notice-reread-unasserted`: the test now changes what the fake answers and reads the new state.
- 5.10: **Final guardrails, after everything above:** build 0 warnings, suite 1659 passed, 0 failed, 0 skipped, `has-pending-model-changes` clean against `AddLoadedByMistakeEndReason`.
- 5.5: **Spec scenario "A contract with unpaid months is cancelled" reworded, approved by Tomás on 2026-11-06.** It said both accruals stay on the ledger, which holds only for an account already read. It now says that of accruals already written, and that a month not yet written stays unwritten while the summary counts it. The code did not change.

**Added after S5, 2026-11-06: each person's roles (spec Requirement 1, Test 93).** Asked for by Mateo's mockup, approved by Tomás. `PartyListing.ActiveRoles` is filled by a search, from one query over contracts `Active` or `PendingTermination`, with the query filter hiding removed parties. `PartyRow.RolesText` is the column, and `PartiesViewModel.RoleFilter` the filter, working on what the search brought so choosing one asks the database nothing. No model change, no migration. What a write returns carries no roles: the screen reads again after every action. RED: the tests failed against empty members. **A regression caught by the existing suite, fixed at its root:** `PartyListing` is a record, which compares a list by reference, so two identical listings stopped being equal (`Correcting_WritesEveryField_...`). It now compares roles by content. That run also measured sockets during the suite: 4,519 `TIME_WAIT` before, peak 5,030, and no `SocketException`. **Risk: none, read-only.** No independent verifier, as S1 and S2.
- 5.10: **M3 applied by Tomás on 2026-11-06**, to the test database and then production. On both, the widened check and the history row were confirmed. No smoke test: it only allows one more value in a check. **The agency's installed version does not write that value, but from the next install on every PC updates together (H.5).**

**S6 written, 2026-11-06, with the correction record Tomás asked for (spec Decision 42, design D26).**
- 6.1: Second inspection: the branch's tree is identical to `origin/develop`, and the three anchors still match. S6's classes did not exist yet.
- 6.2, 6.5b: **M4 is `20261106220915_SeedEconomicIndices`, M4b is `20261106221123_AddIndexValueCorrections`.** M4b is a new migration, not in the original plan, and it makes S6 a model change. Both still to be applied (H.3).
- 6.5: **Deviation:** a correction lists affected adjustments as those whose copy differs from the new level, not those holding the previous one. After two corrections a copy can hold neither, and it still needs correcting; a copy that already matches does not.
- 6.5: **A level with thirteen whole digits is refused too** (`LevelTooLarge`), beyond the design's list: `numeric(18,6)` would overflow into the generic error, which Test 71 forbids.
- 6.5: **A far level warns on a correction as well as on a new value.** Saving the level already stored writes nothing, and does not warn.
- 6.6: **The form asks to confirm every correction:** it shows the old and new level and who is affected before writing. A far level adds its second confirmation after that. No view: S12, and **Mateo's mockup needs a place for the record** (spec Requirement 12).
- 6.7: **RED observed:** Test 30 failed with `Expected 1, Actual 0`, and Tests 94 and 95 with `42P01: relation "index_value_corrections" does not exist`. The rules and the parser only as a compile failure, so the rules rest on the mutations below.
- 6.7: **Full suite: 1768 passed, 0 failed, 0 skipped** (1665 + 103). Build: 0 warnings. `has-pending-model-changes`: none. `TIME_WAIT` went from 561 to 5,064 with no socket failure.
- 6.7: **Nine mutations, each caught by the test meant to:** no `WHEN`, no append-only, no stamp, Empleado without `INSERT`, swapped levels, the seed ignoring what exists, every copy affected, far correction unchecked, the race reaching the generic error. Two first attempts proved nothing, `BEFORE TRUNCATE ... FOR EACH ROW` breaking the migration and `if (false)` not compiling, so they were redone.
- 6.7: **Risk: none, held at medium.** Item 2 was weighed, since M4b adds a grant and a guard: they are on a new table, change no existing permission, and are proven with real logins and by mutation. Assessment: `medium`, `review_due: false`, `under_budget`, so neither the verifier nor the gentle-ai review ran.
- 6.2, 6.5b: **M4 and M4b applied by Tomás on 2026-11-07**, to the test database and then production. On both, one query confirmed the two history rows, one active IPC, RIPTE and ICL each, the three triggers, and `SELECT` and `INSERT` on `index_value_corrections` for both roles. Smoke test on the test database: the app opened and the cash desk found and selected the fictitious contract. No screen uses these tables yet (S12), so the smoke test proves only that the app still works on the migrated database.

**S7 written, 2026-11-07. Risk: item 6 (no test sees the live API).**
- 7.1: **The exact IPC combination works:** `start_date=2026-08-01&limit=1&format=json` gave `["2026-08-01", 12276.766]`. A month it does not hold gives `data: []`, and `start_date=2000-01-01` gives the series' first row, `2016-12-01`, which reads as not published.
- 7.1: **New on ICL, not in design §5:** a date after today is HTTP 400, `La fecha desde no puede mayor a la actual`, while a date with no row is 200 with an empty `detalle`. So the view-model never asks for a future month, typed or on the worklist; a 400 that still arrives, for example from a wrong clock on the last night of a month, reads as a server error. Design §5 amended.
- 7.2: **Deviation from design §5:** `Unreachable` carries `TimedOut` and `SourceFailed` carries `ServerError`, because Test 84 asks for a message of its own for no connection, a server failure, a timeout and a changed format, and §5 mapped those to two outcomes.
- 7.4: **Invalid `IndexSources` settings read as a failing source, and do not reach the shell's settings problems,** which would close the cash desk over one button. `IndexSourceSettings.Problem` is read by nothing today but its tests; the guard is the test over the shipped file.
- 7.5: **A level the typed checks refuse is shown as a failure, never rounded.** Live, only IPC 04/2019 hits it (`212.9596414`, seven decimals), and typing it would be refused too.
- 7.5: **Decided beyond the task:** a month already stored with the same level says so; a differing one offers to open the correction with the source's level, under the correction's own confirmation; a slow answer arriving after choosing another index is dropped; a typed month wins over the worklist.
- 7.6: **RED observed:** with stubs, 56 of 57 adapter and settings tests and 27 of 29 view-model tests failed on behavior. Three failed cases were added after the verifier: an unknown charset, and two with a level of more than two decimals, those two proven by changing `Formats.Level` to round.
- 7.6: **31 mutations, each caught by the test meant to.** Four first attempts did not compile, `when (false)` being unreachable code under warnings as errors, so they were redone with a condition the compiler cannot resolve.
- 7.6: **Full suite: 1861 passed, 0 failed, 0 skipped** (1768 + 93). Build: 0 warnings. `has-pending-model-changes`: none. `TIME_WAIT` went from 6 to a peak of 3,141, measured during the run, with no socket failure.**
- 7.7: **H.6, without a screen:** a scratch program outside the repository drove the real adapter and view-model with a fake registry. ICL 07/2026 gave 34,24 and 11/2026 gave 37,21; IPC 08/2026 gave 12.276,766 and 10/2026 said it is not there yet; TLS validated by .NET's `HttpClient`, no `-k`. Also real: a host that does not exist gave no connection, the retired BCRA v3.0 gave a server error, and an address that never answers timed out at 10,009 ms. The verifier repeated this independently. **No button has been pressed on a screen: task 12.3b.**
- 7.7: **Independent verifier: no blockers,** Tests 82 to 88 pass. Of its advisories, the test gap on rounding and the charset escaping the adapter were fixed. Left as they are: `IndexSourceSettings.Problem` leaving no trace, an address with credentials in it passing the settings check (none is shipped), the decimals message not naming the source, and a discontinued row named "IPC" getting the button, which typed entry has at the baseline too.
- 7.7: **The compiled `appsettings.json` of `bin/Release` still points at the test database:** the shipped file changed, so the build overwrote it with production's, and it was restored with the `IndexSources` section added.
- 7.7: **The ICL-at-offset-0 scenario of Requirement 25 is proven on the button only:** a clause cannot hold that offset until S8.
- 7.7: **Assessment: `high`, by the escalation on item 6. gentle-ai review, three lenses (risk, resilience, reliability): approved and acknowledged,** lineage `review-43a319e6ea61c9ed`. Risk found nothing. Five non-blocking findings, left as later work, none reopening this review:
  - **A hanging source blocks typed entry for the whole press:** the months are asked one after another, up to 10 seconds each, so 12 missing months keep the form's commands waiting about 2 minutes (`IndicesViewModel.cs:246-257`). The one worth fixing first, before S12 gives it a screen, since Requirement 25 asks that manual entry stay available.
  - **A source failure leaves nothing in the error log,** so a retired API, a TLS failure and a changed format cannot be told apart afterwards (`OfficialIndexValueSource.cs:47-71`).
  - **Invalid settings read as an unrecognized answer, with no trace,** and one bad address disables both sources (`OfficialIndexValueSource.cs:39`). The verifier noted this too.
  - **The late-answer check runs after a fetch, not after the worklist query,** so choosing another index meanwhile can show a message about the old one, or send one request nobody reads (`IndicesViewModel.cs:298-301`).
  - **No test sends the BCRA's real `34.24000000000` through `FetchedValueRow`;** only the scratch program did.

**S8 written, 2026-11-07. Risk: item 1 (decides when rent moves), and item 4 for the lock's row wait.**
- 8.1: Second inspection: the branch's tree is identical to `origin/develop` after PR #100, and `AdjustmentSchedule`, `DueAdjustmentQuery`, `AdjustmentWarning` and the grants match the design.
- 8.2: **M5 is `20261107152256_AddClauseWindowOffset`. Applied by Tomás on 2026-11-07**, to the test database and then production, each with only M5 pending. On both, one query confirmed the history row, default 1, the check, the two lock triggers, no clause with an offset other than 1, and `EXECUTE` on `app_replace_clause_indices` for both roles. A second query showed `app_replace_clause_indices` not executable by `anon`, `authenticated` or `service_role`, with `search_path` ending in `pg_temp`; **but `app_clear_must_change_password` is executable by all three and its `search_path` lacks `pg_temp`**, on both databases. Smoke test on the test database: the app opened and the cash desk found and selected the fictitious contract; it proves only that the app still works on the migrated database.
- 8.2: **Two traps, both pinned.** The default of 1 lives only in the migration: on the model, EF would take the CLR 0 as "not set", and a clause on ICL at offset 0 would silently read as 1 (`OffsetZero_IsStoredAsZero`). And EF scaffolded `defaultValue: 0`, which would have moved every clause already there (`AClauseRecordedBeforeThisChange_BehavesAsOffsetOne`).
- 8.2: **Beyond the design:** the lock takes the contract's row before reading, because it reads committed rows only and a direct write could otherwise slip in beside an adjustment still being written. The verifier reproduced that; `AWriteRacingAnAdjustmentBeingConfirmed_WaitsForIt_AndIsRefused` pins it.
- 8.2: **Beyond the design:** the migration revokes the function from Supabase's API roles where they exist, since Supabase's default privileges grant new functions to them and `REVOKE FROM PUBLIC` does not reach those grants. No test here sees it, as those roles exist only on Supabase: **the confirmation query at H.3 does. The same query must also list the earlier `SECURITY DEFINER` functions, which nothing has revoked.** How strongly held: the Supabase default is from memory, not checked; the query settles it.
- 8.3: **Added to the plan: `IContractRegistry.SetClauseAsync`, `ContractClauseViewModel` and `ClauseFormRules`.** The design names the correction and the spec the months on the form, with no task line. No view: S12, task 12.3c.
- 8.3: **The line after a confirmed adjustment is the database's alone,** in the port too, through the error map, so the two cannot disagree. `SchemaConstraintTests.SeedContractWithClause` now saves before an adjustment is added, since one `SaveChanges` does not promise the order and the lock would refuse.
- 8.3: **An offset left blank takes the suggestion, else 1, and the months shown say which.** With several indices the suggestion is the largest any of them needs (weak, until N1).
- 8.6: **Open, needs Tomás: ending before the month's due date leaves that month charged on neither contract.** Ended 5 Oct with due day 10, and a new contract from 1 Nov: October accrues on neither, with no message. That is Requirement 10 working (`AccrualSchedule.cs`), but Decision 39 says "the next 1st" and Test 81 says no period is skipped. Starting the new contract on the 1st of the same month avoids it, except when the end is recorded a month late, where the start is refused (`StartBeforeCurrentMonth`). So Test 81 is proven with the end after the due date, and **not** for this case. The verifier agreed it is not a code blocker and rejected leaving it unwritten.
- 8.8: **RED observed by breaking each rule rather than beforehand:** these tests cannot fail on behavior until the migration exists. 53 mutations in all; every one caught. Five needed a test first: the indices' order, which passed by luck of row order, the direct `INSERT` of a clause, the two row waits, and the temporary tables.
- 8.8: **Full suite: 1972 passed, 0 failed, 0 skipped** (1861 + 111): Domain 664, Presentation 495, Infrastructure 813. Build: 0 warnings. `has-pending-model-changes`: none. Measured during the runs: `TIME_WAIT` peak 5,242, free RAM never below 4.9 GB.
- 8.8: **One failure not explained: `ContractCancellationRegistryTests.AContractAlreadyEnded_IsCancelled_ReplacingItsReason_AndKeepingItsDate` failed once in a full suite run, and passed alone three times and in the next two full runs.** The quiet output kept no message, so the cause is unknown. Its path holds no clause, and `TIME_WAIT` was 5,175 at most in that run, far from the port exhaustion near 15,650. Next time, run with `--logger "console;verbosity=normal"` to keep the message.
- 8.8: **A run was stopped by Claude Code when the machine ran critically low on memory,** before any test could report. Not reproduced: the next runs stayed above 4.9 GB free.
- 8.8: **The compiled `appsettings.json` was overwritten with production's by a build,** though nothing had edited the file: merging PR #100 had rewritten the source file, making it newer. Restored and confirmed pointing at the test database. **Any pull can do this.**
- 8.9: **Independent verifier: one blocker, fixed.** A locked clause form showed the first due date instead of the next one; it now reads the last adjustment. Left as they are: `app_replace_clause_indices` does not refuse an `Ended` contract if called directly, only the port does.
- 8.9: **For S9:** confirming has to take the locks in `LockAsync`'s order and build the window after them, re-reading the clause under the lock, since the clause lock only stops a write after an adjustment and a correction can commit before one (the review's `R4-clause-lock-one-sided`). It also has to allow a `PendingTermination` contract, which is now on the worklist.
- 8.9: **gentle-ai review, in two candidates on a scratch clone,** since the whole slice (28 files, 3,188 lines) hit `lens_context_budget_exceeded`. **Code and M5 (18 files, 2,012 lines; lenses risk and resilience): `review-8612432cd65a1960`, one bounded correction, approved, acknowledged.** **Tests over that code (7 files, 1,143 lines; lens reliability): `review-9b2908403b501756`, approved, acknowledged.** Its four non-blocking findings were taken up: a form's typed offset is never replaced by the suggestion, a new contract keeps its offset through `CreateAsync`, and the tests named after movements assert one exists.
- 8.9: **The correction: `SET search_path = pg_catalog, public` does not exclude the session's temporary schema, it searches it first.** A role could shadow the function's tables with temporary ones, with a trigger running as the function's owner. Now `pg_catalog, public, pg_temp`, pinned by `TheFunction_IgnoresTemporaryTablesNamedLikeItsOwn`, seen failing without it.
- 8.9: **Open, already in production: `app_clear_must_change_password` has that same `search_path`** (`20261025005832_FixClearMustChangePasswordIdentity.cs:25`), and both roles run it. Not fixed here: it is outside this slice, and it deserves its own PR and migration. **Recommended before S9.** The H.3 query's check of the API roles covers it too.
- 8.9: **Fixed in its own PR, migration `20261107213120_HardenClearMustChangePassword`**: `search_path` now ends in `pg_temp`, and `anon`, `authenticated` and `service_role` lose `EXECUTE` where they exist; body, owner and the two roles' grants unchanged. RED observed: as an Empleado with a temporary `app_users`, the function updated the temporary table and left the real flag set. The test databases now get Supabase's API roles and its default privileges on functions (`PostgresFixture`, and every isolated database before it migrates), so the S8 revoke is proven too: before, those roles did not exist here and both revokes were skipped. 3 mutations, all caught; the S8 revoke survived until isolated databases got the default privileges. Full suite: 1987 passed, 0 failed, 0 skipped (Domain 674, Presentation 497, Infrastructure 816); 0 warnings; no pending model changes; `TIME_WAIT` peak 5,290. gentle-ai review (lenses risk, reliability): `review-6c7225d3745a8856`, approved, acknowledged. Two non-blocking findings, for later: **(1)** the fixture's `CREATE ROLE` is not idempotent, so a server that already has those roles would fail every Docker test; harmless today, since each fixture starts its own container. **(2)** `Down` is not exercised by any test. **Still to be applied by Tomás (H.3)**, test database then production.
- 9.1, preview on 2026-11-07: `RentAdjustment.cs:80-112`, `Contract.cs:315-330` (`ConfirmAdjustment` at 322) and `ContractAccount.cs:449-452` still point where S9 expects; **`CanonFor` moved to `AccrualSchedule.cs:220`**, past the 198-226 the task names (the due-day clamp now sits at 190-208). Read from 220 to its end.
- 8.6: **Decided by Tomás on 2026-11-07:** Decision 39 now says the new contract starts on the first 1st the old one did not charge, and the end summary names the month left uncharged (Requirement 10, Test 81). Spec amended, and written: `ContractLifecycleRules.MonthLeftUncharged` names the end date's month when the end falls before its due date, unless a cutover on or after that due date already covers it; the summary leaves it out when that period is already on the ledger, since it then stays charged; the end summary's `UnchargedMonthLine` says it and that a new contract can start on its 1st (to be shown by S12). Test 81 now runs both ends, last month past its due date and this month's 1st before it, with the new contract on that same 1st: no period twice or skipped. RED observed with the rule stubbed (5 domain cases, the new Test 81 case); 4 mutations, all caught. Full suite: 1985 passed, 0 failed, 0 skipped (Domain 674, Presentation 497, Infrastructure 814); 0 warnings; no pending model changes; `TIME_WAIT` peak 5,238. gentle-ai review (lens reliability): `review-c3a841b4ffca624a`, approved, acknowledged. Two non-blocking findings, left for S12, when the line is first shown: **(1)** `UnchargedMonthLine` says a new contract can start on that month's 1st, which `StartBeforeCurrentMonth` refuses when the end is recorded in a later month; drop or condition that sentence. **(2)** Test 81's new case relies on the seeded contract's due day being after the 1st; state it in the test. **Still open:** when the end is recorded a month late, that start is refused (`StartBeforeCurrentMonth`).
