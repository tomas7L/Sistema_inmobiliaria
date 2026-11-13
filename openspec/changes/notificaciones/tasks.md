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

- **gentle-ai findings, Phase 3** (2026-11-12, non-blocking; taken up after the review closed, so not reviewed by it):
  - **`R4-unreadable-credential-reads-as-not-configured`, fixed.** A credential Windows could not read showed "no está
    configurada", and a press read the store twice, logging the failure twice. It now says `NoticesTexts.MailboxUnreadable`, and a
    press reads the store once (plus once in the re-read after it). Pinned by
    `AnUnreadableMailbox_DisablesSendingSayingItCouldNotBeRead_AndAPressReadsTheStoreOnce`, which failed first on the double read
    and then under the mutation calling it missing.
  - **`R3-001`, fixed.** `LoadAgencyName` now turns an unreadable or malformed `appsettings.json` into "no name" (sending disabled),
    never a startup failure. Untested: `Desktop` has no CI tests; the recargo loader parses the same file first, so the realistic
    exposure was already nil.
- **Verifier findings, Phase 3** (2026-11-12): every Phase 3 spec item met; no path found for the app password into a log, a
  message or a binding.
  - **(B1) Fixed, the one blocker: a notice another PC sent between the read and the press vanished from the report.** The press
    keeps only the chosen rows still due, so it was neither sent nor counted; the screen said "Ya enviados antes: 0" against
    Requirements 4 and 5. It now counts it as already sent, and names a row that no longer owes apart. Pinned by
    `APress_ReportsANoticeSentMeanwhileAsAlreadySent_AndOneNoLongerOwedApart`.
  - **(1) Fixed: a failed press or save showed the cash desk's text** ("puede que el cobro se haya registrado…"). They now say
    `NoticesTexts.SendFailed` and `SaveFailed`. Pinned by `AFailedPress_SaysToReadTheListAgain` and the failed-save test.
  - **(2) Left: password copies in memory** (each load reads the credential to test for it; a typed, unsaved password stays in the
    setup until the app closes). Inherent to string APIs; low risk.
  - **(3) Settled 2026-11-13 by the real send (3.7): Gmail accepts the app password with its spaces.** The stored secret was
    19 characters with 3 spaces (only its length and space count were read, never the value), and the send succeeded.
  - Each fix failed its test under mutation (sent-meanwhile not counted, no-longer-owed dropped, cash desk wording), all reverted
    byte-identical. Presentation 552 passed, Desktop build 0 warnings.

- **gentle-ai findings, Phase 2** (2026-11-12, non-blocking; taken up after the review closed, so not reviewed by it):
  - **`R4-ambiguous-delivery` and `R3-ambiguous-send-failure`, fixed.** A connection lost while an email was travelling read
    "Gmail no aceptó el correo", and the notice came back to the list, so the operator could send it twice. The adapter now tells
    the cases apart:
    - Gmail's own refusal (`SmtpCommandException`) says Gmail did not accept it.
    - A send never started (`ServiceNotConnectedException`) says it was not sent and can be resent.
    - A connection lost or timed out mid-send (`IOException`, `TimeoutException`, `OperationCanceledException`,
      `SmtpProtocolException`) says nobody knows whether it arrived, and asks the operator to check Gmail's Sent folder before
      resending.
    - Anything else says it was not sent.

    Pinned by `ASendFailure_SaysWhetherTheEmailMayHaveGoneOut` (six cases).
  - **`R4-session-no-reconnect`, half taken.** There is no reconnect. Once the connection drops, the rest of the batch says "se
    perdió la conexión… Se puede volver a enviar" instead of a misleading refusal.
  - **`R1-001`, fixed.** The desk's catch-all now also removes the bare app password, through the new
    `SecretMask.Mask(text, secret)` that the adapter shares. Pinned by `AFailureQuotingTheAppPassword_ReachesTheOperatorWithoutIt`
    and a `SecretMaskTests` case.
  - **`R3-unrecorded-accept-untested`, fixed.** `AnAcceptedEmailWhoseRowCannotBeCommitted_IsReportedAsFailed_SayingItWentOut`
    refuses the commit through a `DbTransactionInterceptor`.
  - **`R3-cancel-mid-batch-drops-report`, for Phase 3.** A press cancelled mid-batch throws and loses the outcomes collected so
    far. What went out is committed, so nothing is resent. The screen decides whether a send can be cancelled at all.
  - Each fix was seen failing under its mutation, and every mutation was reverted byte-identical. One mutant first survived:
    calling every other failure a refusal. A case for that branch was added, and it then failed as it should.
- **Verifier advisories, Phase 2** (2026-11-12, not blockers):
  - **(1) Fixed: a press cancelled after Gmail accepted an email rolled back its row.** The commit used the caller's token, so the
    email went out with no row, and the notice could be sent again. It now commits with `CancellationToken.None`. Pinned by
    `APressCancelledAfterGmailAcceptedTheEmail_StillRecordsIt`, which fails with the token back.
  - **(2) Fixed: a cancelled login left the SMTP client open.** It is now closed on every failure. Pinned by
    `ACancelledLogin_IsRethrown_AndClosesTheClient`, which fails with the old catch filter. Both mutations were reverted
    byte-identical.
  - **(3) Left: the scrub misses URL-encoded and base64 forms of the password.** MailKit puts neither in an exception message and
    no protocol logger is attached, so this is outside realistic input.
  - **(4) Fixed: a missing period.** "El correo salió, pero no se pudo registrar el envío (…). Revise antes…".
  - **(5) Left, and it predates Phase 2: fifty centavos print as ",5"**, as `Formats.Money` writes them. It now reaches tenants:
    to settle in 4.2, with the final wording.
  - **(6) For Phase 3: re-read the board before sending.** `SendAsync` trusts the notices it is given, so a screen left open past
    midnight would send yesterday's figures and month.

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

- **Phase 3 ships a working Avisos screen, unstyled** (Tomás, 2026-11-12: "Bueno, suma esa pantalla"): tasks 3.4 and 3.7 need the
  mailbox set up and one real notice sent by hand, which no screen allowed until Phase 4. `Desk/Views/NoticesView.xaml` shows the
  three lists, Quitar, Enviar avisos with the report, and the Gmail setup for an Admin. Phase 4 replaces its layout with Mateo's;
  the bindings stay. It is XAML that CI does not test.
- **Sending is also disabled when `Agency:Name` is missing** (Phase 3; held medium): the email is signed with it (D10), and an
  unsigned notice is worse than none. Read apart from the rest of `Agency`, so a missing logo stops printing, not notices.
- **`NoticesViewModel` takes no error log** (design §4.1 named one): it uses the application's, as `MoraViewModel` does. The
  mailbox setup takes one, so its test can prove a failed save reaches the log without the password.
- **A press re-reads the list and the mailbox, and cannot be cancelled** (Phase 2 verifier advisory 6 and review
  `R3-cancel-mid-batch-drops-report`, both settled here): only rows still due are sent, in the figures read at the press.

- **`ITenantNoticeDesk.SendAsync` takes the agency's name** (Phase 2, 2026-11-12; held medium): design D10 names the agency in the email
  from `AgencyDetails`, which lives in `Presentation` and is read in `App.xaml.cs`, so `Infra` cannot reach it. It travels with the
  batch, as the credentials do (design §1). The port had no caller yet.
- **The coverage sentence lives in `Domain/Accounts/BalanceCoverage.cs`** (Phase 2): spec Requirement 2 asks for the Mora screen's
  words, and the composer in `Domain` cannot read `Presentation`'s `MoraTexts`. `MoraTexts.Coverage` points to it; the text is
  unchanged.
- **The email says the amount "puede aumentar" after its date**, not that recargo grows: a frozen account's recargo does not grow,
  and the notice cannot tell it apart (its recargo figure is zero). Placeholder wording, read by Tomás in 4.2.
- **An email Gmail accepted whose row then failed to commit is reported as failed, saying the email went out**, so nobody resends
  it blind (spec Requirement 5, "nothing reported as sent that was not").

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

- [x] 2.1 **[Second inspection, read-only]** Phase 1 landed; `CollectionDesk.cs:444,456` and `SecretMask.cs:11-27` still as cited. **Done 2026-11-12**: #115 merged as `e558790`, `ramatomy` equal in content to `develop`, working tree clean; both anchors as cited
- [x] 2.2 **[RED then GREEN, Test 6]** `NoticeComposer` in `Domain/Notices/`. **Done**: RED, CS0103 (`NoticeComposer` and `BalanceCoverage` missing); GREEN with `NoticeComposerTests` (7 tests), Domain 736 passed. The coverage sentence moved to `Domain/Accounts/BalanceCoverage.cs`, `MoraTexts.Coverage` now points to it (Deviations); Mora's tests unchanged and green
- [x] 2.3 **[RED then GREEN, Tests 8 to 11]** `TenantNoticeDesk.SendAsync` (design §3.3) with a fake sender; Test 9 through two connections, the fake waiting until both are in flight. **Done**: `TenantNoticeDeskTests.Send.cs` (7 tests: the sent row and its email, a refused login, Test 8 through the desk, Test 9, Test 10, Test 11, a send that throws) and `FakeNoticeSender`; Test 4 now also asserts zero logins and zero sends. Test 9's fake holds the first email until `pg_stat_activity` shows the other session waiting on a lock. RED: with `SendAsync` throwing, the 7 send tests failed; GREEN 38/38 across the notice, MailKit and session tests
- [x] 2.4 **[RED then GREEN, Test 13]** `MailKitTenantNoticeSender` behind `ISmtpTransport`; MailKit in `Directory.Packages.props`. **Done**: `Infra/Mail/MailKitTenantNoticeSender.cs`, MailKit **4.18.2** (published 2026-10-23; 5.0.0 was two days old and a major version), `InternalsVisibleTo` for the seam; the sender registered as a singleton outside the terms block. `MailKitTenantNoticeSenderTests` (5 tests). RED was not observed before the adapter existed: the scrub's RED is the mutation in 2.5
- [x] 2.5 **[Prove the guards by making them fail]** Commit before send; the 23505 mapping; the password scrub; the login-first order. **Done**, each reverted byte-identical (md5): commit before send → 5 tests, Test 11 among them; 23505 mapping removed → Test 8 through the desk and Test 9; password scrub removed → both Test 13 tests; a login per notice instead of one first → the sent-row test and Test 11 (`Opens` is 1). A login placed after the insert is not observable: the refused login's rollback leaves the same zero rows
- [x] 2.6 **[Guardrails]** As Phase 1. **Done 2026-11-12**: build exit 0, 0 Advertencia(s), 0 Errores; suite exit 0, 2177 passed (Domain 736, Presentation 530, Infrastructure 911), 0 failed, 0 skipped; `TIME_WAIT` 2,762 before, 5,395 after; `has-pending-model-changes` exit 0. About 940 changed lines with the tests (175+/31- tracked, 768 new), over the ~350 estimate: the tests are most of it
- [x] 2.7 **[Independent verification and gentle-ai review]** Item 5 named. **Verifier done 2026-11-12: no blockers**; every Phase 2 spec Test (6, 8, 9, 10, 11, 13) and Requirements 2, 4, 5, 6 pass; build exit 0 with 0 warnings, also with `--no-incremental`; suite exit 0, 2177 passed, 0 failed, 0 skipped; `has-pending-model-changes` exit 0; MailKit 4.18.2 confirmed published and not a prerelease, resolving MimeKit 4.18.2, BouncyCastle.Cryptography 2.7.0 and System.Security.Cryptography.Pkcs 10.0.13, with no vulnerable package (`dotnet list package --vulnerable --include-transitive`); its mutation of the 23505 mapping failed Tests 8 and 9. Six advisories, see Issues found; 1, 2 and 4 fixed before the review. **gentle-ai done 2026-11-12, in one candidate on a scratch clone** (code and tests, no documents; 15 files, 1,002 lines; lenses risk, resilience and reliability): `review-af125732595c8cab`, approved, acknowledged. Six non-blocking findings, see Issues found
- [x] 2.8 **[Handover]** As Phase 1. **Handed over 2026-11-12**: 18 paths; final run after the review follow-ups: build exit 0, 0 Advertencia(s), 0 Errores; suite exit 0, 2188 passed (Domain 737, Presentation 530, Infrastructure 921), 0 failed, 0 skipped; `TIME_WAIT` 3,107 before, 5,408 after; `has-pending-model-changes` exit 0. No migration

---

## Phase 3: The view-models and the Windows store (T3, PR 3, est. about 300 lines)

**Docker: yes for the full suite. Supabase: no. Model change: no.** Spec tests: 12, 16, 17. Requirements 1, 5, 8 (on the
view-model). Design anchors: D9, D11, §4. Risk: item 5 (the Credential Manager package). Needs: Phase 2 merged.

- [x] 3.1 **[Second inspection, read-only]** Phase 2 landed; `ShellViewModel.cs:43-52` and `App.xaml.cs:146-151` still as cited. **Done 2026-11-12:** #116 merged as `fb5287e`, its tree identical to `cfc7de5`, CI build and core green; both citations hold. Mora has no shell entry yet (S12), so Avisos sits after Anular un cobro
- [x] 3.2 **[RED then GREEN, Tests 12 and 17]** `NoticesViewModel`, `NoticesTexts`. **Done 2026-11-12:** RED was the missing types; GREEN 550 Presentation tests (20 new). A press re-reads the list and the store first (verifier advisory 6) and cannot be cancelled (R3-cancel-mid-batch-drops-report). Mutations, each failing its test and reverted byte-identical: no re-read before sending, removed rows still sent
- [x] 3.3 **[RED then GREEN, Test 16]** `MailboxSetupViewModel`; `NavigationTarget.Notices` for both roles, disabled with the recargo reason. **Done 2026-11-12.** Mutations failing their tests: setup offered to every role, Avisos always available, the store's failure logged unscrubbed
- [x] 3.4 **[Windows store]** `CredentialManagerMailboxStore` in `Desk`, built in `App.xaml.cs`; checked by hand on this PC. **Class written 2026-11-12** with `Meziantou.Framework.Win32.CredentialManager` 3.0.8 (published 2026-10-28, no dependencies; 3.0.9 and 3.0.10 are under two weeks old); Desktop build 0 warnings. Wired in `App.xaml.cs` beside the clipboard, with the working screen (see Deviations); Desktop build 0 warnings. **Hand check done 2026-11-13:** after saving from Avisos, `cmdkey /list` shows `LegacyGeneric:target=SistemaInmobiliaria/Gmail`, generic, user the Gmail address, persistence local machine; the screen then reads "Los avisos se envían desde …" and the password box clears
- [x] 3.5 **[Guardrails]** As Phase 1, plus the Desktop build. **Done 2026-11-12:** Docker 29.9.3; build exit 0, 0 warnings; suite exit 0, 2208 passed (Domain 737, Presentation 550, Infrastructure 921), 0 failed, 0 skipped; TIME_WAIT 1 before, 2,908 after; `has-pending-model-changes` exit 0; Desktop build 0 warnings; no vulnerable package in Desktop. After the verifier's fixes: Presentation 552, Desktop 0 warnings
- [x] 3.6 **[Independent verification and gentle-ai review]** Item 5 named. **Done 2026-11-12.** Verifier: one blocker (B1), fixed; see Issues found. gentle-ai, on a scratch clone in one candidate (16 files, 1,122 lines; lenses risk, resilience and reliability): `review-1f976694ce81b65e`, approved, acknowledged; two non-blocking findings, both taken up afterwards. Presentation 553 passed, Desktop 0 warnings; Domain and Infrastructure untouched since the full run in 3.5
- [x] 3.7 **[Handover]** As Phase 1, plus Tomás's real send from a test Gmail account to himself. **Done 2026-11-13** on the test database, after Tomás applied `20261112160850_AddTenantNotices` there (the only pending migration). The fictitious tenant had no email, so it sat under "Sin correo: llamar"; Tomás set `parties.email` by hand in the SQL editor (one row checked first). After Actualizar, one notice; Enviar avisos delivered it at once. The email's total, $ 5.148.000, equals the cash desk's "Saldo anterior" for the same contract on the same day; its recargo total, $ 2.898.000, spans several periods and the desk shows only the oldest (07/2026, $ 1.134.000), so it was not compared line by line. Findings for 4.2, none blocking: see there. Before launching, the compiled `appsettings.json` was found pointing at production and was repointed to the test database

---

## Phase 4: The screens (T4, PR 4, est. about 200 lines)

**Docker: yes for the full suite. Supabase: no. Model change: no.** Design anchors: D10 (the email's final wording), D11.
Needs: Phase 3 merged, Mateo's screens.

- [ ] 4.1 **[Second inspection, read-only]** Phase 3 landed; S12's and Mora's navigation entries checked for where Avisos sits
- [ ] 4.2 **[View]** Mateo's layout; `NoticesTexts` and the email's final wording, read by Tomás on one real notice. **From the 3.7 send (2026-11-13):** (a) the greeting reads "Hola, Prueba, Maria:" because `NoticeComposer.cs:30` uses the list's "Last, First" name; a greeting wants "Maria Prueba". (b) "El total adeudado ya incluye el recargo" is said twice (the total's paragraph and `BalanceCoverage.cs:11`). (c) The recargo exceeded the debt, from the configured `Recargo:DailyRate` 0.02; the real rate is for Nicolás, not a text fix
- [ ] 4.3 **[Guardrails]** As Phase 3
- [ ] 4.4 **[Handover]** As Phase 1
