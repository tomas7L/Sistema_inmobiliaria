# Tasks: cuenta corriente

Turns `design.md`'s six slices into ordered, checkable work. It does not re-plan the change.

Domain vocabulary: **recargo** = late-payment surcharge; **mora** = being late; **novación** = the
legal replacement of one obligation by another.

## Review Workload Forecast

| Field | Value |
|---|---|
| Estimated changed lines | 1,620–2,020 (the six slices below, including the due-day work the design added) |
| Session review budget | 400 authored lines per slice |
| Budget risk | Medium — every slice is at or under 400, but slices 1 and 4 sit right on the line |
| Chained PRs recommended | Yes |
| Suggested split | PR1 → PR2 → PR3 → PR4 → PR5 → PR6, forced order |
| Delivery strategy | ask-on-risk |
| Chain strategy | one PR at a time into `develop` |

**Docker is needed from PR4 onward.** PRs 1, 2 and 3 are pure domain: no database, no containers,
nothing to start. **Supabase is only touched once**, after PR4 is merged, and the project is
currently paused — it must be restarted first.

### Suggested Work Units

| Unit | Goal | PR | Focused test command | Needs Docker |
|---|---|---|---|---|
| 1 | `Domain/Accounts`: account, movement, kinds, the two figures, oldest-unpaid | PR 1 | `dotnet test tests/Inmobiliaria.Domain.Tests --filter FullyQualifiedName~Account` | no |
| 2 | `RecargoMath` and `RecargoTerms` | PR 2 | `dotnet test tests/Inmobiliaria.Domain.Tests --filter FullyQualifiedName~Recargo` | no |
| 3 | `Contract.DueDay` and `AccrualSchedule` | PR 3 | `dotnet test tests/Inmobiliaria.Domain.Tests` | no |
| 4 | EF configurations **and** the migration, together | PR 4 | `dotnet test tests/Inmobiliaria.Infrastructure.Tests --filter FullyQualifiedName~SchemaConstraint` | **yes** |
| 5 | Lazy materialisation and its concurrency retry | PR 5 | `dotnet test tests/Inmobiliaria.Infrastructure.Tests --filter FullyQualifiedName~Materialis` | **yes** |
| 6 | The mora worklist query | PR 6 | `dotnet test tests/Inmobiliaria.Infrastructure.Tests --filter FullyQualifiedName~Mora` | **yes** |

---

## Phase 1: Domain/Accounts (PR 1, est. 340–400 lines)

- [x] 1.1 Create `MovementKind.cs`: `OpeningBalance`, `RentAccrual`, `ContractualFeeInstalment`, `RecargoFrozen`, `Correction` — `src/Inmobiliaria.Domain/Accounts/MovementKind.cs`
- [x] 1.2 Create `AccountMovement.cs`: kind, signed amount, `OccurredOn`, optional `Period`, optional `CorrectsMovementId`. Immutable after construction; no setters — `.../Accounts/AccountMovement.cs`
- [x] 1.3 Create `ContractAccount.cs`: the aggregate, its movements, `Append(movement)`. `Append` MUST reject a second `OpeningBalance` (spec test 9) — `.../Accounts/ContractAccount.cs`
- [x] 1.4 `ContractAccount.LedgerBalance(asOf)`: sum of movements dated on or before `asOf` — **spec Decision 15, this is NOT the amount owed** — `.../Accounts/ContractAccount.cs`
- [x] 1.5 `ContractAccount.OldestUnpaidPeriod()`: earliest period with an accrual and no settling payment, or none — `.../Accounts/ContractAccount.cs`
- [x] 1.6 `AccountTests.cs`: **[Spec tests 1, 6]** one account per contract; the ledger balance is the sum of movements — `tests/Inmobiliaria.Domain.Tests/AccountTests.cs`
- [x] 1.6b **[Spec test 2]** A tenant holding two contracts has two accounts, and "what this tenant owes" is produced by summing them on demand. The schema half — that no table keyed by tenant exists to hold such a total — is asserted in task 4.11b
- [x] 1.7 **[Spec test 7]** A historical ledger balance counts only movements dated on or before the date asked
- [x] 1.8 **[Spec tests 8, 9, 10]** An account with no movements is valid and reads zero (a contract created in the system); a pre-existing contract carries exactly one `OpeningBalance`; a second one is refused; it never changes after later movements
- [x] 1.9 **[Spec test 5]** A correction is a new movement carrying the difference and naming what it corrects; the original is untouched
- [x] 1.10 **[Spec tests 13, 14, 15]** A partial payment is refused and records nothing; `OldestUnpaidPeriod` returns July when July, August and September are unpaid; the other two keep their own due dates
- [x] 1.11 **[Spec test 27]** Every monetary member is `decimal`; no `double` or `float` appears in `Accounts`
- [x] 1.12 **[Guardrail]** `ArchitectureGuardTests` still green: `Domain/Accounts` adds no reference to EF Core, Npgsql or WPF
- [x] 1.13 **[Isolation check]** `dotnet build` + `dotnet test tests/Inmobiliaria.Domain.Tests` on this branch alone

## Phase 2: Recargo (PR 2, est. 200–260 lines)

- [x] 2.1 Create `RecargoTerms.cs`: daily rate and grace day, supplied by the obligation. **No default values** — a caller that forgets must not silently get 2% — `src/Inmobiliaria.Domain/Accounts/RecargoTerms.cs`
- [x] 2.2 Create `RecargoMath.cs`: `For(amountOwed, dueDate, asOf, terms)`, a pure static function beside the existing `AdjustmentMath`. Truncates once at the edge — `.../Accounts/RecargoMath.cs`. **Deviation:** the task said to truncate through `RoundingRule.TruncateToWholePeso`, but that enum is stored on `AdjustmentClause` and no code reads it; the project truncates with `decimal.Truncate` directly (`AdjustmentProposal.cs:64`, `RentAdjustment.cs:98`). Followed the real pattern. Unifying them is its own change.
- [x] 2.3 `RecargoMath` accepts an optional freeze date; when present, `asOf` is clamped to it (design Decision 5)
- [x] 2.4 **[Spec test 16]** The same inputs on three different `asOf` dates yield a growing figure and create nothing
- [x] 2.5 **[Spec test 17]** 200 days overdue yields the full simple 2% per day with **no ceiling** — assert the exact figure, not merely that it is large
- [x] 2.6 **[Spec test 19 — the standing guard]** No literal rate appears in `RecargoMath`. Asserted over the source text, as written. A reflection-only version was tried first and is strictly weaker: it sees declared members, so it catches a `const` and misses `amountOwed * 0.02m * days` written inside the method. The guard was then proven by injecting that exact literal and watching it fail. The rate arrives in `RecargoTerms`
- [x] 2.7 **[Spec test 26]** A recargo computing to 47,860.80 records as 47,860
- [x] 2.8 **[Spec tests 20, 22]** With a freeze date of 2026-11-15, a computation `asOf` 2026-12-20 returns exactly what had accrued to 2026-11-15; a computation for a date **before** the freeze is unaffected by it
- [x] 2.9 **[Isolation check]** Build and run the domain suite on this branch alone

## Phase 3: Due day and the accrual schedule (PR 3, est. 280–340 lines)

- [x] 3.1 Modify `Contract.cs`: add `DueDay` (1–31, default 10), validated in the constructor. **This is the field three documents wrongly claimed already existed** — design Decision 9 — `src/Inmobiliaria.Domain/Leasing/Contract.cs`. **Structural constraint, proven before writing any code:** `DueDay` MUST be get-only. `{ get; private set; }` is mapped by EF convention the instant it is added, which puts the model out of step with the last migration and fails every Testcontainers test at the fixture — the users-and-roles PR2a failure, repeated. `{ get; }` enters the model only when a configuration names it, the way `StartDate` does, so the column and its migration ship together in PR 4. Verified both ways with `dotnet ef migrations has-pending-model-changes`: get-only reports no changes, settable reports pending.
- [x] 3.2 **[Spec tests 34, 36]** A contract created without a due day reads 10; a due day of 0 or 32 is refused
- [x] 3.3 **[Spec test 35]** A contract created with a due day of 5 reads 5
- [x] 3.4 Create `AccrualSchedule.cs`: given a contract and a date, which periods are due and **at which canon**. The canon is read from `Contract.MonthlyRent` plus each `RentAdjustment.EffectiveDate` / `NewCanon` — never guessed — `.../Accounts/AccrualSchedule.cs`
- [x] 3.5 **[Spec tests 11, 12]** Three periods accrued before a late adjustment keep the old canon; the first period after confirmation uses the new one
- [x] 3.6 **[Spec test 25]** A contract with an `ActualEndDate` has no periods due after it — this is what makes the ended account need no flag (design Decision 6)
- [x] 3.7 **[Spec tests 18, 38]** Two contracts with due days 5 and 10 produce different day counts for an equally overdue period
- [x] 3.8 **[Guardrail]** Re-run `ArchitectureGuardTests` after the `Contract.cs` edit
- [x] 3.9 **[EF model check — not just build]** `dotnet ef dbcontext info -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure`, or the existing `EfModelValidationTests`. `dotnet build` does not validate the EF model, and `Contract` just changed. Ran `dotnet ef migrations has-pending-model-changes` as well, which is strictly stronger: `dbcontext info` and `EfModelValidationTests` both prove the model BUILDS, while only this command proves it still MATCHES the last migration — which is the thing that actually breaks CI. It needs no database and no Docker.
- [x] 3.10 **[Isolation check]** Build and run the domain suite on this branch alone

## Phase 4: Persistence and the migration (PR 4, est. 360–400 lines)

**Needs Docker running.**

- [x] 4.1 Create `ContractAccountConfiguration.cs` and `AccountMovementConfiguration.cs`. **Two EF behaviours had to be configured explicitly, both found by failing tests, not by reading:** (a) `Property(x => x.Id).ValueGeneratedNever()` on both types — the ids come from `Guid.NewGuid()` in the domain factories, and EF uses "is the key generated by me?" to decide whether an untracked entity reached through a navigation is new. Without it EF marked a new movement `Modified`, issued an UPDATE matching no row, and failed with a concurrency error. It never surfaced before because every existing test saves parent and children in one unit of work; appending to an ALREADY-PERSISTED account is the whole point of PR 5, so this would have broken the next slice. (b) the blanket `SetAfterSaveBehavior(Throw)` loop must skip shadow properties: EF assigns the shadow `AccountId` itself during fixup, so throwing on it rejects every append after the first. Nothing is lost — the append-only trigger refuses any UPDATE to the table whatever the column, and neither role holds UPDATE on it — `src/Inmobiliaria.Infrastructure/Persistence/Configurations/`
- [x] 4.2 Modify `ContractConfiguration.cs`: map `due_day` with `CHECK (due_day BETWEEN 1 AND 31)`
- [x] 4.3 Modify `InmobiliariaDbContext.cs`: two DbSets; update the class comment for the new table count
- [x] 4.4 Generate the migration, then hand-edit: `account_movements` gets a **unique index on `(account_id, kind, period) WHERE period IS NOT NULL`** — this is what settles two readers materialising the same period (design Decision 1). **Deviation, and the task as written was wrong:** the index shipped as `UNIQUE (account_id, period) WHERE kind = 'RentAccrual'`. This task dropped the design's own qualifier — design.md Decision 1 says "for accrual movements" — and `WHERE period IS NOT NULL` applies it to every kind. That would forbid correcting one month twice, and worse, it would reject a second payment against one period, which is exactly what a payment plan is. Collection adds that kind next change, so the broad index was a trap waiting for it. A test asserts a period CAN be corrected twice.
- [x] 4.5 Hand-edit: the **append-only trigger** on `account_movements`, copying the one on `rent_adjustments` rather than inventing a second mechanism
- [x] 4.6 Hand-edit: `ALTER TABLE contracts ADD COLUMN due_day smallint NOT NULL DEFAULT 10` with its CHECK. **This is the only `ALTER TABLE contracts` in the whole change**
- [x] 4.7 Hand-edit: GRANTs for both new tables, for both application roles, **in this same migration** — the `new-table-ships-with-grants` convention in `openspec/config.yaml`
- [x] 4.8 Write `Down`: drop the two tables, drop the trigger, drop `due_day`
- [x] 4.9 **[Spec tests 3, 4]** `SchemaConstraintTests`: a raw SQL `UPDATE` and a raw `DELETE` against a movement row are each refused by the trigger
- [x] 4.10 **[Spec test 37]** Every pre-existing contract carries `due_day = 10` after the migration, and no other column on those rows changed
- [x] 4.11 **[Spec test 33]** Connecting as each application role confirms it can perform exactly its intended operations on both new tables — none silently unreachable
- [x] 4.11b **[Spec test 2, schema half]** No table keyed by tenant exists to store a combined balance; the tenant total is only ever computed
- [x] 4.12 **[Guardrail — scope]** Diff every earlier migration and its `.Designer.cs` against `HEAD`: all byte-for-byte unchanged
- [x] 4.13 **[Isolation check]** Apply this migration alone on a fresh Testcontainers instance and run the infrastructure suite, with none of PR5 or PR6 present

## Phase 5: Lazy materialisation (PR 5, est. 260–320 lines)

**Needs Docker running.**

- [x] 5.1 Create the port `IAccountMaterialiser.cs` in **`src/Inmobiliaria.Domain/Accounts/`** and its adapter in **`src/Inmobiliaria.Infrastructure/Accounts/`**: `MaterialiseDueAccrualsThrough(accountId, date)`. **An explicit named step — never a side effect hidden inside a getter** (design Decision 1). Port in Domain, adapter in Infrastructure, following the existing `IDueAdjustmentQuery` / `DueAdjustmentQuery` pair — the interface must not live in Infrastructure, or the domain ends up tied to persistence. **Deviations:** the adapter went to `src/Inmobiliaria.Infrastructure/Persistence/`, not `Infrastructure/Accounts/` — that is where `DueAdjustmentQuery` actually lives, and this project has no `Infrastructure/Accounts/` folder. The method is `MaterialiseDueAccrualsThroughAsync`, following the `GetDueAsync` suffix convention. Neither adapter is registered in DI: `IDueAdjustmentQuery` is not either, because nothing consumes these yet — the wiring arrives with the UI pass, so this introduces no gap
- [x] 5.2 It is idempotent: called twice for the same date, the second call writes nothing
- [x] 5.3 It catches up: called after a three-month silence, it writes three periods, each at the canon in force for its own period
- [x] 5.4 Handle the concurrency case: on the unique-index violation from a competing writer, **re-read rather than retry the write** (design Decision 1)
- [x] 5.5 `AmountOwed(asOf)`: materialise through `asOf`, then `LedgerBalance(asOf)` plus `RecargoMath` — **spec Decision 15, the second of the two figures**. **Shape chosen:** `AmountOwed(asOf, terms)` is a PURE method on `ContractAccount`, and materialisation stays the caller's separate explicit step. The account already holds everything the figure needs — each unpaid period's net and the accrual's own due date — and the terms are an argument, so nothing forced it off the aggregate. This also corrects a claim in PR 1's own `LedgerBalance` comment, which said the second figure "needs the contract's terms and therefore does not live on this type"; the premise was true and the conclusion did not follow. Keeping both figures side by side is what makes the distinction visible where somebody might reach for the wrong one
- [x] 5.6 **[Spec test 6, second half]** The amount owed adds the projected recargo while the ledger balance reads unchanged
- [x] 5.7 **[Spec test 27, not 24]** A payment against a rescinded contract's account is accepted and reduces the balance. **Shown with a negative correction, not a payment movement:** the spec puts "the payment movement itself, and taking money" in Out of Scope and says "the payment movement itself is created by the collection change; this capability owns the rule that decides which period it settles". Inventing a Payment kind here would have built the next change's model. What is proved is this capability's actual guarantee — the account refuses nothing because its contract ended
- [x] 5.8 **[Spec test 23]** Ending a contract whose account owes 300,000 succeeds and leaves the account open — assert against `Contract.End`, which this change does **not** modify
- [x] 5.9 **[Spec test 21]** A `RecargoFrozen` movement carries the frozen amount and the signing date; no boolean anywhere carries this state — assert over the model, not only over behaviour
- [x] 5.10 **[Isolation check]** Build and run both suites on this branch alone

## Phase 6: The mora worklist (PR 6, est. 180–240 lines)

**Needs Docker running.**

- [x] 6.1 Create the mora worklist: the port `IMoraWorklistQuery.cs` in **`src/Inmobiliaria.Domain/Accounts/`** and its adapter in **`src/Inmobiliaria.Infrastructure/Accounts/`**. Per overdue account: days overdue, amount owed, recargo accrued. Same port/adapter split and same consumable shape as `IDueAdjustmentQuery` / `DueAdjustmentQuery`, so notifications can call it without knowing this capability's internals. **Deviations:** the adapter went to `Infrastructure/Persistence/`, beside `DueAdjustmentQuery` and `AccountMaterialiser`, not `Infrastructure/Accounts/` — that folder does not exist in this project. `RecargoTerms` is a REQUIRED PARAMETER of the query, not read from anywhere: a contract carries its own due day but not its own rate, and spec Decision 8 forbids a rate constant, so the caller supplies it. The day a contract carries its rate, this signature does not change. The worklist row carries `RecargoFrozenOn` as a DATE, never a bool — PR 5's own `NoBooleanFlag_CarriesFrozenState` test forbids a boolean anywhere in `Domain.Accounts`, so the constraint enforced itself
- [x] 6.2 **It materialises before it reads** (design Decision 8). This is the change's highest-ranked risk: a worklist that only reads movements reports an unread overdue account as clear. **PROVEN BY REMOVING IT:** deleting the materialisation loop fails 4 tests, including 6.3. Note which tests did NOT fail — the two asserting an account is ABSENT still passed, because a broken worklist also returns absence. That is precisely why 6.3 had to be written as a presence assertion over an account with zero movements
- [x] 6.3 **[Spec test 28 — the risk's own test]** Seed an account **nobody has ever read**, with periods overdue, and assert it appears on the worklist with the right figures
- [x] 6.4 **[Spec test 28]** Three accounts overdue by 5, 38 and 200 days all appear, each carrying days overdue, amount owed and recargo
- [x] 6.5 **[Spec test 29]** An account with no unpaid period does not appear
- [x] 6.6 **[Spec test 22, worklist half]** An account frozen by a novación reports its **frozen** figure, not one that kept growing
- [x] 6.7 **[Spec tests 31, 32 — 30 is the floating-point test, covered in PR 1]** Whatever presents a figure states which of the two it is and which concepts it covers; no tasa municipal, seguro or "Otros Conceptos" amount is included
- [x] 6.8 **[Guardrail]** Every test added in this change lives under `tests/Inmobiliaria.Domain.Tests` or `tests/Inmobiliaria.Infrastructure.Tests` — never in `Inmobiliaria.Desktop`, which is excluded from `Inmobiliaria.Core.slnf` and cannot gate the Linux `core` job
- [x] 6.9 **[Isolation check]** Full solution Release build and the complete suite, zero failures and **zero skipped**

---

## Human Follow-Ups (not assigned to any agent)

- [x] H.1 **Restart the Supabase project** — it paused again on the free plan — then apply PR4's migration with your own credentials. No agent connects to that database. **DONE 2026-10-10:** project restored, `INMOBILIARIA_DB` set to the Session pooler string (port 5432 — the transaction pooler holds no session state and fails partway), `dotnet ef migrations list` confirmed three applied and this one pending, then `dotnet ef database update` reported "Applying migration ... Done." Verified in the SQL editor: migration recorded, trigger present, the accrual index present with its `kind = 'RentAccrual'` filter, `due_day` smallint default 10 NOT NULL, and grants exactly INSERT,SELECT for both roles on both tables. **Honest limit:** the production database holds ZERO contracts, so "every pre-existing contract carries due_day = 10" could not be exercised against real rows. The column default and NOT NULL are what guarantee it, and there was nothing to migrate
- [ ] H.2 Decide how a tenant's spreadsheet figure splits across contracts at load time, for any tenant holding more than one. A data-entry decision, not a modelling one
- [ ] H.3 Supabase Cron as a trigger for materialisation is a later, optional addition that needs no change here. Worth doing once the project is on a plan that does not pause — never a dependency

---

## Things this project has learned the hard way

1. **A migration and its EF model changes ship in ONE pull request.** EF refuses to migrate when the
   model has changes no migration covers; splitting them made CI red during `users-and-roles` in a
   way no code could fix. Phase 4 holds both.
2. **A skipped test is not a passing test.** With Docker off, Testcontainers tests skip silently and
   the suite looks green. Report the skipped count; it must be zero.
3. **Never truncate a build summary.** `tail -2` once hid an analyzer warning and broke both CI
   jobs. Grep for the warning *and* error counts, or check the exit code. **This machine's dotnet
   output is in Spanish**: "Advertencia(s)", "Errores", "Superado", "Con error", "Omitido".
4. **xUnit analyzer rules are build errors here.** `Assert.Equal(1, x.Count)` → `Assert.Single`;
   `Assert.Equal(0, x.Count)` → `Assert.Empty`.
5. **`EF1002`**: assign an interpolated string to a `string` variable before passing it to
   `ExecuteSqlRawAsync`.
6. **`dotnet build` does not validate the EF model.** Any slice touching an entity needs
   `dotnet ef dbcontext info` or `EfModelValidationTests`. Phase 3 changes `Contract`.
