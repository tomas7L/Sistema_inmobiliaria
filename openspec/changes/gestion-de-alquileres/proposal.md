# Proposal: gestion-de-alquileres (the missing rental screens)

Written 2026-11-04, from `openspec/changes/gestion-de-alquileres/exploration.md`, then corrected the same day after auditing it against
Engram, the archived changes and `openspec/domain/`. Vocabulary: **Admin** and **Empleado** are the two roles; a **late adjustment**
is one confirmed after its due date; the **difference** is what the old rent undercharged on months already collected during that
delay; a **pre-existing contract** is one signed before the system went live.

Rules come from the `party-registry`, `unit-registry`, `lease-contract`, `economic-index`, `rent-adjustment`, `contract-account`,
`receipt`, `cash-desk` and `access-control` specs. This proposal cites them; it does not restate them.

**What to read (Tomás):** §1 Intent, §2 Scope, §5 Decisions and §10 Size Forecast. **Skim** §4 Approach. **Skip** §6 Affected Areas,
§7 Risks, §8 Rollback Plan and §11 Success Criteria unless you want the detail.

---

## Contents

1. Intent
2. Scope
3. Capabilities
4. Approach
5. Decisions (with how strongly each is held)
6. Affected Areas
7. Risks
8. Rollback Plan
9. Dependencies and Preconditions
10. Size Forecast
11. Success Criteria

---

## 1. Intent

The application can collect rent, but nothing can get a contract into it. No production code creates a person, a property, a
contract or its account, and today that takes direct database access. Rents cannot change either: the cash desk warns that an
adjustment is ready to confirm, and there is no way to confirm it.

After this change, Admin and Empleado can register people and properties, create and end contracts, type each index's monthly
values, and review and confirm rent adjustments from the application.

**Why now:** the rules and tables exist and are tested, but the agency cannot run its business without these screens, whatever
the initial load turns out to be.

**Success, in one line:** the agency can sign a new contract and apply its adjustments without calling us.

---

## 2. Scope

### In Scope

- **Personas**: list, search, create and edit, including correcting name, DNI and CUIL. Creating a contract finds and reuses an
  existing person instead of duplicating them.
- **Propiedades**: list, create and edit properties and parking spaces, showing derived availability.
- **Contratos**: list, detail and create, with one tenant, one or more lessors, any codeudores, units and shares, due day, honorarios,
  fee plan and optional adjustment clause. Its account is created in the same transaction. Notice and end of a contract, with no
  solvency check (`respuestas-del-dueno.md:292`).
- **Pre-existing contracts**: an Admin creates them with their cutover date in the same operation (Decision 12).
- **Índices**: the IPC, RIPTE and ICL catalog, seeded by migration; typing and correcting monthly values, as published levels.
- **Ajustes**: worklist of due adjustments, a summary with index values, confirmation, history, and corrections as new entries.
- **Late adjustments**: the operator's choice on charging the difference (Decision 4), in its own slice. The operator is reminded to
  notify the tenant of the new figure (Decision 13).
- **The missing controls**, per spec:
  - A contract requires exactly one tenant and at least one lessor.
  - An Empleado cannot create a contract carrying a cutover date.
  - The confirmation summary carries the index values.
- **Applying the visual design** to new and existing screens, from Mateo's mockups (Decision 8).

### Out of Scope

| Not here | Where it goes, and why |
|---|---|
| Opening-balance screen, any Excel importer, splitting a tenant's figure across contracts, deleting the Excel copies | Separate `carga-inicial` change; depends on what Nicolás sends (Decision 3). Carries cuenta-corriente H.2 and cobranza H.6 |
| Mora worklist screen | The next change after this one (Tomás, 2026-11-04). Deferred since `cuenta-corriente` (`archive-report.md:90-92`) and raised by Tomás 2026-10-09. Not needed to sign or adjust a contract |
| Automatic notifications to tenants | The `notificaciones` change. Tomás asked on 2026-11-04 about email, the agency's current way of telling tenants what they owe. Sending with the application closed depends on the open `scheduler-mechanism` decision, and debt figures are unreliable until `carga-inicial` has loaded opening balances |
| Owner liquidation and its consolidated or per-property preference | `liquidacion-al-dueno` |
| Deposit, penalties, early-termination charges, per-contract sellos flag, per-utility account numbers | Deferred since `contract-and-parties`, with no owner yet. Notice and end only record dates and reason |
| Other receipt line kinds, such as Dep. Garantía | A later domain change (`ui-de-cobranza/spec.md:1349`) |
| Fetching index values automatically | The spec makes manual entry the method and retrieval a later change |
| Marking an index discontinued, from a screen | Rare, additive later; the domain already supports it |
| Adding a unit to an existing contract | Reopens the split; no implementation exists and no need has been reported |
| Changing the rent by hand on a contract with no clause | The contract was signed at a fixed rent; and it has a known pricing risk (exploration, gap 8) |
| Uploading signed contract documents | Its own capability, `contract-documents` |
| Deleting anything | Both roles hold no DELETE, by design |

---

## 3. Capabilities

The spec is **one file**, `openspec/changes/gestion-de-alquileres/spec.md`, with a Decisions table, a table of contents and numbered
Tests.

### New Capabilities

None. These screens are the missing half of existing capabilities.

### Modified Capabilities

- **`party-registry`**: search and reuse, correcting identifying data, whether DNI and CUIL may be absent (`spec.md:47`).
- **`unit-registry`**: availability gets an implementation.
- **`lease-contract`**: creating a contract as one transaction, with the required parties; start date on the 1st (Decision 14).
- **`economic-index`**: the seeded catalog.
- **`rent-adjustment`**: **"A Late Adjustment Is Not Charged Retroactively" is amended** by Decision 4, and its tenant notice by
  Decision 13; the applied-from period is separated from the adjustment calendar (Decision 5).
- **`contract-account`**: **two requirements are amended** for the new owed charge: a late adjustment generates no new movement
  (`spec.md:192,202`), and the balance covers rent, instalments and recargo "and nothing else" (`spec.md:453`). The second traces to
  the owner's answer in `respuestas-del-dueno.md:268`.
- **`receipt`**: a new line kind, which needs a migration because the kinds are also a database CHECK list.
- **`cash-desk`**: how the difference appears when collecting.
- **`access-control`**: its cutover rule also covers creating a contract.
- **`desktop-shell`**: five new screens, for both roles.

---

## 4. Approach

### 4.1 Same structure as the Users screen

Each area gets a Domain port with typed refusals, an Infrastructure implementation, and a view-model in Presentation, following
`IUserAdministration` and `UsersViewModel`. Unique violations become Spanish messages, as `SetupErrorMap` does.

### 4.2 Logic first, views when the design arrives

Ports and view-models do not depend on how a screen looks, so they are built and tested first. The XAML is written from Mateo's
mockups, each checked first for what WPF can do at a reasonable cost.

### 4.3 Contract creation is one operation

One port call creates the contract, its parties, its shares and its account in one transaction, and refuses when the required
parties are missing. The form proposes an equal split, and may accept a typed amount that it converts to a percentage
(`lease-contract/spec.md:218-265`). It uses "codeudor", never "garante". The honorarios field suggests the rate last used with that
owner, derived from contract history, because `party-registry/spec.md:52` forbids storing a rate on a person.

**A pre-existing contract carries its cutover date from creation** (Decision 12). Reading an account materialises accruals onto an
append-only ledger, so setting the date afterwards would permanently write that contract's whole history
(`contract-account/spec.md:548-556`).

### 4.4 Two dates for a late adjustment

The adjustment keeps its **due date**, which fixes the contract's calendar. It gains the **period the new rent applies from**. On a
six-month clause, an adjustment due 1 July and confirmed in October applies from the next billable period, and the next one is
still due 1 January. The design settles how this is stored.

### 4.5 The difference on a late adjustment

When confirming a late adjustment, the summary shows the computed difference and the operator chooses, defaulting to no. Choosing
yes adds an owed charge to the account, not a partial payment. Accrued months are never re-priced (`contract-account/spec.md:188`).

Already answered by earlier decisions:

- **Printing:** every stored line prints, on added rows when the paper has none (`ui-de-cobranza/spec.md:76`, Decision 35). Only the
  label remains.
- **Honorarios:** the base is the settled period's rent plus its recargo (`cobranza-y-recibos/spec.md:57`, Decision 21). The difference
  is neither listed nor excluded, so the spec extends this rule.

The spec must settle:

- **Months accrued but still unpaid.** The decision covers months already collected, so these are not covered.
- **Recargo on the charge.** No recorded answer. The nearest precedent is that an opening-balance month projects none
  (`cobranza-y-recibos/spec.md:67`, Decision 31).
- **Which period it settles with**, since payments are whole and each period has one receipt (`respuestas-del-dueno.md:170,355`).

### 4.6 Other points the spec must settle

- **An index value not yet published.** The spec leaves the adjustment pending and never invents a value (`rent-adjustment/spec.md:91`).
  Tomás's unconfirmed assumption is that the operator types the last available one (Engram `domain/decisiones-equipo-ronda-2`). The
  form needs guidance either way, because it accepts whatever is typed. RIPTE is a peso amount over a million, not a percentage
  (`economic-index/spec.md:7`).
- **A back-dated contract without a cutover date.** A start date in the past would accrue past months on first read. The form must
  stop an Empleado here and say that an Admin has to load it, without blocking a new contract signed a few days into its first month.

---

## 5. Decisions

| # | Decision | Held | Why |
|---|---|---|---|
| 1 | One change and one spec, delivered as chained PRs | **Moderate** | The areas depend on each other, and cobranza already worked this way. Separate changes would duplicate the shared rules |
| 2 | Every new screen is for both roles, except the pre-existing option (Decision 12) | **Firm** | `access-control/spec.md:22,29`; the Users gating must not be copied |
| 3 | Screens first; the opening balance and any importer go in `carga-inicial`, decided after seeing the Excel | **Firm** on the order; **open** on the importer | The screens are needed for new contracts anyway. Tomás's 2026-10-16 plan (Engram `decisions/carga-inicial-own-change`) is later than the older reading at `respuestas-del-dueno.md:205`, but nobody knows yet whether the Excel holds contract data |
| 4 | On a late adjustment, the operator chooses whether to charge the difference on months already collected: default no, amount shown first | **Firm** | Decided with Tomás 2026-10-22, deliberately without asking Nicolás. It supersedes the team's 2026-09-16 rule; if Nicolás objects, he wins |
| 5 | A late confirmation does not move the contract's calendar | **Firm**, pending Tomás's approval here | The assistant's recommendation, backed by `rent-adjustment/spec.md:71`: the contract fixes the interval. Not yet a recorded decision |
| 6 | Old debt is not treated as paid | **Firm** as advice, not yet a decision | It would erase real debt. If the Excel lacks months, they are reconstructed with Nicolás, the labelled default of cobranza Decision 18. `contract-account/spec.md:566` still allows months that are truly not owed. Final in `carga-inicial`, after seeing the Excel |
| 7 | Contract data can be corrected only until its account has movements, **except the honorarios rate**, which stays editable | **Moderate** | Follows the fee plan lock (`Contract.cs:173`). Without the exception, an empty rate plus any movement would lock the contract out of the cash desk for good. A change needs no history (Engram `domain/honorarios-placement`): issued receipts keep their figures |
| 8 | Mateo designs from scratch with his own Claude; we program from his mockups, with no agreed date | **Firm** | Decided with Tomás 2026-11-04. Mateo estimates about two days, without a commitment. Direction D records preferences only. Real client data never lands in mockups: the repository is public |
| 9 | IPC, RIPTE and ICL are seeded by migration, tolerating existing rows | **Moderate** | No clause can be recorded on a fresh database otherwise (`economic-index/spec.md:7,13`) |
| 10 | The honorarios rate stays optional at creation, with a warning that the desk needs it; the field suggests the owner's last rate | **Moderate** | The spec keeps it nullable, and the team decided both points 2026-09-16 (Engram `domain/decisiones-equipo-ronda-2`). The desk refuses collection without it (`AccountDirectory.cs:83`) |
| 11 | Mateo defines the look; we follow his base screen on every other screen, through a theme dictionary | **Firm** (Tomás, 2026-11-04) | No theme exists, and new screens must match. If his design has both a light and a dark mode, S6 stores each user's choice. The agency's logo stays the original unless Nicolás approves a corrected one |
| 12 | Only an Admin can create a pre-existing contract, with its cutover date in the same transaction, at any time and not only during the first load | **Moderate** | Required by `contract-account/spec.md:548-556` and `access-control`. An Empleado who finds a client missed in the first load asks an Admin to create the contract. It also settles the "predates the system" marker deferred by `ui-de-cobranza`: having a cutover date is the marker |
| 13 | The confirmation reminds the operator to notify the tenant; the system sends nothing | **Moderate** | `rent-adjustment/spec.md:188` requires notice, and automatic sending depends on an open decision |
| 14 | A contract starts on the 1st of a month: the form refuses another day | **Moderate** | Tomás, 2026-10-16 (Engram `decisions/pending-questions-2026-10-16`): signed on the 20th, it takes effect on the 1st of the next month. Reopen if Nicolás disagrees |

---

## 6. Affected Areas

| Area | Files |
|---|---|
| Domain | `src/Inmobiliaria.Domain/Parties/`, `Units/`, `Leasing/`, `Indices/`, `Accounts/`, `Collection/`: ports, refusals, corrections, the two adjustment dates, the new charge and its receipt line |
| Infrastructure | `src/Inmobiliaria.Infrastructure/Persistence/`: implementations, `DueAdjustmentQuery.cs`, migrations; `SessionServices.cs` |
| Presentation | New folders per area; `Shell/NavigationEntry.cs`, `Shell/ShellViewModel.cs`; the cash desk for the difference |
| Desktop | New views and a theme dictionary; `App.xaml(.cs)`, `MainWindow.xaml(.cs)`; restyling existing views |
| Specs | The capabilities in §3 |
| Tests | `tests/Inmobiliaria.Domain.Tests/`, `Infrastructure.Tests/`, `Presentation.Tests/` |

---

## 7. Risks

| Risk | Mitigation |
|---|---|
| **Wrong rent or debt charged** (high-risk item 1): adjustment dates and the difference change what is owed | Slices S4 and S5 get an independent verifier. Tests pin the late scenarios month by month. A summary precedes every confirmation |
| **A pre-existing contract's history is written permanently** (high-risk item 1) | The cutover date arrives with creation. Tests read the account right after creating one and assert nothing accrued |
| **Permissions** (high-risk item 2): closing the cutover gap on insert | Tests prove the database refuses an Empleado, not just the screen |
| **Mockups arrive late** and block delivery | Logic does not wait. If they run long, Tomás may choose to ship plain views and restyle them later |
| **A mockup is costly in WPF** | Each is checked before programming; differences go back to Mateo |
| **Real data reaches the public repository** | Tomás tells Mateo; mockups are checked for it before merging |
| **Nicolás's answer changes the load** | The load is a separate change; only its own decisions depend on him |
| **A recorded decision was still missed** | The audit did not read every `design.md`, `tasks.md` or older Engram note in full. The spec phase searches again for each requirement it writes |

---

## 8. Rollback Plan

No real contracts are loaded yet, so reverting the PRs and their migrations loses no agency data. Once the load begins, migrations
in this change move forward only, as `rent_adjustments` is append-only.

---

## 9. Dependencies and Preconditions

- All listed capabilities exist and are archived.
- Open decisions in `openspec/config.yaml`: **none touched** (`scheduler-mechanism`, `pdf-library`), since automatic notices stay out.
- **Still open, not settled here:** where the administration rate belongs, on the contract or the owner (`lease-contract/spec.md:290`).
  This change keeps it on the contract.
- Domain observations: `openspec/domain/respuestas-del-dueno.md` §2, §5, §9, §16 and §17; Engram `domain/contrato-locacion-reglas`,
  `domain/decisiones-equipo-ronda-2`, `domain/multi-unit-rent-split`, `domain/honorarios-placement`.
- Mateo's mockups, for S6 only.

| Phase / slice | Docker | Supabase |
|---|---|---|
| spec, design, tasks | No | No |
| S1 to S5 | **Yes** | No |
| S6 views | **Yes** for the full suite | No |
| verify | **Yes** | Optional live smoke, Tomás only, test database restored |

The assistant never handles the Supabase password.

---

## 10. Size Forecast

Advisory, about 400 authored lines per slice. These are estimates made before any design.

| Slice | Content | Estimate |
|---|---|---|
| S1 | Personas and Propiedades: ports, search and reuse, corrections, availability, view-models | ~550 |
| S2 | Contratos: creation transaction, required parties, pre-existing contracts, cutover gap, detail, notice and end, view-models | ~800 |
| S3 | Índices: seed, ports, view-models | ~350 |
| S4 | Ajustes: clause, summary data, two dates, confirmation, history, corrections, view-models | ~700 |
| S5 | The difference on a late adjustment: owed charge, receipt line, cash desk | ~450 |
| S6 | Theme and each user's choice, views and shell for new screens, restyling existing ones | ~950 |

**Total about 3,800 authored lines**, at least six chained PRs; S2, S4 and S6 will likely split further.

Decision needed before apply: No
Chained PRs recommended: Yes
400-line budget risk: High (S2, S4, S6)

---

## 11. Success Criteria

- Logged in as Empleado, a user can create a person and a property, then a contract with them, and that contract appears at the
  cash desk.
- A contract without a tenant or lessor is refused, and an Empleado cannot create one carrying a cutover date, by the database too.
- An Admin can create a pre-existing contract, and reading its account right after writes nothing on or before its cutover date.
- With an index value typed, a due adjustment shows its summary and, once confirmed, the next billable period uses the new rent.
- A late adjustment keeps its calendar and charges the difference only when the operator chooses it.
- A missing index value leaves the adjustment pending and names what is missing.
- Full suite green with Docker running, no skipped tests; `has-pending-model-changes` reports no changes.
