# Apply Progress: Rent Adjustments by Index

## Scope of this batch

Slice 1 only — `Domain/Indices` (PR 1). Tasks 1.1–1.9 from `tasks.md`. Slices 2–4 are
untouched: no `Leasing/Adjustment*`, `Leasing/RentAdjustment*`, EF configurations, migration,
or worklist read model exist in this change.

## Completed Tasks

- [x] 1.1 `IndexPeriod` readonly record struct (Year, Month, `AddMonths`, `IComparable<IndexPeriod>` with `<`/`>`/`<=`/`>=`)
- [x] 1.2 `EconomicIndex` (Id, Name, `DiscontinuedFrom?`, `SuccessorIndexId?`, `MarkDiscontinued` rejecting self-reference)
- [x] 1.3 `IndexValue` (Id, EconomicIndexId, Period, Level with `level > 0` guard, `Correct(newLevel)`)
- [x] 1.4 `IndexResolution` closed result hierarchy: `Resolved(EconomicIndex)` / `Unresolved(reason)`
- [x] 1.5 `IndexResolver.Resolve(index, period, catalog)`: walks `SuccessorIndexId` while `period >= DiscontinuedFrom`, 8-hop cap
- [x] 1.6 `IndexResolverTests` — spec test 15 (one successor hop; period before discontinuation), plus no-successor and hop-cap/cycle cases
- [x] 1.7 `EconomicIndexTests` — discontinuation without successor accepted; self-successor rejected; blank name rejected; discontinuation leaves stored `IndexValue`s unchanged
- [x] 1.8 `IndexValueTests` — zero/negative level rejected on construction and on `Correct`; `Correct` replaces level in place, id stable
- [x] 1.9 `ArchitectureGuardTests` re-run — passes; new `Indices` files add no EF Core/Npgsql/WPF reference to `Inmobiliaria.Domain`

## Files Changed

| File | Action | What Was Done |
|---|---|---|
| `src/Inmobiliaria.Domain/Indices/IndexPeriod.cs` | Created | Value type: year+month, `AddMonths`, ordering operators |
| `src/Inmobiliaria.Domain/Indices/EconomicIndex.cs` | Created | Catalog entry, discontinuation + successor naming |
| `src/Inmobiliaria.Domain/Indices/IndexValue.cs` | Created | Published level per index/period, in-place correction |
| `src/Inmobiliaria.Domain/Indices/IndexResolution.cs` | Created | Closed `Resolved`/`Unresolved` result hierarchy |
| `src/Inmobiliaria.Domain/Indices/IndexResolver.cs` | Created | Pure successor-chain walk with 8-hop cap |
| `tests/Inmobiliaria.Domain.Tests/IndexResolverTests.cs` | Created | 4 tests: one hop, before-discontinuation, no-successor, cycle/hop-cap |
| `tests/Inmobiliaria.Domain.Tests/EconomicIndexTests.cs` | Created | 4 tests: no-successor accepted, self-successor rejected, values untouched, blank name rejected |
| `tests/Inmobiliaria.Domain.Tests/IndexValueTests.cs` | Created | 4 tests: zero/negative rejected (ctor and `Correct`), `Correct` keeps id stable |
| `openspec/changes/rent-adjustments/tasks.md` | Modified | Marked Slice 1 tasks 1.1–1.9 `[x]` |

Authored lines: 381 (5 domain files: 212 lines; 3 test files: 169 lines) — within the 400–480
PR1 budget (under, not over).

## Deviations from Design

- **`IndexResolver.Resolve` signature.** Design's pseudocode and `tasks.md` both write
  `Resolve(index, period)`. That two-argument shape cannot actually walk a multi-hop successor
  chain: after the first hop, resolving the *next* index's own `DiscontinuedFrom`/
  `SuccessorIndexId` requires access to that index object, which a lone `EconomicIndex`
  parameter cannot provide. Implemented as
  `Resolve(EconomicIndex index, IndexPeriod period, IReadOnlyDictionary<Guid, EconomicIndex> catalog)`
  — the caller supplies every index the chain might touch, already loaded in memory. This keeps
  the function pure (design's explicit requirement: "a pure domain function, no
  infrastructure") while making the walk actually possible. Flagging per the instruction to
  follow the spec for *what* and the design for *how*, and note any disagreement: this is a gap
  in the design's *how* rather than a disagreement with the spec, so I resolved it in the
  direction the design's own prose argues for (in-memory chain walk, no infra) rather than
  inventing a hidden static lookup or a service dependency.
- Everything else matches `design.md` Decision 7 and the `economic-index` capability of
  `spec.md` as written.

## Issues Found

None.

## Remaining Tasks (not in this batch's scope)

- [ ] Slice 2 — `Domain/Leasing` arithmetic (PR 2, tasks 2.1–2.20)
- [ ] Slice 3 — EF configurations + migration (PR 3, tasks 3.1–3.16)
- [ ] Slice 4 — Worklist read model + adapter (PR 4, tasks 4.1–4.9)
- [ ] Human follow-ups H.1–H.5 (non-code)

## Work Unit Evidence

| Evidence | Value |
|---|---|
| Focused test command and exact result | `dotnet test tests/Inmobiliaria.Domain.Tests -c Release --filter "FullyQualifiedName~Index"` → 12/12 passed |
| Full domain suite | `dotnet test tests/Inmobiliaria.Domain.Tests -c Release` → 35/35 passed (24 pre-existing + 11 new) |
| Build | `dotnet build Inmobiliaria.sln -c Release` → 0 warnings, 0 errors (`TreatWarningsAsErrors=true`) |
| Architecture guard | `dotnet test tests/Inmobiliaria.Domain.Tests -c Release --filter "FullyQualifiedName~ArchitectureGuard"` → 1/1 passed |
| Runtime harness | N/A — pure domain, no DB, no Testcontainers boundary in this slice |
| Rollback boundary | Delete `src/Inmobiliaria.Domain/Indices/*` and the three new test files; revert the `tasks.md` checkbox edit. Nothing else in the codebase references these types yet |

## Workload / PR Boundary

- Mode: chained PR slice (`stacked-to-main`, target branch `develop` per session preflight)
- Current work unit: Unit 1 — Index catalog + supersession (PR 1)
- Boundary: starts from nothing (no prior `Indices/` code existed) and ends with a complete,
  tested `EconomicIndex`/`IndexValue`/`IndexPeriod`/`IndexResolver` set with zero consumers yet
- Estimated review budget impact: 381 authored lines, under the 400–480 forecast for this unit

## Status

9/9 Slice 1 tasks complete. Ready for verify (Slice 1 scope only) or for Slice 2 apply to begin.
