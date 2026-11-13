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

None yet.

## Deviations

None yet.

## Phase 1: The migration and its tests (PR 1, est. about 200 lines)

**Docker: yes. Supabase: no. Model change: no (SQL-only migration, snapshot unchanged).** Spec tests: 1 to 8 and 10 (9 is not
written, D6). Requirements 1 to 5. Design anchors: D1 to D11, §2, §3, §4, F1 to F3. Risk: item 2 (permissions).

- [ ] 1.1 **[Second inspection, read-only]** Anchors match: `20261112160850_AddTenantNotices.cs:88-101` (guarded loop), `AddUsersAndRoles.cs:148,173-188,209-216`, `HardenClearMustChangePassword.cs:42`, `PostgresFixture.cs:40,76-85`, `TenantNoticeRecordTests.cs:162-173`, `ReciboSchemaTests.cs:948-968`, `AdjustmentDifferenceMigrationTests.cs:115-119`, `ClauseCorrectionTests.cs:447-449`. Latest migration still `20261112160850_AddTenantNotices` (F3). Baseline suite recorded with counts and `TIME_WAIT`
- [ ] 1.2 **[Fixture]** `PostgresFixture.cs` gains the default `GRANT ALL ON SEQUENCES` to the three API roles (D10, §3)
- [ ] 1.3 **[RED]** `SupabaseApiRoleRevocationTests` (Tests 1 to 4, 6 to 8, 10) and its Test 5 class with the plain `postgres:17.6` container (D11); isolated database per test, migrated to `20261112160850_AddTenantNotices` (D9); privileges read with `has_*_privilege`, eight table privileges including `MAINTAIN`; operator function = `prorettype <> 'trigger'::regtype`. Observed failing because the migration does not exist yet
- [ ] 1.4 **[GREEN]** `RevokeSupabaseApiRoles` via `dotnet ef migrations add`, `Up` as design §2, `Down` empty with its comment (D8); `TenantNoticeRecordTests.cs:167` removed (§3). The new tests, `RolePermissionTests`, `ChosenPasswordAndLoginTests`, `ReciboGrantsTests` and `ClauseCorrectionTests` green
- [ ] 1.5 **[Prove the guards by making them fail]** Each mutation run and reverted byte-identical (md5): the table revoke removed → Test 2; the default revoke removed → Test 6; the three `PUBLIC` revokes removed → Test 7. Test 8's helper names a function created open (the spec's "shown failing")
- [ ] 1.6 **[Guardrails]** Build, suite, `has-pending-model-changes`, per the table below, with `TIME_WAIT` sampled during the run
- [ ] 1.7 **[Independent verification and gentle-ai review]** Item 2 named
- [ ] 1.8 **[Handover]** File list, commit Summary and Description, PR title and description to Tomás. No commit by the assistant

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
