# Spec: gestion-de-alquileres (the missing rental screens)

Single-file specification for the `gestion-de-alquileres` change. No capability is **NEW**. Ten are **MODIFIED**
(see "Capabilities covered").

Vocabulary, glossed once: **Admin** and **Empleado** are the two roles; the **canon** is a contract's total monthly rent; a
**period** is a rent month; a period's **due date** is its contract's due day in that month. The **adjustment due date** is the
1st of the month an adjustment falls due. The **applied-from period** is the first period charged at the new canon. A **late
adjustment** has an applied-from period later than the month it fell due. The **difference** is what the old canon undercharged
in between. A **pre-existing contract** is one signed before the system went live, and it carries a **cutover date**.

The proposal is `openspec/changes/gestion-de-alquileres/proposal.md`, merged in PR #88. Rules already in the living specs are
cited, not restated.

**What this spec does not do.** It states what must be true, observably. Class names, method signatures, SQL, XAML, storage
choices and the exact Spanish wording are design's. Where a parameter is design's, the text says so.

## What to read (Tomás)

| Read | Why |
|---|---|
| **Findings** box, F1 to F4 | F1 and F4 are new, and they change what this spec had to decide. F2 would overcharge tenants if left alone. |
| **Decisions** table, rows 15 to 41 | The calls this spec made. The **weak** and **medium** rows are where you can change my mind. |
| Requirement 16 (the difference) | Decision P4 written as behaviour, with a worked example. |
| **Questions** near the end | Four to raise with Nicolás when he answers. None blocks design. |

**Skip** the Tests section and "Parameters Left to Design". **Skim** the rest; scenarios are there so a test can be written from
each one.

**How complete the audit was.** Every proposal requirement was searched again, in Engram with `type: decision` and by keyword
across the archived specs, `design.md`, `tasks.md` and `archive-report.md`. Archived `design.md` and `tasks.md` files were
searched, not read in full, so a decision recorded in unusual words could still have been missed.

## Decisions

**CONFIRMED BY USER** = Tomás said so. **VERIFIED FACT** = proven against the code, a living spec or a public source. **SPEC CALL**
= this spec had to decide because the proposal did not. Strength: **strong**, **medium**, **weak** (act on it only provisionally).

Rows P1 to P14 are the proposal's Decisions, in its numbering, summarised. The proposal holds their reasons. Rows 15 onwards are
this spec's.

| # | Decision | Source | Strength |
|---|---|---|---|
| P1 | One change, one spec, chained PRs. | Proposal | Medium |
| P2 | Every new screen is for both roles, except creating a pre-existing contract. | `access-control/spec.md:22,29` | Strong |
| P3 | Screens first; opening balances and any importer go in `carga-inicial`. | Proposal | Strong on the order |
| P4 | On a late adjustment the operator chooses whether to charge the difference: default no, amount shown first. | CONFIRMED BY USER (2026-10-22). Nicolás wins if he objects | Strong |
| P5 | A late confirmation does not move the contract's calendar. | `rent-adjustment/spec.md:71`. CONFIRMED BY USER (Tomás, 2026-11-05), on the condition that an agreement between the parties has a way through: Decision 39 | Strong |
| P6 | Old debt is not treated as paid. Final in `carga-inicial`. | Proposal | Strong as advice |
| P7 | Contract data can be corrected until its account has a movement, except the honorarios rate, which stays editable. | Proposal | Medium |
| P8 | Mateo designs from scratch; we program from his mockups. No real data in them. | CONFIRMED BY USER (2026-11-04) | Strong |
| P9 | IPC, RIPTE and ICL are seeded by migration, tolerating existing rows. | `economic-index/spec.md:7,13` | Medium |
| P10 | The honorarios rate is optional at creation, with a warning; the field suggests the owner's last rate. | Team, 2026-09-16 | Medium |
| P11 | Mateo defines the look; other screens follow his base screen through a theme dictionary. | CONFIRMED BY USER (2026-11-04) | Strong |
| P12 | Only an Admin creates a pre-existing contract, with its cutover date in the same transaction, at any time. Having a cutover date is the "predates the system" marker. | CONFIRMED BY USER (2026-11-04). Replaces the placement at `ui-de-cobranza/spec.md:1345`, which was "held medium: no marker" | Medium |
| P13 | Confirmation reminds the operator to notify the tenant; the system sends nothing. | `rent-adjustment/spec.md:188` | Medium |
| P14 | A contract starts on the 1st of a month: the form refuses another day. | Tomás, 2026-10-16. Reopen if Nicolás disagrees | Medium |
| 15 | **A clause states how many months before its due date its index window ends: 0, 1, 2 or 3.** The window keeps its length, the clause's interval, and the offset never changes between adjustments of a clause, so no month is skipped or counted twice. The form suggests the offset from the indices chosen: ICL 0, IPC 2, any clause with RIPTE 3, the last values normally out by the due date. It shows the months that will be read, so the operator can check them against the paper. | SPEC CALL from Finding F1. Tomás's reading on 2026-11-05: the agency uses the last value available. Project pattern: what the paper says, the record carries | **Medium** on the field; **weak** on the suggestions, until Nicolás answers N1 |
| 16 | **The applied-from period is the first period whose due date is after the confirmation date and that has not been charged.** | SPEC CALL. Makes `rent-adjustment/spec.md:188` ("next billable period") testable; an early payment already charged that period (`CollectionPlanner.cs:331-341`) | Strong |
| 17 | **The difference covers every period the contract charges from the adjustment's month up to the applied-from period, whether collected or still unpaid.** Amount: (new canon − previous canon) × number of those periods. Opening-balance months and months on or before the cutover are excluded. | SPEC CALL. Decision P4 names only collected months, and leaving unpaid ones out would reward the tenant who pays late | **Medium** |
| 18 | **The difference carries no recargo.** | SPEC CALL. The agency caused the delay (team, 2026-09-16), and the precedent is an opening-balance month (`cobranza-y-recibos/spec.md:67`, Decision 31) | Medium |
| 19 | **The difference is written when confirmed, attached to the applied-from period, and is paid whole with that period.** It cannot be paid apart. | SPEC CALL following the instalment (`contract-account/spec.md:500-505`) and "Only Total Payments" | Medium |
| 20 | **It prints on its own line on both recibos, and joins the honorarios de administración base.** Label is weak: "Diferencia de ajuste". | SPEC CALL. It is rent, so the owner's money and the fee follow it, extending `cobranza-y-recibos/spec.md:57`, Decision 21. Every stored line prints (`ui-de-cobranza/spec.md:76`, Decision 35) | Medium on the base; **weak** on the label |
| 21 | **A zero or negative difference is never offered.** | SPEC CALL. An index that falls gives no refund here | Medium |
| 22 | **Stand-ins are refused by guidance, not by code.** The index form asks for the published level of that exact period and says so. Typing the "last available" level under a missing period is wrong. | SPEC CALL. Answers `proposal.md` §4.6. Index values are shared, so one stand-in would misprice every contract reading that period; Decision 15 is the correct route when a paper reads an older month | **Strong** on rejecting the assumption |
| 23 | **A level far from its neighbour warns and asks for a second confirmation; it is not refused.** Threshold for design. | SPEC CALL. Team criterion 2026-09-16: an extra zero in an index multiplies a rent | Medium |
| 24 | **For a pre-existing contract, adjustments falling due on or before the cutover date are taken as already in the typed canon.** Its next due date is the first step of its calendar after the cutover. | SPEC CALL from Finding F4. Nothing new is stored, which keeps `rent-adjustment/spec.md:59` | Medium |
| 25 | **`PendingTermination` contracts stay on the worklist; `Ended` ones do not.** The desk's warning follows the same list. | SPEC CALL. They still pay rent. Accepts changing `ui-de-cobranza/design.md:578` (C4) | Medium |
| 26 | **A correction applies only to a contract's latest adjustment.** It keeps the same adjustment due date. Past months are not re-priced. | SPEC CALL. Correcting an older one would invalidate every step built on it | Medium |
| 27 | **A correction reverses an unpaid difference and offers the choice again. A difference already paid is settled by hand**, with the signed Otros Conceptos line (`receipt/spec.md:187`). | SPEC CALL. Nothing refunds automatically | Medium |
| 28 | **DNI and CUIL may both be absent; a person with neither shows a warning.** DNI: 6 to 8 digits. CUIL: 11 digits with a valid check digit. Both are stored as digits only, so dots and dashes cannot defeat uniqueness. | SPEC CALL closing `party-registry/spec.md:47`. `Party.cs:11-17` already allows absence. Older LE and LC numbers can be short | Medium; **weak** on the 6-digit floor |
| 29 | **Name, DNI and CUIL are corrected in place, with a confirmation; issued recibos keep what they printed.** No history of the old values. | SPEC CALL. Recibos store names as text (`ReciboConfiguration.cs:41`). Decision P7's logic: documents keep their figures | Medium |
| 30 | **A unit under an `Active` contract is refused for a new contract. Under `PendingTermination` it is allowed after a warning naming the move-out date.** | SPEC CALL. Two active contracts on one unit would charge two tenants for it. A successor can be signed while the outgoing tenant is leaving | Medium |
| 31 | **A start date before the current month needs a cutover date, so only an Admin can load it.** A start date in the current month is allowed for both, with a warning when its first due date has already passed. | SPEC CALL closing `proposal.md` §4.6 | Medium |
| 32 | **A clause can be attached or corrected until the contract's first confirmed adjustment**, even after movements exist. | SPEC CALL. Nothing on the ledger depends on a clause until then | Medium |
| 33 | **Codeudores can be added, replaced or removed at any time. Lessors and the tenant only until the first movement.** Removing leaves a trace; neither role gains DELETE. | SPEC CALL. The real lease lets the lessor demand a replacement codeudor (cláusula DÉCIMA CUARTA, Engram #4), and codeudores never reach a recibo | Medium |
| 34 | **The database refuses a second tenant and an Empleado's cutover date on insert. "At least one tenant and one lessor" is enforced by the creation port only.** | SPEC CALL. See Finding F6, which narrows `proposal.md` §11 | Medium |
| 35 | **A contract loaded by mistake can be cancelled at any time, with or without movements, with a written reason.** Nothing is deleted. It ends with the reason "Cargado por error", everything still owed is reversed with `Correction` movements so its balance is zero, nothing accrues again, and its unit is freed. Both roles. | CONFIRMED BY USER (Tomás, 2026-11-05): a tenant must never show a debt for a contract that is not theirs, however long it took to notice | **Strong** on cancelling at any time; **medium** on both roles: an Empleado can erase real debt, so the summary shows the amount and the name, and who did it is recorded |
| 36 | **Money already collected on it is handled by the recibo, not by the cancellation.** A payment that can still be voided must be voided first. One past the voiding window (30 days, `cobranza-y-recibos` D15) stays as a fact with its recibos; the cancellation reverses only what is unpaid, and says the money is returned by hand. | CONFIRMED BY USER (Tomás, 2026-11-05). A recibo was handed to someone, so it is not a typing error any more | Strong |
| 37 | **A notice and an end recorded by mistake can be undone.** Undoing a notice returns the contract to `Active`. Undoing an end is refused when another contract now covers its unit, or when it was cancelled by Decision 35. Months in between accrue as usual. | SPEC CALL following Decision 35. Without it, one wrong click on a real contract would stop charging its rent for good | Medium |
| 38 | **A correction can also be opened from the contract's history, without an index having changed.** Same figures are allowed, so a mistaken yes on the difference can be taken back while unpaid. | SPEC CALL following Decision 35 | Medium |
| 39 | **An agreement between the parties that departs from the contract adds no new function.** Charging part of a difference, or paying it in instalments: answer no, and type the agreed amount on Otros Conceptos on each recibo. Changing the rent or the calendar: that is a new deal, so the contract ends with the reason "MutualAgreement" and a new one starts on the next 1st, with its old debt still collectable. | SPEC CALL. Tomás asked on 2026-11-05 how they would solve an agreement. Otros Conceptos is signed and already typed (`receipt/spec.md:182-189`), but it stays out of the honorarios base and the ledger, so a promise of later recibos is remembered by the agency, not the system | **Medium**; reopen if these turn out to be frequent |
| 40 | **A "bring values" button fetches ICL and IPC; RIPTE stays typed.** It proposes the months the worklist is missing, and the operator may name another month. It shows what it found, and nothing is stored until the operator confirms. A failure shows its own message and manual entry keeps working. It never runs on its own, and never overwrites a stored level. | Tomás asked for it, 2026-11-05. Finding F11. Replaces the "fetching automatically" row of `proposal.md` §2 for these two indices. `economic-index/spec.md:39` already says retrieval must fill the same table | **Medium** |
| 41 | **ICL is stored as its value on the 1st of each month. No free choice of day.** | SPEC CALL. An adjustment reads two values, and adjustments fall on the 1st (`rent-adjustment/spec.md:59`), as do contract starts (Decision P14). The table is shared by every contract, and one day's gap moves a rent: on 500,000, 1 January against 1 July gives 582,511, and 2 January gives 581,917 | **Medium**, until N2 |
| 42 | **Every change to a stored index level leaves a permanent record: who, when, the previous level and the new one.** The database writes it, so no correction can skip it, and nobody can change or delete it. No written reason is asked for. | Asked for by Tomás on 2026-11-06. A wrong level misprices every contract reading that period, and overwriting it left no way to tell what happened | Medium |

### Findings (the code or the world differs from what was assumed)

| # | Finding | Consequence |
|---|---|---|
| F1 | **The index window cannot be filled on time for IPC or RIPTE.** It ends the month before the due date, hard-coded (`DueAdjustmentQuery.cs:71-72`). INDEC's 2026 releases seen fell on [10 Feb](https://www.noticiasnqn.com.ar/noticias/2026/02/08/327846-inflacion-de-enero-2026-cuando-publica-el-indec-el-primer-dato-del-ano-tras-la-salida-de-lavagna), and on [14 Apr, 14 May, 11 Jun and 14 Jul](https://www.iproup.com/finanzas/66655-el-indec-confirma-el-ipc-de-marzo-a-cuanto-llego-la-inflacion), and RIPTE appears [up to 45 days after its period](https://www.argentina.gob.ar/node/201033). So an adjustment due 1 July needs June's values, and June's IPC came out on 14 July, after a due day of 10. Nearly every such adjustment would be late by construction, and the agency would be asked about a difference each time. [La Nación's October 2024 example](https://lanacion.com.ar/propiedades/casas-y-departamentos/alquileres-cuanto-aumentaran-los-contratos-que-se-actualizan-en-octubre-nid25092024) ends in August, the last month available when it was written: thin evidence of practice, which is why this goes to Nicolás. | Decision 15. Which months the agency reads is its own practice: question N1. This is also the real reason behind the unconfirmed "last available value" assumption. **It does not bite on ICL**, the owner's usual index (`respuestas-del-dueno.md:146`): it is [daily and on the BCRA's site](https://abogados.com.ar/nueva-ley-de-alquileres-el-bcra-aprobo-el-indice-para-contratos-de-locacion/26743), and [compares the adjustment date with the start or last adjustment](https://indicadores.ar/que-es-el-icl), so it needs offset 0, which today's code cannot do. Secondary sources; which day the agency takes is N2. |
| F2 | **A late confirmation would overcharge unwritten months today.** `AccrualSchedule.cs:208-211` prices any period by comparing the adjustment's effective date with the period's due date, and `DueAdjustmentQuery.cs:87` sets that date to the adjustment due date. July to September not yet on the ledger would be charged at the new canon. Nothing exposes this today, because nothing confirms in production. | Requirement 15 prices by the applied-from period. |
| F3 | **A new period charge would be misread as rent and would earn recargo.** The recargo base and the payable rent are the period's net of all movements, with no kind filter (`ContractAccount.cs:249,259`, `CollectionPlanner.cs:415-429`). | Requirement 16 pins both. Design must exclude the new kind. |
| F4 | **A pre-existing contract with a clause would demand its whole history of adjustments.** `AdjustmentSchedule.cs:20-23` starts from the start date when no adjustment exists, and ignores the cutover. A two-year-old semiannual contract would appear due for an adjustment from 18 months ago, priced from a canon that already includes it. | Decision 24. |
| F5 | **`proposal.md` §4.3 overstates when an account writes accruals.** Today only a desk commit persists them (`CollectionDesk.cs:90-97`, a preview saves nothing at `:51`); `IMoraWorklistQuery` and `IAccountMaterialiser` have no consumer yet. | Decision P12 stands: the mora screen is the next change and will write them on read. |
| F6 | **No database rule requires or limits a contract's parties.** Only the role CHECK exists (`InitialSchema.cs:110`), and a second tenant is refused in the domain only (`Contract.cs:347`). Requiring a minimum would mean a deferred trigger, and every fixture that inserts a contract would change, against a gap reachable only by raw SQL, since both roles lack DELETE. | Decision 34. `proposal.md` §11 reads as if the database enforced the minimum too; this spec narrows it. **Easy to reverse in design if you prefer it.** |
| F7 | **No row can be deleted, and today no mistake on a contract can be undone either.** Both roles lack DELETE, by design; the ledger is append-only; `End` and `GiveNotice` only go forward; `EndReason` has four values, none fitting (`EndReason.cs:6-9`, `ContractConfiguration.cs:21-23`). A mistake is undone by appending, as a voided recibo already is. A person or unit created by mistake stays listed, harmless, and can be corrected and reused. | Decisions 33, 35, 36, 37, 38; Requirements 23 and 24. |
| F8 | **Correcting an index overwrites it, and adjustments hold a copy.** `IndexValue.cs:32-44`; `RentAdjustmentIndexValue.cs:6-11` has no foreign key. | Requirement 18 finds affected adjustments by index and period. |
| F9 | **Archived rules mention mid-month starts** (`cobranza-y-recibos/spec.md:79`, Decision 43; `rent-adjustment/spec.md:63`). The domain keeps tolerating them. | Decision P14 lives in the creation port, and does not change those rules. |
| F10 | **A company cannot be represented.** `Party.cs:4` is a natural person, with no CUIT. The owner never mentioned companies. | Out of scope unless N3 says otherwise. |
| F11 | **Official sources exist, with different delays; queried 2026-11-05.** **ICL:** BCRA API v4.0, variable 40, daily, current to that day. **IPC:** `apis.datos.gob.ar`, series `148.3_INIVELNAL_DICI_M_26`; its July and August figures give 2.11% and 1.66%, matching the published 2.1% and 1.7%, but it ended in August while INDEC had published September on 13 October, so it runs at least three weeks behind. **RIPTE:** series `158.1_REPTE_0_0_5` returned no data at all, though its metadata claims July, and the official page already shows August. | Decision 40. The IPC button will often answer "not there yet", and the operator types it from INDEC. A source can disappear: BCRA retired v3.0. The API's schedule is not documented, so the three weeks is one observation. |

## Table of Contents

- Capabilities covered
- Capability: party-registry (MODIFIED)
  - Requirement 1: Persons Are Listed and Found
  - Requirement 2: A Person Is Created With Checked Identifiers
  - Requirement 3: A Person Is Corrected
- Capability: unit-registry (MODIFIED)
  - Requirement 4: Units Are Listed With Derived Availability
  - Requirement 5: A Unit Is Created and Edited
- Capability: lease-contract (MODIFIED)
  - Requirement 6: Contracts Are Listed and Read
  - Requirement 7: A Contract and Its Account Are Created in One Operation
  - Requirement 8: A Pre-Existing Contract Is Created by an Admin
  - Requirement 9: A Contract Is Corrected Until Its First Movement
  - Requirement 10: Notice and End Are Recorded
  - Requirement 23: A Contract Loaded by Mistake Is Cancelled
  - Requirement 24: A Mistaken Notice or End Is Undone
- Capability: economic-index (MODIFIED)
  - Requirement 11: The Catalog Is Seeded
  - Requirement 12: Monthly Values Are Typed and Corrected
  - Requirement 25: ICL and IPC Can Be Brought From Their Official Source
- Capability: rent-adjustment (MODIFIED)
  - Requirement 13: A Clause Carries Its Window Offset and Can Be Corrected
  - Requirement 14: The Worklist Leads to a Complete Summary
  - Requirement 15: A Late Confirmation Keeps Two Dates
  - Requirement 16: The Operator Chooses Whether to Charge the Difference
  - Requirement 17: A Pre-Existing Contract's Calendar Starts After Its Cutover
  - Requirement 18: A Correction Is Reached From a Corrected Index or From the History
- Capabilities: contract-account, receipt, cash-desk (MODIFIED)
  - Requirement 19: The Ledger Carries the Difference
  - Requirement 20: The Recibos and the Desk Show the Difference
- Capabilities: access-control, desktop-shell (MODIFIED)
  - Requirement 21: The Database Refuses an Empleado's Cutover Date on Insert
  - Requirement 22: Five New Screens, for Both Roles
- Out of Scope
- Parameters Left to Design
- Questions
- Preconditions
- Tests

## Capabilities covered

| Capability | Status | What changes |
|---|---|---|
| `party-registry` | MODIFIED | Listing, search, creation and correction; `spec.md:47` is resolved. |
| `unit-registry` | MODIFIED | Availability (`spec.md:41`) gets an implementation; listing and editing. |
| `lease-contract` | MODIFIED | Creation as one transaction with required parties; start on the 1st; correction, notice and end; cancelling a contract loaded by mistake, a fifth end reason; undoing a notice or an end. |
| `economic-index` | MODIFIED | Seeded catalog; typing and correcting values; a button that brings ICL and IPC from their official source. |
| `rent-adjustment` | MODIFIED | **"A Late Adjustment Is Not Charged Retroactively" (`spec.md:186`) is amended**: nothing is charged unless the operator chooses. The window offset, the applied-from period, the worklist's contract states and the corrections flow are added. |
| `contract-account` | MODIFIED | **Two requirements amended**: `spec.md:192,202` (a late adjustment generates no new movement) and `spec.md:453` (what the balance covers). |
| `receipt` | MODIFIED | A new line kind. The scenario at `spec.md:250` stops being true: the contract gains an operation changing its rate. |
| `cash-desk` | MODIFIED | The difference appears in the preview. |
| `access-control` | MODIFIED | The named limit at `spec.md:177-180` is closed. |
| `desktop-shell` | MODIFIED | Five new screens. |

---

## Capability: party-registry (MODIFIED)

### Requirement 1: Persons Are Listed and Found

Both roles MUST be able to list persons and search them by name, DNI or CUIL. A name search MUST ignore case and accents and MUST
match part of a name. A search by DNI or CUIL MUST ignore dots and dashes. The contract form MUST use this same search, so an
existing person is reused rather than duplicated (`party-registry/spec.md:13`). Each listed person MUST show the roles they hold on contracts
in force, `Active` or `PendingTermination`, and the list MUST be filterable by role. A person removed from a contract no longer holds the role there.

#### Scenario: Accents and case do not hide a person

- GIVEN a person named "Núñez, María"
- WHEN an operator searches "nunez"
- THEN that person MUST be found

#### Scenario: A formatted DNI finds its person

- GIVEN a person with DNI 30123456
- WHEN an operator searches "30.123.456"
- THEN that person MUST be found

### Requirement 2: A Person Is Created With Checked Identifiers

Creating a person MUST require first and last name, and MUST check identifiers per Decision 28. A duplicate DNI or CUIL MUST be
refused with a message naming the existing person, and MUST offer to use that person. Refusals MUST use their own operator text.

#### Scenario: A duplicate DNI names who holds it

- GIVEN "Juan Pérez" holds DNI 30123456
- WHEN a person is created with DNI "30.123.456"
- THEN it MUST be refused, naming Juan Pérez
- AND no person MUST be created

#### Scenario: A CUIL with a wrong check digit is refused

- GIVEN a CUIL whose last digit does not match its check digit
- WHEN it is entered
- THEN it MUST be refused with its own message

#### Scenario: Neither identifier warns and is accepted

- GIVEN a foreign tenant with no DNI and no CUIL
- WHEN the person is created
- THEN a warning MUST say that duplicates of this person cannot be detected
- AND the person MUST be created after the operator accepts

### Requirement 3: A Person Is Corrected

Both roles MUST be able to correct name, DNI, CUIL and contact data. Correcting name, DNI or CUIL MUST show old and new values and
ask for confirmation. Identifier checks of Requirement 2 apply. An issued recibo MUST keep the name it printed (Decision 29).

#### Scenario: A corrected name leaves an issued recibo alone

- GIVEN a tenant with an issued recibo printed as "Perez Juan"
- WHEN the name is corrected to "Pérez, Juan Carlos"
- THEN the contract MUST show the new name
- AND the issued recibo MUST still read "Perez Juan"

---

## Capability: unit-registry (MODIFIED)

### Requirement 4: Units Are Listed With Derived Availability

Both roles MUST be able to list units with address, type and availability. Availability MUST be computed on read as
`unit-registry/spec.md:43` defines it, and MUST NOT be stored.

#### Scenario: Availability follows the contract

- GIVEN a unit covered by an `Active` contract
- WHEN that contract is ended and the list is read again
- THEN the unit MUST show as available
- AND no unit row MUST have been written

### Requirement 5: A Unit Is Created and Edited

Both roles MUST be able to create a property or a parking space with the required address (`Address.cs:26-30`) and to correct its
address. Its type MUST NOT change after creation. An identical existing address MUST warn and MUST NOT refuse.

#### Scenario: An identical address warns

- GIVEN a unit at Alem 1258, floor 2, apartment B
- WHEN another is entered with the same address
- THEN a warning MUST name the existing unit
- AND the operator MUST be able to proceed

---

## Capability: lease-contract (MODIFIED)

### Requirement 6: Contracts Are Listed and Read

Both roles MUST be able to list contracts by tenant, lessor, unit and state, and to open a detail showing its dates, state, parties,
units and shares, canon, due day, honorarios rate, fee plan, clause and adjustment history. A contract past its nominal end date
MUST read as `Active` (`lease-contract/spec.md:88`), and the list MUST flag it as past its end date.

#### Scenario: A contract past its end is flagged, not ended

- GIVEN an `Active` contract whose nominal end date has passed
- WHEN the list is read
- THEN it MUST show `Active` and a past-end flag

### Requirement 7: A Contract and Its Account Are Created in One Operation

Creating a contract MUST write the contract, its parties, its unit shares, its optional clause and its empty account in one
transaction. Any refusal or failure MUST leave nothing written. The form MUST refuse:

- no tenant, or no lessor (Decision 34), or no unit;
- a start date not on the 1st (Decision P14), or one before the current month with no cutover date (Decision 31);
- a nominal end date before the start date;
- a canon that is not a positive whole peso;
- shares that do not total 100%;
- a unit already under an `Active` contract (Decision 30).

An amount typed for a unit's share MUST be converted to a percentage, with the residue rule at `lease-contract/spec.md:238`. The due
day MUST default to 10. The word "garante" MUST NOT appear.

The honorarios rate MUST be optional. Choosing a lessor MUST suggest the rate of that person's most recent contract carrying one, if
any. Left empty, a warning MUST say the desk will refuse collection until it is entered (`AccountDirectory.cs:83`), and the operator
MUST be able to proceed. Empty and 0% MUST remain different.

#### Scenario: A complete contract reaches the desk

- GIVEN an Empleado, a tenant, a lessor, a unit and a rate of 8%
- WHEN a contract is created with a start date of next month's 1st
- THEN it MUST be `Active`, with its parties, a 100% share and an empty account
- AND it MUST be findable at the cash desk

#### Scenario: Missing parties write nothing

- GIVEN a form with a lessor and no tenant
- WHEN it is submitted
- THEN it MUST be refused naming the tenant
- AND no contract, party, share or account row MUST exist

#### Scenario: A failed step rolls back everything

- GIVEN a contract whose account creation fails
- WHEN the operation runs
- THEN no contract row MUST remain

#### Scenario: The second tenant is refused by the database

- GIVEN a contract with a tenant
- WHEN a raw SQL insert adds a second tenant, as either role
- THEN PostgreSQL MUST refuse it

#### Scenario: A start date on the 15th is refused

- GIVEN a start date of the 15th
- WHEN the form is submitted
- THEN it MUST be refused, saying a contract takes effect on the 1st

#### Scenario: A contract signed a few days into its month is accepted

- GIVEN today is 5 November and a contract starting 1 November with due day 10
- WHEN an Empleado creates it
- THEN it MUST be accepted, with no warning

#### Scenario: A first due date already passed warns

- GIVEN today is 15 November and a contract starting 1 November with due day 10
- WHEN it is created
- THEN a warning MUST say November is already overdue
- AND the operator MUST be able to proceed

#### Scenario: A unit under an active contract is refused

- GIVEN a unit under an `Active` contract
- WHEN a new contract names it
- THEN it MUST be refused, naming the current tenant

#### Scenario: A unit being vacated warns

- GIVEN a unit under a `PendingTermination` contract with a move-out date
- WHEN a new contract names it
- THEN a warning MUST show that date
- AND the operator MUST be able to proceed

#### Scenario: The rate is suggested from the owner's last contract

- GIVEN a lessor whose last contract carries 6%
- WHEN that lessor is chosen on a new contract
- THEN 6% MUST be suggested and MUST remain editable

### Requirement 8: A Pre-Existing Contract Is Created by an Admin

The cutover date MUST be available on the form to an Admin only, and MUST be written in the same transaction as the contract. Its
bounds are those at `contract-account/spec.md:557`. An Empleado entering a start date before the current month MUST be stopped, with a
message saying an Admin has to load it. The account of a contract created this way MUST NOT hold a movement for any period due on or
before its cutover date, however it is read (`contract-account/spec.md:564`).

#### Scenario: An Admin loads a contract signed two years ago

- GIVEN an Admin, a start date two years back and a cutover date of today
- WHEN the contract is created and its account read
- THEN no accrual MUST exist for any period due on or before today

#### Scenario: An Empleado is stopped by the form

- GIVEN an Empleado and a start date two months back
- WHEN the form is submitted
- THEN it MUST be refused, saying an Admin has to load it

### Requirement 9: A Contract Is Corrected Until Its First Movement

Until its account holds a movement, a contract's dates, canon, due day, fee plan, shares, tenant and lessors MUST be correctable, and a
unit MUST be replaceable, under the same checks as creation. Adding a unit stays out of scope (`proposal.md` §2). After that, each of those MUST be refused
naming why, and saying that a contract wrong in those data is cancelled and created again (Requirement 23). The honorarios rate MUST stay
editable at any time, with a value from 0 to 100; entering one MUST be possible when none exists, and it MUST NOT alter an issued
recibo (`receipt/spec.md:229`). Decisions 32 and 33 govern the clause and the codeudores.

Correcting MUST go through a replacing operation, since neither role can delete a row (Finding F7).

#### Scenario: A missing rate is entered after movements exist

- GIVEN a contract with no rate and a movement on its account
- WHEN a rate of 8% is entered
- THEN it MUST be stored, and the desk MUST allow collection

#### Scenario: A canon cannot change after a movement

- GIVEN a contract with a movement on its account
- WHEN its canon is corrected through this form
- THEN it MUST be refused, and the canon MUST be unchanged

#### Scenario: A correction before any movement re-reads as the new figure

- GIVEN a contract with no movement and a canon typed as 50,000
- WHEN it is corrected to 500,000
- THEN its first period MUST accrue 500,000

#### Scenario: A codeudor is replaced after movements exist

- GIVEN a contract with movements and a codeudor
- WHEN that codeudor is replaced by another person
- THEN the contract MUST name the new codeudor

### Requirement 10: Notice and End Are Recorded

Both roles MUST be able to record notice, with a notice date not after today and a move-out date not before it, moving an `Active`
contract to `PendingTermination`. Both MUST be able to end an `Active` or `PendingTermination` contract with a reason and an end date
not after today and not before its start. The reason "Cargado por error" MUST NOT be offered here: only Requirement 23 uses it. A summary
MUST precede each, and ending MUST show the amount owed. **Debt MUST NOT block
ending** (`respuestas-del-dueno.md:292`). A mistaken one is undone by Requirement 24.

Periods with a due date after the end date MUST NOT accrue (`AccrualSchedule.cs:31`). A period already written stays on the ledger,
and the summary MUST say so when an end date falls before one.

#### Scenario: A contract with debt can be ended

- GIVEN a contract owing 500,000
- WHEN it is ended with a reason
- THEN the summary MUST show 500,000 owed
- AND it MUST end, and the debt MUST still be collectable

#### Scenario: Ending with no reason is refused

- GIVEN an `Active` contract
- WHEN an end is submitted with no reason
- THEN it MUST be refused

#### Scenario: A move-out before notice is refused

- GIVEN a notice date of 10 October
- WHEN a move-out date of 1 October is entered
- THEN it MUST be refused

### Requirement 23: A Contract Loaded by Mistake Is Cancelled

Both roles MUST be able to cancel a contract at any time, however many days have passed, whether or not its account holds movements, and
even when it has already ended under another reason, which the cancellation replaces (Decision 35). Cancelling MUST require a written reason, where only spaces count as empty and no minimum length applies, and MUST be preceded by a summary naming the tenant, the units and the exact amount
that stops being owed. It MUST record who did it and when.

One transaction MUST:

- end the contract with the reason "Cargado por error", keeping the written reason;
- append a `Correction` naming each movement still owed, leaving the ledger balance at zero, with no recargo projected;
- leave every earlier movement unchanged, since nothing is deleted.

Afterward the contract MUST NOT accrue any period, **including periods before the cancellation that were never written**. Its unit MUST read
as available. It MUST NOT appear on the adjustment worklist or be offered for collection. It MUST stay readable, marked as cancelled, with its
reason. Persons and units it used MUST remain. Cancelling MUST be final (Decision 37), and the right data is loaded as a new contract.

A payment that can still be voided MUST block the cancellation, with a message naming the recibos to void first and, when only an Admin can
void them, saying so. A payment past the voiding
window MUST NOT block it: it stays with its recibos, and the summary MUST say that money is returned by hand (Decision 36).

#### Scenario: A contract with unpaid months is cancelled

- GIVEN a contract loaded for the wrong tenant, owing two months, nothing paid, 40 days later
- WHEN it is cancelled with a reason
- THEN its balance MUST be zero and the tenant MUST owe nothing
- AND every accrual already on the ledger MUST still be there, each with its `Correction`
- AND a month not yet written MUST stay unwritten, while the summary still counts it in the amount that stops being owed

#### Scenario: A contract already ended can still be cancelled

- GIVEN a contract ended as "Expiry", owing one month, later found to be a mistake
- WHEN it is cancelled with a reason
- THEN its reason MUST become "Cargado por error", and its balance MUST be zero

#### Scenario: Nothing accrues afterward

- GIVEN a cancelled contract with months never written
- WHEN its account is read a month later
- THEN no movement MUST be written

#### Scenario: The unit is freed

- GIVEN a unit under the cancelled contract
- WHEN a new contract names it
- THEN it MUST be accepted

#### Scenario: No reason, no cancellation

- GIVEN a contract with movements
- WHEN it is cancelled with an empty reason, or one made only of spaces
- THEN it MUST be refused, and nothing MUST be written

#### Scenario: A voidable payment blocks it

- GIVEN a contract with a payment made 3 days ago
- WHEN it is cancelled
- THEN it MUST be refused, naming the recibos to void

#### Scenario: A payment past the voiding window stays

- GIVEN a contract with a payment made 40 days ago and one unpaid month
- WHEN it is cancelled
- THEN the unpaid month MUST be reversed
- AND the payment and its recibos MUST be unchanged
- AND the summary MUST say the money is returned by hand

### Requirement 24: A Mistaken Notice or End Is Undone

Both roles MUST be able to undo a notice, returning the contract to `Active` with both notice dates empty, and undo an end, returning it to
the state it would otherwise hold, with its end date and reason empty. Each MUST ask for a written reason, kept with who did it and when, and
MUST be preceded by a summary.

Undoing an end MUST be refused when another `Active` or `PendingTermination` contract now covers one of its units, naming that contract, and
when it was cancelled by Requirement 23. Periods falling between the end and the undo MUST accrue as usual, with the summary naming them.

#### Scenario: A notice on the wrong contract is undone

- GIVEN a `PendingTermination` contract whose notice was a mistake
- WHEN it is undone with a reason
- THEN it MUST be `Active`, and both dates empty

#### Scenario: A mistaken end resumes charging

- GIVEN a contract ended by mistake on 5 October, undone on 20 November, due day 10
- WHEN its account is read
- THEN October and November MUST accrue

#### Scenario: A unit already let again blocks the undo

- GIVEN an ended contract whose unit now has another `Active` contract
- WHEN the end is undone
- THEN it MUST be refused, naming that contract

---

## Capability: economic-index (MODIFIED)

### Requirement 11: The Catalog Is Seeded

A migration MUST ensure IPC, RIPTE and ICL exist, and MUST NOT fail or duplicate when one already exists under that name, as on the
test database.

#### Scenario: A fresh database can record a clause

- GIVEN a database with all migrations applied
- WHEN the index list is read
- THEN IPC, RIPTE and ICL MUST exist

#### Scenario: An existing row survives

- GIVEN a database already holding "ICL"
- WHEN the migration runs
- THEN it MUST succeed, with exactly one "ICL" whose values are untouched

### Requirement 12: Monthly Values Are Typed and Corrected

Both roles MUST be able to type a published level for an index and period, and correct one. The form MUST state each index's scale
and source, MUST say the level is the one published for exactly that period (Decision 22), and MUST accept Argentine number format
(`1.915.878,76`). It MUST refuse a level that is not positive, a period in a future month and a duplicate of index and period, the
last offering to correct the existing value. A level far from the nearest earlier value MUST warn, and MUST need a second confirmation
(Decision 23).

Correcting MUST show the old and new level, and MUST list the confirmed adjustments that used it (Requirement 18).

Every change to a stored level MUST leave a record of who made it, when, the previous level and the new level (Decision 42). The
database MUST write that record itself, and nobody MUST be able to change or delete one. The form MUST show the corrections of the
value being corrected.

#### Scenario: RIPTE takes a peso amount

- GIVEN RIPTE for June 2026
- WHEN "1.915.878,76" is typed
- THEN the stored level MUST be 1915878.76

#### Scenario: An extra zero is caught

- GIVEN IPC's previous level is 9,000
- WHEN 90,000 is typed for the next period
- THEN a warning MUST appear
- AND nothing MUST be stored until the operator confirms again

#### Scenario: A correction leaves its record

- GIVEN August's IPC stored at 9,440
- WHEN an Empleado corrects it to 9,400
- THEN a record MUST name that Empleado, the moment, 9,440 and 9,400
- AND neither role MUST be able to change or delete that record

#### Scenario: Saving the same level records nothing

- GIVEN August's IPC stored at 9,440
- WHEN it is saved again at 9,440
- THEN no record MUST be written

#### Scenario: A typed value reaches the worklist

- GIVEN a contract due and waiting for August's IPC
- WHEN August's IPC is typed and the worklist read again
- THEN the contract MUST no longer name it as missing

### Requirement 25: ICL and IPC Can Be Brought From Their Official Source

Both roles MUST be able to press a button that asks the official source of ICL or IPC (Finding F11) for the values the worklist is missing, or for
a month the operator names. For ICL, the value MUST be that of the 1st of the month (Decision 41). The screen MUST show each value found and
its month, and MUST store nothing until the operator confirms, under the checks of Requirement 12.

A stored level MUST NOT be overwritten. If the source differs from it, the screen MUST show both and point to the correction of Requirement 12.

Each of these MUST show its own message, and leave manual entry available: no connection, the source failing or changing its format, and the
month not being published there yet. The last one MUST say the value may already exist at INDEC, and MUST NOT read as an error. A slow source MUST
NOT freeze the application. Nothing MUST be requested except on that button, and the request MUST carry no client data.

RIPTE MUST stay typed. A source's figure MUST NOT be trusted beyond its format: a response with a non-positive level or the wrong month MUST be
refused as a source failure.

#### Scenario: ICL is brought and confirmed

- GIVEN a contract due 1 July, on ICL at offset 0, with no value for July
- WHEN the button is pressed
- THEN the source's value for 1 July MUST be shown
- AND it MUST be stored only after the operator confirms

#### Scenario: The source is down

- GIVEN no connection to the source
- WHEN the button is pressed
- THEN a message MUST say it could not be reached and that the value can be typed
- AND the application MUST keep working

#### Scenario: The month is not there yet

- GIVEN IPC of September, which the source does not hold yet
- WHEN the button is pressed
- THEN a message MUST say so, and that it can be typed from INDEC

#### Scenario: A stored level is not overwritten

- GIVEN a stored level that differs from the source's
- WHEN the button is pressed
- THEN both MUST be shown, and the stored one MUST be unchanged

---

## Capability: rent-adjustment (MODIFIED)

### Requirement 13: A Clause Carries Its Window Offset and Can Be Corrected

A clause MUST hold, besides what `rent-adjustment/spec.md:13` lists, the number of months before the adjustment due date at which
its window ends (Decision 15). For due date D, interval N and offset K, the window MUST read from the month D − K − N to the month
D − K. A clause recorded before this change MUST behave with K = 1. The form MUST show the months the next due date will read.

Decision 32 governs when it can change. A correction MUST take effect on the next proposal, and MUST NOT touch a confirmed adjustment.

#### Scenario: Offset 1 reads as today

- GIVEN due date 1 July, interval 6 and offset 1
- WHEN the proposal is computed
- THEN it MUST read December and June

#### Scenario: Offset 0 reads up to the due month, as ICL needs

- GIVEN due date 1 July, interval 6 and offset 0
- WHEN the proposal is computed
- THEN it MUST read January and July

#### Scenario: Offset 2 reads two months back

- GIVEN due date 1 July, interval 6 and offset 2
- WHEN the proposal is computed
- THEN it MUST read November and May
- AND missing values MUST be named for those periods

#### Scenario: A clause cannot change after a confirmed adjustment

- GIVEN a contract with a confirmed adjustment
- WHEN its clause is corrected
- THEN it MUST be refused

### Requirement 14: The Worklist Leads to a Complete Summary

The worklist MUST list every `Active` or `PendingTermination` contract with a due adjustment (Decision 25), naming the missing index
and period when one is missing. The summary MUST show the previous canon, each index's base and end level with their periods, the
coefficient, the new canon, the adjustment due date, the applied-from period and, when one is due, the difference.

A contract two intervals behind MUST show its oldest adjustment; the next MUST appear once that is confirmed, priced from the new canon.

Confirmation MUST write the adjustment and set the canon in one transaction, and MUST record who confirmed. A second confirmation of
the same adjustment, including a concurrent one, MUST be refused with its own message and MUST change nothing. After confirming, the
screen MUST show the new figure and the first period it applies to, and MUST remind the operator to notify the tenant. Nothing is sent
(Decision P13).

#### Scenario: A summary carries its index values

- GIVEN a due adjustment averaging IPC and RIPTE
- WHEN it is opened
- THEN both indices' base and end levels and periods MUST be shown before confirming

#### Scenario: A contract under notice stays on the worklist

- GIVEN a `PendingTermination` contract whose adjustment is due
- WHEN the worklist is read
- THEN it MUST appear
- AND a contract that has `Ended` MUST NOT

#### Scenario: Two operators confirm at once

- GIVEN two operators with the same summary open
- WHEN both confirm
- THEN one MUST succeed
- AND the other MUST be refused, and the canon MUST reflect one adjustment only

#### Scenario: Two intervals behind, in order

- GIVEN canon 100,000, adjustments due January and July, both unconfirmed
- WHEN January is confirmed at 120,000
- THEN July MUST appear, with 120,000 as its previous canon

### Requirement 15: A Late Confirmation Keeps Two Dates

**Amends `rent-adjustment/spec.md:186`.** Each confirmed adjustment MUST record its adjustment due date and its applied-from period
(Decision 16). The due date alone MUST drive the next due date (Decision P5, `spec.md:59`). The applied-from period alone MUST drive
pricing: a period before it MUST be priced at the previous canon, **whether or not it is on the ledger yet** (Finding F2), and a period
from it onward at the new canon. When two adjustments share an applied-from period, the last confirmed MUST win.

A correction MUST behave the same way.

#### Scenario: Confirmed before the due day applies at once

- GIVEN due date 1 July, due day 10, and July not charged
- WHEN it is confirmed on 5 July
- THEN July MUST be its applied-from period, and it MUST NOT be late

#### Scenario: Confirmed on the due day applies next month

- GIVEN due date 1 July, due day 10, and July not charged
- WHEN it is confirmed on 10 July
- THEN August MUST be its applied-from period, because the tenant may already be paying that day

#### Scenario: Confirmed after the due day applies next month

- GIVEN due date 1 July and due day 10
- WHEN it is confirmed on 12 July
- THEN August MUST be its applied-from period

#### Scenario: A period paid early pushes it forward

- GIVEN due date 1 July, due day 10, and July paid early on 3 July
- WHEN it is confirmed on 5 July
- THEN August MUST be its applied-from period

#### Scenario: Unwritten months are not re-priced

- GIVEN canon 500,000, due date 1 July, and no period written since June
- WHEN it is confirmed at 575,000 on 12 October and the account is then read
- THEN July, August, September and October MUST accrue 500,000
- AND November MUST accrue 575,000

#### Scenario: The calendar does not move

- GIVEN a 6-month clause, due date 1 July, confirmed on 12 October
- WHEN the next due date is derived
- THEN it MUST be 1 January

### Requirement 16: The Operator Chooses Whether to Charge the Difference

**Amends `rent-adjustment/spec.md:186`.** When Decision 17 yields a positive amount, the summary MUST show the amount, the months it
covers and a choice defaulting to **no**. Choosing no MUST write no charge. Choosing yes MUST write one charge in the same transaction as the adjustment (Decision 19). The choice MUST NOT be offered
again for that adjustment, and MUST be recorded and shown in the history.

The charge MUST carry no recargo and MUST NOT raise the recargo of the period it attaches to (Decision 18, Finding F3).

**Worked example.** Canon 500,000; adjustment due 1 July; new canon 575,000; due day 10; confirmed 12 October. Applied-from is
November. July, August, September and October were charged at the old canon. The difference is 75,000 × 4 = **300,000**, payable with
November: 575,000 + 300,000 = 875,000 if paid by its due date. If November is paid 10 days late, recargo is computed on 575,000 only.

#### Scenario: The default charges nothing

- GIVEN the worked example, with the choice left at no
- WHEN it is confirmed
- THEN no charge MUST exist
- AND the canon MUST be 575,000

#### Scenario: Choosing yes writes one charge

- GIVEN the worked example
- WHEN yes is chosen and it is confirmed
- THEN one charge of 300,000 MUST be attached to November

#### Scenario: Unpaid months count too

- GIVEN the worked example, with July and August paid and September and October unpaid
- WHEN the difference is computed
- THEN it MUST still be 300,000
- AND September and October MUST still owe their old amounts

#### Scenario: Recargo ignores the charge

- GIVEN the charge attached to November, paid 10 days late
- WHEN the amount owed is read
- THEN recargo MUST be computed on 575,000, not on 875,000

#### Scenario: Not late, no question

- GIVEN an adjustment confirmed before its month's due day
- WHEN the summary is shown
- THEN no difference and no choice MUST be shown

#### Scenario: An index that fell is not offered

- GIVEN a new canon lower than the previous one, confirmed late
- WHEN the summary is shown
- THEN no charge MUST be offered

#### Scenario: Opening-balance months are excluded

- GIVEN a late adjustment whose months include one carried by an `OpeningBalance` and one on or before the cutover
- WHEN the difference is computed
- THEN neither MUST count

#### Scenario: Two late steps do not double count

- GIVEN canon 100,000, adjustments due January (120,000) and July (150,000), both confirmed in August, with due day 10, after August's due day
- WHEN each difference is computed
- THEN January's MUST be 20,000 × 8 months (January to August)
- AND July's MUST be 30,000 × 2 months (July and August)

### Requirement 17: A Pre-Existing Contract's Calendar Starts After Its Cutover

For a contract with a cutover date and no confirmed adjustment, the next due date MUST be the first date of the form start + k × interval
that is after the cutover date (Decision 24). The canon typed on creation MUST be taken as the one in force. Nothing new MUST be stored.
A contract with no cutover date MUST derive as `rent-adjustment/spec.md:59` says.

#### Scenario: History is not demanded

- GIVEN a contract starting 1 September 2024, 6-month clause, cutover 5 November 2026
- WHEN its next due date is derived
- THEN it MUST be 1 March 2027
- AND it MUST NOT appear on the worklist before then

### Requirement 18: A Correction Is Reached From a Corrected Index or From the History

Correcting an index level MUST NOT change any confirmed adjustment or any canon by itself. The screen MUST list contracts whose
adjustment copy used that index and period with the old level. From there, or from the contract's history with no index having changed
(Decision 38), the operator MUST be able to open a correction of the contract's latest adjustment, with a summary and confirmation like
any other. It MUST append a new adjustment naming the one it corrects
(`rent-adjustment/spec.md:236`), keep its due date, and take its applied-from period by Decision 16. A used level on any adjustment other
than the latest MUST be listed and refused with its own message (Decision 26).

If the corrected adjustment charged a difference and its period is unpaid, the correction MUST reverse it with a `Correction` movement in
the same transaction, and MUST offer the choice again on the corrected figures. If it is paid, the summary MUST say it stays and must be
settled by hand, and MUST NOT offer a new charge (Decision 27).

The corrected difference MUST be computed as if the original had carried the corrected canon, in two parts: (corrected canon − the original's
previous canon) × the periods from its month up to the original's applied-from period, plus (corrected canon − the original's new canon) × the
periods from there up to the correction's applied-from period. Decision 21 applies to the total.

#### Scenario: Correcting an index changes no rent by itself

- GIVEN a confirmed adjustment that used IPC of 9,440
- WHEN IPC is corrected to 9,400
- THEN the adjustment and the canon MUST be unchanged
- AND the contract MUST be listed as affected

#### Scenario: A correction is appended

- GIVEN that listed contract
- WHEN the correction is confirmed
- THEN the original row MUST be unchanged
- AND a new row MUST name it, with the corrected coefficient

#### Scenario: An unpaid difference is reversed

- GIVEN the adjustment charged 300,000, unpaid
- WHEN a correction is confirmed choosing no
- THEN a `Correction` of -300,000 MUST name the charge
- AND the charge MUST remain on the ledger, unchanged

#### Scenario: A mistaken yes is taken back from the history

- GIVEN an adjustment confirmed with yes by mistake, its 300,000 unpaid, and no index changed
- WHEN a correction with the same figures is confirmed choosing no
- THEN the charge MUST be reversed, and the canon MUST be unchanged

#### Scenario: The corrected difference counts both stretches

- GIVEN the worked example with yes unpaid, and November and December already charged at 575,000
- WHEN a correction to 580,000 is confirmed on 5 January, applying from January
- THEN the old 300,000 MUST be reversed
- AND the difference offered MUST be 80,000 × 4 + 5,000 × 2 = 330,000, attached to January

#### Scenario: A paid difference is not touched

- GIVEN the adjustment charged 300,000, paid
- WHEN a correction is confirmed
- THEN the summary MUST say it must be settled by hand
- AND no movement for it MUST be written

---

## Capabilities: contract-account, receipt, cash-desk (MODIFIED)

### Requirement 19: The Ledger Carries the Difference

**Amends `contract-account/spec.md:192,202`:** a late adjustment generates no new movement **unless the operator chooses to charge the
difference**. Accrued movements MUST still never re-price. **Amends `spec.md:453`:** the balance covers rent, instalments, recargo and
**the difference on a late adjustment**, and nothing else, and every label naming the figure's concepts MUST say so.

The ledger MUST admit a new append-only kind for the charge, carrying the applied-from period, positive, dated on that period's due date.
It MUST be a database-checked kind, added by a migration. A period holding only that charge, because its contract ended before the period
accrued, MUST remain payable (extending `cobranza-y-recibos/spec.md:79`, Decision 43).

Voiding a payment of that period MUST leave the charge owed, like the accrual. Reversing a charge not yet due MUST NOT leave a negative balance on
any date: its `Correction` MUST take effect on the same date as the charge.

#### Scenario: Reversing a charge not yet due leaves no credit

- GIVEN a charge dated 10 November, and today is 5 November
- WHEN it is reversed and the ledger balance is read for today
- THEN the balance MUST NOT show a credit from the reversal

#### Scenario: A raw update is refused

- GIVEN a charge on the ledger
- WHEN a raw SQL update or delete is attempted
- THEN it MUST be refused

#### Scenario: The balance includes it on its date

- GIVEN a charge dated 10 November
- WHEN the ledger balance is read for 9 November and for 10 November
- THEN only the second MUST include it

#### Scenario: A contract ended before the charge's period

- GIVEN a charge on November, and the contract ended on 20 October
- WHEN the desk opens that account once November has begun
- THEN November MUST be payable, for the charge alone
- AND no rent line MUST appear

#### Scenario: Voiding leaves it owed

- GIVEN November paid, charge included
- WHEN that payment is voided
- THEN November MUST owe the rent and the charge again

### Requirement 20: The Recibos and the Desk Show the Difference

A period with a charge MUST print a line for it on both recibos, derived from the ledger and never typed (`receipt/spec.md:143`). The
honorarios de administración base MUST include it (Decision 20). The desk's preview MUST show the line and name which months it covers, and
the ledger figure MUST include it. A payment MUST NOT settle the period's rent without it.

The line kinds' CHECK list MUST gain the new kind by migration, and existing recibos MUST NOT change.

#### Scenario: Both recibos carry the line, and the fee follows

- GIVEN November: rent 575,000, charge 300,000, no recargo, and a rate of 8%
- WHEN it is paid
- THEN both recibos MUST carry a 300,000 line
- AND the honorarios de administración line MUST be -70,000

#### Scenario: It cannot be paid apart

- GIVEN that period
- WHEN 575,000 is handed
- THEN it MUST be refused as not the total

---

## Capabilities: access-control, desktop-shell (MODIFIED)

### Requirement 21: The Database Refuses an Empleado's Cutover Date on Insert

**Closes the named limit at `access-control/spec.md:177-180`.** PostgreSQL MUST refuse an `INSERT` into `contracts` carrying a non-null
`cutover_date` unless the login role is a member of `inmobiliaria_admin`. An Empleado's `INSERT` with no cutover date MUST still succeed.

#### Scenario: An Empleado's raw insert is refused

- GIVEN a session as an Empleado
- WHEN a contract with a cutover date is inserted by raw SQL
- THEN PostgreSQL MUST refuse it

#### Scenario: Both ordinary inserts succeed

- GIVEN a contract with no cutover date, inserted as an Empleado, and one with a cutover date, inserted as an Admin
- WHEN each is inserted
- THEN both MUST succeed

### Requirement 22: Five New Screens, for Both Roles

Personas, Propiedades, Contratos, Índices and Ajustes MUST be reachable by both roles. The Admin gating of Usuarios MUST NOT be copied
(`ShellViewModel.cs:48-52`). No refusal an operator can cause by normal use MUST reach the generic error, and each screen MUST re-read from
the database after an action. Views follow Mateo's mockups (Decisions P8, P11); view-models and ports do not wait for them.

#### Scenario: An Empleado sees all five

- GIVEN an Empleado logged in
- WHEN the shell is shown
- THEN all five MUST be available, and Usuarios MUST NOT

---

## Out of Scope

Everything in `proposal.md` §2 "Out of Scope" stays out. In addition:

| Not here | Why |
|---|---|
| Companies as parties | Finding F10, unless N3 says otherwise |
| Refunding a paid difference, or an overcharge from a mistyped index | Settled by hand on Otros Conceptos (Decision 27) |
| Correcting an adjustment other than the latest | Decision 26 |
| Reversing a cancellation made by mistake | Final by Decision 35; the contract is loaded again. Debt it erased is not restored |
| Merging two persons loaded as duplicates | Only possible when neither has DNI or CUIL; both stay, and one is simply not used |
| Detecting a stand-in typed as an index value | Cannot be told apart from a real one (Decision 22) |

## Parameters Left to Design

- How the applied-from period is stored, given the first-of-month CHECK on `effective_date` (`20260918233049_AddRentAdjustments.cs:67`).
- Whether the movement lock of Requirement 9 is also a trigger, following `contracts_fee_plan_lock`.
- How a double confirmation is refused by the database.
- How accent-insensitive search is done without a new extension, if possible.
- The plausibility threshold of Decision 23.
- The timeout of the button of Requirement 25, and where the sources' addresses live, as configuration so a new source needs no code change where possible.
- Exact Spanish wording, and the recibo row's position.

## Questions

### For Nicolás, when he answers (none blocks design)

- **N1. On a contract adjusted by IPC on 1 July, which is the last month you use?** (June, May?) And on RIPTE and ICL? Confirms the suggestions
  of Decision 15. Tomás expects "the last one available". This also closes the long-unanswered `rent-adjustments` H.2.
- **N2. For ICL, do you compare the 1st of the month with the 1st of the month?** It changes daily, but an adjustment reads only two values, so
  the table holds one a month. Decision 41 assumes the 1st, as [one calculator does](https://hacecuentas.com/calculadora-actualizacion-alquiler-icl-bcra-mensual).
- **N3. Do you have any tenant or owner that is a company?** Finding F10.
- **N4. Which address do you send from, and from which program?** Tomás understands they use Outlook but send from a Gmail address, while the
  letterhead shows a Hotmail one. Today a tenant asks by WhatsApp how much that month costs, and they look it up and send the bill by mail.
  Input for the notices change, not this one.

## Preconditions

| Phase | Docker | Supabase |
|---|---|---|
| spec, design, tasks | No | No |
| S1 to S5 | **Yes** | No |
| S6 views | **Yes** for the full suite | No |
| verify | **Yes** | Optional live smoke, Tomás only, test database restored |

## Tests

Each proves a requirement through its public interface. Database rules are proven by raw SQL against `postgres:17.6`. Prove a guard by seeing it
fail first.

**Persons and units**

1. Search ignores case and accents, and matches part of a name. (R1)
2. Search by a formatted DNI or CUIL finds the person. (R1)
3. Identifiers are stored as digits only; "30.123.456" and "30123456" collide. (R2)
4. A duplicate DNI or CUIL is refused naming the holder; nothing written. (R2)
5. A CUIL with a wrong check digit is refused; DNI length bounds are enforced. (R2)
6. Neither identifier warns and is accepted. (R2)
7. Correcting a name leaves an issued recibo's name unchanged. (R3)
8. Availability derives from `Active` and `PendingTermination`, and no unit row is written when a contract ends. (R4)
9. A unit's type cannot change; an identical address warns without refusing. (R5)

**Contracts**

10. Past-end contracts read `Active` and are flagged. (R6)
11. A complete creation writes contract, parties, share and empty account, and it appears at the desk, as an Empleado. (R7)
12. No tenant, no lessor or no unit: each refused, with no row left. (R7)
13. A forced failure in account creation leaves no contract row. (R7)
14. A second tenant is refused by raw SQL, as each role. (R7)
15. A start date not on the 1st is refused by the port; the domain still tolerates one for existing rules. (R7, F9)
16. A start in the current month is accepted, warning only once the first due date has passed. (R7)
17. A non-whole or non-positive canon is refused; shares not totalling 100% are refused; a typed amount converts with the residue rule. (R7)
18. A unit under `Active` is refused; under `PendingTermination` it warns with the date. (R7)
19. The rate suggestion comes from the lessor's most recent contract carrying one; empty warns; empty and 0% stay different. (R7)
20. An Admin creates a pre-existing contract, and reading its account writes nothing on or before the cutover. (R8)
21. The cutover's bounds are refused in the form as in the domain. (R8)
22. An Empleado with a start before the current month is stopped, and the form does not offer the cutover. (R8)
23. Each locked field is correctable before the first movement and refused after. (R9)
24. A corrected canon before any movement is what accrues. (R9)
25. The rate can be entered after movements, the desk then allows collection, and an issued recibo is unchanged. (R9)
26. A codeudor can be replaced after movements; the tenant cannot. (R9, Decision 33)
27. Notice moves to `PendingTermination`; a future notice date and a move-out before notice are refused. (R10)
28. Ending needs a reason and valid dates, is allowed with debt, shows the owed amount, and that debt stays collectable. (R10)
29. Nothing accrues after an end date. (R10)

**Indexes**

30. The migration leaves exactly one IPC, RIPTE and ICL on an empty database and on one already holding "ICL". (R11)
31. Argentine number format parses exactly, and RIPTE over a million is stored without loss. (R12)
32. A non-positive level, a future month and a duplicate are each refused. (R12)
33. A far level warns and stores nothing until confirmed again. (R12)
34. A typed value clears that contract's missing-index entry. (R12)

**Adjustments**

35. Offset 1 reads what the query reads today, and existing clauses behave as offset 1. (R13)
36. Offset 0, 2 and 3 read the right periods and name the right missing ones; 4 is refused; consecutive adjustments of a clause leave no month skipped or counted twice. (R13)
37. An average with one index missing at any offset stays pending, with no proposal. (R13, `rent-adjustment/spec.md:91`)
38. A clause can be attached or corrected after movements, and is refused after a confirmed adjustment. (R13, Decision 32)
39. The summary carries base and end levels, periods, coefficient, due date and applied-from. (R14)
40. `PendingTermination` is listed, `Ended` is not, and the desk warns on the former. (R14)
41. Two intervals behind: the oldest shows, then the next, priced from the new canon. (R14)
42. A double and a concurrent confirmation: one wins and the canon moves once. (R14)
43. Confirmation records who confirmed, and shows the figure, first period and notice reminder. (R14)
44. Applied-from: before the due day, on it, after it, and with the period paid early. (R15)
45. **Unwritten months at the old canon.** With no period written, a late confirmation leaves every period before applied-from at the old canon, proven month by month. Must fail on today's `CanonFor`. (R15, F2)
46. Written months are unchanged by a late confirmation. (R15, `contract-account/spec.md:188`)
47. The calendar does not move: the next due date comes from the due date. (R15)
48. Two adjustments on one applied-from period: the last confirmed prices it. (R15)
49. Default no writes no charge. (R16)
50. Yes writes one charge in the adjustment's transaction; a forced failure leaves neither. (R16)
51. The worked example gives 300,000, with July and August paid or not. (R16)
52. Recargo on a late period ignores the charge, on projection and when charged. Must fail if the kind joins `PeriodNet`'s base. (R16, F3)
53. No difference and no choice when not late, or when the new canon is lower. (R16)
54. Opening-balance months and months on or before the cutover do not count. (R16)
55. Two late steps give 20,000 × 8 and 30,000 × 2. (R16)
56. A pre-existing contract's next due date is the first step after its cutover; it is not listed before. Without a cutover, today's derivation holds. (R17)
57. Correcting an index changes no adjustment and no canon, and lists the affected contracts by index and period. (R18)
58. A correction appends, names the original, keeps the due date, and takes its own applied-from. (R18)
59. A correction of a non-latest adjustment is refused with its own message. (R18)
60. An unpaid difference is reversed by a `Correction` naming it; a paid one writes no movement and the summary says so; the same figures from the history take back a mistaken yes. (R18)

**Ledger, recibos, access, shell**

61. The new kind is append-only and refused outside the CHECK list by raw SQL. (R19)
62. The balance includes the charge from its due date, and its labels name it. (R19)
63. A charge on a period its ended contract never accrued stays payable, with no rent line. (R19)
64. Voiding a payment leaves the charge owed. (R19)
65. Both recibos carry the line, derived; the fee base gives -70,000 in the example, and the instalment stays out of the base. (R20)
66. Paying the rent without the charge is refused as not the total. (R20)
67. An existing recibo reads identically after the migration. (R20)
68. An Empleado's raw insert with a cutover date is refused; without one it succeeds; an Admin's succeeds. (R21)
69. An Empleado's and an Admin's raw updates keep behaving as `access-control/spec.md:163-180` says. (R21)
70. Both roles get all five screens, and Usuarios stays Admin-only. (R22)
71. Each refusal in this spec has its own operator text, and none reaches the generic error. (R22)

**Undoing mistakes**

72. Cancelling leaves balance zero, every movement unchanged with its `Correction`, the reason, who and when, at any age, with or without movements, as each role. Must prove nothing is deleted by raw SQL reads. (R23)
73. A cancelled contract accrues nothing afterward, including unwritten earlier periods. Must fail if the end date alone governs accrual. (R23)
74. A cancelled contract frees its unit, leaves the worklist, is not offered for collection, stays readable, and cannot be undone. (R23)
75. An empty reason is refused; the "Cargado por error" reason is accepted by the database CHECK. (R23)
76. A voidable payment blocks cancelling, naming its recibos; one past the window does not, and stays with its recibos. (R23)
77. A cancelled tenant's other contract is unaffected. (R23)
78. Undoing a notice returns to `Active` with both dates empty. (R24)
79. Undoing an end resumes accrual for the months between; refused when a unit is covered by another contract or when it was cancelled. (R24)
80. A codeudor can be removed, leaving a trace, and neither role holds DELETE afterward. (R9, Decision 33)
81. **An agreed new deal goes through.** A contract ended by "MutualAgreement" with debt, and a new one on the same unit from the next 1st: the old debt stays collectable, no period is charged twice or skipped, and the old adjustment leaves the worklist. (Decision 39)

**Bringing index values** (against a fake source; no test touches the network)

82. ICL's value is that of the 1st, for the missing month and for a named one; nothing is stored before confirming. (R25)
83. A confirmed value goes through the same checks and the same table as a typed one, and clears the worklist. (R25)
84. No connection, a server failure, a timeout and a changed format: each gives its own message, none crashes, and manual entry still works. (R25)
85. A month not there yet says so, and is not an error. (R25)
86. A response with a non-positive level or the wrong month is refused as a source failure. (R25)
87. A stored level is never overwritten, and a differing one shows both. (R25)
88. RIPTE offers no button, and nothing is requested except when the button is pressed. (R25)

**Found on the last review**

89. "Cargado por error" is not offered when ending a contract normally. (R10)
90. A contract already ended can be cancelled, its reason replaced and its balance zero. A blocked cancellation says when only an Admin can void. (R23)
91. The corrected difference gives 330,000 in the spec's example; a paid one offers no new charge. (R18)
92. Reversing a charge not yet due leaves no credit on any date, whether by a correction or by a cancellation. (R19, R23)
93. A search names each person's roles on contracts in force; an ended or cancelled contract and a removed party count for nothing; the list filters by role. (R1)
94. Correcting a level as a real Admin and as a real Empleado records who, when, the previous and the new level; saving the same level records nothing. (R12)
95. Neither role can update or delete a correction record, and an insert cannot choose its author. (R12)
