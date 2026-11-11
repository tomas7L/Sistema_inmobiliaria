# Exploration: mora (the overdue-accounts worklist)

Done 2026-11-11, read-only. Verified by reading code unless marked **[inferred]**. A sweep covered `src/`, `tests/`,
`openspec/specs/`, every active and archived change, `openspec/domain/`, `docs/` and Engram. A parent spot check re-read
`MoraWorklistQuery.cs:76-102`, `ContractAccount.cs:364-372,478-481` and `design.md:321`.

Paths are relative to the repository root, with `Domain` = `src/Inmobiliaria.Domain/` and
`Infra` = `src/Inmobiliaria.Infrastructure/Persistence/`.

## Finding

The worklist's logic already exists. `cuenta-corriente` PR 6 (2026-10-10) shipped it against
`openspec/specs/contract-account/spec.md:330`, "Accounts in Mora Are Queryable, and the List Is Reminder-Ready". What is
missing is everything that lets an operator see it: no registration, no view-model, no screen. Reading the code also shows
**two ways an account that owes money drops off the list**, one of them hitting exactly the accounts `carga-inicial` will
load.

## What exists

| Piece | Where |
|---|---|
| Port | `Domain/Accounts/IMoraWorklistQuery.cs`: `GetAccountsInMoraAsync(asOf, RecargoTerms, ct)`, oldest debt first |
| Row | `Domain/Accounts/MoraAccount.cs`: days overdue, amount owed, recargo so far, `RecargoFrozenOn` (a date, never a bool) |
| Implementation | `Infra/MoraWorklistQuery.cs` (129 lines). Materialises every account before reading (`:40-55`), so an account nobody ever opened still appears |
| Tests | `tests/Inmobiliaria.Infrastructure.Tests/MoraWorklistQueryTests.cs`: 11 tests, rent only. Also `MaterialiserCutoverTests.cs:206-227` (cutover), `ContractCancellationRegistryTests.cs:399` (a cancelled contract drops off) |

Already right, with a test: an ended contract that still owes stays on the list (`MoraWorklistQueryTests.cs:349`); a
novación-frozen account reports its frozen figure (`:250-279`); cutover is respected; order is most days overdue first.

## What is missing

| # | Gap | Evidence |
|---|---|---|
| G1 | **Not registered.** Only tests construct `MoraWorklistQuery`; `IAccountMaterialiser` is not registered either | `src/Inmobiliaria.Infrastructure/Access/SessionServices.cs:43-66`; `archive/2026-10-10-cuenta-corriente/archive-report.md:90-92` |
| G2 | **No view-model and no screen.** `MoraAccount.cs:14-18` says the UI pass owes the label naming what the balance covers | — |
| G3 | **A month loaded as an opening balance hides the whole account [inferred, no test].** The row needs a `RentAccrual` on the oldest unpaid period (`MoraWorklistQuery.cs:90-96`), and a monthly opening balance blocks that accrual (`ContractAccount.cs:478-481`, applied by
`AccountMaterialiser.cs:73-77`). A tenant loaded with three old unpaid months disappears even while later months are overdue: the tenant furthest behind is the one never shown | Code reading only; the first task proves it with a failing test |
| G4 | **A period holding only an adjustment difference hides the account.** Same line, since a difference is not a `RentAccrual`. Recorded, not fixed | `gestion-de-alquileres/design.md:321` (F5): "The next change, mora, must read the new kind" |
| G5 | **Days overdue ignore the grace days** the recargo applies (`RecargoMath.cs:55`), so an account inside the grace window shows days overdue with recargo 0 [inferred] | `MoraWorklistQuery.cs:98-102` |
| G6 | **`OldestUnpaidPeriod()` ignores `asOf`** while `LedgerBalance(asOf)` honours it [inferred]. Harmless when asked about today; wrong when asked about a past date | `ContractAccount.cs:364-372` |
| G7 | **Cost is linear in accounts**: one materialisation each, about a second or two at ~100 contracts. Accepted when written | `archive-report.md:96-98` |

G3 and G4 share one cause: the worklist takes a period's due date only from its rent accrual. Fixing that once, by
giving every charge that carries a period a due date the list can count from, closes both.

## What the owner said

- 2% per day, simple, from the day of mora, no cap. At about three months he refers the case to lawyers; cláusula QUINTA
  allows termination after two unpaid months (`openspec/domain/respuestas-del-dueno.md:348-349`).
- The real brake is making debt visible early (`contract-account/spec.md:282-300`). That is the point of this list.
- **Not found anywhere:** who is warned (tenant, codeudor, owner), and whether "in mora" starts the day after the due date
  or after the grace days. Both are agency questions only if the spec cannot settle them from the contract.

## Out of this change

- Sending anything. That is `notificaciones`, which waits on `scheduler-mechanism` (`openspec/config.yaml:12-16`) and on
  `carga-inicial` (`gestion-de-alquileres/proposal.md:75`).
- Making the query cheaper (G7), unless measurement says otherwise.

## Dependency order

1. Prove G3 and G4 with failing tests, then fix the due-date source. No migration expected [inferred]: the dates already
   exist on the movements.
2. Settle G5 and G6 in the spec.
3. Register the query (G1) and write the view-model with its tests. Both are independent of how the screen looks.
4. The view, with Mateo's theme, in the same batch as `gestion-de-alquileres` S12.

Steps 1 to 3 need Docker (Testcontainers). None needs Supabase.

## Size

About 150–250 authored lines for steps 1–3, plus the view. Moderately held: G3's fix may touch the account's
due-date logic, which the cash desk also reads.

## Could not be determined

- Whether G3 truly reproduces: no test seeds an opening-balance month followed by unpaid rent.
- Whether the fix for G3 and G4 changes anything the cash desk already shows. It shares `ContractAccount`, so the spec
  must require an unchanged-output check against the current code.
