# Proposal: Contract and Parties

Domain vocabulary (Argentine lease terms, glossed once): **locador** = lessor/owner,
**locatario** = tenant, **codeudor** = solidary co-debtor jointly liable for the whole debt,
**honorarios** = the agency's management fee, **tácita reconducción** = automatic renewal.

## Intent

The agency's current tool hangs tenancy data on the property itself (`inquilino_id`,
`vencimiento_contrato`, `garante_id`, `precio`). Every turnover overwrites the prior tenancy,
so rental history is destroyed, vacancy has no valid representation, and the circular
property↔tenant foreign key cannot express condominio or multiple codeudores — both confirmed
present in the real documents (see `domain/contrato-locacion-reglas`).

**Why now:** every later change — current account, collection, receipts, index adjustments —
computes *against a contract*. There is nothing to attach a debt period, a receipt or an
adjustment to until this exists. It is the foundation change and the first to produce code.

**Success:** a signed lease, its 1..N units with their saved rent split, its 1..N locadores,
1 locatario, 0..N codeudores, its honorarios rate and its scanned PDF are all recorded and
re-readable without losing any prior tenancy of the same unit.

## Scope

### In Scope
- Six entities: `Party`, `ContractParty`, `Unit`, `ContractUnit`, `Contract`, `ContractDocument`
- Contract lifecycle states, dates, canon and honorarios percentage as **stored data**
- Multi-unit contracts with a stored per-unit rent share (percentage)
- Uploaded signed lease plus addenda (Supabase Storage pointer + metadata)
- Unit availability **derived** from contract state, never stored
- First `.sln` / `.csproj` scaffolding, EF Core + Npgsql wiring, initial migration

### Out of Scope
- Current account, debt, collection, receipts, PDF output
- IPC/RIPTE index adjustments; notifications; users and roles
- Per-utility account numbers (EPE, Aguas Provinciales, gas) — collection change
- Deposit, penalty and early-termination **arithmetic** (dates stored, math deferred)

## Capabilities

### New Capabilities
- `party-registry`: one identity per natural person; DNI/CUIL as natural key; role-agnostic
- `unit-registry`: physical leasable asset (property or cochera) with `UnitType`
- `lease-contract`: contract lifecycle, dates, canon, honorarios rate, party roles and multiplicity
- `contract-documents`: uploaded signed lease and addenda, one-to-many

### Modified Capabilities
None — `openspec/specs/` is empty; this is the first change.

## Approach

| Entity | Reasoning |
|---|---|
| `Party` | Role-agnostic identity. An owner who rents elsewhere is not duplicated; DNI/CUIL uniqueness enforced once. |
| `ContractParty` | Explicit join entity carrying `Role` (Lessor / Tenant / Codebtor) — the role is payload, so no EF skip-navigation. Multiplicity (1..N / 1 / 0..N) is validation, not schema. |
| `Unit` | Physical asset only. Single table with a `UnitType` discriminator (TPH). |
| `ContractUnit` | Join entity carrying `SharePercentage`. A contract covers 1..N units; a unit belongs to many contracts across time. The share is payload, so this is an explicit entity. |
| `Contract` | Canon (a single total), dates, lifecycle state, index reference, and the **honorarios percentage**. |
| `ContractDocument` | One-to-many so an addendum coexists with the original; never overwrite a pointer. |

**Three decisions already settled — do not reopen in spec or design:**

1. **`OwnerMandate` is dropped; honorarios live on `Contract`.** The administration mandate is
   cláusula DÉCIMA SEXTA *of the lease itself*, not a separate relationship. Modelling it as its
   own entity invented a lifecycle the real document does not have. No rate history is needed:
   each issued receipt stores its already-computed amount, so the printed document *is* the
   historical record. See `domain/honorarios-placement`. The exploration's §4 and its
   entity table are superseded by this proposal. (`OwnerMandate` stays dropped; the sixth
   entity in this proposal is `ContractUnit`, introduced by decision 4 below, not a revival
   of the mandate.)
2. **No Draft / Pending-signature state.** The lease is signed on paper and uploaded afterwards,
   so it cannot meaningfully exist before signature. States: `Active`, `PendingTermination`,
   `Ended` (with reason). A future `StartDate` already covers "signed, not yet started". Past
   `NominalEndDate` is **not** `Ended` — the real lease continues without tácita reconducción.
3. **TPH for `Unit` stays.** Divergence between property and cochera is real but consists of the
   per-utility account numbers, which belong to the collection change. **Revisit trigger:** if
   type-specific fields grow beyond a handful, split to TPT. Do not pre-build for it.
4. **A contract covers 1..N units, with the rent split stored as a percentage.** There is no
   "multi-unit mode" flag — a single-unit contract is simply N=1 at 100%. The canon is one
   total amount on the `Contract` (the real receipt has a single "Alquiler" line), and
   `ContractUnit.SharePercentage` distributes it. On create or edit of a multi-unit contract the
   UI proposes an equal split, allows editing it, and persists the result; it is not re-asked
   monthly, only when the units or the total change. The monthly liquidación always uses the
   stored split. **Invariant: the shares MUST sum to exactly 100%.**
   - **Percentage, not amount — this is the load-bearing part.** The canon is adjusted
     semiannually by the IPC/RIPTE average (cláusula CUARTA). Stored fixed amounts would still
     sum to the *previous* total after the first adjustment, breaking the invariant twice a year,
     guaranteed. Percentages propagate through every adjustment for free. The UI may accept a
     typed amount for convenience and convert it before persisting.
   - **Rounding must be deterministic.** An equal split of a total that does not divide evenly
     leaves a residue; the spec must name which unit absorbs it (e.g. the largest share, ties
     broken by unit id). Use `decimal`, never floating point.
   - **No split history is needed** — each issued receipt stores its already-computed amounts,
     the same reasoning as the honorarios rate in decision 1.
   - **Consequence for a later change:** when a contract's units have *different* owners, the
     split is what determines each owner's liquidación. That arithmetic belongs to the collection
     change; only the stored split belongs here.

**Terminology correction (business rule, not a rename):** the domain term is *codeudor*
(solidary co-debtor, liable for the whole debt), never *garante* (guarantor). Different
liability regimes under Argentine law; the original SPEC.md draft was wrong. This propagates
into later collection and legal-recourse design.

## Affected Areas

| Area | Impact | Description |
|---|---|---|
| repository root | New | First `.sln`, `Directory.Build.props`, projects |
| `src/*/Domain` | New | Six entities, `PartyRole`, `ContractStatus`, `UnitType`, `DocumentKind` |
| `src/*/Infrastructure` | New | `DbContext`, EF configurations, initial migration |
| Supabase Postgres | New | Tables `parties`, `units`, `contracts`, `contract_parties`, `contract_units`, `contract_documents` |
| Supabase Storage | New | Bucket for signed lease PDFs |
| `openspec/config.yaml` | Modified | `testing.status` re-detected once the first `.csproj` exists |

## Assumptions

Recorded deliberately; neither blocks this change, and neither may be silently invented.

- **"Honorarios: Sin Asignar"** in the agency's current tool is unexplained. *Assumption:* it can
  mean a genuinely unassigned fee, so the honorarios percentage on `Contract` is modelled
  **nullable**. Reversible cheaply if the owner says otherwise.
- **"Regimen Impositivo"** on the receipt — whose tax condition it reflects is unknown.
  *Assumption:* it is not needed for this change. If it is the owner's, it touches `Party` and
  will arrive as a later delta.
- `ContractDocument.UploadedBy` has no user entity to reference yet. *Assumption:* store a plain
  identifier string now; convert to a foreign key in the users-and-roles change.

## Open Decisions Touched

Flagged, not resolved (per `openspec/config.yaml`):

- **`credential-exposure`** — this change ships the first real Npgsql connection string to a
  client machine and creates the first Storage bucket. It does not resolve the decision, but it
  is the point where it stops being theoretical.
- ~~**`supabase-org`**~~ — **RESOLVED 2026-09-07: this project gets its own Supabase
  organization**, not shared with the team's padel system, because that system could grow to
  consume the shared organization-level quota (MAU, bandwidth, storage) and leave this one
  without a database.
- `scheduler-mechanism` and `pdf-library` — untouched.

## Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| Change exceeds the 800-line review budget | **High** | Chain PRs: (1) scaffolding + domain entities, (2) EF persistence + migration. Decide at `sdd-tasks`. |
| Honorarios nullability guess is wrong | Med | Nullable-first is the reversible direction; a NOT NULL tightening is a trivial later migration. |
| No test runner exists, so verification is build-only | **High** | **xUnit confirmed 2026-09-07.** Scaffold the test project inside this change; otherwise `sdd-verify` has nothing to run. Design must also choose how EF Core is tested against Postgres — the InMemory provider is not acceptable, it validates no constraints and passes tests that fail in production. |
| Rent-split invariant silently broken by an index adjustment | **High if amounts stored** | Store the share as a percentage, never an amount (decision 4). A spec scenario must assert the invariant holds after a canon change. |
| Contract fields creep toward collection concerns | Med | Non-goals above are explicit; canon and honorarios are *stored*, never *computed*, here. |
| Migration written against a Supabase org later abandoned | Resolved | `supabase-org` decided: dedicated organization. Create it before apply. |

## Rollback Plan

Cheap — this is the first change and there is no production data.

1. Revert the migration (`dotnet ef database update 0`) or drop the five tables.
2. Delete the Storage bucket if created.
3. `git revert` the PR(s); with chained PRs, revert in reverse order.
4. Delete `openspec/changes/contract-and-parties/`; no merged spec exists to unwind.

No consumer depends on this schema yet, so nothing downstream breaks.

## Dependencies

- Supabase organization created (resolved: dedicated, not shared with the padel system) and a
  project provisioned inside it
- .NET 10 LTS SDK installed on both developer machines
- Domain observations: `domain/contrato-locacion-reglas`, `domain/honorarios-placement`,
  `domain/cobranza-definiciones-confirmadas`, `domain/multi-unit-rent-split`,
  `domain/supabase-org-y-multiunidad`

## Size Forecast

Honest estimate, authored lines (additions + deletions):

| Slice | Est. lines |
|---|---|
| Solution + project scaffolding, CI adjustment | 120–180 |
| Six entities, enums, value objects | 260–350 |
| Rent-split invariant + deterministic rounding | 60–100 |
| `DbContext` + EF configurations | 230–320 |
| Initial EF migration (generated, but reviewable) | 380–650 |
| Test project skeleton | 60–120 |

**Total 1110–1720 lines — well over the 800-line budget.** The user has explicitly asked for
short, readable PRs rather than budget-maxed ones, so target **400–500 authored lines per
slice**, giving roughly three chained PRs rather than two. `sdd-tasks` must plan the split and
the `ask-on-risk` strategy must surface it before apply.

## Success Criteria

- [ ] A lease with two locadores, one locatario and two codeudores round-trips through the database
- [ ] The same unit holds two sequential contracts with different tenants and prices; both remain readable
- [ ] A contract past `NominalEndDate` still reports as the active governing contract of its unit
- [ ] Unit availability is computed from contract state, with no stored availability column
- [ ] An original lease and a later addendum coexist as two `ContractDocument` rows
- [ ] A contract covering two units persists a split that sums to exactly 100%, and a split that does not sum to 100% is rejected
- [ ] Changing a multi-unit contract's total canon leaves the stored percentages untouched and still summing to 100%
- [ ] An equal split of a total that does not divide evenly assigns the residue deterministically
- [ ] The solution builds in CI; the `build` required check passes
- [ ] No `garante` appears anywhere in code, schema or specs
