```yaml
schema: gentle-ai.verify-result/v1
evidence_revision: sha256:2db6d2fd4499288ee7bf4e7a9c15cc6b1d5b39b531184f4ad2f97b0c1d588bbc
verdict: pass_with_warnings
blockers: 0
critical_findings: 0
requirements: 17/17
scenarios: 36/36
test_command: dotnet test Inmobiliaria.Core.slnf --configuration Release
test_exit_code: 0
test_output_hash: sha256:de91318d39ad395af3d90bf49bc482422d90ae4f3318820ef8757004d5637a0f
build_command: dotnet build Inmobiliaria.sln --configuration Release --no-incremental
build_exit_code: 0
build_output_hash: sha256:2b1a8bc61fb3fed654b00460084c12b41f9e48d7824d28bd38d355318f4341c6
```

# Verification Report: users-and-roles

**Verified at**: git commit `e2d5c9130378d80d2607a87a7c787ce2cc670237` (branch `ramatomy`), confirmed an
ancestor of `origin/develop` via PR #29 (`git merge-base --is-ancestor` returned true). Working tree
clean. Docker Desktop running; Testcontainers `postgres:17.6` executed for every integration test —
zero skips.

**Mode**: Full spec-driven verification. Proposal, spec.md (single-file, 17 requirements / 36
scenarios / 34 numbered tests), design.md, tasks.md (86 tasks across 6 delivered PR slices + 4 Human
Follow-Ups), and apply-progress.md were all read in full before judging implementation.

## 1. Build and Test Evidence

| Command | Result |
|---|---|
| dotnet build Inmobiliaria.sln --configuration Release --no-incremental | 0 Advertencia(s) (Warnings), 0 Errores (Errors). Exit 0. All 5 projects built, including Inmobiliaria.Desktop (WPF). |
| dotnet test Inmobiliaria.Core.slnf --configuration Release | Inmobiliaria.Domain.Tests: Superado 69, Con error 0, Omitido 0. Inmobiliaria.Infrastructure.Tests: Superado 65, Con error 0, Omitido 0. Exit 0 (confirmed on a second --no-build re-run). 134/134 total, 0 failed, 0 skipped. |

No Testcontainers test was skipped anywhere in the run - Docker was reachable for the entire suite, so
every one of the 34 spec tests that requires a real PostgreSQL 17.6 engine actually executed against
one, not merely against a fake.

## 2. Spec Test Traceability (all 34 numbered tests)

Each row names the production code that implements the behavior and the test that proves it, both by
file and line. All 34 trace to code AND to a currently-passing test; none is untested.

| # | Requirement proven | Production code | Test |
|---|---|---|---|
| 1 | No credential on disk / in environment | appsettings.json (no password field, verified by direct read) | CompositionGuardTests.cs:20 NoDatabaseCredential_FoundInConfigurationFiles, :43 NoDatabaseCredential_FoundInTheRuntimeEnvironment |
| 2 | Supabase Auth never invoked | NpgsqlAuthenticator.cs:44 (single OpenConnectionAsync call; no Supabase/Gotrue package anywhere in the project graph) | AuthenticationTests.cs:25 ProjectGraph_HasNoSupabaseAuthPackageReference, :45 LoginPath_OpensExactlyOneNpgsqlConnection |
| 3 | Correct credentials authenticate | NpgsqlAuthenticator.cs:22-91 (AuthenticateAsync) | AuthenticationTests.cs:58 CorrectCredentials_Succeed |
| 4 | Wrong password rejected generically | NpgsqlAuthenticator.cs:46-55 (catch InvalidPassword/InvalidAuthorizationSpecification to Rejected) | AuthenticationTests.cs:75 WrongPassword_RejectedWithGenericResult |
| 5 | Unknown username fails identically | Same catch block, same Rejected type, no distinguishing data (AuthenticationResult.cs) | AuthenticationTests.cs:90 UnknownUsername_RejectedIdenticallyToWrongPassword |
| 6 | No DB access before login | ApplicationServices.cs:17-27 (AddPreLoginServices registers only ConnectionEndpoint and IAuthenticator, neither DB-shaped) | CompositionGuardTests.cs:67 PreLoginServiceCollection_RegistersNothingDatabaseShaped |
| 7 | Role derived from pg_has_role, never stored | NpgsqlAuthenticator.cs:93-108 (ReadIdentityAsync); AppUser.cs has no role property | AuthenticationTests.cs:104 RoleIsDerivedFromPgHasRole_AndADatabaseSideChangeTakesEffectNextLogin |
| 8 | DB-side role change takes effect next login | Same as #7, role is re-read every login, never cached | AuthenticationTests.cs:104 (same test covers both) |
| 9 | Admin creates a user as one unit | PostgresUserProvisioning.cs:36-90 (CreateUserAsync, one transaction wrapping app_create_login_role plus AppUsers.Add) | UserProvisioningTests.cs:61 CreateUser_CreatesLoginRoleAndAppUsersRowAsOneUnit_AndAForcedFailureOrphansNeither |
| 10 | Deactivated user cannot log in | PostgresUserProvisioning.cs:149-172 (DeactivateAsync calls app_set_role_login with false) | UserProvisioningTests.cs:123 Deactivate_SetsRoleToNoLogin_AndTheNextLoginAttemptFails |
| 11 | Deactivated user historical rows still readable | ContractDocumentConfiguration.cs:49-50, RentAdjustmentConfiguration.cs:80 (both FKs ON DELETE RESTRICT, row never deleted) | UserProvisioningTests.cs:149 Deactivate_LeavesHistoricalContractDocumentAndRentAdjustmentRowsUnchangedAndReadable |
| 12 | Deactivated username never reused | PostgresUserProvisioning.cs:63-69 (pre-check against AppUsers.AnyAsync, active or not) | UserProvisioningTests.cs:196 CreateUser_WithADeactivatedUsersUsername_IsRejected |
| 13 | Self password change keeps session alive | PostgresPasswordService.cs:28-69 (ChangeOwnPasswordAsync) | AuthenticationTests.cs:134 SelfPasswordChange_KeepsTheAlreadyOpenSessionWorking |
| 14 | Admin reset does not disturb an open session | PostgresUserProvisioning.cs:104-141 (ResetPasswordAsync, only affects a fresh connection) | UserProvisioningTests.cs:222 ResetPassword_TakesEffectOnlyAtNextLogin_PriorSessionStaysAlive |
| 15 | Forced change gates everything else (split) | DB half: PostgresUserProvisioning.cs:83 (mustChangePassword true). Construction-order half: LoginBootstrap.cs:45-62 plus App.xaml.cs:64-114 (MainWindow constructed only at line 104, past both gates) | DB half: UserProvisioningTests.cs:261 CreateUser_SetsMustChangePasswordTrue. Construction-order half: LoginBootstrapTests.cs:29 MustChangePasswordPending_CannotReachMainWindow_UntilTheChangeSucceeds, :44 CancellingAForcedChange_AbortsInsteadOfFallingThroughToMainWindow |
| 16 | Admin reset raises the requirement again | PostgresUserProvisioning.cs:137 (user.RequirePasswordChange call) | UserProvisioningTests.cs:279 ResetPassword_SetsMustChangePassword_AndNextAuthenticationRequiresIt |
| 17 | Re-entering the provisional password rejected | PostgresPasswordService.cs:43-47 (ordinal currentPassword equals newPassword check, before any DB bind) | UserProvisioningTests.cs:302 ChangeOwnPassword_RejectsResubmittingTheJustAuthenticatedPassword_RequirementStaysPending |
| 18 | Voluntary change raises no requirement | PostgresPasswordService.cs:63-64 (flag cleared unconditionally on success) | UserProvisioningTests.cs:334 ChangeOwnPassword_VoluntaryChangeWithNothingPending_RaisesNoRequirement |
| 19 | MustChangePassword does not confine a direct client | Absence is the proof: no code anywhere (NpgsqlAuthenticator, the migration GRANTs) checks this flag; it is read only by the application own bootstrap, confirmed by reading NpgsqlAuthenticator.cs end-to-end | PasswordDdlTests.cs:23 PendingMustChangePassword_DoesNotConfineADirectDatabaseClient |
| 20, 21 | Password DDL survives quote and injection payload | Migration 20260924202549_AddUsersAndRoles.cs:173-178 (app_set_role_password using format with percent-I percent-L) plus PostgresPasswordService.cs:56-57 (ExecuteSqlInterpolatedAsync, real parameters) | PasswordDdlTests.cs:43-45 PasswordDdl_SetsTheExactLiteralPasswordWithNoInjection, Theory over two payloads |
| 22 | Empleado refused for user-governance writes | Migration line 163 (GRANT INSERT, UPDATE ON app_users TO inmobiliaria_admin only, no Empleado grant) | RolePermissionTests.cs:204 Empleado_DirectAppUsersWriteAndProvisioningFunction_BothRefused |
| 23 | Admin succeeds at the same operation | Migration lines 163, 213-214 (GRANT to inmobiliaria_admin) | RolePermissionTests.cs:236 Admin_DirectAppUsersWriteAndProvisioningFunction_BothSucceed |
| 24 | Neither role can disable the append-only trigger | Migration: no ALTER TABLE OWNER TO anywhere; neither role granted table ownership (design Decision 9) | RolePermissionTests.cs:279 NeitherRole_CanDisableTheAppendOnlyTrigger |
| 25 | Empleado can terminate a contract, positive | Migration line 161 (GRANT INSERT, UPDATE ON contracts to both roles); Contract.cs:240 GiveNotice, :256 End | RolePermissionTests.cs:335 Empleado_CanTerminateAContract_NoticeAndEndBothSucceed |
| 26 | Empleado can confirm a rent adjustment, positive | Migration line 162 (GRANT INSERT ON rent_adjustments, rent_adjustment_index_values to both); RentAdjustment.cs:80 Confirm, Contract.cs:141 ConfirmAdjustment | RolePermissionTests.cs:373 Empleado_CanConfirmARentAdjustment_WithConfirmedByRecorded |
| 27 | Aggregate reporting succeeds at the DB, app-enforced only; confirmed correct, not a defect | Migration line 147 (GRANT SELECT ON ALL TABLES); no GRANT construct can forbid an aggregate over already-readable rows | RolePermissionTests.cs:417-433 Empleado_AggregateOverContracts_SucceedsBecauseNoGrantCanForbidIt asserts ExecuteScalarAsync completes with no exception thrown, exactly the required positive result |
| 28 | No row-level restriction | Migration: no ENABLE ROW LEVEL SECURITY or policy anywhere; baseline GRANT SELECT uniform for both roles | RolePermissionTests.cs:302 BothRoles_SeeEveryRowOfATableTheyMayRead |
| 29 | New table GRANTs ship in the same migration | Migration lines 146-164 (full GRANT set, all 13 pre-existing tables plus app_users) | RolePermissionTests.cs:82 AllThirteenTables_AreSelectableByBothRoles |
| 30 | UploadedBy is a real FK | Migration lines 62-100 (contract_documents delta); ContractDocumentConfiguration.cs:49-50 | SchemaConstraintTests.cs:442 UploadedByUserId_ResolvesToAppUserRow_AndPlainUploadedByColumnIsGone |
| 31 | Uploader FK survives deactivation | ContractDocumentConfiguration.cs:49-50 (OnDelete Restrict) | UserProvisioningTests.cs:359 Deactivate_ContractDocumentUploaderReference_StillResolvesToDisplayName |
| 32 | ConfirmedBy nullable, no backfill | Migration lines 107-111 (AddColumn Guid confirmed_by, nullable true, no UPDATE) | SchemaConstraintTests.cs:487 ConfirmedByColumn_IsNullable |
| 33 | Confirming records the confirmer | RentAdjustment.cs:80 Confirm, Contract.cs:141 ConfirmAdjustment (required, non-nullable confirmedBy parameter) | RolePermissionTests.cs:437 ConfirmingAnAdjustment_AsAnyAuthenticatedUser_StoresHerAppUserReference |
| 34 | Append-only trigger still rejects UPDATE/DELETE | Trigger created in the pre-existing, byte-unmodified 20260918233049_AddRentAdjustments.cs; RentAdjustmentConfiguration.cs:85 SetAfterSaveBehavior Throw | SchemaConstraintTests.cs:665 AppendOnlyTrigger_StillRejectsUpdateAndDelete_ForBothApplicationRoles |

Result: 34/34 traced to production code and to a currently-passing test. Zero untested requirements.

## 3. Reverse Check - Does Design/Spec Claim Anything the Code Does Not Do?

Given this change's history (two invented requirements previously removed after user pushback), this
direction was checked deliberately, not just the forward direction.

- Migration 20260924202549_AddUsersAndRoles.cs independently re-read, statement by statement.
  Confirmed: (a) contracts appears in exactly one statement, GRANT INSERT, UPDATE ON contracts TO
  inmobiliaria_admin, inmobiliaria_empleado (line 161) - no ALTER TABLE contracts anywhere, matching
  design Decision 5's scope guard; (b) app_users is the only table with an asymmetric grant (line
  163, Admin-only INSERT, UPDATE) - matches spec Decision 8's "exactly two exclusions" claim, and no
  other table shows a hidden asymmetry; (c) the four functions match design Decision 3/6 exactly,
  including app_clear_must_change_password being the sole SECURITY DEFINER object with a pinned
  search_path.
- AppUser.cs constructor and mutators, read directly, not from a comment. No role column exists
  anywhere on the type (confirmed, matches spec Decision 5's "no role column" claim). Deactivate()
  and RequirePasswordChange() are simple, idempotent boolean flips with no hidden side effects beyond
  what design.md describes.
- NpgsqlAuthenticator.cs full body confirms the honest limit of spec test 19. No code path in this
  file, nor anywhere in the migration's GRANT set, reads or checks must_change_password for connection
  admission - the flag is read only by App.xaml.cs's bootstrap loop, and only for the in-application
  path. This matches (does not overstate) design Decision 6 and spec Decision 14's own "credential
  hygiene, not a security boundary" framing, checked against the actual code, not accepted from the
  comment that says so.
- App.xaml.cs's RunBootstrapAsync (lines 64-114), read end-to-end. MainWindow is constructed at
  exactly one call site (line 104), reached only after the while loop's AwaitingForcedPasswordChange
  branch either does not trigger or completes without hitting its own continue (line 97, reached only
  from BootstrapStage.Aborted). This matches design Decision 10's diagram exactly - no path bypasses
  the gate.
- ContractConfiguration.cs confirmed byte-for-byte untouched by this change (git log shows its last
  two modifying commits, 63cac45 and e7c7cd1, both predate this change's first commit, ce447a8). This
  confirms design Decision 5's claim that the CHECK constraint was deliberately not added here, and
  that contracts is reached only through the one GRANT statement, matching tasks.md's H.4 follow-up,
  correctly not implemented in this change.
- No claim found that overstates enforcement. Spec test 27's own assertion (RolePermissionTests.cs:432,
  "No exception: this is the whole point of the test") is the correct positive assertion per the
  instructions for this verification, not flagged as a defect.

No design or spec claim was found asserting a behavior the code does not actually deliver.

## 4. Deliberate Choices - Confirmed as Decided, Not Overlooked

Per this verification's instructions, each is reported only where relevant, not as a finding:

- MustChangePassword confirmed as credential hygiene only (section 3 above), matches spec test 19's
  intent exactly.
- In-app user creation confirmed Empleado-only: PostgresUserProvisioning.cs:26 hard-codes
  EmpleadoGroupRole = inmobiliaria_empleado as the only group role CreateUserAsync ever grants; the
  reasoning (task 3.12's proven ADMIN OPTION non-inheritance) is documented in both the code's XML
  remarks and docs/runbooks/bootstrap-first-admin.md.
- Password lifetime in process memory: NpgsqlAuthenticator.cs hands the plaintext to
  NpgsqlDataSourceBuilder and drops the local reference; the data source retains it for the
  connection's life, exactly as design Decision 1 states and accepts.
- Visual design out of scope: MainWindow.xaml unchanged; LoginWindow.xaml/ChangePasswordWindow.xaml
  use only WPF default styling (confirmed by reading both files, no custom brushes, fonts, or themes).
- LoginBootstrap/BootstrapStage (LoginBootstrap.cs) confirmed present and doing its stated job: a
  pure, dependency-free state machine that both App.xaml.cs and LoginBootstrapTests.cs exercise
  identically, letting the Linux core job prove the construction-order rule despite
  Inmobiliaria.Desktop being excluded from Inmobiliaria.Core.slnf. Its absence from design.md/tasks.md
  is a documented deviation, not a defect, it does exactly what it is described as doing.
- Four Human Follow-Ups tasks (H.1-H.4) confirmed as the user's/operator's own steps, not agent work:
  - H.1 (apply migration to live Supabase): correctly not attempted, no agent connected to the live
    project anywhere in this verification either.
  - H.2 (log_statement check against live Supabase): resolved, tasks.md task 3.13 records the project
    owner's own 2026-09-25 report (log_statement = ddl, gate passes).
  - H.3 (human decision if task 3.12 fails): task 3.12 did fail (ADMIN OPTION does not inherit), and
    PR 4 resolved the consequence by choosing Empleado-only in-app provisioning, which matches spec
    Decision 6's own text, marked CONFIRMED BY USER in spec.md's decision table. The checkbox remains
    unticked in tasks.md; see Finding W1 below for why this is a documentation gap, not a code gap.
  - H.4 (CHECK constraint carried to the collection change): correctly not implemented here (section 3
    above); tasks.md itself already carries it forward.

## 5. Task Completion vs. Code State

All 86 numbered tasks across PR 1, PR 2a, PR 2b, PR 3, PR 4, PR 5 are checked complete in tasks.md and
apply-progress.md, and the corresponding files/tests were independently confirmed present in the
working tree (Section 2 above; every file apply-progress.md names for each PR exists at the path
claimed). Task 3.13 is marked complete in tasks.md as RESOLVED by the project owner (2026-09-25),
consistent with apply-progress's PR 4 section. The four Human Follow-Ups remain unticked by design
(Section 4).

All six PR-slice commits are present in linear history on ramatomy (ce447a8, 136b8c3, 48fb58d,
e912c99, 376898a, 49df02e, e2d5c91 - one extra commit, 376898a, is a follow-up test fix within the
PR 3 slice) and confirmed merged into origin/develop via PR #29.

## 6. Findings

CRITICAL: none.

### WARNING

1. tasks.md's H.3 checkbox is unticked, but the underlying decision was substantively made and
   already recorded elsewhere. H.3 reads "a human decides... not an agent decision." Task 3.12 did
   fail as anticipated, and PR 4's apply-progress documents that the agent itself chose
   "Empleado-only in-app provisioning" as the resolution, reasoning from the migration's own frozen
   SQL body. This happens to match spec.md Decision 6's text, which is independently marked
   CONFIRMED BY USER, so the substance of the decision does trace back to a real user confirmation,
   just not one visibly connected to H.3's own checkbox in this file. This is a documentation/
   task-tracking consistency gap, not a code defect: nothing in the shipped code depends on an
   undecided question. Recommend ticking H.3 with a cross-reference to spec.md Decision 6 the next
   time tasks.md is touched.
2. Three of five delivered PR slices exceeded the 800-line review_budget_lines under delivery_strategy
   ask-on-risk. apply-progress reports each overage honestly and in detail (PR 2a+2b combined
   approximately 1,031 authored lines, PR 3 approximately 1,181, PR 4 approximately 807, all over
   budget; PR 1 and PR 5 were under). This verification has no artifact recording that the user was
   actually asked and consented before each oversized slice proceeded, only that the overage was
   disclosed after the fact. Not a code defect, flagged for process compliance with the project's own
   delivery-strategy guard, which exists to protect reviewer cognitive load.

### SUGGESTION

1. CompositionGuardTests.NoDatabaseCredential_FoundInConfigurationFiles (spec test 1) is a lexical
   substring scan for the literal tokens Password, ConnectionString, Pwd= - not a semantic or
   exhaustive credential detector. It currently passes correctly because appsettings.json genuinely
   holds no credential (confirmed by direct read), but a future field named unconventionally (for
   example a base64 blob under an unrelated key) would not be caught by this test. Worth strengthening
   if the configuration surface grows.
2. Local develop branch is 2 commits behind origin/develop (a git fetch/pull away). This is local
   repository state, not a defect in the change itself, origin/develop already contains e2d5c91 via
   PR #29, but noted since it could otherwise be mistaken for an unmerged change on a later inspection
   of the local branch alone.

## 7. Verdict

PASS WITH WARNINGS.

- 0 CRITICAL findings, safe to archive as far as this verification's own gate is concerned
  (openspec/config.yaml's archive rule: "NEVER archive a change whose verify report still has
  unresolved CRITICAL findings", there are none).
- 2 WARNING findings, both process/documentation-tracking gaps with no corresponding code defect;
  neither blocks archive per this project's own rules, which reserve blocking specifically for
  unresolved CRITICAL findings.
- 2 SUGGESTION findings, informational only.
- All 34 spec tests trace to implementation and to a passing test. All 17 requirements and 36
  scenarios are covered. Full build clean (0/0). Full test suite green (134/134, 0 skipped). No
  design or spec claim overstates what the code does.

## Key Learnings

1. RolePermissionTests.Empleado_AggregateOverContracts_SucceedsBecauseNoGrantCanForbidIt asserting no
   exception on SELECT sum(monthly_rent) is the correct positive proof for spec test 27, not a gap,
   PostgreSQL has no GRANT that can forbid an aggregate over rows a role may already read.
2. The gentle-ai sdd-verify-validate envelope requires flat key colon value scalar lines inside a
   single fenced yaml block; nested YAML mappings for requirements/scenarios are rejected as
   malformed, the correct shape is requirements colon completed slash total.
3. evidence_revision, test_output_hash, and build_output_hash must each match the pattern
   sha256 colon 64 lowercase hex characters exactly; a raw 40-character git commit SHA-1 fails that
   pattern and must itself be re-hashed with SHA-256 to produce a conforming identifier.
4. git merge-base --is-ancestor commit origin/branch is the reliable way to confirm a locally fetched
   branch tip is already merged upstream even when the local tracking branch itself is behind.
5. Tracing all 34 spec tests required reading production code independently of apply-progress.md's
   own claims (constructors, migration SQL, and bootstrap wiring), not merely trusting the prior PR's
   self-report, this caught nothing wrong here, but is the check this project's config.yaml
   explicitly requires ("never accepted from a comment").
