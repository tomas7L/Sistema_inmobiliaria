# Design: cobranza y recibos (slice A: tenant payment and recibo)

Technical design for the `cobranza-y-recibos` change, from `spec.md` (49 decisions, 76 tests,
after the 2026-10-14 review pass) and `proposal.md` in this folder. Written
2026-10-14. Nothing here is code; nothing here edits the spec.

**How to read this document (Tomás).**

| Read | Why |
|---|---|
| **Decisions** (the table right below) | Every choice in one line, with how hard to lean on it |
| **1. The early payment and the lazy accrual** | Decision 37, the part you asked about. Plain-language first, then the mechanism |
| **2. The payment, step by step** | What happens between "preview" and "commit", in order |
| **6. Who may do what** | The three "who may" parameters. All three are defaults, two go to Nicolás |
| **10. Open questions**, **11. Pending verifications**, **12. Spec issues found** | What still needs a person |
| **13. Slices** | The PR plan, with sizes and which test goes where |

**Skip unless you want the detail:** 3 (numbering internals), 4 (one live payment per period), 5
(tables), 7 (domain shapes), 8 (file list), 9 (testing strategy). They are written for whoever
implements and for the second inspection at the start of each session.

---

## Decisions

**VERIFIED FACT** = read in the code or a primary source, cited in the body. **SPEC-derived** = the
spec already decided it; the design only gives it a shape. **DESIGN CALL** = decided here. **DEFAULT**
= a question about how the agency works, answered provisionally and sent to Nicolás. Strength:
**strong**, **medium**, **weak** (act on it only provisionally).

| # | Decision | Basis | Strength |
|---|---|---|---|
| D1 | **An early payment writes the period's `RentAccrual` (and its instalment, when it carries one) itself, inside the payment transaction, as the identical row lazy accrual would later write**: same kind, same period, same canon, dated on the period's due date. The later accrual finds it and writes nothing; the database refuses a duplicate. A period that already carries its charge (an early payment voided before its due date) gets nothing new: the payment writes the rent and instalment only when they are missing (§1). | DESIGN CALL on VERIFIED code: `AccountMaterialiser.cs:46` skips a period `HasAccrualFor` already holds; unique index `ux_account_movements_accrual_period` (`20261010145210_AddContractAccounts.cs:81-86`) | **strong** |
| D2 | **Nothing about a period appears on the ledger before the date it was charged.** `Payment` and `RecargoCharged` are dated on the later of the received date and the period's charge date; a voiding's `Correction` is dated on the later of the voiding day and the movement it reverses. The **received date** itself is stored on the collection and printed on the recibo. | DESIGN CALL | **medium** |
| D3 | **The preview materialises missing accruals in memory only and saves nothing**; the commit materialises inside its own transaction. | SPEC-derived (Test 18: "the preview writes nothing") | **strong** |
| D4 | **One commit = one database transaction at READ COMMITTED.** Ledger rows and the collection first, the counter last, then the recibos. No SERIALIZABLE. | DESIGN CALL; Postgres row-lock semantics (§3) | **medium-strong** |
| D5 | **The counter is a dedicated one-row table, advanced by one `UPDATE ... SET next_number = next_number + 2 ... RETURNING`.** A rollback undoes the advance, so no number is lost. A unique index on the recibo number is the backstop. | VERIFIED (Postgres docs, READ COMMITTED update re-evaluation) | **strong** |
| D6 | **The pooler question is mostly answered already: the application connects in Supavisor SESSION mode (port 5432).** The counter uses only transaction-scoped row locks, which also hold under transaction mode. Still to be run once against the real pooler. | VERIFIED (`appsettings.json:7,10`); real-pooler run PENDING | **medium-strong** |
| D7 | **The cutover number is its own column, `starting_number`, writable only by `inmobiliaria_admin` (column-level grant).** A trigger copies it into `next_number` and refuses any change once a recibo exists. | DESIGN CALL; who = CONFIRMED BY USER (Tomás, 2026-10-14: Volkode sets it under an Admin login) | mechanism **medium-strong**; Admin-only **medium** |
| D8 | **"One live payment per period" is a unique index on `collections (account_id, period, attempt)`**, plus an insert trigger that requires `attempt` = previous collections + 1 and every previous one voided. Two concurrent payments of one period collide on the index; a voided period takes the next attempt. | DESIGN CALL | **medium-strong** |
| D9 | **A `collections` table is the payment event**: account, period, attempt, received date, recorded-at, recorded-by, and the ids of its `Payment` and `RecargoCharged` movements. Both recibos point at it. | DESIGN CALL | **medium** |
| D10 | **Recorded-at, recorded-by, annulled-at and annulled-by are set by a database trigger** (`now()`, `current_user`) and cannot be supplied. Each operator logs in as their own Postgres role, so `current_user` is the person. | VERIFIED (users-and-roles design Decision 2) | **strong** |
| D11 | **The recibo-line catalog is a C# enum mirrored by a `CHECK` list**, not a table. Each kind carries behaviour (derived or typed, sign rule, which copy) that a table row cannot. | DESIGN CALL; precedent `ck_contract_documents_kind` | **medium-strong** |
| D12 | **A recibo stores a text snapshot of its header** (tenant, lessors, units) taken at issuance. A later rename of a party never rewrites an issued document. | DESIGN CALL | **medium** |
| D13 | **The four new tables are append-only twice over**: `SELECT, INSERT` grants only, plus a `BEFORE UPDATE OR DELETE` trigger that also stops raw SQL. | VERIFIED precedent (`AddContractAccounts.cs:105-139`) | **strong** |
| D14 | **Back-dating bound: 7 calendar days, either role.** Enforced in the domain and again by a database trigger on `collections`. | CONFIRMED BY USER (Tomás, 2026-10-14) | **medium** (Nicolás may still narrow it) |
| D15 | **Voiding has a time limit that depends on the role.** Up to **7 calendar days** after the recibo was recorded, either role may void the pair (a desk mistake is fixed by whoever made it). From day 8 to day **30**, **Admin only**. After day 30, **nobody**: the pair can no longer be voided. A reason is mandatory; user and time are recorded by the database. Enforced in the domain and again by the `recibo_annulments` insert trigger (role checked with `pg_has_role`, age measured from the collection's database-stamped `recorded_at`). The domain receives the acting role and the collection's recorded-at through `VoidingPlanner.Plan` (§7.5). | TEAM DECISION (Tomás, 2026-10-14: the employee may void a mistake; after 5–7 days only the owner; eventually nobody), the 30-day ceiling is a DESIGN CALL. No law or standard fixes a limit for non-fiscal recibos; the shape follows ordinary cash-desk internal control (supervisor review of adjustments, segregation of duties) | rule **medium**; the 30-day figure **weak**, to be replaced by "until the period is liquidated to the owner" when `liquidacion-al-dueno` exists |
| D16 | **"Today" comes from one clock port** pinned to `America/Argentina/Buenos_Aires`; the database triggers use the same zone. | SPEC-derived (Decision 34) | **medium** |
| D17 | **A pre-existing contract accrues nothing due on or before its cutover date**, rent and instalments both, and that date is **stored on the contract**: `contracts.cutover_date`, nullable, set when a contract is loaded as predating the system, `NULL` for one created here, frozen once its account holds a movement. The accrual schedule reads it from the contract, so no caller can forget it. It is not derived from the earliest opening balance: an account read before its opening balances are typed would have no cutover and materialise its whole history, permanently. Closes a gap in today's code. | CONFIRMED BY USER (Tomás, 2026-10-14); DESIGN CALL on VERIFIED gaps (`AccrualSchedule.cs:27` walks from `StartDate`; `MoraWorklistQuery.cs:40-48` materialises every account on each read) | **medium** |
| D18 | **Ledger constraints widened**: one charge per period across `RentAccrual` and `OpeningBalance`; one instalment per period; at most one period-less opening balance, and only if zero; a kind `CHECK` (none exists today). | DESIGN CALL; "no kind CHECK" VERIFIED (`InmobiliariaDbContextModelSnapshot.cs:87-116`) | **medium-strong** |
| D19 | **Instalment *k* attaches to the *k*-th period the accrual schedule produces**, written by the same step that writes that period's rent, with its period set at creation. | DESIGN CALL; the "*k*-th period" reading is DEFAULT where the first calendar month is skipped | **medium** |
| D20 | **The fee plan is two nullable columns on `contracts`** with a both-or-neither `CHECK`; changing it after an instalment exists is refused by the domain and by a trigger. | SPEC-derived shape | **medium** |
| D21 | **The commit recomputes the preview and compares it as a value**; any difference refuses with "preview stale". | SPEC-derived (Decision 29) | **strong** |
| D22 | **The per-period recargo projection is extracted from `AmountOwed`** into a named method both use. No behaviour change. | DESIGN CALL on VERIFIED code (`ContractAccount.cs:124-144`) | **strong** |
| D23 | **Ports in `Inmobiliaria.Domain`, adapters in `Inmobiliaria.Infrastructure/Persistence`.** | VERIFIED precedent (`IAccountMaterialiser`, `AccountMaterialiser.cs:11`) | **strong** |
| D24 | **"On the contract" means "a period the accrual schedule produces, or one carrying an opening balance".** | DESIGN CALL; sharper than the spec's wording (Spec issue 5) | **medium** |
| D25 | **Honorarios de administración line = −truncate(rate ÷ 100 × (rent + recargo))**, stored on the recibo. | SPEC-derived (Decision 21), strong | **strong** |
| D26 | **An early-written `RentAccrual` is final.** An adjustment effective on or before its due date and confirmed after the early payment is a late confirmation: the row stays, nothing is generated for the period, the next period takes the new canon. No new code: the materialiser already skips a charged period (`HasAccrualFor`), and the living rule "A Period Accrues at the Canon in Force, and Never Re-Prices" already covers any accrued period | SPEC-derived (spec Decision 48) | **medium-strong** |
| D27 | **A `SaldoDeudor` recibo line kind** prints the figure of a month carried by an `OpeningBalance`, in place of `Rent`, on both copies. The honorarios de administración base for that month is that month's figure itself, never the whole loaded debt: `−truncate(rate ÷ 100 × figure)` | CONFIRMED BY USER (Tomás, 2026-10-14; spec Decision 49) | **medium-strong** |

---

## Table of Contents

1. [The early payment and the lazy accrual](#1-the-early-payment-and-the-lazy-accrual)
2. [The payment, step by step](#2-the-payment-step-by-step)
3. [Recibo numbering and the pooler](#3-recibo-numbering-and-the-pooler)
4. [One live payment per period](#4-one-live-payment-per-period)
5. [Tables, constraints and grants](#5-tables-constraints-and-grants)
6. [Who may do what](#6-who-may-do-what)
7. [Domain shapes](#7-domain-shapes)
8. [Where the code goes](#8-where-the-code-goes)
9. [Testing strategy](#9-testing-strategy)
10. [Open questions for Nicolás](#10-open-questions-for-nicolás)
11. [Pending verifications](#11-pending-verifications)
12. [Spec issues found](#12-spec-issues-found)
13. [Slices, sizes and test mapping](#13-slices-sizes-and-test-mapping)
14. [Threat matrix](#14-threat-matrix)
15. [Migration and rollback](#15-migration-and-rollback)
16. [Risks](#16-risks)

### Claims checked against the code

| Claim | Evidence | Verdict |
|---|---|---|
| A rent accrual is dated on its due date | `AccountMovement.cs:75-83` (`OccurredOn = dueOn`); `AccountMaterialiser.cs:51-52` passes `due.DueDate` | **TRUE** |
| Recargo and the mora worklist measure from the accrual's own date | `ContractAccount.cs:137-143`; `MoraWorklistQuery.cs:90-98` | **TRUE** — this is why D1 dates the early accrual on the due date |
| Lazy accrual skips a period already accrued, and the database refuses a duplicate | `AccountMaterialiser.cs:46`; index `ux_account_movements_accrual_period`, filter `kind = 'RentAccrual'` | **TRUE** |
| The schedule only yields periods whose due date has arrived | `AccrualSchedule.cs:42` | **TRUE** — why Decision 37 needs a design |
| `account_movements.kind` has a `CHECK` listing the kinds | Snapshot `:87-116`; migration `:54-56` | **FALSE.** The proposal says the CHECK is "extended"; there is none. D18 adds one |
| Instalments and opening balances carry no period | `AccountMovement.cs:68-69`, `:93` | **TRUE** — both factories change |
| A second opening balance is refused outright | `ContractAccount.cs:57-62` | **TRUE** — rewritten for month-by-month loading |
| The materialiser knows nothing of opening balances | `AccountMaterialiser.cs:44-55`; `AccrualSchedule.cs:27` starts at `StartDate` | **TRUE — a gap.** A pre-existing contract would accrue every month since its signing. D17 |
| The application connects through Supavisor session mode | `appsettings.json:7` (comment) and `:10` (`"Port": 5432`) | **TRUE** |
| No EF retry strategy wraps user transactions | `SessionDbContextFactory.cs:28-30` | **TRUE** — `BeginTransactionAsync` is safe to use, as `PostgresUserProvisioning.cs:75` already does |
| Each operator is their own Postgres login role | users-and-roles design Decision 2; migration `AddUsersAndRoles.cs:183-184` | **TRUE** — `current_user` names the person |
| The administration rate is `numeric(5,2)`, 0 to 100, nullable, set only at construction | `Contract.cs:24`, `:105-110`; `ContractConfiguration.cs:14-16`, `:59-61` | **TRUE** |
| A get-only property stays out of the EF model until a configuration names it | `Contract.cs:35-41` (`DueDay` comment) | **TRUE** — used to keep slice 1 schema-neutral |

---

## 1. The early payment and the lazy accrual

### In plain words

A tenant pays July on 2 July; July falls due on the 10th. Today the system would refuse, because July
is not on the account yet: nothing writes July's rent until the 10th, and then only when somebody
reads the account.

The design: **the payment writes July's rent itself, exactly as the system would have written it on
the 10th** — same amount, same month, same date (the 10th). It also writes July's honorarios
contractuales instalment when July carries one. Then the payment settles it. When the 10th comes
and the account is read, the system looks for July's rent, finds it, and writes nothing. If two
things try to write July's rent at the same moment, the database accepts one and refuses the other.

What the tenant and the agency see: the recibo says *received 2 July*, and the collection record keeps
2 July. What the ledger shows: July's rent and its payment both dated the 10th, so between the 2nd
and the 10th July reads as neither owed nor paid — exactly how an unpaid July reads today until the
10th. From the 10th onward it reads as owed and paid, zero.

### Why the rent is dated the 10th and not the 2nd (D1)

| Option | What goes wrong | Decision |
|---|---|---|
| **Accrual dated on the due date** (chosen) | The ledger rows for July carry a date later than the day they were written. A report of "money received today" must read `collections.received_on`, never movement dates (named in §16) | **Chosen** |
| Accrual dated on the received date (the 2nd) | Recargo and the mora worklist both take the due date from the accrual's own date (`ContractAccount.cs:137-143`, `MoraWorklistQuery.cs:98`). If that payment is ever voided, July would start counting recargo from the 2nd: eight days of surcharge the tenant does not owe. Fixing that needs a new `due_on` column and a backfill through an append-only trigger | Rejected |
| Accrual on the due date, payment on the received date | Between the 2nd and the 10th the ledger holds a payment with nothing to pay: a credit of one month's rent, and a preview whose "resulting balance" reads negative. The spec forbids representing credit (Decision 27) | Rejected |
| A new kind, e.g. "early rent" | Escapes the unique index that makes the race safe, and every reader (oldest unpaid, mora, recargo) would need to learn it | Rejected |

The chosen option is the only one where the row written early is **byte-for-byte the row lazy accrual
would write**. That is what makes idempotency strong: there is nothing to reconcile, because both
paths produce the same fact.

### The reconciliation, mechanically

1. **Which periods may be paid early.** A new `AccrualSchedule.PeriodsBegun(contract, asOf)` returns
   every period whose month has begun by `asOf` **and** that the schedule would produce on its due
   date (the existing rules at `AccrualSchedule.cs:31-42`: due date on or after the start, not after
   an actual end). `PeriodsDue` stays as it is. A period in `PeriodsBegun` but not in
   `PeriodsDue(asOf)` is "begun, not yet due".
2. **What the payment writes for it.** The same `AccrualPeriod` record the materialiser uses
   (`Period`, `DueDate`, `Canon`), turned into `AccountMovement.RentAccrual(id, canon, period,
   dueDate)` — the call at `AccountMaterialiser.cs:51-52`, unchanged — plus the instalment for that
   period when the plan assigns one (§7.4).
3. **Recargo.** `RecargoMath.For(owing, dueDate, receivedOn, terms)` counts `receivedOn − dueDate`
   days (`RecargoMath.cs:75-78`), which is negative, so it returns zero and no `RecargoCharged` is
   written (spec Decision 24). Nothing special-cases early payment: the arithmetic already says it.
4. **The later accrual.** `AccountMaterialiser` keeps calling `HasAccrualFor(period)` before
   writing; the early row answers yes. The instalment gains the same guard (`HasInstalmentFor`).
5. **The race.** If the materialiser and the payment write July at the same instant, the second
   insert waits on the unique index until the first commits, then fails with SQLSTATE `23505`
   (Postgres, "Unique indexes" and "Index uniqueness checks" in the Postgres 17 manual). If the
   payment loses, its whole transaction rolls back and the operator is told the account changed and
   to preview again — the next preview finds July already accrued and proceeds normally. If the
   materialiser loses, it already clears and re-reads (`AccountMaterialiser.cs:67-83`). Either way
   exactly one July rent row and at most one July instalment row exist. Proven by the new **Test
   70** (§12, §13), in Infrastructure, because a race needs a real database.
6. **Recargo projection until due.** Nothing to do. Once paid, July nets to zero and
   `AmountOwed` skips it (`ContractAccount.cs:129-131`). If the payment is voided before the 10th,
   the reversal is dated the 10th (D2), so July reappears as owed on the 10th and counts recargo from
   the 10th, as if it had never been paid.

**A voided early payment, paid again (spec Decision 37, Test 73).** Voiding an early payment reverses
its `Payment` but leaves the `RentAccrual` and instalment it wrote: they are the period's charge, not
the payment's. As of a received date before the due date every movement of the period is dated on
the due date, so the period reads as not owed, yet it is already charged; under the first wording of
*payable* it was neither "owed" nor "not yet charged", and step 2 would have written a second rent row
into the unique index. Two rules close it. *Payable* is "no live payment settles the period, and it is
owed or begun-not-yet-due", which never asks whether the period is charged (a live payment is a
`Payment` no `Correction` names; the attempt rule of §4 is the database's version). And step 2 writes
the rent and instalment only when `IsChargedFor(period)` and `HasInstalmentFor(period)` say they are
missing. The new payment then writes only its `Payment`, dated on the due date (D2).

### The ledger date rule (D2)

One rule covers every case: **a movement about a period is never dated before that period was
charged.**

| Movement | Dated on |
|---|---|
| `RentAccrual`, instalment | The period's due date (unchanged) |
| `OpeningBalance` | The day it was typed (unchanged) |
| `RecargoCharged`, `Payment` | The later of the received date and the period's charge date (the date of its `RentAccrual` or `OpeningBalance`) |
| `Correction` written by a voiding | The later of the voiding day and the date of the movement it reverses |

For an ordinary late payment both "later of" rules collapse to the plain answer — the received date,
and the voiding day — which is what the spec describes. They differ only for an early payment, and
for a back-dated payment of an opening-balance month typed after the money arrived. Spec issue 6
records the one sentence of the spec this refines.

---

## 2. The payment, step by step

```
operator ──► Preview(account, period?, receivedOn?, typed lines)
               │  load account + contract + adjustments (no tracking)
               │  materialise missing due accruals IN MEMORY          (D3)
               │  pick period: named, else oldest unpaid, else month in course
               │  refuse? ──► Preview carries the refusal, commit impossible
               │  build both recibos' lines, the two named totals
               ▼
             CollectionPreview  (a value: every figure, plus "today" as resolved)
               │
operator confirms
               ▼
             Commit(preview)
               BEGIN (READ COMMITTED)                                  (D4)
               │  reload account + contract, materialise in memory again
               │  recompute the preview; differs? ──► ROLLBACK, "preview stale"   (D21)
               │  INSERT missing due accruals (and instalments)
               │  INSERT early accrual + instalment, if not yet due and not yet charged (D1)
               │  INSERT RecargoCharged (if > 0), Payment                         (D2)
               │  INSERT collections row  ── unique (account, period, attempt)    (D8)
               │  UPDATE recibo_numbering ... RETURNING first number              (D5)
               │  INSERT locatario recibo (N) + lines, propietario recibo (N+1) + lines
               COMMIT
```

**Why the counter comes last.** Every issuance in the agency queues on that one row. Taking it
after every other check and insert means it is held only for the two recibo inserts and the commit,
and a payment refused for any other reason (wrong amount, period already paid, stale preview) never
touches it. That also gives the spec's "a refused payment consumes no number" for free.

**Why accruals are written inside the commit and not before it.** Running the materialiser first,
in its own transaction, would leave accrual rows behind when the payment is then refused — and the
spec says a refused payment writes nothing (Tests 1, 2, 6). Inside the transaction they roll back
with everything else. The rows are the same rows lazy accrual writes, so a concurrent materialiser
is resolved by the unique index as in §1, step 5.

**What the preview compares on commit.** `CollectionPreview` is a record with value equality: period,
received date, the resolved "today", previous amount owed, both recibos' lines and totals, the ledger
figure, the resulting balance, the warnings. The commit builds the same record from fresh data and
requires equality. This covers every case the spec names — another operator paid this period (it is
no longer payable), another operator paid a different period of this account (previous amount owed
moved), the day turned over (resolved "today" moved), the rate changed (the propietario line moved).

**Refusal reasons** (one enum, `CollectionRefusal`, each with operator text): `AmountNotTotal` (the
operator's handed amount differs from the ledger figure, Decision 27), `PeriodAlreadySettled`,
`PeriodNotBegun`, `PeriodNotOnContract`, `NothingPayable`, `AccountFrozen`, `RateNeverEntered`,
`ReceivedOnInFuture`, `ReceivedOnBeyondBound`, `NegativeTasaMunicipal`, `CutoverNotSet`,
`PreviewStale`, `ConcurrentChange` (any `23505` or `40P01` inside the commit).

**Database errors mapped to refusals.** The commit and the voiding catch `PostgresException` and map
it by SQLSTATE and constraint name; anything unmapped surfaces as an error, never as success. Every
trigger refusal is raised as `RAISE EXCEPTION ... USING ERRCODE = 'P0001', CONSTRAINT = '<name>'`, so
the adapter reads `PostgresException.ConstraintName` for `P0001` exactly as it does for `23505`, and
never parses message text.

| SQLSTATE | Constraint or trigger name | Refusal |
|---|---|---|
| `23505` | `ux_collections_live_attempt`, `ux_account_movements_period_charge`, `ux_account_movements_instalment_period`, `ux_recibos_number` | `ConcurrentChange` |
| `23505` | `ux_recibo_annulments_recibo` | `AlreadyAnnulled` |
| `40P01` | any (deadlock) | `ConcurrentChange` |
| `P0001` | `collections_received_on_future` | `ReceivedOnInFuture` |
| `P0001` | `collections_received_on_bound` (the 7-day bound) | `ReceivedOnBeyondBound` |
| `P0001` | `collections_attempt_rule` (§4) | `PeriodAlreadySettled` |
| `P0001` | `recibo_annulments_admin_only` (days 8–30, Empleado) | `AdminOnly` |
| `P0001` | `recibo_annulments_window_closed` (day 31 on) | `WindowClosed` |

The domain checks the same rules first, so a `P0001` in practice means the application and the
database disagree (a stale constant, or raw SQL); mapping it keeps the operator's message right
either way.

**Picking the period.**

1. Named: it must be **payable** — no live payment settles it (`HasLivePaymentFor`), **and** it is
   owed (nets above zero as of the received date, after in-memory materialisation) or begun and not
   yet due (`PeriodsBegun` minus `PeriodsDue`; it may already carry its charge, left by a voided early
   payment, §1).
2. Not named: the oldest owed period (`OldestUnpaidPeriod`, after in-memory materialisation); if none,
   the month in course when it is begun-not-yet-due with no live payment; if neither, `NothingPayable`.
3. Owed periods include opening-balance months: they are due from the moment the account exists.

---

## 3. Recibo numbering and the pooler

### The counter (D5)

```sql
CREATE TABLE recibo_numbering (
    id              smallint PRIMARY KEY CHECK (id = 1),
    starting_number integer  NULL CHECK (starting_number > 0),
    next_number     integer  NULL
);
INSERT INTO recibo_numbering (id) VALUES (1);   -- the single row exists from the migration
```

Issuance, one statement, inside the payment transaction:

```sql
UPDATE recibo_numbering
   SET next_number = next_number + 2
 WHERE id = 1 AND next_number IS NOT NULL
RETURNING next_number - 2 AS first_number;
```

- **No row returned** = the cutover number was never set → `CutoverNotSet`, rollback.
- **Locatario takes `first_number`, propietario `first_number + 1`** (spec Decision 23).
- **Gapless on rollback.** The `UPDATE` is part of the transaction; a rollback restores the old
  value, so the next payment receives the same numbers. A `SEQUENCE` would not: `nextval` is never
  rolled back (Postgres 17 manual, "Sequence Manipulation Functions": "nextval operations are never
  rolled back"). That is why the spec forbids one.
- **No duplicate under concurrency.** The `UPDATE` takes a row lock held to commit. A second session
  blocks on it; at READ COMMITTED, once the first commits, the second re-evaluates its `WHERE` on the
  *updated* row and increments from the new value (Postgres 17 manual, §13.2.1 "Read Committed
  Isolation Level": the second updater "will wait for the first updating transaction to commit or
  roll back… the second updater can proceed with updating the originally found row… re-evaluated to
  see if the updated version of the row still matches"). So two sessions always receive adjacent,
  distinct pairs.
- **Backstop.** `recibos.number` carries a unique index. A counter set back by hand (Test 66) makes
  the next issuance fail with `23505`, the whole payment rolls back, nothing duplicates. That is the
  named limit of spec Decision 36: the grant cannot tell an honest increment from a tampered one; the
  index makes tampering loud instead of silent.
- **Executed through the EF connection**, as an ADO command enlisted in the current EF transaction
  (`context.Database.GetDbConnection()` with `CurrentTransaction.GetDbTransaction()`), not through
  `SqlQuery`, so EF never wraps the `UPDATE` in a sub-select. Held **medium**; Tests 16 and 31 prove
  it (Docker).

**Rejected alternatives.** A settings key/value row: mixes a hot, locked row with configuration that
is read everywhere, and widens what the `UPDATE` grant reaches. `SELECT ... FOR UPDATE` then a separate
`UPDATE`: two round trips for the same lock. An advisory lock: session-scoped variants break under a
transaction pooler, and it adds nothing the row lock does not give.

### The cutover number (D7)

`starting_number` is the cutover parameter; `next_number` is the live counter. A `BEFORE UPDATE`
trigger on `recibo_numbering`:

- when `starting_number` changes: refuse if `EXISTS (SELECT 1 FROM recibos)`; otherwise set
  `NEW.next_number := NEW.starting_number`. So it may be set and corrected freely until the first
  recibo, and the first recibo carries exactly that number (spec Tests 33–35);
- when `next_number` changes while `starting_number IS NULL`: refuse (no issuance before cutover).

Race between an Admin correcting the cutover and the first issuance: the issuance holds the row lock;
the Admin's `UPDATE` waits, then fires its trigger on the new row version, and the trigger's
`EXISTS` query — a new statement under READ COMMITTED — sees the committed first recibo and refuses.
Held **medium-strong**; covered by Test 34.

### The pooler (D6)

The proposal and the spec asked how Supavisor in **transaction** mode affects the lock. The
application does not use transaction mode: it connects to the pooler host on **port 5432, session
mode**, deliberately, because its per-user role identity and Npgsql's prepared statements need a
session (`src/Inmobiliaria.Desktop/appsettings.json:7,10`). Under session mode the client holds one
server connection for the whole session, so it behaves as a direct connection.

Even under transaction mode the design would hold, because it depends only on **transaction-scoped**
state: a row lock and a unique index, both released at commit. Transaction-mode poolers assign one
server connection for the duration of each transaction; what they break is **session-scoped** state
(`SET`, session advisory locks, `LISTEN`, server-side prepared statements) — PgBouncer "Features"
table, pool_mode=transaction; Supabase docs, "Connecting to Postgres → Supavisor transaction mode".
The design uses none of those.

**Not claimed as verified:** that the concurrency tests pass against the real Supabase pooler. They
run against `postgres:17.6` in Testcontainers. Pending verification PV1 (§11).

---

## 4. One live payment per period

The spec requires it "in the database, not only in the application", and a voided period must accept a
new payment. A partial unique index on `Payment` movements cannot express "live", because voiding
never touches the `Payment` row — it appends a `Correction` — and an index cannot look at another row.

**Chosen (D8): an attempt number.** `collections` carries `attempt smallint`, and a unique index on
`(account_id, period, attempt)`. The first payment of a period is attempt 1; after it is voided, the
next is attempt 2. A `BEFORE INSERT` trigger refuses an attempt that is not
`count(collections of that account and period) + 1`, or any insert while an earlier collection of the
period has no annulment.

| Situation | What stops it |
|---|---|
| Two operators pay July at once | Both compute attempt 1; the second insert waits on the index and fails with `23505` → `ConcurrentChange`, nothing written (Test 17) |
| July paid, then paid again | Trigger: an earlier collection is live → refused (Tests 6, 26) |
| July voided, then paid again | Attempt 2 is the next number and attempt 1 is annulled → accepted (Test 26) |
| A raw `INSERT` claiming attempt 5 | Trigger refuses it |

| Alternative | Why it lost |
|---|---|
| Lock the account row (`SELECT ... FOR UPDATE`) and re-check | Correct only if every writer remembers to lock; the spec wants the database to refuse regardless |
| A `voided` flag on the collection, unique where not voided | Needs an `UPDATE` of an append-only row — the exact thing every other table here forbids |
| SERIALIZABLE isolation | Correct, but turns every conflict into a retry loop the application must own, for a guarantee an index gives for free |

**The `Payment` movement itself** keeps no unique index. It is only ever written with its
collection, in one transaction, and `collections.payment_movement_id` references it. A raw `INSERT` of
a lone `Payment` remains possible for a role with `INSERT` on `account_movements` — the same named gap
every movement kind already has (spec "a ledger holding one without the other is a defect"). Named, not
closed.

---

## 5. Tables, constraints and grants

Two migrations, each in the slice that changes the model (§13).

### Migration A — the ledger and the fee plan (slice 2)

| Object | Change |
|---|---|
| `contracts` | `ADD COLUMN contractual_fee_amount numeric(14,2) NULL`, `ADD COLUMN contractual_fee_instalments smallint NULL`, `ADD COLUMN cutover_date date NULL` (D17). `CHECK ((amount IS NULL) = (instalments IS NULL))`, `CHECK (amount IS NULL OR amount > 0)`, `CHECK (instalments IS NULL OR instalments BETWEEN 1 AND 6)`. Existing rows take `NULL` in all three and nothing else moves (Test 64) |
| `contracts` trigger | `BEFORE UPDATE OF contractual_fee_amount, contractual_fee_instalments`: refuse if the contract's account holds any `ContractualFeeInstalment` (Decision 32). `BEFORE UPDATE OF cutover_date`: refuse if the contract's account holds any movement (D17) |
| `account_movements` | `ck_account_movements_kind`: `kind IN ('OpeningBalance','RentAccrual','ContractualFeeInstalment','RecargoFrozen','Correction','Payment','RecargoCharged')` (D18, new) |
| | Replace `ck_account_movements_accrual_has_period` with `ck_account_movements_period_required`: `kind NOT IN ('RentAccrual','ContractualFeeInstalment','Payment','RecargoCharged') OR period IS NOT NULL` |
| | `ck_account_movements_opening_balance_period`: `kind <> 'OpeningBalance' OR period IS NOT NULL OR amount = 0` |
| | `ck_account_movements_payment_sign`: `kind <> 'Payment' OR amount < 0`; `ck_account_movements_recargo_charged_sign`: `kind <> 'RecargoCharged' OR amount > 0` |
| | Replace `ux_account_movements_accrual_period` with `ux_account_movements_period_charge` on `(account_id, period)` **where `kind IN ('RentAccrual','OpeningBalance')`**: one charge per period, which is also "a period holding an opening balance takes no accrual" |
| | `ux_account_movements_instalment_period` on `(account_id, period)` where `kind = 'ContractualFeeInstalment'` |
| | `ux_account_movements_opening_zero` on `(account_id)` where `kind = 'OpeningBalance' AND period IS NULL` |
| Grants | **None new.** `account_movements` keeps `SELECT, INSERT` (spec); `contracts` already has `UPDATE` for both roles (`AddUsersAndRoles.cs:161`) |

The materialiser's existing `23505` catch (`AccountMaterialiser.cs:67-83`) already covers the renamed
and the new index.

**Adding enum members is schema-neutral**; the constraints and indexes are not. The `MovementKind`
members can therefore land in slice 1 without a migration (the snapshot stores `Kind` as a string with
no list, VERIFIED), while every constraint above ships with migration A.

### Migration B — collection and recibos (slice 5)

| Table | Columns (all `NOT NULL` unless marked) | Constraints |
|---|---|---|
| `collections` | `id uuid PK`, `account_id uuid FK`, `period date`, `attempt smallint`, `received_on date`, `recorded_at timestamptz`, `recorded_by name`, `payment_movement_id uuid FK UNIQUE`, `recargo_movement_id uuid NULL FK UNIQUE` | `ux_collections_live_attempt (account_id, period, attempt)`; period first-of-month `CHECK`; `attempt >= 1` |
| `recibos` | `id uuid PK`, `collection_id uuid FK`, `number integer`, `copy varchar(16)`, `contract_id uuid`, `period date`, `received_on date`, `tenant_name text`, `lessor_names text`, `units_description text`, `legend text`, `total numeric(14,2)` | `ux_recibos_number (number)`; `ux_recibos_collection_copy (collection_id, copy)`; `copy IN ('Locatario','Propietario')`; `number > 0` |
| `recibo_lines` | `id uuid PK`, `recibo_id uuid FK`, `position smallint`, `kind varchar(32)`, `amount numeric(14,2)` | `ux_recibo_lines_position (recibo_id, position)`; `ux_recibo_lines_kind (recibo_id, kind)`; kind `CHECK` list (D11); `kind <> 'TasaMunicipal' OR amount >= 0`; `kind <> 'HonorariosAdministracion' OR amount <= 0` |
| `recibo_annulments` | `id uuid PK`, `recibo_id uuid FK UNIQUE` (`ux_recibo_annulments_recibo`), `reason text`, `annulled_by name`, `annulled_at timestamptz` | `btrim(reason) <> ''`; the `UNIQUE` is "never annulled twice" |
| `recibo_numbering` | §3 | §3 |

**Triggers.**

| Trigger | On | Does |
|---|---|---|
| `*_append_only` (one shared function, the `account_movements` shape) | `collections`, `recibos`, `recibo_lines`, `recibo_annulments` | `BEFORE UPDATE OR DELETE`: raise (Test 48, Test 14) |
| `collections_stamp` | `collections` | `BEFORE INSERT`: `NEW.recorded_at := now(); NEW.recorded_by := current_user;` — a supplied value is overwritten (Test 14, "ignored at creation"); refuse `received_on` after local today or more than 7 days before it (D14, D16); enforce the attempt rule (§4) |
| `recibo_annulments_stamp` | `recibo_annulments` | `BEFORE INSERT`: `NEW.annulled_at := now(); NEW.annulled_by := current_user;` |
| `recibo_numbering_cutover` | `recibo_numbering` | §3 |

"Local today" in SQL is `(now() AT TIME ZONE 'America/Argentina/Buenos_Aires')::date`. `now()` is the
transaction's start time, so one payment carries one consistent instant.

No trigger function here is `SECURITY DEFINER`: inside one, `current_user` is the function's owner,
not the operator, so the stamps (D10) and the voiding role check (D15) would both be wrong (Postgres 17
manual, "CREATE FUNCTION", `SECURITY DEFINER`). §9 covers how Test 72 proves it.

**Grants (in the same migration, convention `new-table-ships-with-grants`).**

```sql
GRANT SELECT, INSERT ON collections, recibos, recibo_lines, recibo_annulments
    TO inmobiliaria_admin, inmobiliaria_empleado;
GRANT SELECT ON recibo_numbering TO inmobiliaria_admin, inmobiliaria_empleado;
GRANT UPDATE (next_number)     ON recibo_numbering TO inmobiliaria_admin, inmobiliaria_empleado;
GRANT UPDATE (starting_number) ON recibo_numbering TO inmobiliaria_admin;
```

Column-level `UPDATE` is standard Postgres (`GRANT ... UPDATE (column)`, Postgres 17 manual,
"GRANT"). It is how "only Admin sets the cutover" is enforced by the database, as the spec requires,
while both roles still advance the counter. `collections` is a fifth table the spec's list does not
name (Spec issue 1); it gets the insert-only pattern.

---

## 6. Who may do what

| Act | Who (design) | Enforced by | Strength |
|---|---|---|---|
| Take a payment dated today | Admin, Empleado | Grants (§5) | SPEC-derived, strong |
| Type a received date up to **7 calendar days** back | Admin, Empleado | Domain check + `collections_stamp` trigger | CONFIRMED BY USER (Tomás), **medium** |
| Void a pair, days 0–7 after it was recorded (reason required) | Admin, Empleado | Domain check + `recibo_annulments` insert trigger | TEAM DECISION, **medium** (D15) |
| Void a pair, days 8–30 | **Admin only** | Same trigger, `pg_has_role(current_user, 'inmobiliaria_admin', 'MEMBER')` | TEAM DECISION, **medium** (D15) |
| Void a pair after day 30 | **Nobody** | Same trigger | DESIGN CALL, **weak** on the figure (D15) |
| Set or correct the cutover number | **Admin only** | Column grant `UPDATE (starting_number)` | **medium** — data loading is an Admin act today (user provisioning is Admin-only, `AddUsersAndRoles.cs:209-215`); Q4 is a confirmation, not a blocker |
| Change a contract's fee plan before any instalment | Admin, Empleado | Existing `UPDATE` on `contracts` + lock trigger | SPEC-derived, medium |

**Why 7 days.** The spec wants "a small fixed number of days until `liquidacion-al-dueno` provides a
closure" (Parameter 2). Seven covers money received on a Friday and recorded after a long weekend or a
holiday, and a back-dated entry can never cross more than one weekly cycle of recargo. It is a constant
in one place (`CollectionRules.BackDateBoundDays`) and in the trigger, and the second is checked
against the first by a test.

**Why voiding narrows with time.** A mistake at the desk is usually noticed the same day or the same
week, and the person who made it should be able to undo it, so both roles may void for 7 days. After
that, a voiding is no longer a desk correction but a change to money already reported, and it is
reserved for the owner, as ordinary cash-desk control reserves adjustments to a supervisor. After 30
days nothing may be voided at all, so the history of a month eventually becomes final. No law or
standard sets these figures for a non-fiscal recibo; they are internal controls. Thirty days is a
stand-in for the real boundary, "the month has been liquidated to the owner", which arrives with
`liquidacion-al-dueno`. All three figures are constants in one place (`CollectionRules`) mirrored by
the trigger, and a test checks the two agree.

Both roles keep `INSERT` on `recibo_annulments`, as the spec's grant list says; the time and role
limits are enforced by the trigger, which also stops raw SQL. The spec carries this as Decision 45 (Spec issue
10, resolved).

---

## 7. Domain shapes

New namespace `Inmobiliaria.Domain.Collection`. Nothing in it references EF or Npgsql
(`ArchitectureGuardTests` would fail the build otherwise).

### 7.1 Ledger changes (`Inmobiliaria.Domain.Accounts`)

```csharp
public enum MovementKind { OpeningBalance, RentAccrual, ContractualFeeInstalment,
                           RecargoFrozen, Correction, Payment, RecargoCharged }

// AccountMovement — changed and new factories
static AccountMovement OpeningBalance(Guid id, decimal amount, DateOnly occurredOn, DateOnly? period);
static AccountMovement ContractualFeeInstalment(Guid id, decimal amount, DateOnly period, DateOnly dueOn);
static AccountMovement RecargoCharged(Guid id, decimal amount, DateOnly period, DateOnly occurredOn); // > 0
static AccountMovement Payment(Guid id, decimal amount, DateOnly period, DateOnly occurredOn);        // < 0

// ContractAccount — new members
decimal  PeriodNet(DateOnly period, DateOnly asOf);
decimal  RecargoProjectedFor(DateOnly period, DateOnly asOf, RecargoTerms terms);   // D22, AmountOwed uses it
bool     IsChargedFor(DateOnly period);        // RentAccrual or OpeningBalance holds it
bool     HasInstalmentFor(DateOnly period);
bool     HasLivePaymentFor(DateOnly period);   // a Payment no Correction names (payability, B1)
bool     IsFrozen(DateOnly asOf);
```

`Append` changes: an `OpeningBalance` is refused when it is non-zero without a period, when its period
is already charged, when a period-less zero already exists, or when it is a period-less zero on an
account that already holds period opening balances (D18 — the last rule is **weak**: it only prevents a
confusing mix). A `RentAccrual` is refused for a period already charged.

### 7.2 Accrual schedule

```csharp
IReadOnlyList<AccrualPeriod> PeriodsDue(Contract c, DateOnly asOf);     // floor from c.CutoverDate (D17)
IReadOnlyList<AccrualPeriod> PeriodsBegun(Contract c, DateOnly asOf);   // D1, same floor
int? InstalmentNumberFor(Contract c, DateOnly period);  // k when period is the k-th scheduled period and k <= count
```

`c.CutoverDate` filters out every period whose due date is on or before it. The schedule reads it
from the contract, so no caller (the materialiser, the mora worklist, the payment) can forget it.
`PeriodsDue` keeps its signature, and a contract with no cutover date behaves exactly as today, so
`cuenta-corriente`'s tests stand unchanged.
`InstalmentNumberFor` counts from the contract's first scheduled period, **including** periods before
the cutover, so a pre-existing contract skips instalments 1 and 2 when its first two periods predate
go-live (Test 59) rather than renumbering them.

### 7.3 Collection

```csharp
public sealed record CollectionRequest(Guid AccountId, DateOnly? NamedPeriod, DateOnly? ReceivedOn,
    decimal HandedAmount, decimal? TasaMunicipal, decimal? Seguro, decimal? OtrosConceptos);

public sealed record CollectionPreview(
    DateOnly Period, DateOnly ReceivedOn, DateOnly ResolvedToday, bool PeriodWasEarly,
    decimal PreviousAmountOwed, decimal LedgerFigure, decimal ResultingAmountOwed,
    ReciboDraft Locatario, ReciboDraft Propietario,
    IReadOnlyList<CollectionWarning> Warnings, CollectionRefusal? Refusal);   // value equality (D21)

public static class CollectionPlanner   // pure
{
    CollectionPreview Plan(ContractAccount account, Contract contract, RecargoTerms terms,
                           CollectionRequest request, DateOnly today, ReciboHeader header);
}
```

`ReceivedOn` null means "today" and the preview records which day that was (`ResolvedToday`). `HandedAmount`
is what the operator says the tenant handed over for the ledger figure; it must equal `LedgerFigure`
exactly (Decision 27). Typed lines are truncated toward zero on entry (`RoundingRule.TruncateToWholePeso`,
reused, not re-implemented).

### 7.4 Recibo

```csharp
public enum ReciboCopy { Locatario, Propietario }

public enum ReciboLineKind   // order = print order
{ Rent, ContractualFeeInstalment, Recargo, TasaMunicipal, Seguro, OtrosConceptos, SaldoDeudor, HonorariosAdministracion }

public sealed record ReciboLine(ReciboLineKind Kind, int Position, decimal Amount);
public sealed record ReciboDraft(ReciboCopy Copy, IReadOnlyList<ReciboLine> Lines) { decimal Total { get; } }

public static class ReciboLineCatalog   // D11: the behaviour a table could not hold
{
    bool IsLedgerDerived(ReciboLineKind k);          // Rent, Instalment, Recargo, SaldoDeudor
    bool AppearsOn(ReciboLineKind k, ReciboCopy c);  // Instalment: Locatario only; Honorarios: Propietario only
    bool MayBeNegative(ReciboLineKind k);            // Seguro, OtrosConceptos (and Honorarios is always <= 0)
}
```

`Recibo` (persisted, slice 5) is immutable after construction and carries the number, copy, header
snapshot, legend, lines and total; `ReciboAnnulment` carries recibo id and reason (user and time come
from the database, D10). The legend is one constant, `Recibo.NonFiscalLegend` ("Documento no válido
como factura"); its wording is for the contador (spec Decision 10, weak).

**Honorarios line (D25):** `−TruncateToWholePeso(rate / 100 × (rent + recargo))`. Recibo N° 24270:
8 / 100 × (507,000 + 91,260) = 47,860.80 → **−47,860**. Rate `null` → `RateNeverEntered`; rate 0 →
line of zero, printed, with warning `ZeroAdministrationRate` (spec Decision 22). The line is computed
at issuance and stored; nothing re-reads the rate afterwards (Test 43).

**Saldo Deudor (D27, a default; Q5).** A month carried by an `OpeningBalance` prints that figure on a
`SaldoDeudor` line, on both copies, instead of `Rent`; it has no recargo (spec Decision 31). Its
honorarios base is the figure itself: `−TruncateToWholePeso(rate / 100 × figure)`; 8% of 160,000 →
**−12,800** (Test 75).

### 7.5 Voiding

```csharp
public static class VoidingPlanner   // pure
{
    VoidingPreview Plan(ContractAccount account, CollectionRecord collection,
                        IReadOnlyList<Recibo> pair, string reason, DateOnly today, RecargoTerms terms,
                        UserRole actingRole, DateOnly recordedOn);   // D15
}
```

It produces: two annulments (the pair, whichever recibo was named — spec Decision 19), one `Correction`
of `+|Payment|` naming the `Payment`, one of `−RecargoCharged` naming it when there was one, each with
the period set (the factory already allows it, `AccountMovement.cs:118-123`) and dated per D2. The
instalment and the rent accrual are not touched. Refusals: `ReasonRequired`, `AlreadyAnnulled`,
`AdminOnly` (an Empleado, days 8–30) and `WindowClosed` (anyone, day 31 on). `actingRole` is the
operator's `UserRole` (`Inmobiliaria.Domain.Access`); `recordedOn` is the agency-local day of the
collection's database-stamped `recorded_at`, converted by the desk through the clock's zone, never a
value the operator supplies. The age is `today − recordedOn` in calendar days; the figures live in
`CollectionRules` beside the back-date bound.

### 7.6 Ports (D23)

```csharp
// Inmobiliaria.Domain.Collection
public interface ICollectionDesk
{
    Task<CollectionPreview> PreviewAsync(CollectionRequest request, CancellationToken ct = default);
    Task<CollectionResult>  CommitAsync(CollectionPreview preview, CollectionRequest request, CancellationToken ct = default);
    Task<VoidingPreview>    PreviewVoidAsync(Guid reciboId, string reason, UserRole actingRole, CancellationToken ct = default);
    Task<VoidingResult>     VoidAsync(VoidingPreview preview, CancellationToken ct = default);
}

public interface IReciboNumbering
{
    Task SetStartingNumberAsync(int startingNumber, CancellationToken ct = default);
}

public interface IAgencyClock { DateOnly Today { get; } }   // D16
```

`PreviewVoidAsync` takes the acting role and reads the collection's `recorded_at` from the database
itself, then passes both to `VoidingPlanner.Plan`. The trigger re-checks with `pg_has_role`, so a
wrong role supplied by the application is still refused there.

---

## 8. Where the code goes

| Path | Action | What |
|---|---|---|
| `src/Inmobiliaria.Domain/Accounts/MovementKind.cs` | Modify | `Payment`, `RecargoCharged` |
| `src/Inmobiliaria.Domain/Accounts/AccountMovement.cs` | Modify | Factories of §7.1 |
| `src/Inmobiliaria.Domain/Accounts/ContractAccount.cs` | Modify | `Append` rules, new members, `AmountOwed` refactored onto `RecargoProjectedFor`, `OldestUnpaidPeriod` doc comment rewritten (it still says "the operator never chooses", `:151-153`) |
| `src/Inmobiliaria.Domain/Accounts/AccrualSchedule.cs` | Modify | Cutover floor, `PeriodsBegun`, `InstalmentNumberFor` |
| `src/Inmobiliaria.Domain/Accounts/IAgencyClock.cs` | Create | Clock port |
| `src/Inmobiliaria.Domain/Leasing/Contract.cs` | Modify | `ContractualFeePlan` (amount, count) with validation; `ChangeContractualFeePlan(plan, ContractAccount account)` refusing once an instalment exists; `CutoverDate` (`DateOnly?`, D17), set when the contract is loaded as predating the system and refused once its account holds a movement |
| `src/Inmobiliaria.Domain/Collection/*.cs` | Create | §7.3–7.6 |
| `src/Inmobiliaria.Infrastructure/Persistence/AccountMaterialiser.cs` | Modify | Cutover floor, `IsChargedFor`, writes the instalment beside the rent |
| `src/Inmobiliaria.Infrastructure/Persistence/CollectionDesk.cs` | Create | The transaction of §2, voiding |
| `src/Inmobiliaria.Infrastructure/Persistence/ReciboNumbering.cs` | Create | Cutover setter |
| `src/Inmobiliaria.Infrastructure/Persistence/AgencyClock.cs` | Create | `TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires")` |
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/` | Create/Modify | `ContractConfiguration` (plan columns), `AccountMovementConfiguration` (constraints, indexes), and `Collection`, `Recibo`, `ReciboLine`, `ReciboAnnulment`, `ReciboNumbering` configurations |
| `src/Inmobiliaria.Infrastructure/Persistence/InmobiliariaDbContext.cs` | Modify | DbSets; summary comment updated |
| `src/Inmobiliaria.Infrastructure/Persistence/Migrations/` | Create | Migration A (slice 2), migration B (slice 5), each with hand-written triggers and grants |
| `tests/Inmobiliaria.Domain.Tests/`, `tests/Inmobiliaria.Infrastructure.Tests/` | Create | Per §13 |

**Not touched:** `Inmobiliaria.Desktop`, `RecargoMath`, `RecargoTerms`, `Contract.End()`,
`MoraWorklistQuery` (it keeps reading the accrual's date, which D1 keeps truthful), every existing
migration.

---

## 9. Testing strategy

| Kind | What | Why |
|---|---|---|
| Domain, no database | Period selection, payability, recargo charged, preview, refusals, recibo lines, signs, honorarios, voiding plan, dates, schedule floor, fee plan | Pure arithmetic and rules over in-memory objects; runs in the Linux CI slice |
| Infrastructure, Testcontainers `postgres:17.6` | Atomicity, concurrency (counter, same period, early payment vs lazy accrual), append-only by raw SQL, grants per role, cutover lock, migration of existing contracts | Only a real database can refuse raw SQL, hold a lock or race two sessions |
| Command check | `dotnet ef migrations has-pending-model-changes` after slices 2 and 5 (Test 67) | Stronger than the model validator; needs no Docker |

**Atomicity tests force their failure** rather than hoping for one: a test-only interceptor
(`DbCommandInterceptor`) throws on the first `INSERT INTO recibos` (Test 15) and on the propietario
insert (Test 16). **Concurrency tests use two real sessions** with a barrier, and each guard is proven
by making it fail once (remove the unique index in a scratch run; the test must go red), per the
project's habit.

**Test 72 fixtures (the voiding window).** The window is measured from `collections.recorded_at`,
which `collections_stamp` sets to `now()`, so a test cannot simply insert a pair "recorded 8 days
ago". The trigger is never loosened for it: no test hook, no caller-supplied date. Instead the fixture
inserts the collection, its movements and its recibo pair as the container's superuser inside a
transaction that runs `SET LOCAL session_replication_role = replica`, which skips ordinary triggers
for that transaction only, and writes `recorded_at` explicitly. The voiding under test then runs on a
separate connection logged in as a real login role that is a member of `inmobiliaria_empleado` or
`inmobiliaria_admin`, created the way user provisioning creates one, never as the superuser: a
superuser passes `pg_has_role` for every role, so a test run as one proves nothing about the Empleado
limit. The `recibo_annulments` and `collections` trigger functions are not `SECURITY DEFINER` (§5); a
guard-proving run executes the Empleado day-8 case as the superuser once and must see it succeed,
which shows the role is what the test exercises.

**Docker must be running** for slices 2, 5 and 6. A skipped Testcontainers test is an unrun test.

---

## 10. Open questions for Nicolás

None blocks implementation. Each has a default already designed in.

| # | Question (one sentence) | Default if unanswered | Changes if he disagrees |
|---|---|---|---|
| Q1 | ~~How many days back may the received date be typed?~~ **Answered by Tomás, 2026-10-14:** 7 days, either role. Nicolás may still narrow it. | 7 calendar days, either role (D14) | A constant and a trigger line |
| Q2 | ~~Who may annul a recibo?~~ **Answered by Tomás, 2026-10-14:** either role for 7 days, Admin only until day 30, nobody after (D15). Nicolás may still change the figures. | D15 | Constants and the trigger |
| Q3 | When a contract starts after the due day (say on the 20th), is the first honorarios contractuales instalment charged with the next month's rent? | Yes: instalment 1 rides with the first month that actually charges rent (D19) | `InstalmentNumberFor` only |
| Q4 | ~~Who sets the starting recibo number?~~ **Answered by Tomás, 2026-10-14:** Volkode types it at go-live under an Admin login, so Nicolás has nothing to do. | Admin only (D7) | — |
| Q5 | ~~For a month loaded from the old spreadsheet, is the administration fee taken on the whole figure?~~ **Answered by Tomás, 2026-10-14:** on that month's whole figure, not on the whole debt. | Yes: the base is the opening-balance figure, which already holds rent and mora, truncated; the figure prints on a Saldo Deudor line (D27, spec Decision 49) | The fee base for opening-balance months only |

---

## 11. Pending verifications

Nothing below was verified. Each needs something this design did not have.

| # | What | Needs | Who / when |
|---|---|---|---|
| PV1 | Tests 17, 31 and 70 behave the same through the real Supavisor session pooler (port 5432) as against Testcontainers | Supabase restored; a one-off run of those three tests pointed at it | Tomás, before go-live, not before merge |
| PV2 | Migration A finds no existing `account_movements` row that violates the new constraints (a non-zero period-less opening balance, an instalment without a period), and that no contract predating the system has been loaded yet (it would take `cutover_date = NULL` and accrue its history on its next read; if one exists, its date is set before the account is read again) | `SELECT kind, count(*) FROM account_movements GROUP BY kind;` on Supabase before applying | Tomás, before applying migration A |
| PV3 | Column-level grants and the four triggers are created when migration B is applied by Supabase's `postgres` role, and `SELECT now() AT TIME ZONE 'America/Argentina/Buenos_Aires'` returns local time there | Supabase | Tomás, when applying migration B |
| PV4 | `TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires")` resolves on the office Windows machines (.NET resolves IANA ids on Windows through ICU) | A Windows run; CI is Linux | Slice 4, on Tomás's machine; fall back to `"Argentina Standard Time"` if it throws |
| PV5 | The raw counter `UPDATE ... RETURNING` enlisted in the EF transaction rolls back with it | Docker (Tests 15, 16) | Slice 6 |

---

## 12. Spec issues found

All resolved. Each row names the spec decision that closed it; the table stays as the record of why
those decisions exist.

| # | Issue | Where | Resolution |
|---|---|---|---|
| 1 | The design needs a fifth table, `collections` (§4, D9). The access-control requirement and Test 65 list only recibos, lines, annulments and the counter | access-control requirement; Test 65 | **RESOLVED** by spec Decision 40: `collections` is in the insert-only list and in Test 65 |
| 2 | Test 65 says "`SELECT` and `UPDATE` on the counter" for both roles; the design narrows Empleado to `UPDATE (next_number)` so the cutover is Admin-only by the database, as the same requirement permits | Test 65 | **RESOLVED** by spec Decisions 39 and 40: Test 65 says both roles advance the counter and only Admin sets its starting number |
| 3 | **Pre-existing contracts would accrue their whole history.** The spec covers instalments before go-live (Decision 32, Test 59) but not rent: today's materialiser walks from `Contract.StartDate` (`AccrualSchedule.cs:27`) and would raise a `RentAccrual` for every month since signing on top of the opening balance | contract-account, opening balance | **RESOLVED** by spec Decision 41 and Test 71; amended by the review pass so the cutover date is stored on the contract (D17, Test 76) |
| 4 | **Early payment racing a lazy accrual is proven only in Domain** (Test 68). The race is a database property | Test 68 | **RESOLVED** by spec Decision 42 and Test 70 (slice 6, §13) |
| 5 | "Not on the contract" is now "a month before the contract's start or after its end". A contract starting on the 20th with due day 10 never charges its first calendar month (`AccrualSchedule.cs:36-42`); that month is neither before the start nor payable | collection, period scenarios | **RESOLVED** by spec Decision 43 (D24) |
| 6 | "Corrections are dated on the day they were made" (voiding scenario "Earlier history reads as it did"). For an early payment voided before its due date, a correction dated today would precede the payment it reverses | collection, voiding | **RESOLVED** by spec Decisions 44 and 47 (D2) |
| 7 | The spec grants annulment `INSERT` to both roles yet leaves "who may void" to design. Any answer other than "both" contradicts the grant list | access-control; collection voiding | **RESOLVED** by spec Decision 45: both roles keep `INSERT`; who may void and until when is enforced by the `recibo_annulments` insert trigger (D15), which a grant cannot express, so the grant list and the rule do not contradict |
| 8 | Test 43 ("a later change of rate leaves an issued recibo unchanged"): `Contract` has no method that changes `HonorariosPercentage` (`private set`, assigned only in the constructor, `Contract.cs:24,118`) | Test 43 | **RESOLVED** by spec Decision 46 and Test 43 |
| 9 | The proposal (not the spec) says the `account_movements.kind` CHECK is "extended"; no such CHECK exists | proposal §2 | **RESOLVED** in design (D18); no spec change needed |
| 10 | **Voiding has a role-dependent time limit** (D15): either role for 7 days after recording, Admin only until day 30, nobody after. The spec says only "who may void is design's" (Decision 33) and has no test for a refused voiding | collection voiding; access-control | **RESOLVED** by spec Decision 45 and Test 72 (fixtures, §9) |

---

## 13. Slices, sizes and test mapping

Six PRs into `develop`, in order. A slice that changes the EF model ships its migration (slices 2 and
5 only). Sizes are authored lines (additions + deletions), a planning heuristic, not a cap. For
calibration: `cuenta-corriente` PR5 landed at about 781 lines against a 260–320 estimate, almost all
of it tests, so these ranges assume the test volume that project actually produces.

| Slice | Content | Docker | Model change | Lines |
|---|---|---|---|---|
| **1** | Ledger rules in the domain: new kinds and factories, opening balance per period, payability and the default period, cutover floor read from `Contract.CutoverDate` (get-only, unmapped until slice 2), `PeriodsBegun`, early accrual planning, per-period recargo, ledger dates (D2) | no | **no** (enum members only) | 400–480 |
| **2** | Fee plan and `cutover_date` on `Contract`, instalment attachment, materialiser writes instalments and honours the floor, **migration A** | **yes** | **yes** | 420–520 |
| **3** | The recibo document: catalog (with `SaldoDeudor`), lines, signs, both copies, honorarios line, legend, pair numbering in the domain | no | no | 380–460 |
| **4** | Collection and voiding planners: preview, refusals, dates and clock, commit equality | no | no | 400–480 |
| **5** | **Migration B**: collection and recibo tables, counter, triggers, grants, configurations; schema tests | **yes** | **yes** | 400–500 |
| **6** | `CollectionDesk`: the transaction, voiding, cutover setter; atomicity and concurrency tests | **yes** | no | 380–480 |

**Total ≈ 2,380–2,920 authored lines**, above the proposal's 1,210–1,640: the proposal predates
Decisions 21–37, the opening-balance rewrite and the early payment, and did not use the calibration
above. Slices 1, 3 and 4 need nothing but the .NET SDK.

Decision needed before apply: Yes (chain strategy, under `ask-on-risk`)
Chained PRs recommended: Yes
400-line budget risk: High

### Every spec test, by slice

| Slice | Spec tests |
|---|---|
| 1 | 3, 4, 5, 6, 7, 9, 52, 53, 54, 55, 56, 68 (rent part), 71 (Domain part) |
| 2 | 57, 58, 59, 60, 61, 62, 63, 64, 67 (A), 68 (instalment part), 71 (Infrastructure part), 76 |
| 3 | 21, 28, 29, 30, 36, 37, 38, 39, 40, 41, 42, 43, 44, 46, 49, 50, 75 |
| 4 | 1, 2, 8, 10, 11, 12, 13, 19, 20, 22, 23, 24, 27, 45, 47, 69, 73, 74 |
| 5 | 14, 32, 33, 34, 35, 48, 51, 65, 66, 67 (B), 72 |
| 6 | 15, 16, 17, 18, 25, 26, 31, 70 |

All 76 spec tests appear once (67, 68 and 71 split across two slices by part). Tests 9 and 68 are
Domain tests by the spec; slice 6's Test 18 is the Infrastructure proof that the preview writes
nothing.

**Placement checked by the review pass.** Test 70 moved from slice 2 to slice 6: it races a real
payment commit, which only `CollectionDesk` (slice 6) performs. Tests 33, 35 and 66 stay in slice 5,
reworded to run an issuance raw against the counter (its `UPDATE ... RETURNING` and the recibo
insert): that proves the counter's own guards where they are built, and the commit path through the
same statement is proven in slice 6 by Tests 16 and 31. Tests 14, 34, 48, 51, 65 and 72 use raw SQL
or the slice-5 configurations only. Tests 11 and 24 keep only what the domain can check; the
database-set values they used to assert moved to Tests 14 and 51. Every test in slices 1, 3 and 4 is
a Domain test.

---

## 14. Threat matrix

N/A — no routing, shell, subprocess, VCS/PR automation, executable-file classification, or
process-integration boundary. The security-relevant boundaries here are database privileges, covered
by §5, §6 and Tests 65 and 66.

---

## 15. Migration and rollback

- **Migration A** adds two nullable columns and constraints; existing contracts take no plan (Test 64).
  Its `Down` drops the trigger and function first (the `AddContractAccounts` pattern, `:148-152`),
  restores `ux_account_movements_accrual_period` and `ck_account_movements_accrual_has_period`, and drops
  the columns. PV2 must pass before it is applied to Supabase.
- **Migration B** creates five tables, inserts the counter's single row, and grants. Its `Down` drops
  triggers and functions, then tables.
- **Before the first real recibo**, rollback is lossless. **After it**, rollback is not a schema
  operation: issued numbers and payments are agency records. A defect is fixed forward; a wrong recibo
  is voided, never deleted (proposal §9).
- **Preconditions to announce:** Docker running for slices 2, 5, 6; Supabase restored, and only Tomás
  connecting, when either migration is applied to the live database.

---

## 16. Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| A report of "money received" reads movement dates and misses an early payment until its due date | Medium | D2 named here; `collections.received_on` is the received date, and the doc comments on `Payment` say so |
| The 7-day bound is wrong for the agency | Medium | Q1; one constant plus one trigger line |
| Counter contention under load | Low | ~100 contracts; the lock is held for two inserts and a commit (§2) |
| A deadlock between two commits inserting accruals in different orders | Low | Accruals appended in period order; Postgres aborts one with `40P01`, mapped to `ConcurrentChange`, nothing written |
| Migration A fails on Supabase because a stray row breaks a new constraint | Low | PV2 before applying |
| The header snapshot misses a field `recibo-pdf` later needs | Medium | Snapshot columns are additive; issued recibos keep what they had |
| A lone `Payment` inserted by raw SQL without its collection | Low | Named gap (§4), same as every movement kind; both roles are the agency's own staff |
| Voiding by either role is abused | Low | Reason, user and time recorded by the database; Q2 |

---

## Key Learnings

1. Dating an early-written rent accrual on its due date, rather than on the payment date, makes it
   identical to the row lazy accrual would write, so the existing unique index settles any race with
   no reconciliation logic.
2. In this project both recargo and the mora worklist take a period's due date from its accrual
   movement's own date, so changing that date for any reason silently moves the surcharge clock.
3. The application already connects to Supavisor in session mode on port 5432, so the transaction-mode
   pooler concern in the proposal applies only as a defence-in-depth argument.
4. "One live row per key" over an append-only table cannot be a partial unique index on a flag; an
   attempt number with a unique index and an insert trigger expresses it without any update.
5. The existing lazy materialiser walks from the contract's start date and would accrue the whole
   history of a contract that predates the system, so a cutover floor is required before opening
   balances are loaded month by month.
