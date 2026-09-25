# Tasks: Users and Roles

Domain vocabulary: Empleado = employee, the restricted role; Admin = the agency owner's role.
This file turns design.md's five-slice plan (with its pre-planned 2a/2b split) into six ordered,
checkable PRs. It does not re-plan the change. Scope guard carried from design: `ContractConfiguration.cs`
is NOT touched, and no PR issues `ALTER TABLE contracts` — the table is reached only through a
table-level `GRANT`.

## Review Workload Forecast

| Field | Value |
|---|---|
| Estimated changed lines | 2,315–2,855 (sum of the six itemised slices below) — see Risks: this does not match design.md's own headline figure of "1,910–2,410", a ≈400-line internal inconsistency in design.md that is flagged, not silently resolved |
| Session review budget | 800 lines (`review_budget_lines`), not the skill's generic 400-line default |
| 800-line budget risk | High — every individual slice below is itself under 800 lines, but the total is 3–3.5x the per-session budget if delivered as one PR |
| Chained PRs recommended | Yes |
| Suggested split | PR1 → PR2a → PR2b → PR3 → PR4 → PR5, forced order, no parallel slices |
| Delivery strategy | ask-on-risk |
| Chain strategy | pending — orchestrator to ask the user (stacked-to-main / feature-branch-chain / size-exception) |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|---|---|---|---|---|---|
| 1 | `Domain/Access` (4 files) + domain tests | PR 1 | `dotnet test tests/Inmobiliaria.Domain.Tests --filter FullyQualifiedName~Access` | N/A — pure domain, no DB | Delete `Domain/Access/*` + its tests; nothing else depends on it yet |
| 2a | `AppUserConfiguration`, the two entity changes, two configuration edits, 26 test call sites | PR 2a | `dotnet test tests/Inmobiliaria.Domain.Tests` + `dotnet ef dbcontext info -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure` | N/A — no migration yet; the `dbcontext info` call forces EF model build without a live DB | Revert the two entities, two configs, `AppUserConfiguration`, `InmobiliariaDbContext` DbSet, and the 26 call sites; PR1 unaffected |
| 2b | Migration: table, both schema deltas, roles, GRANTs, four functions, `Down`, runbook doc, gate-check tests | PR 2b | `dotnet test tests/Inmobiliaria.Infrastructure.Tests --filter FullyQualifiedName~SchemaConstraint` | `dotnet ef database update` then run tests; requires Docker Desktop for Testcontainers `postgres:17.6` | `dotnet ef database update 20260918233049_AddRentAdjustments`; delete the new migration; PR1/2a code untouched (nothing persisted before this) |
| 3 | `Infrastructure/Access` ports and adapters + tests 1–8, 13, 19–28, 33, 34 | PR 3 | `dotnet test tests/Inmobiliaria.Infrastructure.Tests --filter FullyQualifiedName~RolePermission\|FullyQualifiedName~Authentication\|FullyQualifiedName~PasswordDdl\|FullyQualifiedName~CompositionGuard` | Requires Docker Desktop for Testcontainers `postgres:17.6` | Delete `Infrastructure/Access/*` (ports+adapters) and their tests; PR1/2a/2b schema unaffected — nothing yet authenticates |
| 4 | In-app provisioning/reset/deactivation + tests 9–12, 14, 15 (db half), 16–18, 31 | PR 4 | `dotnet test tests/Inmobiliaria.Infrastructure.Tests --filter FullyQualifiedName~UserProvisioning` | Requires Docker Desktop for Testcontainers `postgres:17.6` | Delete `IUserProvisioning.cs`, `PostgresUserProvisioning.cs` + tests; PR1–3 unaffected — auth still works without provisioning |
| 5 | Desktop bootstrap, both windows, both ViewModels, packages + test 15 (construction-order half) | PR 5 | `dotnet test tests/Inmobiliaria.Infrastructure.Tests --filter FullyQualifiedName~CompositionGuard` (construction-order proof) | `dotnet build` on Windows for the WPF half; `Inmobiliaria.Core.slnf` test run on Linux for the guard test | Revert `App.xaml.cs`, delete both windows/ViewModels, revert the two `.csproj`/`Directory.Packages.props` edits; PR1–4 fully functional without a UI |

Note: the two unproven-fact gate checks (ADMIN OPTION inheritance, `log_statement`) are tasks 3.12–3.13
inside PR 2b, not a separate PR — they gate PR 4, not PR 2b's own merge.

---

## Phase 1: Domain/Access (PR 1, est. 240–320 lines)

- [x] 1.1 Create `UserRole.cs` enum (`Admin`, `Empleado`) — `src/Inmobiliaria.Domain/Access/UserRole.cs`
- [x] 1.2 Create `AppUser.cs` (Id uuid, Username, DisplayName, IsActive, MustChangePassword; no password or role column) — `src/Inmobiliaria.Domain/Access/AppUser.cs`
- [x] 1.3 Create `IUserSession.cs` (UserId, Username, DisplayName, Role, MustChangePassword, `ClearMustChangePassword()`) — `src/Inmobiliaria.Domain/Access/IUserSession.cs`
- [x] 1.4 Create `UserSession.cs` implementing `IUserSession` — `src/Inmobiliaria.Domain/Access/UserSession.cs`
- [x] 1.5 `AppUserTests.cs`: constructor guards (null/whitespace username, display name) — `tests/Inmobiliaria.Domain.Tests/AppUserTests.cs`
- [x] 1.6 `UserSessionTests.cs`: `ClearMustChangePassword` flips the flag once and is idempotent — `tests/Inmobiliaria.Domain.Tests/UserSessionTests.cs`
- [x] 1.7 **[Guardrail]** Run `ArchitectureGuardTests`: `Domain/Access` adds no reference to EF Core, Npgsql, or WPF (design Decision 1)
- [x] 1.8 **[Isolation check]** `dotnet build` + `dotnet test tests/Inmobiliaria.Domain.Tests` on this branch alone

## Phase 2: EF Configuration + Entity Changes (PR 2a, est. 360–440 lines)

- [x] 2.1 Create `AppUserConfiguration.cs`: table `app_users`, `CHECK (username = lower(username))`, unique username — `src/Inmobiliaria.Infrastructure/Persistence/Configurations/AppUserConfiguration.cs`
- [x] 2.2 Modify `ContractDocument.cs`: replace `string UploadedBy` with `Guid UploadedByUserId`; drop `ThrowIfNullOrWhiteSpace`; reject `Guid.Empty` — `src/Inmobiliaria.Domain/Leasing/ContractDocument.cs`
- [x] 2.3 Modify `RentAdjustment.cs`: `Confirm(...)` gains a required, non-nullable `Guid confirmedBy` parameter — `src/Inmobiliaria.Domain/Leasing/RentAdjustment.cs`
- [x] 2.4 Modify `Contract.cs`: `ConfirmAdjustment` gains a required `Guid confirmedBy` parameter — `src/Inmobiliaria.Domain/Leasing/Contract.cs`
- [x] 2.5 Modify `ContractDocumentConfiguration.cs`: FK to `AppUser` (`ON DELETE RESTRICT`) replaces the text column; correct the doc comment — `src/Inmobiliaria.Infrastructure/Persistence/Configurations/ContractDocumentConfiguration.cs`
- [x] 2.6 Modify `RentAdjustmentConfiguration.cs`: map `confirmed_by` (nullable) + its FK; `SetAfterSaveBehavior(Throw)` loop already covers the new property with no new code — `src/Inmobiliaria.Infrastructure/Persistence/Configurations/RentAdjustmentConfiguration.cs`
- [x] 2.7 Modify `InmobiliariaDbContext.cs`: add `DbSet<AppUser>`; update the class comment for the thirteenth table — `src/Inmobiliaria.Infrastructure/Persistence/InmobiliariaDbContext.cs`
- [x] 2.8 Update all 26 existing call sites across the 5 affected test files for the two new required parameters — **no** `Guid? confirmedBy = null` shortcut (design Decision 8 rejects it) — `tests/Inmobiliaria.Domain.Tests/*`
- [x] 2.9 **[EF model check — not just build]** Run `dotnet ef dbcontext info -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure` (or an Infrastructure test that reads `context.Model`) to force `OnModelCreating` to validate before any migration exists. `dotnet build` alone does not catch EF model-validation failures — a prior slice in this project passed build and broke CI this exact way
- [x] 2.10 **[Guardrail]** Re-run `ArchitectureGuardTests` after the `Contract.cs`/`ContractDocument.cs`/`RentAdjustment.cs` edits
- [x] 2.11 **[Isolation check]** Build and run `Inmobiliaria.Domain.Tests` + `Inmobiliaria.Infrastructure.Tests` on this branch alone; no migration exists yet, so no Postgres-dependent test should be attempted here

## Phase 3: Migration, Roles, GRANTs (PR 2b, est. 365–455 lines)

- [x] 3.1 Generate the migration: `dotnet ef migrations add AddUsersAndRoles -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure` — creates `app_users`
- [x] 3.2 Hand-edit `Up`: `contract_documents` delta in order — add `uploaded_by_user_id uuid NULL`; seed the fixed hard-coded legacy `AppUser` row (`username='legacy'`, `is_active=false`); `UPDATE ... COALESCE(match by username, legacy uuid)`; `SET NOT NULL`; `DROP COLUMN uploaded_by`; add FK `ON DELETE RESTRICT`
- [x] 3.3 Hand-edit `Up`: `rent_adjustments` delta — `ADD COLUMN confirmed_by uuid NULL` + FK, **no `UPDATE`** against existing rows
- [x] 3.4 Hand-edit `Up`: create `inmobiliaria_admin`/`inmobiliaria_empleado` idempotently (`IF NOT EXISTS`, `NOLOGIN`); baseline `GRANT USAGE ON SCHEMA public`, `GRANT SELECT ON ALL TABLES`, `ALTER DEFAULT PRIVILEGES ... GRANT SELECT` (no `FOR ROLE`); every per-table `INSERT`/`UPDATE` grant from design Decision 9's table, including the single ungrouped `contracts` grant. **Scope guard: no `ALTER TABLE contracts` anywhere in this migration** — `contracts` is reached only by this GRANT statement
- [x] 3.5 Hand-edit `Up`: create the four functions (`app_set_role_password`, `app_create_login_role`, `app_set_role_login`, `app_clear_must_change_password` — the only `SECURITY DEFINER` one, `search_path` pinned); grant `EXECUTE` per design Decision 9's table
- [x] 3.6 Write `Down` in the fixed order: restore `uploaded_by` as text and write usernames back **first**; then drop FKs and `confirmed_by`; then `DROP FUNCTION`; then `REVOKE ALL` and `DROP ROLE` the two **group** roles only — never an individual login role
- [x] 3.7 Create `docs/runbooks/bootstrap-first-admin.md`: one transaction, owner credential — `CREATE ROLE ... LOGIN PASSWORD`, `ALTER ROLE ... CREATEROLE`, `GRANT inmobiliaria_admin, inmobiliaria_empleado TO <admin> WITH ADMIN OPTION`, matching `INSERT INTO app_users (..., must_change_password=true)`
- [x] 3.8 **[Guardrail — scope guard]** Diff `20260913215911_InitialSchema.cs`, `20260918233049_AddRentAdjustments.cs` (and their `.Designer.cs`) against `HEAD`: confirm both are byte-for-byte unmodified and `ContractConfiguration.cs` is not touched
- [x] 3.9 **[Spec test 29]** `RolePermissionTests` (Testcontainers): after migration, connect as each role and confirm every table in `information_schema.tables` is reachable exactly per its intended grant — none silently unreachable
- [x] 3.10 **[Spec test 30]** `SchemaConstraintTests`: `contract_documents.uploaded_by` is gone; `uploaded_by_user_id` resolves to an `AppUser` row
- [x] 3.10b **[Carried over from PR 2a — this test WILL break when the migration lands]** `SchemaConstraintTests.InvalidDocumentKindCheckConstraint_Rejected` still raw-SQL-inserts `'tester'` into the text column `uploaded_by`. It is green today only because Docker is unreachable and it is skipped. Rewrite it against `uploaded_by_user_id`, inserting a real `AppUser` row first so the FK resolves. Do not delete the test — it proves the `kind` CHECK constraint, which is unrelated to this change and must keep working
- [x] 3.11 **[Spec test 32]** `SchemaConstraintTests`: `rent_adjustments.confirmed_by` is nullable; the migration issued no `UPDATE` against existing rows
- [x] 3.12 **[GATE — Design Decision 9 / Open Question, resolve before PR 4]** `RolePermissionTests` (Testcontainers): create a login role granted `inmobiliaria_admin` `WITH ADMIN OPTION` per the runbook (task 3.7); authenticated **as that role**, attempt `GRANT inmobiliaria_empleado TO <new_role>`. Record whether PostgreSQL 17 honors the inherited `ADMIN OPTION`. **If it fails**: stop — PR 4's provisioning function cannot rely on inheritance; the bootstrap runbook must instead grant `inmobiliaria_empleado ... WITH ADMIN OPTION` directly to every individual Admin login role, and PR 4 (Phase 5) is re-scoped accordingly before it starts

  **RESOLVED — it fails, as design.md suspected.** Tested against a real PostgreSQL 17.6 container (`RolePermissionTests.AdminOptionInheritance_ProvenNotAssumed`): an Admin login role that is only a **plain member** of `inmobiliaria_admin` (provisioned the way `app_create_login_role` — task 3.5 — actually does it: a bare `GRANT inmobiliaria_admin TO ...` with no `WITH ADMIN OPTION` clause, and no `CREATEROLE`) cannot `GRANT inmobiliaria_empleado TO <any_role>` — PostgreSQL 17 rejects it with `42501 insufficient_privilege`. ADMIN OPTION is scoped per `(role, member)` grant edge; it is not inherited through nested group membership or through the default `INHERIT` attribute, which propagates only ordinary privileges (`SELECT`/`INSERT`/etc.), never the right to administer role membership. The runbook's own first-Admin case was unaffected (it grants ADMIN OPTION on both roles directly, per task 3.7), but PR 4's in-app provisioning **cannot rely on inheritance** for every Admin created after the first. `docs/runbooks/bootstrap-first-admin.md` documents the consequence in its "If task 3.12 failed" section; H.3 stands as the human decision point before PR 4 starts.
- [x] 3.13 **[GATE — RESOLVED 2026-09-25 by the project owner, against the live Supabase project]** Result: `log_statement = ddl`, `log_min_duration_statement = -1`, `log_parameter_max_length = -1`. **Gate PASSES — ship PR 4 and PR 5 as designed.** `log_statement` records only statements the server receives at the top level; the client sends `SELECT app_set_role_password(...)`, a `SELECT`, and the `ALTER ROLE` runs inside the plpgsql function where `log_statement` does not reach it. That is precisely what design Decision 3's function shape was chosen for. The second door is shut too: `log_min_duration_statement = -1` means no slow-statement logging, so a delayed call cannot be logged with its parameters either.
  **Residual risk carried into PR 4, narrow but not zero:** `log_parameter_max_length = -1` logs bind parameters in full whenever a statement *is* logged, and PostgreSQL logs failing statements by default (`log_min_error_statement = error`). A failed `SELECT app_set_role_password(...)` could therefore put the password in the log. PR 4 MUST validate its inputs before binding the password — the call must not be allowed to fail with the secret already bound — and MUST confirm this behaviour rather than assume it.
  Original instruction, kept for the record:
  ```sql
  SELECT name, setting FROM pg_settings
   WHERE name IN ('log_statement', 'log_min_duration_statement', 'log_parameter_max_length');
  ```
  Actions per outcome: `none`/`mod` → ship PR 4/PR 5 as designed; `ddl` → already neutralized by Decision 3 (the client issues `SELECT app_set_role_password(...)`, a `SELECT`, not DDL) — ship as designed; `all`, or `log_min_duration_statement = 0` → **STOP**, the plaintext password reaches the log — do not ship in-app password management until this is off, or adopt the documented SCRAM client-side-hashing escape hatch from Decision 3; `setting = NULL` → the GUC is masked for a non-superuser, read it from the Supabase dashboard (Project Settings → Database → Logs) instead
- [x] 3.14 **[Isolation check]** Apply this migration alone on top of `20260918233049_AddRentAdjustments` against a fresh Testcontainers instance; `dotnet test tests/Inmobiliaria.Infrastructure.Tests --filter FullyQualifiedName~SchemaConstraint` green with no PR3/PR4/PR5 code present

## Phase 4: Infrastructure/Access Ports and Adapters (PR 3, est. 570–670 lines)

- [ ] 4.1 Create `SupavisorUsername.cs`: `For(role, projectRef) => $"{role}.{projectRef}"` — `src/Inmobiliaria.Infrastructure/Access/SupavisorUsername.cs`
- [ ] 4.2 Create `ConnectionEndpoint.cs` (Host/Port/Database/ProjectRef/SslMode from `appsettings.json`, no secret) — `.../Access/ConnectionEndpoint.cs`
- [ ] 4.3 Create `IAuthenticator.cs` + `AuthenticationResult.cs` (`Success`/`Rejected`/`NotProvisioned`) — `.../Access/IAuthenticator.cs`, `AuthenticationResult.cs`
- [ ] 4.4 Create `NpgsqlAuthenticator.cs`: builds the data source, handshake, `SELECT current_user, pg_has_role(...)`, loads the `app_users` row — `.../Access/NpgsqlAuthenticator.cs`
- [ ] 4.5 Create `ISessionDbContextFactory.cs` + `SessionDbContextFactory.cs` — `.../Access/ISessionDbContextFactory.cs`, `SessionDbContextFactory.cs`
- [ ] 4.6 Create `IPasswordService.cs` + `PostgresPasswordService.cs`: self password change via `SELECT app_set_role_password(@role,@pw)` with real Npgsql parameters — `.../Access/IPasswordService.cs`, `PostgresPasswordService.cs`
- [ ] 4.7 Create `ApplicationServices.cs`: `AddPreLoginServices` extension; registers nothing DB-shaped before login — `.../Access/ApplicationServices.cs`
- [ ] 4.8 Modify `DesignTimeDbContextFactory.cs`: add one doc sentence recording `INMOBILIARIA_DB` as development-time only — **no behavioral change**
- [ ] 4.9 **[Spec tests 1, 6]** `CompositionGuardTests`: scan config files + runtime environment for a credential (none found); build the pre-login `ServiceCollection` and assert no descriptor assignable to `DbContext`/`NpgsqlDataSource`/`NpgsqlConnection`/`ISessionDbContextFactory` — `tests/Inmobiliaria.Infrastructure.Tests/CompositionGuardTests.cs`
- [ ] 4.10 **[Spec test 1, cont.]** Lexical source guard: `MigrateAsync(`, `Migrate(`, `INMOBILIARIA_DB` appear only in `DesignTimeDbContextFactory.cs` and the test fixture
- [ ] 4.11 **[Spec test 2]** `AuthenticationTests`: no Supabase Auth package reference in the project graph; login opens exactly one Npgsql connection
- [ ] 4.12 **[Spec tests 3, 4, 5]** `AuthenticationTests` (Testcontainers): correct credentials authenticate; wrong password fails with a generic message; unknown username fails with identical wording
- [ ] 4.13 **[Spec tests 7, 8]** `AuthenticationTests` (Testcontainers): role derives from `pg_has_role`, not a stored column; a database-side membership change takes effect on the next login with no app-side update
- [ ] 4.14 **[Spec test 13]** `AuthenticationTests` (Testcontainers): self password change via `IPasswordService` succeeds; the already-open session keeps working without re-authenticating
- [ ] 4.15 **[Spec test 19]** `PasswordDdlTests` (Testcontainers): a user with `MustChangePassword` pending, connecting directly, has privileges identical to any other time — the honest limit, not enforcement
- [ ] 4.16 **[Spec tests 20, 21]** `PasswordDdlTests` (Testcontainers): `o'brien55` and `x'; DROP TABLE app_users; --` are each set as the exact literal password via `app_set_role_password`; `app_users` still exists; the payload string authenticates afterward
- [ ] 4.17 **[Spec tests 22, 23]** `RolePermissionTests` (Testcontainers): as Empleado, a direct `INSERT INTO app_users` and a call to the provisioning function are each refused with no row/role created; the equivalent Admin operation succeeds
- [ ] 4.18 **[Spec test 24]** `RolePermissionTests` (Testcontainers): `ALTER TABLE rent_adjustments DISABLE TRIGGER ...` refused for both roles
- [ ] 4.19 **[Spec test 28]** `RolePermissionTests` (Testcontainers): both roles, granted `SELECT` on the same table, retrieve every row — no row-level policy filters either
- [ ] 4.20 **[Spec test 25 — positive assertion, do not reduce to a smoke test]** `RolePermissionTests` (Testcontainers): as Empleado, `Contract.GiveNotice` **and** `Contract.End` both succeed end-to-end, writing `status`, `actual_end_date`, `end_reason`. This proves a grant exists; keep the full termination flow
- [ ] 4.21 **[Spec test 26 — positive assertion, do not reduce to a smoke test]** `RolePermissionTests` (Testcontainers): as Empleado, confirm a rent adjustment against a seeded contract+clause+index values (reuse `SchemaConstraintTests`' `SeedContractWithClause` helper); assert the `rent_adjustments` row **and** `rent_adjustment_index_values` rows insert in one transaction with `confirmed_by` = her `AppUser` id — the standing guard against the removed money clause returning as an `INSERT` grant quietly withheld
- [ ] 4.22 **[Spec test 27]** `RolePermissionTests` (Testcontainers): as Empleado, `SELECT sum(monthly_rent) FROM contracts` **succeeds** — asserts the exclusion is application-enforced only, never a claimed `GRANT`
- [ ] 4.23 **[Spec test 33]** `RolePermissionTests` (Testcontainers): confirming an adjustment as any authenticated user stores that user's `AppUser` reference in `confirmed_by`
- [ ] 4.24 **[Spec test 34]** `SchemaConstraintTests`: re-run the archived append-only assertions (raw SQL `UPDATE`/`DELETE` on `rent_adjustments`) against the modified schema — still refused for both roles
- [ ] 4.25 **[Guardrail]** Confirm every test added in this phase lives under `tests/Inmobiliaria.Infrastructure.Tests/`, never `Inmobiliaria.Desktop` — Desktop is excluded from `Inmobiliaria.Core.slnf` and cannot gate the `core` CI job
- [ ] 4.26 **[Isolation check]** Build and run `Inmobiliaria.Infrastructure.Tests` on this branch alone, on top of PR1+2a+2b merged, with none of PR4/PR5's code present

## Phase 5: In-App Provisioning, Reset, Deactivation (PR 4, est. 380–470 lines)

- [ ] 5.1 Create `IUserProvisioning.cs` + `PostgresUserProvisioning.cs`: `CreateUser` (login role + `app_users` row as one transaction via `app_create_login_role`), `ResetPassword`, `Deactivate` (`app_set_role_login(name,false)` + `is_active=false`) — `src/Inmobiliaria.Infrastructure/Access/IUserProvisioning.cs`, `PostgresUserProvisioning.cs`
- [ ] 5.2 **[Spec test 9]** `UserProvisioningTests` (Testcontainers): Admin creates a user as one unit — both the login role and `app_users` row exist; a forced rollback proves neither is orphaned
- [ ] 5.3 **[Spec test 10]** `UserProvisioningTests`: `ALTER ROLE ... NOLOGIN` on an active user fails their next login attempt
- [ ] 5.4 **[Spec test 11]** `UserProvisioningTests`: a deactivated user's `ContractDocument`/`RentAdjustment` rows remain readable and unchanged
- [ ] 5.5 **[Spec test 12]** `UserProvisioningTests`: provisioning a username already assigned to a deactivated user is rejected or never produces a shared identity
- [ ] 5.6 **[Spec test 14]** `UserProvisioningTests`: an Admin's reset on a different user takes effect only at that user's next login, proven by keeping their prior session alive across the reset
- [ ] 5.7 **[Spec test 15 — database half]** `UserProvisioningTests`: provisioning a user sets `AppUser.MustChangePassword = true` (the "cannot reach anything else" half is proven in Phase 6, task 6.7, over the bootstrap construction order — this is the split named in design's Testing Strategy)
- [ ] 5.8 **[Spec test 16]** `UserProvisioningTests`: after an Admin resets an active user's password, `MustChangePassword` is set and the next authentication requires a change first
- [ ] 5.9 **[Spec test 17]** `UserProvisioningTests`: re-submitting the just-authenticated password as the "new" one is rejected; the pending requirement remains set
- [ ] 5.10 **[Spec test 18]** `UserProvisioningTests`: a voluntary password change with nothing pending does not set `MustChangePassword`
- [ ] 5.11 **[Spec test 31]** `UserProvisioningTests`: a `ContractDocument`'s uploader FK still resolves and displays the uploader's name after that uploader is deactivated
- [ ] 5.12 **[Guardrail]** Confirm every test added in this phase lives under `tests/Inmobiliaria.Infrastructure.Tests/`
- [ ] 5.13 **[Isolation check]** Build and run `Inmobiliaria.Infrastructure.Tests` on this branch alone, on top of PR1–PR3 merged, with none of PR5's Desktop code present

## Phase 6: Desktop Bootstrap (PR 5, est. 400–500 lines)

- [ ] 6.1 Add `CommunityToolkit.Mvvm` and `Microsoft.Extensions.DependencyInjection` package versions to `Directory.Packages.props`; add `Inmobiliaria.Desktop`'s first `Inmobiliaria.Infrastructure` project reference — `Directory.Packages.props`, `src/Inmobiliaria.Desktop/Inmobiliaria.Desktop.csproj`
- [ ] 6.2 Create `LoginViewModel.cs` (`ObservableObject`; Username, Password, IsBusy, ErrorMessage, LoginCommand; depends only on `IAuthenticator`) — `src/Inmobiliaria.Desktop/LoginViewModel.cs`
- [ ] 6.3 Create `LoginWindow.xaml` + `LoginWindow.xaml.cs` — `src/Inmobiliaria.Desktop/LoginWindow.xaml{,.cs}`
- [ ] 6.4 Create `ChangePasswordViewModel.cs` (`IsForced` toggles cancel; depends on `IPasswordService`, `IUserSession`) — `src/Inmobiliaria.Desktop/ChangePasswordViewModel.cs`
- [ ] 6.5 Create `ChangePasswordWindow.xaml` + `ChangePasswordWindow.xaml.cs` — `src/Inmobiliaria.Desktop/ChangePasswordWindow.xaml{,.cs}`
- [ ] 6.6 Modify `App.xaml.cs`: `OnStartup` calls `AddPreLoginServices()`, shows `LoginWindow` modally; on `MustChangePassword` shows `ChangePasswordWindow` modally before constructing `MainWindow`; a cancelled forced change disposes the session and returns to `LoginWindow`
- [ ] 6.7 **[Spec test 15 — construction-order half]** Add a test in `Inmobiliaria.Infrastructure.Tests` (NOT a WPF/Desktop test — Desktop cannot gate the `core` job) asserting over the bootstrap construction sequence that with `MustChangePassword = true`, the path cannot reach `MainWindow`/anything else until the change succeeds
- [ ] 6.8 Modify `openspec/config.yaml`: move `credential-exposure` to `resolved_decisions`; correct the `data-api-disabled` rationale; add the "every new table ships its GRANTs" convention entry
- [ ] 6.9 **[Guardrail]** Confirm `Inmobiliaria.Desktop.csproj`'s new Infrastructure reference exposes no EF/Npgsql type to XAML outside the bootstrap composition root
- [ ] 6.10 **[Isolation check]** Build the full solution and run `Inmobiliaria.Core.slnf` (Linux `core` job) and the Windows `build` job on this branch alone, on top of PR1–PR4 merged

## Human Follow-Ups (non-code, not assigned to any agent)

- [ ] H.1 User applies the new migration to the live Supabase project with their own credentials — no agent connects to Supabase (same as tasks 3.9–3.13's constraint)
- [ ] H.2 User runs the exact query in task 3.13 against the live Supabase project and reports the `log_statement` outcome before Phase 5 (PR 4) starts
- [ ] H.3 If task 3.12's `ADMIN OPTION` proof fails, a human decides between widening the bootstrap runbook (grant `WITH ADMIN OPTION` per Admin login role) versus revisiting Decision 9 — not an agent decision
- [ ] H.4 The CHECK constraint `(status='Ended') = (actual_end_date IS NOT NULL AND end_reason IS NOT NULL)` is a carried follow-up for the collection change, per design Decision 5 — not this change

## Risks / Findings (surfaced, not silently resolved)

1. **design.md's own size forecast is internally inconsistent.** Its headline text (Migration/Rollout
   section) states "1,910–2,410 authored lines, five chained PRs", but its own itemised per-PR table
   (240–320 + 360–440 + 365–455 + 570–670 + 380–470 + 400–500) sums to **2,315–2,855**. This tasks.md
   uses the itemised sum as the operative estimate. Both figures exceed the 800-line session budget by
   a wide margin, so the chaining decision is unaffected — but the ≈400-line gap is a design.md defect
   worth a maintainer's attention.
2. **Spec test 33 and test 31 are absent from design.md's own per-PR test-number ranges**, even though
   design's "Testing Strategy" table pairs them with tests it does enumerate (33 with 34; 31 with 11).
   This tasks.md assigns 33 to PR 3 (task 4.23, alongside 34) and 31 to PR 4 (task 5.11, alongside 11)
   by following the Testing Strategy table's pairing rather than the narrower per-PR prose, so all 34
   spec tests have exactly one home. Flagging this rather than silently patching design.md.
3. **Test 15 is genuinely split across two PRs** (database half in PR 4, task 5.7; construction-order
   half in PR 5, task 6.7), per design's own "Split" note in Testing Strategy. Neither half alone
   proves the full requirement; both tasks must land before test 15 is reported as covered.
4. Slice ordering is forced and none of it can run in parallel: PR2a needs PR1's entity; PR2b needs
   PR2a's configuration; the Phase 3 gate checks need PR2b's roles/functions; PR3 needs PR2b's roles to
   authenticate against; PR4 needs PR3's session; PR5 needs PR3's ports and PR4's provisioning.

## Key Learnings

1. `dotnet build` does not validate the EF model — a slice that adds an entity or navigation needs a
   `dotnet ef dbcontext info` (or equivalent) check before its migration exists, not just a green build.
2. `Inmobiliaria.Desktop` is excluded from `Inmobiliaria.Core.slnf`, so any guard or gate test placed
   there cannot gate a merge through the Linux `core` CI job and must live in `Inmobiliaria.Infrastructure.Tests`.
3. Two facts this change depends on are unproven and must be tested before the slice that assumes them:
   PostgreSQL 17's `ADMIN OPTION` inheritance, and whether Supabase's `log_statement` would leak a
   plaintext password to the server log.
4. Tests 25 and 26 exist specifically to stop a previously-removed restriction (Empleado excluded from
   money-moving or append-only writes) from quietly returning as a withheld GRANT, so both must remain
   positive, end-to-end assertions rather than smoke tests.
5. design.md's own size-forecast prose and its itemised per-PR table disagree by roughly 400 lines,
   which is worth surfacing rather than picking one number silently.
