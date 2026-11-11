# Proposal: mora (the overdue-accounts worklist)

Written 2026-11-11, from `openspec/changes/mora/exploration.md`. Vocabulary: an account is **in mora** when it holds an
unpaid period past its due date; an **opening-balance month** is a month a pre-existing contract owed at go-live, typed
by `carga-inicial`; a **difference-only period** is a period whose only unpaid charge is a late adjustment's difference.

Rules come from the `contract-account`, `lease-contract`, `access-control` and `desktop-shell` specs. This proposal cites
them; it does not restate them.

**What to read (Tomás):** §1 Intent, §2 Scope, §5 Decisions and §9 Size. **Skim** §4 Approach. **Skip** §6 to §8 and §10
unless you want the detail.

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
9. Size Forecast
10. Success Criteria

---

## 1. Intent

The surcharge is 2% a day, simple and uncapped: it doubles a debt in fifty days. The specification's answer was never a
cap but visibility, "early enough that nobody reaches 200 days" (`contract-account/spec.md:282-300`). The query that
gives that visibility was written in October and nobody can see it: it is not connected to the application and has no
screen.

It also has a hole. An account whose oldest unpaid month was typed as an opening balance, or holds only an adjustment
difference, **vanishes from the list entirely**, even while later months are overdue (exploration G3, G4). The first case
is exactly what `carga-inicial` will load: the tenant furthest behind would be the one never shown.

After this change, Admin and Empleado open a list of every account in mora, worst first, with who owes, how long, how
much and how much of it is recargo, and nobody falls off it.

**Success, in one line:** every account that owes an overdue period appears on the list, and the agency sees it without
opening accounts one at a time.

---

## 2. Scope

### In Scope

- **Close the hole (G3, G4):** every unpaid period counts toward the list with a due date, whatever charge it holds.
- **Rows the operator can act on:** tenant, unit and contract handle beside the existing figures (days overdue, owed,
  recargo, frozen date). Today a row carries only ids (`MoraAccount.cs`).
- **The label the balance owes:** what the figures cover and what they do not (tasa, seguros, otros), as `MoraAccount.cs:14-18`
  and "The Balance Names What It Covers" require.
- **Registration** of the query in the session (G1), with the agency's recargo terms the cash desk already reads.
- **A view-model** with its tests, and a navigation entry for both roles.
- **The view**, built from Mateo's theme in the same batch as `gestion-de-alquileres` S12.

### Out of Scope

| Not here | Where it goes, and why |
|---|---|
| Sending any notice, by email or otherwise | `notificaciones`: waits on `scheduler-mechanism` (`openspec/config.yaml:12-16`) and on `carga-inicial` (`gestion-de-alquileres/proposal.md:75`) |
| Who gets warned (tenant, codeudor, owner) | Same: a list shows, it warns nobody. Not found in any owner answer |
| Making the query cheaper (G7) | About a second or two at ~100 contracts; revisit only if measured slow |
| Answering for a past date (G6) | The list is only ever asked about today. Recorded in the spec so nobody relies on it |
| Changing how much is owed | Nothing here alters a balance, a recargo or a receipt |

---

## 3. Capabilities

### New Capabilities

None.

### Modified Capabilities

- **`contract-account`**: "Accounts in Mora Are Queryable" gains the cases the exploration found (opening-balance month,
  difference-only period) and the row's identifying fields.
- **`desktop-shell`**: one more screen, for both roles.

---

## 4. Approach

### 4.1 Prove the hole before closing it

G3 was found by reading code, not by running it. The first task seeds an opening-balance month followed by unpaid rent and
asserts the account is **present**. An absence test cannot catch this class of bug: a broken list also returns absence
(`archive/2026-10-10-cuenta-corriente/archive-report.md:145-147`).

### 4.2 One due date for every unpaid period, inside the query

The list currently counts days only from a rent accrual (`MoraWorklistQuery.cs:90-96`). It will take the oldest unpaid
period's due date from whichever charge that period holds:

- a **rent accrual** or an **adjustment difference**: the movement's own date, which is already its due date
  (`AccountMovement.cs`, both factories take `dueOn`);
- an **opening-balance month**: that month's due date on the contract, computed by the existing
  `AccrualSchedule.DueDateFor(period, contract.DueDay)` that rent already uses, never by a second formula. The movement's
  own date is the day it was typed at go-live, which would make a debt of many months look a few days old.

The change stays inside `MoraWorklistQuery`. `ContractAccount`, which the cash desk reads, is not touched (Decision 3).

### 4.3 Identifying the row

The view-model needs a name, not a GUID. The query will carry the tenant, the unit and the contract handle, read the same
way `IAccountDirectory` already reads them for the cash desk search, so the two screens name an account identically.

### 4.4 Logic now, the view with Mateo's batch

Same rule as `gestion-de-alquileres` §4.2: the port, the registration and the view-model do not depend on how the screen
looks, so they are built and tested now; the XAML waits for the theme.

---

## 5. Decisions

| # | Decision | Held | Why |
|---|---|---|---|
| 1 | Its own small change, not folded into `gestion-de-alquileres` S12 | **Moderate** | That change listed mora as out of scope and the next one (Tomás, 2026-11-04). Reopening its spec would cost more than a short new one |
| 2 | An opening-balance month counts its days from that month's due day on the contract | **Moderate** | The only date that measures how late the debt is. The typed figure already includes its mora, so the row's recargo for that month stays as the cash desk shows it: none projected (`cobranza-y-recibos` Decision 31). Reopen if Nicolás counts it otherwise |
| 3 | The fix lives in `MoraWorklistQuery`; `ContractAccount` is not changed | **Firm** | `ContractAccount` feeds the cash desk and the receipts. Changing it to fix a list risks figures the agency already prints |
| 4 | Days overdue count from the due date itself, not after grace days (G5) | **Firm** | The agency's grace is 0 (`appsettings.json`, `Recargo:GraceDays`; `RecargoTerms.cs:26-31`: the receipts count from the due day). With 0 the two readings agree; the spec records which one wins if a payment plan ever carries grace |
| 5 | Both roles see the list | **Firm** | `access-control`: both roles do the same operational work. The Users screen's Admin-only gating must not be copied |
| 6 | The list shows; it warns nobody and changes nothing | **Firm** | Sending is `notificaciones`. A read-only screen cannot charge anyone wrongly |
| 7 | A row opens that account at the cash desk | **Weak** | Cheap with existing navigation, and the operator's next step is usually to see the detail. Drop it if it complicates the view |

---

## 6. Affected Areas

| Area | Files |
|---|---|
| Domain | `src/Inmobiliaria.Domain/Accounts/MoraAccount.cs` (identifying fields) |
| Infrastructure | `src/Inmobiliaria.Infrastructure/Persistence/MoraWorklistQuery.cs`; `src/Inmobiliaria.Infrastructure/Access/SessionServices.cs` |
| Presentation | a new `Mora/` folder; `Shell/NavigationEntry.cs`, `Shell/ShellViewModel.cs` |
| Desktop | the view, with Mateo's batch |
| Tests | `tests/Inmobiliaria.Infrastructure.Tests/MoraWorklistQueryTests.cs`, `tests/Inmobiliaria.Presentation.Tests/` |
| Specs | `contract-account`, `desktop-shell` |

---

## 7. Risks

| Risk | Mitigation |
|---|---|
| **An account that owes stays invisible** (high-risk item 6: no test catches it today) | Presence tests for each kind of oldest unpaid period, each proven by reverting the fix and watching it fail |
| **The fix changes what the cash desk shows** | Decision 3 keeps it out of `ContractAccount`. A before-and-after dump of every desk figure, as S10 did, must match byte for byte |
| **Navigation entries collide with S12**, which adds five entries to the same files | Mora's entry lands after S12's 12.2, or both are written together |
| **Wrong recargo terms reach the list** | It reads the same configured terms as the cash desk, never a constant |

---

## 8. Rollback Plan

No migration is expected. Reverting the PR restores the current list and removes the screen; no stored data changes.

---

## 9. Size Forecast

Advisory, about 400 authored lines per slice.

| Slice | Content | Estimate |
|---|---|---|
| M1 | Close the hole, identifying fields, registration, view-model | ~200–300 |
| M2 | The view, with Mateo's batch | ~150 |

One PR for M1 now; M2 with S12. Moderately held: identifying the row may need a join the directory already does.

Decision needed before apply: No
Chained PRs recommended: No
400-line budget risk: Low

---

## 10. Success Criteria

- An account whose oldest unpaid month is an opening balance appears, counted from that month's due day.
- An account whose oldest unpaid period holds only an adjustment difference appears.
- Every row names its tenant and unit, and says what the figures cover.
- An account up to date, or cancelled, does not appear; a frozen account still shows its frozen figure.
- Every figure the cash desk shows is unchanged.
- Both roles reach the list from the menu.
- Full suite green with Docker running, no skipped tests.
