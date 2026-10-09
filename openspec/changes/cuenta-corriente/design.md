# Design: cuenta corriente

Technical design for the `contract-account` capability, from `spec.md` in this folder. Written
2026-10-09.

---

## Claims checked against the code

Every statement this design makes about what already exists was read from the file named beside it,
per `openspec/config.yaml`'s rule that an invariant is verified against its constructor and never
taken from a comment.

| Claim | Evidence | Verdict |
|---|---|---|
| A contract knows when it started and its current canon | `Leasing/Contract.cs` — `StartDate`, `MonthlyRent` | **TRUE** |
| An adjustment records the date its canon took effect, and both canons | `Leasing/RentAdjustment.cs:13-15` — `EffectiveDate`, `PreviousCanon`, `NewCanon` | **TRUE.** This is what makes Decision 1 possible: the canon in force on any past date is derivable from stored data |
| A clause knows its periodicity | `Leasing/AdjustmentClause.cs:16` — `IntervalMonths` | **TRUE** |
| A contract knows the day its rent falls due | `Leasing/Contract.cs` — searched every property | **FALSE.** No such field exists. Earlier drafts of this change asserted "the 10 is the contract's due day, not a constant" three times across three documents; none of them checked. Every calculation here depends on it, so this change now adds it — see Decision 9 |
| Adjustment history is append-only and a correction is a new row | `specs/rent-adjustment/spec.md`, enforced by a database trigger | **TRUE** — the ledger copies this shape rather than inventing one |
| A late adjustment generates no retroactive charge | `specs/rent-adjustment/spec.md`, "A Late Adjustment Is Not Charged Retroactively" | **TRUE** — the whole design rests on it |
| `Contract.End` has no solvency check | `Leasing/Contract.cs:254-269` | **TRUE, and it stays that way.** Spec Decision 11 |
| A due-worklist query already exists for adjustments | `tests/Inmobiliaria.Infrastructure.Tests/DueAdjustmentQueryTests.cs` | **TRUE** — the mora worklist follows its shape |
| `RoundingRule` has one member and anticipates a second | `Leasing/RoundingRule.cs` — `TruncateToWholePeso` | **TRUE** — reused, not duplicated |

---

## Decision 1 — rent accrues lazily, with catch-up, and no scheduler

**A period's accrual is materialised the first time anything asks about it**, not by something that
wakes up monthly. Asking means reading a balance, querying the mora worklist, or collection asking
what to charge. The operation is *"materialise every period due up to today that is not yet
written"*, so it is idempotent and self-healing: asked after a three-month silence, it writes three
periods.

**The alternative, and why it lost.** A scheduled job writing each month's accrual needs something
running while the application is closed. Two candidates existed: a Windows Scheduled Task on an
office PC, and Supabase Cron (`pg_cron`), which runs inside Postgres and is documented to schedule
anything from every second to yearly.

Supabase Cron is the better of the two and it genuinely works — but a paused project has no running
Postgres, and the free plan pauses after a week of inactivity. The office closes for two weeks in
January, the project pauses, and January's accrual is never written. Nothing backfills it: the job
simply runs again at its next scheduled time, leaving a silent hole.

**The argument that actually decided it.** A scheduled job that must survive having missed a run has
to compute the periods it missed — which is exactly what lazy materialisation does. **The lazy path
is contained inside any correct scheduled one.** Building it first costs nothing and loses nothing,
and a cron job can later be added as a *trigger* for the same operation without touching it.

**It also refuses to make the model depend on a paid plan.** Supabase Pro removes the pausing, and
the project is likely to move there anyway — for the backups, which matter more. But a design where
the ledger silently stops being correct if somebody lets a subscription lapse is a trap. Under this
decision the system is correct on the free plan, correct on Pro, and correct with no cron at all.

**Consequence for `scheduler-mechanism`:** this change no longer needs that open project decision
resolved. Notifications still will.

### What makes it possible

The canon for a past period is not guessed, it is read: start from `Contract.StartDate` and
`MonthlyRent`, then apply each `RentAdjustment` from its `EffectiveDate`. Materialising a period is
therefore deterministic — the same question asked twice writes the same figure, which is what lets
the operation be safely repeatable.

### What it costs, stated plainly

A read can write. A function named "get the balance" may append rows, which is surprising and must
be named clearly in the code — `MaterialiseDueAccrualsThrough(date)` as an explicit step, never a
side effect hidden inside a getter.

And two readers asking at the same moment could both try to write the same period. The database
settles it: a unique constraint on `(account_id, kind, period)` for accrual movements makes the
second one fail, and the caller re-reads. Not a lock, not a transaction dance — a constraint and a
retry.

---

## Decision 2 — two figures, summed on read, never cached

No stored running total of either figure.

- **Ledger balance** = `SUM(amount)` over the account's movements, filtered by date for a historical
  read. What is written.
- **Amount owed as of a date** = that ledger balance plus `RecargoMath` projected to the same date.
  What the tenant actually owes, and what an operator acts on.

They are different because recargo is deliberately not a movement until frozen or charged. Earlier
drafts called both "the balance", which is a contradiction the spec now names as Decision 15: a
sum of movements cannot include something that is not a movement.

Two methods, two names, and no third thing called "balance".

**Rejected: a cached `CurrentBalance` column.** It is the classic optimisation and the classic bug:
every write path must remember to update it, and the day one forgets, the number is wrong and
nothing says so. The scale does not justify the risk — roughly 25 to 30 movements per contract per
year, so a decade of a hundred contracts is about 30,000 rows, and summing one account's movements
is an indexed read of a few dozen.

Revisit only with a measurement, never with a feeling.

---

## Decision 3 — one movement table with a kind discriminator

A single `account_movements` table carrying `kind`, `amount` (signed), `occurred_on`, an optional
`period`, and an optional `corrects_movement_id`.

**Rejected: a table per kind.** Four tables that all mean "a line in a ledger" would make the
balance a four-way union, and every new kind — the payment movement that collection adds, the
novación freeze — a schema change. The kinds differ in what they mean, not in their shape.

The kinds this change defines:

| Kind | Sign | Written by |
|---|---|---|
| `OpeningBalance` | either | a person, once, only for a contract predating the system |
| `RentAccrual` | debit | materialisation (Decision 1) |
| `ContractualFeeInstalment` | debit | materialisation, from the signing plan |
| `RecargoFrozen` | debit | the novación (Decision 5) |
| `Correction` | either | a person, referencing what it corrects |

`Payment` is deliberately absent: collection adds it. The discriminator is a `CHECK`ed text column,
following `contract_documents.kind`, so adding one is a migration of one line rather than a table.

**Append-only is enforced by a database trigger**, copying `rent_adjustments` rather than inventing
a second mechanism. Spec tests 3 and 4 attack it with raw SQL, which is the only way to prove the
rule holds against something other than this application.

---

## Decision 4 — recargo is a pure function, and its terms are arguments

```
RecargoFor(amountOwed, dueDate, asOf, terms) -> decimal
```

`terms` carries the rate and the grace period. Nothing in the function body knows that the rate is
2% or that the grace is until day 10; both arrive from the obligation being measured. Spec test 19
asserts no literal rate appears in the calculation, and spec Decision 8 is where the rule comes
from.

A lease supplies its terms from the contract. A payment plan, in a later change, supplies its own —
and the function never changes.

**Rejected: a `RecargoPolicy` service resolved from a container.** A pure function taking its inputs
is testable without a fixture, cannot read anything it was not given, and cannot acquire a hidden
dependency later. The project already prefers this shape: `AdjustmentMath` is a static calculation
over supplied values.

Truncation happens once, at the edge, through the existing `RoundingRule.TruncateToWholePeso` —
not re-implemented.

---

## Decision 5 — the novación is a movement, and the freeze is read from it

Signing a payment plan writes one `RecargoFrozen` movement carrying the accrued amount and the
signing date. The recargo calculation then stops at that date: `asOf` is clamped to the freeze date
when one exists.

**Rejected, explicitly and in the spec: a boolean.** A flag cannot answer "frozen since when", and
the date is the entire legal effect — a *novación* extinguishes the obligation rather than pausing
it. As a dated movement, a balance asked for any earlier date still reads correctly, which a flag
makes impossible. Spec tests 20 through 22 pin all three behaviours.

---

## Decision 6 — an ended contract needs no state, because no period comes due

The account carries no "closed" flag. Materialisation asks the contract which periods are due, and a
contract with an `ActualEndDate` has none after it. Payments keep landing because nothing refuses
them.

**Rejected: an `IsClosed` flag on the account.** It would be a second source of truth about
something the contract already knows, and the two would eventually disagree. The same reasoning that
rejected the boolean in Decision 5.

This is what makes spec tests 23 through 25 pass without special cases: rescinding a contract owing
money works because nothing was built to prevent it.

---

## Decision 7 — the oldest unpaid period is a question the account answers

`OldestUnpaidPeriod()` returns the earliest period with an accrual and no settling payment, or
nothing when the account is clear. Collection calls it and posts its payment against the answer; it
never reaches into movements to work this out.

This keeps spec Decision 6 — *the operator does not choose* — enforced in one place rather than
re-implemented by every caller.

---

## Decision 8 — the mora worklist is a query, shaped like the one that already exists

A read-only query returning, per overdue account: days overdue, amount owed, and recargo accrued so
far. It mirrors the due-adjustment worklist (`DueAdjustmentQueryTests`), including the part that
matters most — it is reachable from outside without knowing this capability's internals, so
notifications can consume it.

**It must materialise before it reads.** Under Decision 1 an unread account has unwritten periods,
so a worklist that only read movements would report an overdue account as clear — the exact failure
Decision 1 must not cause. The query materialises through today first, then reads.

**And it reports a frozen account's frozen figure**, not one that kept growing. Spec test 22.

---

## Decision 9 — the due day lives on the contract, defaulting to 10

`Contract` gains a `DueDay` (1–31, default 10). Mora is counted from it.

**This is not an enhancement, it is a missing foundation.** Three documents of this change asserted
that the contract already carried it. It did not. Without it there is no "when does a period fall
due", no day count, and therefore no recargo — nothing in `contract-account` could have been built.

**Why per contract rather than a constant.** The agency applies the 10th to everyone today, so a
constant would work this week. But the owner may want another day for a particular tenant, and the
project already answers this question the same way every time it comes up: the adjustment clause,
the honorarios percentage and the rent split all live on the contract because each contract
negotiated its own. What the paper says, the record carries.

The default is what keeps it free: a column with a default of 10 costs the operator nothing until
somebody needs it.

**Migration:** `ADD COLUMN due_day smallint NOT NULL DEFAULT 10` with a `CHECK (due_day BETWEEN 1
AND 31)`. Every existing row takes 10, which is what they already are in practice. This is the only
`ALTER TABLE contracts` in the change, and spec test 37 asserts no other column on those rows moved.

---

## Where the code goes

| Path | Action | What |
|---|---|---|
| `src/Inmobiliaria.Domain/Accounts/ContractAccount.cs` | Create | The aggregate: its movements, its balance, `OldestUnpaidPeriod()` |
| `.../Accounts/AccountMovement.cs` | Create | One movement; immutable after construction |
| `.../Accounts/MovementKind.cs` | Create | The discriminator |
| `.../Accounts/RecargoTerms.cs` | Create | Rate and grace, supplied by the obligation |
| `.../Accounts/RecargoMath.cs` | Create | The pure function, beside the existing `AdjustmentMath` |
| `.../Accounts/AccrualSchedule.cs` | Create | Which periods a contract owes between two dates, and at which canon |
| `src/Inmobiliaria.Infrastructure/Persistence/Configurations/` | Create | Two configurations, account and movement |
| `.../Persistence/Migrations/` | Create | **One** migration: both tables, the append-only trigger, the unique constraint, and the GRANTs |
| `.../Persistence/InmobiliariaDbContext.cs` | Modify | Two DbSets |
| `src/Inmobiliaria.Domain/Leasing/Contract.cs` | **Modify** | Gains `DueDay`, validated 1–31, defaulting to 10 (Decision 9) |
| `.../Persistence/Configurations/ContractConfiguration.cs` | **Modify** | Maps the column and its CHECK |
| `.../Access/` | — | **Untouched** |

**Not touched:** every existing migration, and anything under `Inmobiliaria.Desktop`.

---

## Testing strategy

| Spec tests | Kind | Why |
|---|---|---|
| 1, 2, 6, 7, 13, 14, 15, 26, 27 | Domain, no database | Arithmetic and rules over in-memory objects |
| 16, 17, 18, 19 | Domain | Recargo, including the one asserting no literal rate exists |
| 20, 21, 22, 23 | Domain | The novación freeze and its dated movement |
| 3, 4, 5 | Integration, Testcontainers | Append-only is a database trigger; raw SQL is the only honest proof |
| 8, 9, 10, 11, 12 | Integration | Opening balance and the canon-in-force rule, against real persistence |
| 24, 25 | Integration | Payments after the contract ended |
| 28, 29 | Integration | The mora worklist, including the frozen figure |
| 30, 31, 32, 33 | Integration | Balance labelling, decimal storage, and the GRANTs shipped with the migration |

Docker is required from the migration slice onward. The first two slices are pure domain and need
nothing.

---

## Size forecast

Review budget is **400 authored lines per slice**.

| Slice | Content | Lines | Docker |
|---|---|---|---|
| 1 | `Accounts` domain: account, movement, kinds, balance, oldest-unpaid + tests | 320–400 | no |
| 2 | `RecargoMath` and `RecargoTerms` + tests | 200–260 | no |
| 3 | `AccrualSchedule`: which periods, at which canon, from the adjustment history + tests | 240–300 | no |
| 4 | EF configurations **and** the migration, with trigger, constraint and GRANTs + schema tests | 340–400 | **yes** |
| 5 | Lazy materialisation and its concurrency retry + tests | 260–320 | yes |
| 6 | The mora worklist query + tests | 180–240 | yes |

**Total 1,540–1,920 authored lines across six slices.** Slice 4 ships its model changes and its
migration together, without exception: splitting them is what made CI red during `users-and-roles`,
and EF refuses to migrate when the model has changes no migration covers.

---

## Open questions

None blocking. Two notes for whoever runs the go-live load rather than for this design:

1. The agency's spreadsheet is keyed by **tenant**; an account here belongs to a **contract**. They
   coincide for a tenant with one contract; for a tenant holding two, somebody decides how the
   figure splits at load time.
2. Adding Supabase Cron later as a trigger for materialisation is a one-line addition and needs no
   change here. Worth doing once the project is on a plan that does not pause — but never a
   dependency.

---

## Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| A reader writes, surprising somebody | Medium | Materialisation is a named, explicit step, never hidden inside a getter |
| Two readers materialise the same period at once | Low | Unique constraint on `(account_id, kind, period)` plus a re-read; the database decides, not a lock |
| The mora worklist reads before materialising and reports an overdue account as clear | **High if missed** | Decision 8 states the order; a test seeds an account nobody has read and asserts it appears |
| Someone later adds a recargo cap, reading "uncapped" as an oversight | Medium | The spec carries the reason inside the requirement, and a test asserts 200 days uncapped |
| The novación is reimplemented as a flag | Medium | Spec test 21 asserts no boolean carries this state |
| A cached balance is added for speed | Low | Decision 2 says revisit only with a measurement |

---

## Key Learnings

1. `RentAdjustment.EffectiveDate` together with `PreviousCanon` and `NewCanon` makes the canon in
   force on any past date derivable from stored data, which is what allows a period to be
   materialised lazily instead of being written when it falls due.
2. A scheduled accrual job that must survive a missed run has to compute the periods it missed, so
   lazy materialisation is contained inside any correct scheduled implementation and building it
   first forecloses nothing.
3. Supabase Cron runs inside Postgres and would schedule this cleanly, but a paused project has no
   running Postgres and the free plan pauses after a week, so a design depending on it would be
   correct only while somebody keeps paying.
4. A lazy materialisation design makes every read-only query a potential writer, so the mora
   worklist must materialise before it reads or it will report an unread overdue account as clear.
5. Rejecting a boolean twice in one design — for the novación freeze and for the ended account — is
   the same reasoning both times: a flag is a second source of truth that cannot answer "since
   when", and the date is the whole of the effect.
