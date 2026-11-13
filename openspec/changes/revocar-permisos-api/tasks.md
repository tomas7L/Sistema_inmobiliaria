# Tasks: revocar-permisos-api (closing Supabase's API roles out of the database)

Turns `design.md` §7 (one slice) into ordered, checkable work. It does not re-plan the change and does not edit `spec.md` or
`design.md`. Where applying exposes a gap, it is listed under **Issues found**.

## What to read (Tomás)

| Read | Skip |
|---|---|
| This header, the **Review Workload Forecast**, and **Phase 2** (what you apply, and the query that closes it) | **Phase 1** except its **Verification** table. The checkboxes are for the assistant and for the second inspection at the start of each session |

## Review Workload Forecast

| Field | Value |
|---|---|
| Estimated changed lines | About 200 authored (additions + deletions, tests included; the migration's Designer file is generated and not counted), design §7 |
| Review budget | About 400 authored lines per PR, a planning heuristic and not a cap |
| 400-line budget risk | Low |
| Chained PRs recommended | No: one PR |
| Delivery strategy | ask-on-risk |

Decision needed before apply: No

## Issues found

- **Independent verifier, 2026-11-13: no blockers.** Every Requirement (1 to 5) met, every written Test (1 to 8, 10) passes; its own
  probes on a throwaway `postgres:17.6`, migrating as a non-superuser like `postgres` on Supabase: the F2 query went from 9 rows to
  none, the migration ran twice without error, an Empleado's insert still fired its trigger, the group roles' function matrix and
  default `SELECT` held. Three advisories:
  - **(1) Fixed: design F1 was wrong.** Revoking on an object the migrating role does not own is a warning only when it holds some
    privilege there; with none, Postgres refuses and the migration rolls back. Safe either way; `design.md` §5 F1 corrected.
  - **(2) Left: spec Requirement 1 says a database without the API roles is not changed**, but the three `PUBLIC` revokes
    (Requirement 3) still apply there, as intended. Wording only.
  - **(3) Left: column-level grants are not covered** by the migration, the tests or the F2 query. Supabase grants none; out of domain.
- **gentle-ai review, 2026-11-13** (`review-9b3e389ca9120928`, approved): three non-blocking findings.
  - **(R3-sequence-revoke-unproven) Fixed after the review, so not reviewed by it.** No sequence exists in the model, so Tests 2-3's
    sequence assertion passed on nothing. The test now creates `earlier_sequence` before migrating, asserts every API role holds it,
    and the assertion after failed under the mutation removing the sequences revoke.
  - **(R3-non-superuser-path-untested) Left.** The tests migrate as the container superuser; Supabase's `postgres` is not one, and
    an object in `public` it holds no privilege on would abort the migration (design F1, corrected). The verifier ran it by hand as a
    non-superuser without error; production's F2 rows are all this system's tables. A failure rolls back whole and names the object.
  - **(R1-001, suggestion) Left.** Without `FOR ROLE`, the default revoke covers only objects the migrating role creates. That is
    design D4, on purpose: every migration runs as `postgres`.

## Deviations

None yet.

## Phase 1: The migration and its tests (PR 1, est. about 200 lines)

**Docker: yes. Supabase: no. Model change: no (SQL-only migration, snapshot unchanged).** Spec tests: 1 to 8 and 10 (9 is not
written, D6). Requirements 1 to 5. Design anchors: D1 to D11, §2, §3, §4, F1 to F3. Risk: item 2 (permissions).

- [x] 1.1 **[Second inspection, read-only]** Anchors match: `20261112160850_AddTenantNotices.cs:88-101` (guarded loop), `AddUsersAndRoles.cs:148,173-188,209-216`, `HardenClearMustChangePassword.cs:42`, `PostgresFixture.cs:40,76-85`, `TenantNoticeRecordTests.cs:162-173`, `ReciboSchemaTests.cs:948-968`, `AdjustmentDifferenceMigrationTests.cs:115-119`, `ClauseCorrectionTests.cs:447-449`. Latest migration still `20261112160850_AddTenantNotices` (F3). Baseline suite recorded with counts and `TIME_WAIT`. **Done 2026-11-13**: #122 merged as `90c2d88`, working tree clean; every anchor as cited. No file under `src/` or `tests/` changed since `6ab4dd2`, so the baseline is that commit's run: 2213 passed (Domain 738, Presentation 554, Infrastructure 921), 0 failed, 0 skipped
- [x] 1.2 **[Fixture]** `PostgresFixture.cs` gains the default `GRANT ALL ON SEQUENCES` to the three API roles (D10, §3). **Done**
- [x] 1.3 **[RED]** `SupabaseApiRoleRevocationTests` (Tests 1 to 4, 6 to 8, 10) and its Test 5 class with the plain `postgres:17.6` container (D11); isolated database per test, migrated to `20261112160850_AddTenantNotices` (D9); privileges read with `has_*_privilege`, eight table privileges including `MAINTAIN`; operator function = `prorettype <> 'trigger'::regtype`. Observed failing because the migration does not exist yet. **Done**: `SupabaseApiRoleRevocationTests` (7 tests: 1, 2 with 3, 4, 6, 7, 8, 10) and `MigrationWithoutSupabaseRolesTests` (Test 5). RED: 6 failed (2-3, 5, 6, 7, 8, 10), each on its assertion; Tests 1 and 4 passed, as a before state should
- [x] 1.4 **[GREEN]** `RevokeSupabaseApiRoles` via `dotnet ef migrations add`, `Up` as design §2, `Down` empty with its comment (D8); `TenantNoticeRecordTests.cs:167` removed (§3). The new tests, `RolePermissionTests`, `ChosenPasswordAndLoginTests`, `ReciboGrantsTests` and `ClauseCorrectionTests` green. **Done**: `20261113165451_RevokeSupabaseApiRoles`, snapshot unchanged. The 8 new tests, `TenantNoticeRecordTests` and the permission classes named, plus `ClearMustChangePasswordHardeningTests`: 84 of 86 passed on the first run; the two failures were line 167 (as spec F4 predicted, then removed) and Test 10 reading the history table by a column name it does not have (snake_case; it now asks EF). After both: the change's three classes, 17 passed
- [x] 1.5 **[Prove the guards by making them fail]** Each mutation run and reverted byte-identical (md5): the table revoke removed → Test 2; the default revoke removed → Test 6; the three `PUBLIC` revokes removed → Test 7. Test 8's helper names a function created open (the spec's "shown failing"). **Done**, each reverted byte-identical (md5): table revoke → Tests 2-3; default revoke → Test 6; the three `PUBLIC` revokes (replaced by `SELECT 1;`) → Tests 7 and 8. A first try at the last one deleted the lines, left an empty raw string that did not compile, and ran the old build: discarded. After the review (R3-sequence-revoke-unproven): the sequences revoke replaced by `NULL;` → Tests 2-3, reverted byte-identical
- [x] 1.6 **[Guardrails]** Build, suite, `has-pending-model-changes`, per the table below, with `TIME_WAIT` sampled during the run. **Done 2026-11-13**: Docker 29.9.3; build exit 0, 0 Advertencia(s), 0 Errores; suite exit 0, 2221 passed (Domain 738, Presentation 554, Infrastructure 929 = 921 + 8), 0 failed, 0 skipped; `TIME_WAIT` 165 before, peak 5,292 during (20-second samples), 5,319 after; `has-pending-model-changes` exit 0
- [x] 1.7 **[Independent verification and gentle-ai review]** Item 2 named. **Done 2026-11-13.** Verifier: no blockers, three advisories (Issues found). gentle-ai, on a scratch clone in one candidate (code and tests, no documents; 6 files, 1,897 lines with the generated Designer; lenses risk and reliability): `review-9b3e389ca9120928`, approved, acknowledged; three non-blocking findings, one taken up afterwards (Issues found)
- [x] 1.8 **[Handover]** File list, commit Summary and Description, PR title and description to Tomás. No commit by the assistant. **Handed over 2026-11-13**: 8 paths; after the review follow-up, the change's three test classes 17 passed and the test project built with 0 warnings; the full suite's last run (1.6) predates only that one test's strengthening

### Verification, PR 1 (Docker: yes)

| Command | Expected |
|---|---|
| `dotnet build Inmobiliaria.sln --configuration Release` | exit code 0; **Advertencia(s)** and **Errores** both 0. MSB3027 means the app is open |
| `dotnet test Inmobiliaria.Core.slnf --configuration Release` (Docker up) | exit code 0; 0 failed; 0 skipped; passed = baseline + new tests − none removed (line 167 is an assertion, not a test) |
| `dotnet ef migrations has-pending-model-changes -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure` | exit code 0, no pending changes |

---

## Phase 2: Applying it (Tomás, after PR 1 is merged)

**Docker: no. Supabase: yes, the test project and production, restored first. Only Tomás connects.** Spec Decision 11.

- [ ] 2.1 **Test database:** `database update` as every migration (`docs/ESTADO.md`, "Aplicar una migración"). Then the F2 query in its SQL editor returns **no rows**, and the app opens and logs in as before
- [ ] 2.2 **Production:** the same `database update`, which applies `20261112160850_AddTenantNotices` first and this migration after (spec Finding F5). Then the F2 query returns **no rows**, and the app opens and logs in as before
- [ ] 2.3 **Close:** `docs/ESTADO.md`'s "Permisos de más en producción" item removed; the `access-control` delta merged into `openspec/specs/` at archive

The F2 query (`notificaciones/design.md:261-264`):

```sql
SELECT grantee, table_name, string_agg(privilege_type, ',' ORDER BY privilege_type)
FROM information_schema.role_table_grants
WHERE table_schema = 'public' AND grantee IN ('anon', 'authenticated', 'service_role')
GROUP BY grantee, table_name ORDER BY table_name, grantee;
```

If it returns a row: do not grant anything back; paste the row in the chat (design §6, F1).
