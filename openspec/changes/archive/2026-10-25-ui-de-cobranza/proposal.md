# Proposal: ui-de-cobranza (the cash desk, the recibo on paper, and go-live setup)

Written 2026-10-16, from the exploration in `openspec/changes/ui-de-cobranza/exploration.md` (mirrored
in Engram `sdd/ui-de-cobranza/explore`, observation #125). Domain vocabulary, glossed once:
**cobranza** = collection; **recibo** = receipt; **locatario** = tenant; **propietario** = owner;
**recargo** = late-payment surcharge; **tasa municipal**, **seguro**, **Otros Conceptos** = the lines
typed at closing; **cutover** = the go-live boundary of a contract that predates the system.

Business rules come from `openspec/domain/respuestas-del-dueno.md` (cited as §N) and from the archived
`collection`, `receipt`, `contract-account` and `access-control` specs. This proposal cites them; it
does not restate them.

**What to read (Tomás):** §1 Intent, §2 Scope, §5 Decisions, §6 Open Items, §10 Preconditions and §11
Size Forecast (it ends with the one decision you owe before apply). **Skim** §4 Approach. **Skip** §7
Affected Areas, §8 Risks and §9 Rollback unless you want the detail.

---

## Contents

1. Intent
2. Scope
3. Capabilities
4. Approach
5. Decisions (with how strongly each is held)
6. Open Items
7. Affected Areas
8. Risks
9. Rollback Plan
10. Dependencies and Preconditions
11. Size Forecast
12. Success Criteria

---

## 1. Intent

`cobranza-y-recibos` (slice A) built everything needed to take a payment and issue its two recibos —
but only as callable, tested code. Nobody at the agency can use it: the desktop application is a login
window and an empty main window that receives no session, no database factory and no role
(`src/Inmobiliaria.Desktop/App.xaml.cs:104`, `MainWindow.xaml:9-11`).

This change puts that work in front of the operator. After it, an operator can log in, pick a
contract account, see what is owed and why, take the payment with a confirmation summary, get the two
recibo numbers, see the recibos on screen and print them, and void a recibo by its printed number
within the allowed window. An Admin can also do the two things go-live day requires: set each
pre-existing contract's cutover date and set the recibo starting number.

**Why now:** collection is the first capability the agency uses every day, and the domain under it is
finished, verified and archived. Every later change (initial data load, owner liquidation, PDF) is
more useful once there is a screen to use it from.

**Success, in one line:** on go-live day, with data loaded, Nicolás's staff can take a real payment and
hand over a printed recibo without anyone opening pgAdmin.

---

## 2. Scope

### In Scope

- **Desktop shell**: the main window receives the session after login, shows who is logged in and
  their role, and navigates between the screens below. Admin-only screens are hidden for an Empleado
  (the database still enforces; hiding is convenience, not security).
- **Session-scoped composition**: one place that wires the post-login services from the session and
  its database factory, provable by a test.
- **Account picker**: find a contract account by tenant, unit or contract, showing whether it is
  ready to collect (cutover set, honorarios rate entered, not frozen) before the operator starts.
- **Cash desk**: preview what is owed (period, previous amount owed, ledger figure, both recibo
  drafts, warnings), type the optional received date, period and closing lines, type the handed
  amount, confirm against a summary (convention `confirm-with-a-summary`), commit, show the two
  numbers. A stale or concurrent-change refusal re-previews and shows what changed; it never retries
  silently.
- **Recibo display and print**: the issued locatario and propietario recibos on screen, printed
  through the Windows print dialog. Includes the non-fiscal legend ("Documento no válido como
  factura"). **Printing is always optional** (Decision 13): the desk never prints on its own, and
  any issued recibo can be found later, by its number or from the recent collections, and printed
  or reprinted then.
- **Voiding**: find a recibo by its printed number or from a list of recent collections (with an
  annulled flag), type the reason, preview the reversal, confirm. Pre-gated by role and window in the
  screen; enforced by the domain and the database.
- **Admin go-live screen**: list contracts with their cutover state and whether it is still
  changeable; set a contract's cutover date; show and set the recibo starting number, and whether it
  is still changeable (no recibo issued yet).
- **A domain rule for the cutover date value**: `Contract.SetCutoverDate` today refuses only when the
  account has movement and accepts any date (`Contract.cs:199-210`). Design defines the rule.
- **Cutover date hardened to Admin at the database**: a small hand-written migration extending the
  existing `contracts_cutover_lock` trigger with a `pg_has_role` check, so an Empleado cannot change
  it through pgAdmin either.
- **New read and setup ports** in Domain, implemented and Testcontainers-tested in Infrastructure:
  account directory with readiness flags; recibo reader (by collection id, by number, recent
  collections); go-live setup.
- **New projects, scoped explicitly here** (config `rules.apply`: no csproj outside an accepted change
  that scopes it): `src/Inmobiliaria.Presentation` and `tests/Inmobiliaria.Presentation.Tests`, both
  added to `Inmobiliaria.sln` and `Inmobiliaria.Core.slnf`.
- Agency recargo terms (2%, no grace) supplied from configuration, not a constant
  (`RecargoTerms.cs:14,19-29`).

### Out of Scope

| Not here | Where it belongs | Why |
|---|---|---|
| Loading parties, units, contracts, ContractAccounts and opening balances (incl. cuenta-corriente H.2) from Nicolás's spreadsheet | **Initial data load** — its own change, right after this one | Tomás, 2026-10-16 (obs 127). Go-live day depends on it; this change's code does not |
| PDF generation of the recibo | **`recibo-pdf`** | Tomás, 2026-10-16 (obs 126). Open decision `pdf-library` is **touched but not resolved** (§10) |
| Contract, party, unit and user management screens | Later UI passes | Not needed to collect |
| Opening-balance entry screen | Initial data load | Same as row 1 |
| Owner liquidation and payout | `liquidacion-al-dueno` | Blocked on owner questions |
| Notifications | `notificaciones` | Touches open decision `scheduler-mechanism`, not this change |
| The optional rounding option | A later additive change | §11: optional nicety |
| De-duplicating `BuildPreview` vs `AccountMaterialiser` | Archive follow-up | Internal; the UI does not depend on it |

---

## 3. Capabilities

The spec is **one file**, `openspec/changes/ui-de-cobranza/spec.md`, with a Decisions table, a table
of contents and numbered Tests (working agreement). The capability names below are its sections and
the names under which requirements merge at archive.

### New Capabilities

- **`desktop-shell`** — post-login session handover, role display, navigation, Admin-only screen
  visibility, session-scoped composition, one generic error path for non-refusal failures.
- **`cash-desk`** — account picker with readiness, the preview → handed amount → summary → commit flow,
  stale/concurrent re-preview, recibo display and print, the void flow by recibo number.
- **`go-live-setup`** — the Admin screen: cutover date per contract and the recibo starting number,
  each showing whether it is still changeable.

### Modified Capabilities

- **`contract-account`** — the cutover-date requirement gains a rule for the date's value (design
  defines it) and states that only an Admin may set it.
- **`access-control`** — setting `contracts.cutover_date` becomes Admin-only, enforced by the database
  (trigger with `pg_has_role`), alongside the starting number which already is (`access-control`
  spec :112, :132).
- **`receipt`** — the Purpose sentence "rendering is `recibo-pdf`" (`receipt/spec.md:7-8`) is narrowed:
  on-screen display and system printing are here; PDF stays in `recibo-pdf`. Held **medium** that this
  is a spec change rather than wording only; spec decides.

`collection` is **not** modified: its Purpose ("does not present a screen") stays true of that
capability; the screen is `cash-desk`.

---

## 4. Approach

### 4.1 Where the screen logic lives (approach B)

A new **non-WPF** project, `Inmobiliaria.Presentation` (net10.0, CommunityToolkit.Mvvm, references
`Inmobiliaria.Domain` only), holds the view-models: the cash-desk flow, stale re-preview, refusal
mapping, role gating, busy state. `Inmobiliaria.Presentation.Tests` tests them with faked ports. Both
sit in `Inmobiliaria.Core.slnf`, so they run on the Linux `core` CI job with no Docker.
`Inmobiliaria.Desktop` keeps only XAML, printing and composition.

Why: this screen moves money. With view-models in Desktop (approach A, the `LoginViewModel` precedent),
none of that logic could be tested on the Linux job, because Desktop is excluded from `Core.slnf`.

Rejected: **A** (untestable money-moving logic). Fallback: **C** — pure flow types in Domain or
Infrastructure, thin view-models in Desktop — if Tomás prefers no new projects; it puts UI-shaped
state in the wrong layer and leaves view-model glue untested.

### 4.2 Session-scoped composition

An `AddSessionServices(factory, session)` extension in Infrastructure, mirroring
`AddPreLoginServices`, wires the post-login services. `App.xaml.cs` calls it and builds the main window
with a shell view-model. One `DbContext` per operation (`factory.Create()` per preview/commit flow),
because a `DbContext` is not thread-safe; commands are disabled while busy. The wiring is proved in
`Inmobiliaria.Infrastructure.Tests`, as `CompositionGuardTests` already does for login.

### 4.3 New ports

The domain port `ICollectionDesk` does not let anyone pick an account, print, or void by number
(exploration gaps 1–3). Three new Domain ports, implemented in `Infrastructure/Persistence`:

1. **Account directory** — search accounts by tenant, unit or contract, with readiness flags.
2. **Recibo reader** — the issued recibo pair by collection id and by number; recent collections with
   an annulled flag.
3. **Go-live setup** — contracts with cutover state and lock; set cutover through the domain method;
   starting-number state, including whether any recibo exists.

### 4.4 The cash-desk flow

Pick account → optional period, received date and closing lines → automatic preview → show previous
owed, ledger figure, both recibo drafts and warnings → operator types the handed amount (never
pre-filled) → summary → commit → show numbers → display, with **Print** as an action the operator
may take or skip (Decision 13). A preview with handed amount 0 is
used as the "what is owed" step: it carries all figures and drafts, refused only as not-total.

The handed amount must equal the **ledger figure** (rent + instalment + recargo), not the locatario
recibo total, which also carries tasa municipal, seguro and Otros Conceptos (spec Decision 27). How
the desk labels the two figures when closing lines are typed is a **spec/design item**, not an owner
question.

### 4.5 Printing

The issued recibo is rendered as a WPF `FlowDocument` and sent through `PrintDialog`. No PDF library is
chosen. How one printed sheet lays out the two parts the owner describes (§1: *"uno se lo lleva, otro
se lo quedan ellos"*) is a design item, with Open Item 2 below as its only external dependency.

The layout reproduces the agency's own recibo design (Open Item 2) on **blank paper**, and is sized
from the paper chosen in the print dialog rather than fixed to A4 (Decision 14): A4 is the default,
and a different generic size (Carta, Oficio, A5) reflows the same content instead of clipping it.
No layout editor and no pre-printed-form positioning.

### 4.6 Cutover hardening

A hand-written migration replaces the `contracts_cutover_lock` trigger function so that a change to
`cutover_date` is refused unless the session holds `inmobiliaria_admin`, in addition to the existing
one-way rule. **No EF model change** is expected, so there is no snapshot coupling; if design finds
one, model and migration ship in the same PR (house rule). `dotnet ef migrations
has-pending-model-changes` is run on that slice either way.

---

## 5. Decisions

Strength: **strong** = decided by Tomás or verified fact; **medium** = well-reasoned team call;
**weak** = act on it only provisionally.

| # | Decision | Source | Strength |
|---|---|---|---|
| 1 | The **Admin-only go-live screen** (cutover date per contract, recibo starting number) is in this change. Both values are entered on go-live day. | Tomás, 2026-10-16 (Engram obs 123) | Strong |
| 2 | **Recibo printing is in**: on-screen recibo plus system print via WPF `FlowDocument`/`PrintDialog`, no PDF library. PDF stays in `recibo-pdf`. | Tomás, 2026-10-16 (obs 126) | Strong |
| 3 | **Initial data load is out** (parties, units, contracts, ContractAccounts, opening balances from the spreadsheet, incl. cuenta-corriente H.2) — its own change right after this one. | Tomás, 2026-10-16 (obs 127) | Strong |
| 4 | **`contracts.cutover_date` is Admin-only at the database**, by a small hand-written migration extending `contracts_cutover_lock` with a `pg_has_role` check. Tomás applies it to Supabase. | Tomás, 2026-10-16 (obs 128); evidence: contracts has table-level `GRANT UPDATE` to both roles (`AddUsersAndRoles.cs:161`) | Strong |
| 5 | Contracts take effect on the 1st of a month; cobranza H.7 closed with its default. Context only. | Tomás, 2026-10-16 | Strong |
| 6 | **Approach B**: view-models in a new non-WPF `Inmobiliaria.Presentation`, tested in `Inmobiliaria.Presentation.Tests` on the Linux job. Fallback C. | Exploration | **Medium (~65%)** |
| 7 | **Session-scoped composition** via `AddSessionServices(factory, session)` in Infrastructure; one `DbContext` per operation. | Exploration; `AddPreLoginServices` precedent | Strong (technical) |
| 8 | **Three new Domain ports** (account directory, recibo reader, go-live setup), implemented in Infrastructure. Without them neither the picker, print nor void can exist. | Exploration gaps 1–3 | Strong |
| 9 | A **domain rule for the cutover date value** is added; design defines it. | `Contract.cs:199-210` has none | Strong that one is needed; the rule itself is open to design |
| 10 | The desk labelling of ledger figure vs. recibo total is a spec/design item, not a question for Nicolás. | Spec Decision 27 | Medium |
| 11 | Recargo terms (2%, no grace) come from configuration, not a constant. | `RecargoTerms.cs:14` has no default by design | Strong |
| 12 | Hiding Admin screens from an Empleado is convenience; the database is the enforcement. | Config `credential-exposure`; access-control spec | Strong |
| 13 | **Printing is optional and repeatable.** Nothing prints automatically after a collection or a void; the operator prints on demand, then or later, from the recibo found by number or in the recent list. How a reprint of a voided recibo is marked is a spec item. | Tomás, 2026-10-22 | Strong |
| 14 | **The print layout follows the chosen paper size**, A4 by default, reproducing the agency's own recibo design on blank paper. No layout editor, no pre-printed stationery. | Tomás, 2026-10-22 (A4, own design); size adaptation proposed by the assistant | Strong on A4 + own design; **medium** on how both parts fit a smaller sheet (design) |

---

## 6. Open Items

| # | Item | For whom | Blocks | Default if not answered |
|---|---|---|---|---|
| 1 | **Chain strategy**: `stacked-to-main` or `feature-branch-chain` (§11). | **Tomás** | `sdd-tasks` / `sdd-apply` | None — not chosen here |
| 2 | ~~Blank paper or pre-printed stationery?~~ **Answered 2026-10-22 (Tomás):** A4, with the agency's own design. **Sample received the same day**: A4 landscape, original and duplicate side by side, a fixed list of lines printed even at $0, amount in words. Its layout is described in Engram (`discovery/recibo-paper-layout`) without personal data; the sample itself never goes into the repo. What the fixed lines mean for this change is settled in the spec. | Tomás | Nothing | — |
| 3 | The value rule for the cutover date (e.g. bounds, first-of-month). | Design | S2 | Design proposes; Decision 5 is the context |
| 4 | Labelling of ledger figure vs. recibo total at the desk. | Spec/design | S3 | Show both, named |
| 5 | ~~Non-fiscal legend still pending the contador (archived `receipt` Decision 10, weak).~~ **Closed 2026-10-16:** the contador confirmed the recibo is non-fiscal; living `receipt` spec updated. | Contador | Nothing in this change | The legend prints as specified |

No item needs Nicolás. Item 2 does not block the spec.

---

## 7. Affected Areas

| Area | Action | What |
|---|---|---|
| `src/Inmobiliaria.Presentation/` | **Create (new csproj)** | View-models: shell, picker, cash desk, void, go-live |
| `tests/Inmobiliaria.Presentation.Tests/` | **Create (new csproj)** | View-model tests with faked ports |
| `Inmobiliaria.sln`, `Inmobiliaria.Core.slnf`, `Directory.Packages.props` | Modify | Add the two projects; package versions if needed |
| `src/Inmobiliaria.Desktop/` | Modify | `App.xaml.cs` composition, `MainWindow` shell, new views, `FlowDocument` printing, appsettings recargo terms |
| `src/Inmobiliaria.Domain/` | Modify/Create | Three new ports; cutover-date value rule on `Contract` |
| `src/Inmobiliaria.Infrastructure/` | Modify/Create | `AddSessionServices`; port implementations under `Persistence/` |
| `src/Inmobiliaria.Infrastructure/Persistence/Migrations/` | Create | Hand-written migration extending `contracts_cutover_lock` |
| `tests/Inmobiliaria.Domain.Tests/` | Create | Cutover value rule |
| `tests/Inmobiliaria.Infrastructure.Tests/` | Create | Port implementations, composition guard, cutover trigger refuses an Empleado |

**Not touched:** `CollectionDesk`, `CollectionPlanner`, `VoidingPlanner`, recibo numbering, the ledger.

---

## 8. Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| A money-moving flow ships with untested logic | Med (High under approach A) | Approach B; refusal mapping, stale re-preview and role gating tested in `Presentation.Tests` |
| A stale preview committed, or retried silently | Medium | Commit already re-previews (design D21); the VM shows the difference and asks again; a test proves no silent retry |
| Handed amount compared with the recibo total instead of the ledger figure | Medium | Both figures shown, named; spec Decision 27; a test types closing lines and pays the ledger figure |
| Readiness gaps (rate never entered, cutover not set, no starting number) make every commit refuse on go-live day | Medium | Picker shows readiness up front; go-live screen shows starting-number state |
| Non-refusal failures (unmapped `PostgresException`, missing header data) crash the window | Medium | One generic error path in the shell; a test |
| The cutover trigger change locks out the Admin or breaks the one-way rule | Low-Med | Infrastructure tests for Admin allowed, Empleado refused, locked after movement for both. **Prove the guard by making it fail** |
| An EF model change sneaks in and splits model and migration | Low | `has-pending-model-changes` on the migration slice |
| Print layout does not match the agency's paper | Medium | Open Item 2 asked before S4 |
| Two new csprojs break CI plumbing | Low | Plumbing lands first, in S1, with one trivial test on both CI jobs |
| `DbContext` shared across concurrent commands | Low | One context per operation; commands disabled while busy |

---

## 9. Rollback Plan

- **Code**: each slice is its own PR to `develop`; reverting a slice's merge commit removes it. The
  domain under it (`collection`, `receipt`) is untouched, so reverting the UI leaves it working as it
  does today.
- **Migration**: its `Down` restores the previous `contracts_cutover_lock` function body. Lossless at
  any time — it changes a check, not data. Applying or reverting it on Supabase is done by Tomás.
- **New projects**: removing them from `Inmobiliaria.sln` and `Core.slnf` and deleting the folders
  reverts S1's plumbing.
- **Data written through the screens** (recibos, payments, cutover dates) is agency record and is not
  rolled back: a wrong recibo is voided, never deleted; a wrong cutover date can be corrected only
  while the account holds no movement.

---

## 10. Dependencies and Preconditions

- `collection`, `receipt`, `contract-account`, `access-control` exist and are archived — this change
  consumes them and modifies only the parts in §3.
- Open decision **`pdf-library`**: touched by Decision 2 and **not resolved** — system printing needs no
  library; the PDF choice stays with `recibo-pdf`. **`scheduler-mechanism`**: not touched.
- Go-live day also needs the initial data load change (Decision 3); this change's code does not.

**Preconditions, per phase (announce before starting):**

| Phase / slice | Docker | Supabase |
|---|---|---|
| spec, design, tasks | No | No |
| S1 shell + composition + plumbing | **Yes** (composition guard in Infrastructure.Tests) | No |
| S2 go-live (ports, rule, migration) | **Yes** | **Restored, by Tomás, only to apply the migration** after merge |
| S3 cash desk | **Yes** (account directory) | No |
| S4 recibo reader, print, void | **Yes** | No |
| verify | **Yes** — a skipped Testcontainers test is an unrun test | Optional live smoke, Tomás only |

The assistant never handles the Supabase password.

---

## 11. Size Forecast

Advisory. Planning heuristic about 400 authored changed lines per slice (config `review_budget_lines`
800). `sdd-tasks` owns the real plan.

| Slice | Content | Estimate |
|---|---|---|
| S1 | Shell, session composition, Presentation projects plumbing | ~450 |
| S2 | Go-live: ports, cutover value rule, migration, screen, tests | ~500 |
| S3 | Cash desk: account directory, view-model, view, tests | ~650 |
| S4 | Recibo reader, display and print, void flow | ~550 |

**Total about 2,150 authored lines**, four chained PRs; S3 may split in two.

Decision needed before apply: Yes
Chained PRs recommended: Yes
400-line budget risk: High

**Tomás must choose the chain strategy before `sdd-tasks`/`sdd-apply`**: `stacked-to-main` (each
slice PR targets `develop` once the previous one merges) or `feature-branch-chain` (PR 1 targets a
feature branch, each later PR targets the previous one). Not chosen here.

---

## 12. Success Criteria

- [ ] After login, the main window knows the session and role; an Empleado does not see the go-live
      screen, and an Empleado's direct `UPDATE` of `cutover_date` is refused by the database.
- [ ] An operator finds an account by tenant, unit or contract and sees whether it is ready to collect.
- [ ] A payment goes preview → handed amount → summary → commit, shows both recibo numbers, and the
      handed amount is checked against the ledger figure.
- [ ] A stale or concurrent-change refusal re-previews and shows the difference; nothing is retried
      silently.
- [ ] Both issued recibos display on screen and print through the Windows print dialog, with the
      non-fiscal legend, only when the operator asks; an earlier recibo can be found and reprinted.
- [ ] A recibo found by its printed number can be voided with a reason, inside the window allowed to
      the operator's role.
- [ ] An Admin sets a contract's cutover date and the recibo starting number, and the screen shows when
      either is no longer changeable.
- [ ] View-model tests run on the Linux `core` job; `dotnet test Inmobiliaria.Core.slnf` and
      `dotnet build Inmobiliaria.sln` pass with Docker running and zero skipped Testcontainers tests.
