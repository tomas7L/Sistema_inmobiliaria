# Tasks: gestion-de-usuarios (the Admin's user screen)

Turns `design.md` §10 (three slices, S1 to S3) into ordered, checkable work. It does not re-plan the change
and does not edit `spec.md` or `design.md`. Where slicing exposed a gap, it is listed under **Issues found**.

Vocabulary: **Admin** and **Empleado** are the two roles; a **provisional password** is one set by someone
other than the user, which the user must replace at the next login.

Slice to phase map (one slice = one phase = one PR): S1 = Phase 1, S2 = Phase 2, S3 = Phase 3.

## What to read (Tomás)

| Read | Skip |
|---|---|
| This header, the **Review Workload Forecast**, the **Human Follow-Ups** and **Issues found** | Everything inside the phases except the **Verification** table at the end of each. The checkboxes are for the assistant and for the second inspection at the start of each session |

## Review Workload Forecast

| Field | Value |
|---|---|
| Estimated changed lines | About 1,250 authored (additions + deletions, tests included), design §10 |
| Review budget | About 400 authored lines per PR, a planning heuristic and not a cap |
| 400-line budget risk | Medium: S3 is estimated at about 450 |
| Chained PRs recommended | Yes |
| Suggested split | PR1 (S1) to PR3 (S3), forced order, one slice per PR |
| Delivery strategy | auto-chain |
| Chain strategy | stacked-to-main, as in `ui-de-cobranza`: each slice PR targets `develop` once the previous one has merged |

Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: stacked-to-main
400-line budget risk: Medium

All three slices touch credentials or permissions (high-risk item 2), so each is reviewed alone.

---

## Phase 1: Rules and login (S1, PR 1, est. about 380 lines)

**Docker: yes. Supabase: no. Model change: no.** Spec tests in this PR: 4, 11, 13 (mask half), 18, 19, 24,
25. Design anchors: D4, D5, D6, D7, D10, §2.2 to §2.5, §4.

- [x] 1.1 **[Second inspection, read-only]** Confirm the design's anchors still match the code: `NpgsqlAuthenticator.cs:25,34,52-58`, `PostgresPasswordService.cs` (the `SameAsCurrent` exception), `ChangePasswordViewModel.cs:85-101`, `FileErrorLog.cs:47-50`. With Docker up, run the full baseline once and record the counts here
- [x] 1.2 **[RED then GREEN, Test 4]** `Username.Normalize` and `Username.IsValid` in `src/Inmobiliaria.Domain/Access/Username.cs`, tests in `tests/Inmobiliaria.Domain.Tests/UsernameTests.cs`: valid names (with `ñ`), uppercase and `Ñ` lowercased, decomposed `ñ` composed, trimmed; spaces, accented vowels, leading digit or symbol, 2 and 31 characters refused
- [x] 1.3 **[RED then GREEN, Test 18]** `PasswordRule.Check` and `PasswordRefusalText` in `src/Inmobiliaria.Domain/Access/PasswordRule.cs`, tests in `tests/Inmobiliaria.Domain.Tests/PasswordRuleTests.cs`: same as current, 7 refused, 8 accepted, no composition demanded, username refused in any case, spaces kept, 64 accepted, text elements counted
- [x] 1.4 **[RED then GREEN, Test 11]** `ProvisionalPassword.Generate` and its 256-word list in `src/Inmobiliaria.Domain/Access/ProvisionalPassword.cs`, tests in `tests/Inmobiliaria.Domain.Tests/ProvisionalPasswordTests.cs`: shape `word-word-word-ddddd`, words 3 to 6 `a`-`z` letters, 256 distinct, 1,000 generated all different, at least 8 characters, passes `PasswordRule` for any username
- [x] 1.5 **[RED then GREEN, Test 13 mask half]** `SecretMask.Mask` in `src/Inmobiliaria.Domain/Access/SecretMask.cs`, tests in `tests/Inmobiliaria.Domain.Tests/SecretMaskTests.cs`: `password=x`, `PASSWORD 'x'`, `PASSWORD E'x'`, doubled quotes inside; `FileErrorLog` calls it; its existing tests stay green
- [x] 1.6 **[RED then GREEN, Test 13 real exception]** `tests/Inmobiliaria.Infrastructure.Tests`: with `EXECUTE` on `app_set_role_password` revoked from the Admin group by the superuser, a reset raises a real `PostgresException`; `SecretMask.Mask(ex.ToString())` does not contain the password. Record here whether the unmasked text did (design F5)
- [x] 1.7 **[RED then GREEN, Tests 19]** `PasswordChangeRefusedException` in `src/Inmobiliaria.Infrastructure/Access/`; `PostgresPasswordService.ChangeOwnPasswordAsync` checks `PasswordRule` before creating a context; existing `SameAsCurrent` tests updated to the new exception with the same text; a too-short and a username password are refused and a forced change stays pending (Infrastructure.Tests)
- [x] 1.8 **[Desktop, no test project]** `ChangePasswordViewModel` catches `PasswordChangeRefusedException` and shows the refusal's text; build only
- [x] 1.9 **[RED then GREEN, Tests 24, 25]** `NpgsqlAuthenticator` normalizes the username first; Infrastructure.Tests: `Nuñez ` logs in as `nuñez`, `Sofia` as `sofia`; a password differing only in case fails; wrong password and unknown user still give the identical failure
- [x] 1.10 **[Prove the guards by making them fail]** Break the rule's minimum, the mask's `PASSWORD` branch and the login normalization once each in a scratch run; each test must go red. Record here
- [x] 1.11 **[Guardrails]** `ArchitectureGuardTests` green; `has-pending-model-changes` reports no changes
- [x] 1.12 **[Handover]** File list, commit Summary and Description, PR title and description to Tomás. No commit by the assistant

### Verification, PR 1 (Docker: yes)

| Command | Expected |
|---|---|
| `dotnet build Inmobiliaria.sln -c Release` | exit code 0; **Advertencia(s)** and **Errores** both 0 |
| `dotnet test Inmobiliaria.Core.slnf -c Release` (Docker up) | exit code 0; 0 failed; 0 skipped; passed = baseline + new tests |
| `dotnet ef migrations has-pending-model-changes -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure` | exit code 0, no pending changes |

---

## Phase 2: Port and adapter (S2, PR 2, est. about 420 lines)

**Docker: yes. Supabase: no. Model change: no.** Spec tests: 1, 2, 3, 5, 6, 7, 8, 9, 10, 13 (adapter
half), 14 (port half), 15, 21, 22, 23. Design anchors: D1, D2, D3, D8, D9, §2.1, §3.

- [x] 2.1 **[Second inspection, read-only]** PR 1 merged intact; `PostgresUserProvisioning.cs` and `UserProvisioningTests.cs` unchanged since the design
- [x] 2.2 **[Domain]** `IUserAdministration`, `UserListing`, `UserAdminResult`, `ProvisionalCredential` (`ToString` without the password), `UserAdminRefusal` and its text; `AppUser.Activate()` with Domain.Tests
- [x] 2.3 **[RED then GREEN]** Rename `PostgresUserProvisioning` to `PostgresUserAdministration` and its test class; implement the port per design §3.1; delete `IUserProvisioning`; fix the stale comments (spec F3)
- [x] 2.4 **[Tests 1-3, 5-10, 15, 22, 23]** in Infrastructure.Tests, as an Admin and as an Empleado, per the spec's wording
- [x] 2.5 **[Test 21]** `SessionServicesTests` resolves `IUserAdministration` for both roles without a connection
- [x] 2.6 **[Prove the guards by making them fail]** The 42501 mapping and the Admin-target refusal, once each
- [x] 2.7 **[Guardrails and handover]** as 1.11 and 1.12

### Verification, PR 2 (Docker: yes)

Same three commands as PR 1.

---

## Phase 3: Screen (S3, PR 3, est. about 450 lines)

**Docker: yes for the full suite. Supabase: no (the live smoke is H.2).** Spec tests: 12, 14 (view-model
half), 16, 17, 20. Design anchors: D11, D12, §5, §6.

- [ ] 3.1 **[Second inspection, read-only]** PR 2 merged intact
- [ ] 3.2 **[RED then GREEN, Tests 12, 14, 16, 17]** `UsersViewModel`, `UserRow`, `PendingUserAction`, `IClipboard` in Presentation, with a fake port and a fake clipboard
- [ ] 3.3 **[RED then GREEN, Test 20]** `ShellViewModelTests`: Usuarios for an Admin only
- [ ] 3.4 **[Desktop]** `NavigationEntry`, `ShellViewModel`, `App.xaml.cs`, `MainWindow`, `UsersView`, `WpfClipboard`; build only
- [ ] 3.5 **[Specs]** Merge the deltas into `openspec/specs/user-registry/spec.md` and `openspec/specs/desktop-shell/spec.md`
- [ ] 3.6 **[Guardrails and handover]** as 1.11 and 1.12

### Verification, PR 3

Same three commands as PR 1.

---

## Human Follow-Ups (Tomás)

| # | When | What |
|---|---|---|
| H.1 | Before each slice | Docker running |
| H.2 | After PR 3 | **Live smoke on the test Supabase**, restored first; only Tomás connects. `tomas` creates an Empleado, logs in with it (forced change), resets, deactivates, reactivates. **One Empleado with `ñ` in the username logs in** (spec F4; if it fails, `ñ` leaves Decision 8). M1 Copy pastes into WhatsApp Web; M2 the screen at 1366×768 |

## Issues found (while slicing)

1. **Test 13 is split.** The mask is S1 (with a real exception through the self-service and reset paths
   that exist today); the adapter's own failure paths are proven in S2.
2. **Test 14 is split.** The port refuses an Admin target in S2; the view-model offers no action on Admin
   rows in S3.
3. **Test 19 lives in Infrastructure.Tests**, against `PostgresPasswordService`, because the dialog's
   view-model is in Desktop, outside the CI filter. The dialog's catch is checked by build only.

## Deviations (recorded beside tasks during apply)

**Phase 1 (S1), applied 2026-11-01.**

- 1.1: baseline with Docker up, 899 passed, 0 failed, 0 skipped (Domain 373, Presentation 218,
  Infrastructure 308). Anchors matched the design.
- 1.2 to 1.5: RED observed as a compile failure (the four Domain types missing), then GREEN.
- 1.6: **design F5 confirmed.** The real `PostgresException` from `app_set_role_password` carries the
  password in its text (`unmasked text carries the password: True`); the masked text does not. The test makes
  an Admin reset a role it did not create (42501 inside the function) instead of revoking `EXECUTE`: a revoked
  function fails before running, so its error would not quote the statement and would prove nothing.
- 1.7 and 1.9: the code was written before the Infrastructure tests, so there is no RED run for them; 1.10
  stands in for it.
- 1.10: minimum set to 7 → 3 PasswordRule tests red; SQL clause mask removed → the F5 test red; login
  normalization reduced to `Trim()` → all 4 login cases red. Each restored, then green.
- 1.11: build 0 warnings, 0 errors; full suite 961 passed, 0 failed, 0 skipped (Domain 425, Presentation
  218, Infrastructure 318); `has-pending-model-changes`: no changes.
- Size: about 705 changed lines against an estimate of 380 (the tests and the 256-word list). Not split:
  the rules, their tests and the login change are one behaviour.
- Review (gentle-ai, lenses risk and reliability, lineage `review-0899e21e2c28dcb5`): one CRITICAL
  finding, that the "not the username" check uses the session name, which in tests carries the
  `.test-ref` suffix. In production the session name is the plain role name (Supavisor strips the suffix),
  so the rule holds there; but no test showed production's shape. Correction: a test with a plain session
  name (`sofiaperez` refused) and a comment in `PostgresPasswordService` saying where the name comes from.
  The first targeted validation failed on reviewer transport (nothing changed); the identical re-offer
  passed. **Approved and acknowledged.** After the correction: 963 passed, 0 failed, 0 skipped; build 0
  warnings.
- Review advisories, non-blocking, to fold into S2:
  - **Do:** `AFailedPasswordStatement_...` should assert the unmasked text contains the password before
    asserting the masked one does not, so the test cannot pass vacuously (observed True on 2026-11-01).
  - **Do:** `SecretMask` should also mask a `PASSWORD '` literal whose closing quote was cut off.
  - **Do:** `TheUsername_IsRefusedAsAPassword` builds the password from the composed name; replace it
    with the plain-name test or derive from the raw name.
  - **No action:** "missing `using` in `ChangePasswordViewModel`": the file already imports
    `Inmobiliaria.Domain.Access`, and the solution builds.
  - **No action, recorded:** the rule runs in the application only; a direct database client can set any
    password. Same honest limit as `user-registry` "The Requirement Does Not Confine a Direct Database
    Client".

**Phase 2 (S2), applied 2026-11-01.**

- 2.1: PR #81 merged intact (docs and code as two commits); `PostgresUserProvisioning.cs` unchanged since the
  design.
- 2.2: RED observed as a compile failure (port types and `AppUser.Activate()` missing), then GREEN.
- 2.3: the adapter takes a `roleNameFor` seam: through Supavisor the role name is the username; the
  Testcontainers database has no Supavisor, so tests compose `.test-ref` into it. Validation and login are
  unchanged by the seam. **Found while running:** EF applies the model's snake_case convention to unmapped
  `SqlQuery<T>` rows too, so the aliases are `is_admin`, not `"IsAdmin"` (first run: 42703).
- 2.4: user-registry tests 9 to 12, 14 to 16 and 31 ported to the new port in `UserAdministrationTests`; the
  self-service tests 17 and 18 moved with them unchanged. Test 9 (atomic reactivation) uses a generated password
  containing NUL, which fails after `LOGIN` was restored inside the transaction, instead of revoking `EXECUTE`.
- S1 advisories done here: the F5 test asserts the real text contains the password before masking;
  `SecretMask` masks a literal cut off before its closing quote, without crossing the line end; the
  composed-name username test is renamed and points to the plain-name one.
- 2.6: 42501 always mapped to `AdminOnly` → 2 tests red; Admin target accepted → 1 red; registration removed →
  2 red. Each restored.
- 2.7: build 0 warnings, 0 errors; full suite 981 passed, 0 failed, 0 skipped (Domain 433, Presentation 218,
  Infrastructure 330); `has-pending-model-changes`: no changes. About 1,100 lines added and 659 removed (the
  old port, adapter and their tests, ported rather than kept).
- Review (gentle-ai, risk and reliability, lineage `review-b4b907646c0aa3a3`): **approved and acknowledged**, no
  correction. One capture had to be relaunched because the assistant's script skipped the second lens; the
  identical re-offer ran it.
- Review advisories, non-blocking, to fold into S3:
  - **Do:** a duplicate that races past the taken-username check (double click, two Admins), or a role that
    exists with no row (42710 / 23505), should come back as `UsernameTaken`, not the generic error.
  - **Do:** 42501 on **create** from an Admin session means the Admin lacks the runbook grants, not "another
    Admin created this user"; give it its own text.
  - **Do:** `DeactivateAsync` on an already inactive user from an Empleado session answers Success; check the
    session role first so it answers `AdminOnly`.
  - **Do:** add the foreign-role test for `ReactivateAsync`, and `UserNotFound` for deactivate and reactivate.
  - **No action, recorded:** the password is generated before the database confirms the caller may create
    roles, so a refused create sends it once; it never becomes a login (the transaction rolls back) and the
    application log masks it. PostgreSQL's own server log is Supabase's, outside this application.
