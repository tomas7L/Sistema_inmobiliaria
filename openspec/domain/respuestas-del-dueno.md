# Owner answers — the agency's own words

Domain facts confirmed directly by **Nicolás Demaría**, owner of Del Lago Servicios Inmobiliarios
(Sunchales, Santa Fe; corredor inmobiliario, matrícula CCI Santa Fe N°420).

## What this file is for, and what it is not

A specification says **what the system does**. This file says **where that came from**: who said it,
when, and what we believed before they said it.

The two are deliberately kept apart. A spec that carried its own provenance would be unreadable, and
provenance that lived only inside specs would be lost for every fact whose change has not been
written yet — which is most of what follows. When a rule below reaches a specification, the spec
states the rule and cites this file. Neither repeats the other.

**Rules for maintaining it.** Add a round, never rewrite one. If a later answer contradicts an
earlier one, leave both and say so — the contradiction is information. Keep the owner's own Spanish
where the exact wording carries meaning; a translation of a domain term is a interpretation, and
interpretations are what this file exists to protect against.

Quotations are what the owner said. Everything under **What this means** is our reading of it, and
may be wrong.

---

## Open — not answered yet

These block nothing today. Each names the change that needs it.

| # | Question | Needed by |
|---|----------|-----------|
| 1 | **The exact current receipt number.** Numbering continues from it; see §7. | **Blocks `cobranza`** — the first receipt the system issues cannot be numbered without it |
| 2 | Where the administration rate belongs — on the contract or on the owner relationship. See §3. | `cobranza`, and possibly a revision of `lease-contract` |
| 3 | Whether the two-part receipt (§1) means one sheet per party, or one sheet total. | `cobranza` |

---

## Round 2 — 2026-09-23, meeting

### 1. The receipt is one sheet with two parts

> *"Uno se lo lleva, otro se lo quedan ellos. La parte de la derecha es la que comparten, la otra es
> la que se quedan con la firma del propietario."*

**What we believed before.** From the two real receipts (N° 24269 and N° 24270) the project inferred
**two independent documents** of a single collection event, drawn from one correlative sequence and
differing only by the sign of the honorarios line. That reading is recorded at
`openspec/changes/archive/2026-09-14-contract-and-parties/exploration.md:8` and `:101`.

**What this means.** The earlier reading is at least incomplete. Before `cobranza` is specified, the
two physical receipts must be re-examined with this description in hand. Do not carry the
two-independent-documents model forward without re-deriving it.

> The conclusion that a contract may have **1..N lessors** does not depend on this. It is
> independently supported by the lease itself ("LAS LOCADORAS"), so it stands regardless.

### 2. Honorarios contractuales — a second, unmodelled concept

> *"Lo llaman honorarios contractuales. Las paga el inquilino."*
> *"Aparece junto con lo que paga de alquiler."*
> *"Son cuotas fijas."* — *"hasta 6 en total"*, later stated more precisely as **1 to 6**: paying
> the whole amount at once is simply one instalment.
> *"No se va antes de pagar todas las cuotas, se cerrará contrato luego de pagar todo lo que adeuda."*

**The two honorarios, side by side.** This is the distinction to get right, because the two are
charged to different people, computed differently, and printed on different parts of the receipt.

| | **Honorarios de administración** | **Honorarios contractuales** |
|---|---|---|
| Who pays | The **owner** | The **tenant** |
| What it is | A **PERCENTAGE** of the rent — 8%, 6%, it varies by owner | A fixed **AMOUNT** for handling the contract |
| What it pays for | Managing the property, month to month | Setting up the contract, once |
| When | **Every month**, for the life of the contract | **Once**, at signing |
| How it is paid | Deducted from the owner's monthly liquidation | In **1 to 6 fixed instalments** |
| Where it prints | The **owner's** part of the receipt | The **tenant's** part of the receipt |
| Does it adjust? | Yes, implicitly — it is a percentage of a rent that adjusts | **No.** The instalments are fixed at signing |

**What this means for the model.**

- Never store the tenant's honorarios as a percentage. It is an amount, agreed once.
- "1 to 6" means paying in full is simply **one instalment**, not a separate case. Do not model a
  single payment and an instalment plan as two different things.
- The instalments are **FIXED**: they do not move when the rent adjusts. Store an amount and a
  count; compute nothing. This is the opposite of the owner's rate, which rides the rent up.
- The tenant's instalment appears on the **monthly receipt beside the rent**, but only on the
  **tenant's** part. The owner's part never carries it.
- A contract **cannot close while the tenant still owes**, so the case "tenant leaves with
  instalments outstanding" does not exist.

**Consequences.**

1. **This breaks an assumption.** The project believed the two documents of one collection differ
   only by the sign of the honorarios. They do not: the tenant's part carries a line the owner's
   part does not.
2. **"Honorarios" now names two different things.** Every use of the word written before
   2026-09-23 means *honorarios de administración* — the monthly percentage paid by the **owner**.
   Anything written from now on must say which one it means. A note to this effect is in
   `openspec/specs/lease-contract/spec.md`.
3. **Nothing models any of this.** A repository-wide search for `honorarios` returns only the
   owner's monthly percentage.
4. **`Contract.End()` has no solvency check.** `src/Inmobiliaria.Domain/Leasing/Contract.cs:254`
   lets a contract reach `Ended` regardless of what is owed. That is not a defect — the rule did not
   exist when it was written — but it is now a requirement for `cobranza`.

### 3. The administration rate — still open

Confirmed in round 1: the rate **varies by owner, not by unit**. The implementation stores it on
`Contract` (`src/Inmobiliaria.Domain/Leasing/Contract.cs:24`), a placement chosen deliberately at
`openspec/changes/archive/2026-09-14-contract-and-parties/proposal.md:63`, on the grounds that the
administration mandate is cláusula DÉCIMA SEXTA **of the lease itself** rather than a separate
agreement with the owner.

The cost of that placement: an owner holding several contracts carries the same rate copied into
each, with nothing binding the copies together.

**Status: OPEN.** The team is reviewing it. Recorded here so the question is not lost and so nobody
treats the current placement as settled.

### 4. "Otros Conceptos" is a signed amount

> *"Solo seguros quiere que aparezca diferente, otra opción más, afuera de otros conceptos. Lo demás
> que aparezca como otros conceptos que él pondría."*
> *"También tiene que sumar o restar."*

On the Abregu receipt, the two lines that prompted this:

| Line | What it is |
|---|---|
| **$23.382** — "Otros Conceptos" | Whatever he needs it to be. He works the figure out elsewhere and types the result, writing it negative when it subtracts. *"No importa saber exactamente qué es."* |
| **$28.900** — tasa municipal | What the municipality charges. Typed by hand; there is no API for it and never will be. |

**What this means.**

- The amount on that line **carries a sign**. Modelling it as a positive amount would be wrong from
  the first day.
- **Seguros gets its own line**, separate from the free field.
- Neither figure is computed by the system. Both are entered.

> **Warning for whoever writes the collection lines.** This repository guards several amounts as
> strictly positive — `monthlyRent` at `Contract.cs:67` and `:98`, and index levels in
> `IndexValue.cs`. Those guards are correct where they are. Copying one onto a receipt line by
> reflex would be a defect.

### 5. The adjustment index is chosen per contract

> *"Generalmente está utilizando casi siempre ICL."* — and he wants to choose per contract,
> according to what is agreed with all parties.
> On the Abregu contract's IPC/RIPTE average: *"No, hace el promedio aparte"* — by hand.

Calculator he uses: <https://ikiwi.net.ar/calculadoras/ajuste-alquiler-ipc/>

Its inputs are contract start month, duration in years, adjustment frequency (monthly through
annual) and initial rent; it returns the whole adjustment schedule by IPC. It does **not** average
IPC with RIPTE — no published calculator does, because that average is a term of the contract, not
an index. The same site publishes ICL and Casa Propia calculators.

**What this means: nothing to change.** This is already modelled correctly.

- `openspec/specs/rent-adjustment/spec.md:13` requires 1..N index references with the rule derived
  from the count.
- `src/Inmobiliaria.Domain/Leasing/AdjustmentClause.cs:27` derives it rather than storing it:
  `_indices.Count == 1 ? CombinationRule.Single : CombinationRule.Average`.
- ICL is already a first-class catalog index at `openspec/specs/economic-index/spec.md:7`.

A contract adjusting by ICL alone works today; so does one averaging IPC and RIPTE. The system
computing the average is the intended improvement over doing it by hand — not a conflict.

### 6. Only total payments

> *"No puede pagar todo junto. No se permiten pagos parciales, si totales."*

**What this means.** No partial-payment state, no part-paid month, no balance within a period. This
closes an open question raised at
`openspec/changes/archive/2026-09-14-contract-and-parties/exploration.md:249`.

### 7. Receipt numbering continues

> *"Desde donde quedaron. Ellos. No desde 0."*

**What this means.** The sequence carries on from the agency's current number. The receipts analysed
(24269 / 24270) are from 2025, so the current number is a different one and **is not yet known** —
see the open list above. Nothing in the repository addresses numbering at all today.

### 8. Tax regime — "mostly"

> *"Es del consumidor final mayormente."*
> *"En caso de que tenga que facturar otra cosa iría en una factura aparte."*

**What this means.** The word *mayormente* is the load-bearing one: it is **not** a constant.
Anything needing different tax treatment leaves the receipt entirely and becomes a separate invoice.

This confirms the mitigation already chosen: the tax regime belongs to the **document**, not to the
collection, so the tenant's and the owner's parts can differ without a schema change.

### 9. Opening balances — nobody starts at zero

> *"Cada inquilino deberá lo que falta que pague, arrancan todos con saldos antes, nosotros a mano
> cargaremos todos los datos de cada persona."*

**What this means.** `cuenta corriente` needs a way to enter an opening balance per tenant, loaded
by the agency. No automated migration and no import from the old system: they will type it.

This is good news for scope — but note that
`openspec/changes/users-and-roles/proposal.md:319` currently reasons from "no real agency data yet",
which is true today and stops being true the day they start loading.

### 10. Electronic invoicing is OUT OF SCOPE

> *"Sí, las hace él."* — through the **ARCA web portal, by hand.** No digital certificate, no
> invoicing software.

He would like the system to do it *("si el sistema las podría hacer le ahorra un paso")* and was
told explicitly, in the meeting, that it is a **separate, later project** — not part of this one.

**What this means.** Integrating would start from zero: digital certificate tied to his CUIT, WSAA
authentication, WSFE, punto de venta registration, homologación before production. And the liability
is his, not ours: a wrongly issued invoice is a fiscal problem for the agency.

**Recorded here because nothing in the repository says it.** A search for `ARCA`, `AFIP` and
`factura` returns nothing, while `cobranza` is repeatedly scoped as "collection, receipts and PDF
output" — close enough that a future reader could route invoicing into it. They should not.

### 11. Rounding

> *"De última lo redondean ellos"* — at the register. A decimal result does not bother them.
> Asked, as an optional nicety: that the system offer a rounding option.

**What this means.** The truncation rule stands and is no longer an inference. Two things that sound
contradictory are both true and describe different moments: the **printed document truncates**, and
the agency then rounds the **physical cash** when it changes hands.

The requested rounding option is additive. `src/Inmobiliaria.Domain/Leasing/RoundingRule.cs` already
anticipates a second member.

---

## Round 1 — 2026-09-05, written questions

### 12. Recargo has no cap

2% per day, simple, from the day of mora, with **no upper limit**. At roughly three months of
non-payment he refers the matter to lawyers (cláusula QUINTA: two unpaid months allow termination).

**What this means.** The cap is legal, not arithmetic. The system MUST NOT cap the calculation.

### 13. One receipt per period

If a tenant pays two months at once, **two separate receipts** are issued, each reporting its own
month. There is no consolidated multi-period receipt.

### 14. The administration rate varies by owner, not by unit

The 8% on the analysed receipts is the rate for VICO LESLIE / VICO ALEJANDRA specifically, not a
system constant. An early draft specification said "porcentaje_comision configurable por unidad" —
that was wrong. See §3 above for where it is stored and why that is still open.

### 15. The Empleado role is the development team's call

The owner deferred it. Settled by the team and the project owner during `users-and-roles`: the
Empleado does everything operational and is excluded from exactly two things — user management and
aggregate business reporting.
