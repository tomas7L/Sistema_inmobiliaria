# Exploration: Contract and Parties

**Change:** `contract-and-parties`
**Phase:** explore
**Status:** ready for proposal

Domain-modeling exploration. No application code exists yet (no `.sln`, no `.csproj`), so
this is not a code-reading exercise. Evidence comes from two real receipts (N° 24269
locatario / N° 24270 propietario) and one real signed lease, both verified against the
`domain/*` observations in persistent memory.

Spanish legal terms are kept where they name a specific legal instrument with no clean
English equivalent, glossed on first use.

---

## Scope

**In scope:** the Contract entity, the parties to it (lessors, tenant, codebtors), the Unit
being leased, the per-owner management-fee arrangement, and the uploaded signed contract
document.

**Out of scope** (each is a later change): current account and debt, collection and receipt
generation, PDF output, index-based rent increases, notifications, users and roles.

---

## 1. Why a Contract entity is structurally required

The prior SPEC.md draft had no Contract entity. It hung `inquilino_id`,
`vencimiento_contrato`, `indice_aumento`, `garante_id` and `precio` directly on the Property.
That design fails in four concrete ways:

- **History is destroyed on every turnover.** Each new tenant overwrites the previous
  tenant, price and dates. "Who rented this unit before, and for how much" becomes
  unanswerable.
- **Vacancy has no valid representation.** A unit between tenants has no legitimate value
  for `inquilino_id`, so lifecycle state gets encoded as null-versus-not-null on an entity
  that should be stable.
- **Continuation has nowhere to live.** The real lease continues past its nominal end date
  (see §6) without creating a new contract. A one-row-per-unit design has no place to record
  that.
- **The circular FK is a symptom, not a bug to patch.** `propiedad.inquilino_id` pointing at
  `inquilino.propiedad_o_cochera_id` and back is what happens when a genuinely many-to-many
  relationship is forced into two competing scalar foreign keys. It cannot express
  condominio or multiple codebtors at all.

With a Contract entity: Unit → Contract is one-to-many across time, and Contract ↔ Party is
many-to-many with roles. History, vacancy, condominio and multiple codebtors all become
representable without special cases.

---

## 2. Central fork: party identity

**Option A — one `Party` entity, roles scoped per contract via a `ContractParty` join.**
**Option B — separate `Tenant` / `Owner` / `Codebtor` entities, as the draft implied.**

### Evidence already settled by the real documents

- **Condominio is real, not hypothetical.** The receipt lists two owners on one unit
  ("VICO LESLIE / VICO ALEJANDRA"), and the lease text alternates between "EL LOCADOR" and
  "LAS LOCADORAS" depending on number. The lessor role is many-to-many.
- **Multiple codebtors are real.** The lease has two (Rivero Nadia Soledad and Gómez Mauricio
  Ezequiel), each with independent name, DNI, CUIL, email and domicile — full identity
  records, not lightweight references.
- **The draft already conceded cross-contract recurrence.** Its own notes flagged that a
  guarantor may appear on more than one contract. A scalar FK cannot express that, and a
  dedicated `Codebtor` table cannot either without its own join to Contract — at which point
  it has reinvented Option A with a narrower table.
- **Role-crossing duplicates identity.** The moment one natural person crosses roles — an
  owner who rents elsewhere, a codebtor who is a lessor on their own property — their DNI,
  CUIL, email and domicile get duplicated across tables with no shared constraint. DNI and
  CUIL are natural unique keys for a person in Argentina; one `Party` table enforces that
  uniqueness once.

### The honest case for Option B

Single-role lookups ("list all tenants") are simpler. That is real, but it is solved by a
filtered query or view on `Role = Tenant`, not by splitting the schema. The other argument —
avoiding columns irrelevant to a given role — is solved correctly by keeping `Party`
role-agnostic and pushing every role-specific attribute onto the join or onto a separate
mandate entity.

### Recommendation

**Option A.** One `Party` entity; roles scoped per contract via `ContractParty` with a Role
of Lessor, Tenant or Codebtor. This is forced by evidence in hand, not chosen by taste.
Option B does strictly more schema work to arrive at a weaker model.

Keep `Party` person-only for this change — no legal-entity discriminator. Every party in the
real documents is a natural person, and there is no evidence yet of a company lessor or
tenant.

---

## 3. Multiplicity

| Role | Cardinality per contract | Evidence |
|---|---|---|
| Lessor | 1..N | Two owners on receipt 24269/24270; "LAS LOCADORAS" in the lease |
| Tenant | 1 | One locatario on every document seen |
| Codebtor | 0..N | Two on the real lease; no upper bound evidenced |

No multiplicity cap belongs in the schema. If the business later wants to limit lessors or
codebtors, that is validation logic.

### Terminology correction (required before spec)

The domain term is **codeudor** — a solidary co-debtor, jointly and severally liable for the
whole debt — not **garante** (guarantor), which the draft used. These are distinct liability
regimes under Argentine law, and later collection and legal-recourse design depends on which
one applies. This is a business-rule correction, not a rename.

---

## 4. Where the honorarios percentage belongs

> **SUPERSEDED — see `proposal.md`.** This section recommends an `OwnerMandate` entity. That
> recommendation was rejected: the administration mandate is cláusula DÉCIMA SEXTA *of the lease
> contract itself*, not a separate relationship, so the entity modelled something the real
> document does not contain. The honorarios percentage lives on `Contract`. The final model has
> **five** entities, not six. The analysis below is kept for its reasoning; its conclusion is not
> current.

**Honorarios** — the agency's management fee. Confirmed by the owner: it varies **by owner**,
not by unit. The draft's placement on the Property is wrong.

Three candidate homes:

- **On `Party` directly.** Simplest, but conflates identity with a commercial arrangement and
  reintroduces the exact "irrelevant column" problem that argued against Option B in §2.
- **On an administration mandate entity.** *(recommended)* This matches the real document's own
  legal structure: cláusula DÉCIMA SEXTA is an express administration mandate from the lessor
  to the corredor, a relationship distinct from the lease itself. An `OwnerMandate` entity
  scopes the fee correctly per owner and leaves room for mandate start, end and revocation
  without overloading Contract.
- **As a per-contract override on Contract.** Worth allowing as a nullable field falling back
  to the mandate, but only if the proposal phase confirms one owner can carry different rates
  across contracts. No evidence for that yet — do not add it speculatively.

**Recommendation:** introduce `OwnerMandate`, holding the honorarios percentage, tied to the
lessor Party.

> The receipt arithmetic confirms the fee is charged on rent *plus* late charge:
> `(507000 + 91260) × 0.08 = 47860.80 → $47.860`. The 8% is VICO's specific rate, not a system
> constant. Fee *calculation* belongs to the collection change; only its *placement* is settled here.

---

## 5. The Unit

A shared `Unit` concept covering both property and cochera (parking space) holds at the
identity level: both are addressable, ownable, rentable assets moving through the same
contract lifecycle. A `UnitType` discriminator is the pragmatic default, since no
cochera-specific or property-specific attribute divergence is evidenced yet.

| Belongs on Unit | Belongs on Contract |
|---|---|
| Address / domicile | Price (canon) |
| `UnitType` | Index reference |
| Physical identity (building, lot, unit number) | Start / end dates, lifecycle state |

**Removed from Unit entirely:** `inquilino_id`, `vencimiento_contrato`, `indice_aumento`,
`garante_id`, `fecha_desocupacion`, `estado_disponibilidad`. None of these describe the
physical asset; every one describes a tenancy.

---

## 6. Contract lifecycle and dates

The real lease runs a fixed 24-month term (01/09/2025 to 31/08/2027) and continues past
expiry **without tácita reconducción** — meaning the same contract continues on the same terms
indefinitely until a party actively terminates, rather than an automatic renewal creating a
fresh term.

**The key consequence: "past nominal end date" must never be conflated with "ended."** A
contract can be past `NominalEndDate` and still be the single active governing contract for
its unit.

Recommended states: **Active** (covers both within-term and continuing past nominal end),
**PendingTermination** (30-day notice served, move-out date set, not yet ended), and
**Ended** (with an end reason: expiry, early termination by tenant, termination for cause, or
mutual agreement).

Dates to store on Contract: `StartDate`, `NominalEndDate` (still needed after continuation —
it is the base for the 10% early-termination penalty of cláusula SÉPTIMA), `NoticeGivenDate`
(to validate the 30-day rule), `PlannedMoveOutDate` (the draft's `fecha_desocupacion`, now
tenancy-scoped rather than unit-scoped), and `ActualEndDate`.

**Unit availability is derived, not stored.** A unit is available when no contract in Active
or PendingTermination state covers it. The draft's `estado_disponibilidad` column is a
denormalization guaranteed to drift out of sync with actual contract state. Caching it is an
implementation concern if performance ever demands it, not a domain-modeling one.

---

## 7. Contract document storage

The system never generates the lease — the signed document is uploaded and attached.

This needs a `ContractDocument` entity in a one-to-many relationship from Contract, not a
single URL column, precisely because of addenda: a later-signed addendum is a second document
against the same contract, and both must stay retrievable. Versioning is therefore modeled as
multiple rows, never by overwriting a reference.

Fields: storage path or key, original filename, content type, uploaded-at, uploaded-by, and a
document kind distinguishing the original lease from addenda and termination notices.
Supabase Storage holds the bytes; the database holds the pointer and metadata.

Out of scope: PDF generation and any OCR or parsing of the uploaded file.

---

## 8. EF Core and Npgsql implications

Kept brief — depth belongs to the design phase.

- `ContractParty` carries a Role beyond its two foreign keys, so it must be an explicit join
  **entity**, not a bare EF Core skip-navigation many-to-many. Mapping the role from a C# enum
  to a plain column is easier to evolve than a native Postgres enum type.
- `Unit` fits table-per-hierarchy via a discriminator column, given the lack of evidenced
  attribute divergence in §5. Address is a natural owned type flattened into the same table.
- `OwnerMandate` and `ContractDocument` are their own tables.

Resulting schema for this change: `parties`, `units`, `contracts`, `contract_parties`,
`owner_mandates`, `contract_documents`. Six tables, and no circular foreign key — the
many-to-many is mediated by the join instead of by two competing scalars.

---

## Open questions for the proposal phase

These must be decided deliberately, not resolved silently during proposal drafting.

1. **`OwnerMandate` scope** — one rate per owner globally, or a rate per owner per property?
   The owner's phrasing reads as global, but confirm rather than assume.
2. **Pre-signature contract state** — is a Draft or Pending state needed before signature?
   No evidence yet.
3. **`UnitType` strategy** — table-per-hierarchy holds only while property and cochera share
   attributes. Revisit if divergence appears.
4. **"Honorarios: Sin Asignar"** — this entry in the agency's current tool may represent a
   genuinely unassigned fee. If so, the honorarios percentage must be nullable rather than
   required. *(One of four open data questions with the agency owner.)*
5. **"Regimen Impositivo" ownership** — if the receipt's tax condition belongs to the owner
   rather than the tenant, it touches Party and OwnerMandate modeling directly.
   *(Open data question.)*

The other two open data questions — what "Otros Conceptos" represents, and whether
intra-month partial payments exist — do not touch contract or party modeling and are safely
deferred to the collection change.

---

## Recommendation

Adopt six entities:

| Entity | Purpose |
|---|---|
| `Party` | One identity per person; DNI/CUIL as natural key; role-agnostic |
| `ContractParty` | Join carrying Role (Lessor / Tenant / Codebtor); no multiplicity cap |
| `Unit` | Physical asset; `UnitType` discriminator; address as owned type |
| `Contract` | Price, dates, lifecycle state, index reference |
| ~~`OwnerMandate`~~ | **Rejected — see the note in §4.** The percentage lives on `Contract`. |
| `ContractDocument` | One-to-many uploaded documents; supports addenda |

Every element follows from evidence confirmed in the real receipts and signed lease —
condominio, dual codebtors, per-owner fee variance, continuation without tácita reconducción,
and an uploaded rather than generated document. The design removes the draft's circular
foreign key and its history-destroying shape.

**Next phase:** propose.
