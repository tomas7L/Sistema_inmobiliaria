# Tasks: notificaciones (telling tenants what they owe, from the application)

Turns `design.md` §7 (four slices, T1 to T4) into ordered, checkable work. It does not re-plan the change and does not edit
`spec.md` or `design.md`. Where applying exposes a gap, it is listed under **Issues found**.

Slice to phase map: T1 = Phase 1, T2 = Phase 2, T3 = Phase 3 (one PR each, now), T4 = Phase 4 (with `gestion-de-alquileres`
S12's screens).

## What to read (Tomás)

| Read | Skip |
|---|---|
| This header, the **Review Workload Forecast**, **Issues found** and **Deviations** | Everything inside the phases except the **Verification** table at the end of each. The checkboxes are for the assistant and for the second inspection at the start of each session |

## Review Workload Forecast

| Field | Value |
|---|---|
| Estimated changed lines | About 1,250 authored (additions + deletions, tests included): T1 about 400, T2 about 350, T3 about 300, T4 about 200, design §7 |
| Review budget | About 400 authored lines per PR, a planning heuristic and not a cap |
| 400-line budget risk | Medium: T1 sits at the budget because the migration and its model change must ship together |
| Chained PRs recommended | Yes: one PR per phase, each merged before the next starts |
| Delivery strategy | ask-on-risk |

Decision needed before apply: No

## Issues found

- **gentle-ai findings, Phase 1** (2026-11-12, non-blocking): **`R3-001`** (WARNING), `TenantNoticeDeskTests.cs:235-239`: the recargo oracle sums every `account_movements` row, so the infrastructure test never seeds an adjustment difference on a month not yet begun, which `LedgerForPayment` excludes; a desk using the unfiltered ledger would still pass. **`R3-002`**, the tests assert against the static `DeskWorld.Today` while the desk reads a live `AgencyClock`: a run crossing midnight or a month boundary can fail. **`R3-003`**, `DeskOver` and Test 7's `AccountDirectory` never dispose their `DbContext`. **`R4-send-throws-not-supported`**, `SendAsync` throws `NotSupportedException` instead of returning outcomes; no caller exists until Phase 2, which implements it.
- **Taken up after the review closed, so not reviewed by it** (2026-11-12): **`R3-001` fixed.** `EveryNotice_StatesTheDesksFigure_AndTheRecargoInsideIt_FrozenIncluded` now seeds a 40,000 difference on next month and its oracle leaves it out of the ledger. Seen failing with the desk's ledger replaced by every movement (expected 1,054,800, actual 1,014,800), the mutation reverted byte-identical (md5). **`R3-003` is false:** `DeskWorld.NewContext()` records every context in `_contexts` and `DisposeAsync` disposes them all (`CollectionDeskFixture.cs:298`, `:400-405`). **`R3-002` left as it is:** it predates Phase 1, since `DeskWorld.Today` and `NewDesk()` read the live clock across the whole cash desk test family, and fixing only these tests would not close it. Final run: build exit 0, 0 Advertencia(s), 0 Errores; suite exit 0, 2158 passed (Domain 729, Presentation 530, Infrastructure 899), 0 failed, 0 skipped; `TIME_WAIT` 100 before, 3,496 after; `has-pending-model-changes` exit 0.
- **Verifier advisories, Phase 1** (2026-11-12, not blockers): (1) the already-sent list indexes `byContract[n.ContractId]`; a sent row whose contract had no account would throw `KeyNotFoundException`, unreachable today because every contract has an account; (2) as with the recibo tables, the append-only trigger does not cover a TRUNCATE by the table owner; neither app role holds TRUNCATE (probe P1); (3) the existing Test 7 removes the tenant in the same save that creates the contract; the verifier's probe covered a replacement in a later session.
- **Phase 1 is about 1,180 authored lines, not about 400** (2026-11-12): about 430 of production code and migration SQL, about
  620 of tests, the rest the shared-file edits; the generated Designer and snapshot excluded. The tests are most of it: two
  isolated-database test classes with their own seeding. Advisory only; nothing was cut to fit.
- **The test fixture now grants tables to Supabase's API roles** (`PostgresFixture.cs`, `GrantFunctionsLikeSupabaseAsync`
  renamed `GrantLikeSupabaseAsync`, its one caller in `ReciboSchemaTests.cs`): without `ALTER DEFAULT PRIVILEGES ... GRANT ALL
  ON TABLES`, the migration's guarded revoke had nothing to revoke and its test passed for nothing. The shared database and every
  isolated one now carry Supabase's table defaults, as production does (design F2). No existing test changed result.

## Deviations

- **"Shaped like an address" uses `System.Net.Mail.MailAddress.TryCreate`, not MailKit's `MailboxAddress.TryParse`** (design
  §3.4, §9): MailKit arrives in Phase 2 (task 2.4). A display-name form ("Ana <ana@x.com>") is refused: the parsed address must
  equal the trimmed text. **Phase 2 revisits it** when MailKit lands, and decides whether to switch.
- **`TenantNoticeDesk.SendAsync` throws `NotSupportedException`** pointing at task 2.3; `ITenantNoticeDesk.SendAsync` is declared.
  The per-call wrapper forwards to it.
- **Spec Test 4's mail-port half holds by construction in Phase 1**: the desk is given no sender, so it cannot call one. The
  test asserts the row half (no sent row after two reads). Phase 2 re-asserts the port half with a counting fake once `SendAsync`
  takes a sender.
- **The sender's shape is Phase 1's proposal**: `ITenantNoticeSender.OpenAsync(credentials)` returns a `MailSessionOpening`
  (an `IMailSession`, or the failure reason); `IMailSession.SendAsync(OutgoingMail)` returns a `MailDelivery`. Phase 2 may refine it.
- **Spec Test 5's recargo is checked against the database, not a preview field**: the desk's preview carries the whole-account
  figure (`PreviousAmountOwed`) but not a whole-account recargo. The test checks each total against the preview and each recargo
  as that total minus `sum(amount)` of the account's movements, read by SQL; zero on the frozen account.
- **Days overdue use `IMoraWorklistQuery.GetAccountsInMoraAsync`**, the method's real name (design §1 says
  `ReadAllAsync`).

---

## Phase 1: Reading today's notices (T1, PR 1, est. about 400 lines)

**Docker: yes. Supabase: only to apply the migration to the test database, Tomás. Model change: yes, with its migration in
this PR.** Spec tests: 1 to 5, 7, 14, 15, 18. Requirements 1 to 3, 7, 8 (registration). Design anchors: D1 to D4, D12,
§2.1, §2.2, §3.1, §3.2, §3.3 (`ReadAsync`), §3.5, F1 to F3. Risk: items 1 (stored data: a new table) and 2 (grants).

- [x] 1.1 **[Second inspection, read-only]** Anchors match: `ContractAccount.cs:318-343`, `AccountListings.cs:12,26-40`, `MoraWorklistQuery.cs:41-63`, `SessionServices.cs:62-69`, `AccountMovementConfiguration.cs:111-113`, `20261015204506_AddCollectionsAndRecibos.cs:205-255`, `20261107152256_AddClauseWindowOffset.cs:125-135`, `RolePermissionTests.cs:42`, `ReciboGrantsTests.cs`. Latest migration still `20261110150305_AddAdjustmentDifference`. Baseline suite recorded with counts and `TIME_WAIT`. **Done 2026-11-12**: every anchor matched at HEAD; the Mora method is `GetAccountsInMoraAsync` (Deviations). Baseline `dotnet test Inmobiliaria.Core.slnf -c Release`: exit 0, 2133 passed (Domain 723, Presentation 530, Infrastructure 880), 0 failed, 0 skipped; `TIME_WAIT` 4 before, 3,565 after
- [x] 1.2 **[Refactor under green]** `ContractAccount.LedgerForPayment(receivedOn)` extracted from `AmountOwedForPayment` (design §2.2); a Domain test that `LedgerForPayment` plus the notice recargo equals `AmountOwedForPayment` for every movement kind, frozen included. The cash desk's existing tests pass unchanged. **Done**: RED observed (CS1061, `LedgerForPayment` missing, 4 errors); GREEN with `tests/Inmobiliaria.Domain.Tests/Accounts/LedgerForPaymentTests.cs` (3 tests: every kind's ledger figure, recargo equal to the projected sum, frozen ledger equal to the desk figure). Domain 729 passed, existing tests unchanged
- [x] 1.3 **[Domain types]** `Domain/Notices/`: `NoticeKind`, `TenantNotice`, `NoticeBoard`, `MailboxCredentials` (whose `ToString()` prints the address only, with a test), `NoticeSendOutcome`, `NoticeSendReport`, `ITenantNoticeDesk`, `ITenantNoticeSender`, `IMailboxCredentialStore` (design §2.1). `SendAsync` declared, not implemented: Phase 2. **Done**: four files in `Domain/Notices/` (also `UnreachableTenant`, `SentNotice`, `NoticeSendStatus`, `OutgoingMail`, `MailSessionOpening`, `MailDelivery`, `IMailSession`); `MailboxCredentialsTests` 3 tests green
- [x] 1.4 **[RED then GREEN, Tests 14 and 15]** `TenantNoticeEntry`, its configuration and the `AddTenantNotices` migration (design §3.1, §3.2): unique index, append-only trigger, stamp trigger, grants, guarded API-role revoke. `ReciboGrantsTests`-style test for exact `INSERT,SELECT`, refused update and delete, and the stamped sender; `RolePermissionTests` count raised by one. The API-role revoke proven against the roles `PostgresFixture` creates, and seen failing with the revoke removed. **Done**: RED, the 19 new infrastructure tests failed at the fixture on `PendingModelChangesWarning` before the migration existed; GREEN after `20261112160850_AddTenantNotices` (`TenantNoticeRecordTests`, 9 tests). `RolePermissionTests.AllFourteenTables_AreSelectableByBothRoles`. The fixture now grants tables to the API roles (Issues found)
- [x] 1.5 **[RED then GREEN, Tests 1 to 5 and 7]** `TenantNoticeDesk.ReadAsync` (design §1, §3.3): Mora list for days overdue, the desk figure for total and recargo, the tenant's email added to `AccountListingEntry`, loaded and mistaken contracts left out, an ended contract kept, the "no email" list, the already-sent list; reading writes no row and sends nothing. **Done**: `TenantNoticeDeskTests`, 10 tests (Tests 1, 2, 3 with four unusable emails, 4, 5, 7, and the already-sent list beside last month's row). RED as 1.4
- [x] 1.6 **[RED then GREEN, Test 18]** `PerCallTenantNoticeDesk`, registered inside the terms block of `SessionServices`; `SessionServicesTests` resolves it per role with valid terms and finds none without them. **Done**: RED, `EveryPortAScreenNeeds_ResolvesForEachRole_WithoutCreatingAContext` failed for both roles (`No service for type ITenantNoticeDesk`); GREEN 16/16
- [x] 1.7 **[Prove the guards by making them fail]** Each mutation run and reverted: the unique index removed; the append-only trigger removed; the stamp trigger removed; the API-role revoke removed; the `CutoverDate` filter removed; the registration moved outside the terms block. **Done**, each reverted byte-identical (md5): unique index → `ASecondNoticeForTheSameContractKindAndMonth_IsRefused_ButTheNextMonthIsNot`; append-only trigger → `TheAppendOnlyTrigger_RefusesAnUpdateOrDelete_EvenFromTheOwner`; stamp trigger → `BothRoles_ReadAndInsert_AndTheSenderIsTheirCurrentUser_WhateverTheInsertSays` (both roles) and `ANoticeSentThisMonth_IsShownAsSent_AndLastMonthsHidesNothing`; API-role revoke → `SupabasesApiRoles_HoldNothing_OnTheSentRecord`; `CutoverDate` filter → `OnlyAnEndedContractThatOwes_IsListed_...`; registration outside the block → `InvalidRecargoTerms_LeaveTheDeskUnregistered_AndTheOtherPortsResolvable`
- [x] 1.8 **[Differential]** Every cash desk figure (`AmountOwedForPayment`, `PreviewAsync`) and every Mora row for accounts covering every movement kind, dumped against `2dacaab` in a `mktemp -d` copy and against the change: identical. **Done**: six accounts (rent only; paid with recargo, instalments, a correction, a difference on a begun and on a not-yet-begun month; frozen; loaded with an opening balance; ended; owing nothing), every movement kind present; 47 lines (6 previews, 5 Mora rows, 6 desk figures, the movements), `cmp` identical. A first run differed only because the scratch corrected an arbitrary rent; made deterministic, then identical. Scratch never committed
- [x] 1.9 **[Guardrails]** Build, suite, `has-pending-model-changes`, per the table below, with `TIME_WAIT` sampled during the run. **Done**: build exit 0, 0 Advertencia(s), 0 Errores; suite exit 0, 2158 passed (Domain 729, Presentation 530, Infrastructure 899), 0 failed, 0 skipped; `TIME_WAIT` 284 before, peak 3,499 during (20-second samples), 3,487 after; `has-pending-model-changes` exit 0
- [x] 1.10 **[Independent verification and gentle-ai review]** Items 1 and 2 named. **Verifier done 2026-11-12: no blockers**; every claimed spec Test (1, 2, 3, 4 database half, 5, 7, 14, 15, 18) passes; build exit 0 with 0 warnings; suite exit 0, 2158 passed, 0 failed, 0 skipped (`TIME_WAIT` 3 before, 3,542 after); six probes of its own passed (Empleado and Admin TRUNCATE refused with 42501; a supplied `recorded_by` overwritten by `current_user`; a replaced tenant gets the notice; a frozen account's total equals `AmountOwedForPayment` with recargo 0; loaded and mistaken contracts in neither list; migration Down leaves no `tenant_notices` object and Up restores it). Three advisories, see Issues found. **gentle-ai: pending.** Assessment: risk high (item 2 escalated), 24 paths, 2,864 changed lines with the generated files, `review_due` true. Too large for one review: split per `gestion-de-alquileres` tasks 4.9 (code first, then tests). **gentle-ai done 2026-11-12, in two candidates on a scratch clone:** code and migration without tests or documents (15 files, 2,085 lines; lenses risk and resilience): `review-84881dd02ee2d9fe`, approved, acknowledged. Tests over that code, fixture included (8 files, 654 lines; lens reliability): `review-32a8116a7322efe0`, approved, acknowledged. `tasks.md` and `ESTADO.md` in neither. Four non-blocking findings, see Issues found
- [x] 1.11 **[Handover]** File list, commit Summary and Description, PR title and description to Tomás, and the migration to apply to the test database (H.3). No commit by the assistant. **Handed over 2026-11-12**: 24 paths, commit and PR text in the session; `20261112160850_AddTenantNotices` to apply to PRUEBA after the merge

### Verification, PR 1 (Docker: yes)

| Command | Expected |
|---|---|
| `dotnet build Inmobiliaria.sln --configuration Release` | exit code 0; **Advertencia(s)** and **Errores** both 0. MSB3027 means the app is open |
| `dotnet test Inmobiliaria.Core.slnf --configuration Release` (Docker up) | exit code 0; 0 failed; 0 skipped; passed = baseline + new tests |
| `dotnet ef migrations has-pending-model-changes -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure` | exit code 0, no pending changes |

---

## Phase 2: Sending (T2, PR 2, est. about 350 lines)

**Docker: yes. Supabase: no. Model change: no.** Spec tests: 6, 8 to 11, 13. Requirements 2 (text), 4 to 6. Design anchors:
D5 to D8, D10, §2.1 (`NoticeComposer`), §3.3 (`SendAsync`), §3.4, F4, F6. Risk: item 5 (MailKit, a new dependency). Needs:
Phase 1 merged.

- [ ] 2.1 **[Second inspection, read-only]** Phase 1 landed; `CollectionDesk.cs:444,456` and `SecretMask.cs:11-27` still as cited
- [ ] 2.2 **[RED then GREEN, Test 6]** `NoticeComposer` in `Domain/Notices/`
- [ ] 2.3 **[RED then GREEN, Tests 8 to 11]** `TenantNoticeDesk.SendAsync` (design §3.3) with a fake sender; Test 9 through two connections, the fake waiting until both are in flight
- [ ] 2.4 **[RED then GREEN, Test 13]** `MailKitTenantNoticeSender` behind `ISmtpTransport`; MailKit in `Directory.Packages.props`
- [ ] 2.5 **[Prove the guards by making them fail]** Commit before send; the 23505 mapping; the password scrub; the login-first order
- [ ] 2.6 **[Guardrails]** As Phase 1
- [ ] 2.7 **[Independent verification and gentle-ai review]** Item 5 named
- [ ] 2.8 **[Handover]** As Phase 1

---

## Phase 3: The view-models and the Windows store (T3, PR 3, est. about 300 lines)

**Docker: yes for the full suite. Supabase: no. Model change: no.** Spec tests: 12, 16, 17. Requirements 1, 5, 8 (on the
view-model). Design anchors: D9, D11, §4. Risk: item 5 (the Credential Manager package). Needs: Phase 2 merged.

- [ ] 3.1 **[Second inspection, read-only]** Phase 2 landed; `ShellViewModel.cs:43-52` and `App.xaml.cs:146-151` still as cited
- [ ] 3.2 **[RED then GREEN, Tests 12 and 17]** `NoticesViewModel`, `NoticesTexts`
- [ ] 3.3 **[RED then GREEN, Test 16]** `MailboxSetupViewModel`; `NavigationTarget.Notices` for both roles, disabled with the recargo reason
- [ ] 3.4 **[Windows store]** `CredentialManagerMailboxStore` in `Desk`, built in `App.xaml.cs`; checked by hand on this PC
- [ ] 3.5 **[Guardrails]** As Phase 1, plus the Desktop build
- [ ] 3.6 **[Independent verification and gentle-ai review]** Item 5 named
- [ ] 3.7 **[Handover]** As Phase 1, plus Tomás's real send from a test Gmail account to himself

---

## Phase 4: The screens (T4, PR 4, est. about 200 lines)

**Docker: yes for the full suite. Supabase: no. Model change: no.** Design anchors: D10 (the email's final wording), D11.
Needs: Phase 3 merged, Mateo's screens.

- [ ] 4.1 **[Second inspection, read-only]** Phase 3 landed; S12's and Mora's navigation entries checked for where Avisos sits
- [ ] 4.2 **[View]** Mateo's layout; `NoticesTexts` and the email's final wording, read by Tomás on one real notice
- [ ] 4.3 **[Guardrails]** As Phase 3
- [ ] 4.4 **[Handover]** As Phase 1
