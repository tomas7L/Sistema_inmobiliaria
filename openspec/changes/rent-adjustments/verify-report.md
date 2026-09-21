# Verification Report: rent-adjustments

**Change**: rent-adjustments -- Rent Adjustments by Index
**Mode**: Full spec-driven verification (proposal/spec/design/tasks all present)
**Date**: 2026-09-18
**Branch state**: develop at 3c65123; working tree clean; ramatomy fully merged (no diff vs develop)

## Completeness

| Slice | PR | Tasks | Status |
|---|---|---|---|
| 1 - Domain/Indices | #12 | 1.1-1.9 (9) | Complete |
| 2 - Domain/Leasing arithmetic + Contract wiring | #14 | 2.1-2.20 (20) | Complete |
| 3 - EF configurations + migration | #15 | 3.0-3.16 (17) | Complete |
| 4 - Worklist read model + adapter | #16 | 4.1-4.9 (9) | Complete |
| Human follow-ups | -- | H.1-H.5 (5) | Correctly left unchecked (non-code, not assigned to any agent) |

55/55 code tasks complete. Confirmed against the on-disk tasks.md (all checked) and the git log
(3c65123 merges #16, 36215da "add the due adjustment worklist", following #12/#14/#15).

Artifact sync note (SUGGESTION, not a code defect): the on-disk
openspec/changes/rent-adjustments/apply-progress.md is stale -- it documents only Slices 1-3 and
explicitly states Slice 4 is untouched. The Engram-stored apply-progress observation
(topic key sdd/rent-adjustments/apply-progress, id 28, revision 4) correctly documents all four
slices as complete and matches the actual codebase and git history. Recommend overwriting the
on-disk file from the Engram copy before archiving so a filesystem-only reader is not misled.

## Build and Test Evidence (executed, not assumed)

    $ dotnet build Inmobiliaria.sln --configuration Release
    Compilacion correcta. 0 Advertencia(s), 0 Errores. (7.55s)

    $ dotnet test Inmobiliaria.Core.slnf --configuration Release
    Inmobiliaria.Domain.Tests.dll         -> Superado: 52, Con error: 0, Omitido: 0, Total: 52
    Inmobiliaria.Infrastructure.Tests.dll -> Superado: 19, Con error: 0, Omitido: 0, Total: 19

Docker was running for this session; 0 tests skipped -- every SkippableFact in
SchemaConstraintTests and DueAdjustmentQueryTests executed for real against Testcontainers
postgres:17.6, not skipped. Matches the expected 52 domain + 19 integration = 71 total.

git diff --stat develop..HEAD is empty -- nothing uncommitted, nothing to lose.

## Spec Compliance Matrix -- the 18 numbered tests

| # | Test | Covering test(s) | Status |
|---|---|---|---|
| 1 | Averaging variations, not levels | AdjustmentMathTests.Compute_AveragesVariations_NotRawLevels | PASS |
| 2 | Single-index skips averaging | AdjustmentMathTests.Compute_SingleIndexClause_SkipsAveraging | PASS |
| 3 | Truncation to a whole peso | AdjustmentMathTruncationTests.Confirm_ComputedAmountWithCentavos_TruncatesToWholePeso | PASS |
| 4 | Truncation never rounds up | AdjustmentMathTruncationTests.Confirm_TruncationNeverRoundsUp | PASS |
| 5 | No commercial rounding | AdjustmentMathTruncationTests.Confirm_ComputedAmountAlreadyWhole_IsNeverLiftedToARounderFigure | PASS |
| 6 | Decimal, never float | Domain: ConfirmAdjustment_ThroughContract_TruncatesExactlyOnceWithNoProgressiveDrift; Integration: SchemaConstraintTests.NumericColumnPrecision_MatchesMoneyAndIndexLevelConventions | PASS (both halves) |
| 7 | Missing index leaves adjustment pending | DueAdjustmentQueryTests.MissingIndexValue_LeavesCoefficientAndProposedCanonNullAndCanonUnaffected | PASS |
| 8 | Partial index set never averaged | AdjustmentMathTests.Compute_OneOfTwoIndicesMissing_CoefficientStaysNull | PASS |
| 9 | Worklist names missing index/period | DueAdjustmentQueryTests.ThreeContractsAwaitingIpc_AreReturnedNamingTheMissingIndexAndPeriod | PASS |
| 10 | Worklist clears once value arrives | DueAdjustmentQueryTests.OnceTheMissingValueArrives_NoneOfTheContractsWaitsOnAMissingValue | PASS |
| 11 | Late adjustment charges nothing retroactively | ContractAdjustmentLateConfirmationTests.ConfirmAdjustment_ConfirmedLate_AppendsExactlyOneAdjustment | SCOPED/PARTIAL -- see below, not fully proven |
| 12 | Late adjustment resumes correct canon | ContractAdjustmentLateConfirmationTests.ConfirmAdjustment_ConfirmedLate_CanonAfterwardEqualsTheNewCanon | SCOPED/PARTIAL -- see below |
| 13 | Adjustment history append-only | SchemaConstraintTests.AppendOnlyTrigger_RejectsRawUpdateAndDelete (raw SQL UPDATE/DELETE, real PostgresException) | PASS |
| 14 | Corrected index value produces new adjustment | SchemaConstraintTests.CorrectingAnIndexValue_LeavesTheConfirmedAdjustmentUnchangedAndAppendsACorrection | PASS |
| 15 | Index supersession resolves | IndexResolverTests.Resolve_OneSuccessorHop_ResolvesThroughSuccessor | PASS |
| 16 | No clause never becomes due | DueAdjustmentQueryTests.ContractWithNoAdjustmentClause_NeverAppearsInGetDueAsync | PASS |
| 17 | Shares survive an adjustment | ContractShareSurvivesAdjustmentTests.ConfirmAdjustment_TwoUnitSixtyFortySplit_KeepsSharesSummingToOneHundred | PASS |
| 18 | Per-contract interval honoured | AdjustmentScheduleTests.NextDueDate_ThreeMonthInterval_IsNeverAssumedSemiannual | PASS |
| extra | Cross-splice refused, not computed (Decision 7) | AdjustmentMathTests.Compute_BaseAndEndResolveToDifferentIndices_IsRefused | PASS (domain only -- see WARNING findings for the adapter-level gap) |

Tests 11/12 carried forward honestly, per the orchestrator instruction and the change's own
documentation (tasks.md, design.md, apply-progress all say the same thing): no billing exists in
this change, so "no retroactive charge was generated" is not assertable. Only "exactly one
adjustment appended, no other row" (11) and "canon equals new canon afterward" (12) are proven.
Reported here as NOT fully covered -- the current-account change must re-assert both for real.
This is not a new defect; it is disclosure of an already-acknowledged, already-scoped gap.

16 of 18 numbered tests are fully proven at runtime; 2 are honestly partial by design.

## Requirement-Level Compliance (beyond the 18 numbered tests)

Spec has 20 requirements and 36 Given/When/Then scenarios across the three capabilities
(economic-index: 5 req / 8 scenarios; rent-adjustment: 13 req / 24 scenarios; lease-contract: 2 req
/ 4 scenarios). All scenarios were checked; the two gaps below are new findings not carried in any
change document.

- economic-index / Index Identity and Published Value Stored Per Period each state a hard
  "MUST reject" rule enforced by a real database constraint (ix_economic_indices_name_active, a
  partial unique index on name WHERE discontinued_from IS NULL; and
  ix_index_values_economic_index_id_period, a unique index on (economic_index_id, period)).
  Neither constraint has any test at any layer -- not a domain test, not an integration test.
  This is the same class of gap the project's own config.yaml calls out from the previous
  change's verification. See CRITICAL findings below.
- All other 34 scenarios have direct or clearly-implied covering evidence (see the numbered-test
  matrix above, plus EconomicIndexTests/IndexValueTests for the remaining economic-index
  scenarios, DueAdjustmentQueryTests/SchemaConstraintTests for the worklist and clause scenarios,
  and ContractShareSurvivesAdjustmentTests for the lease-contract delta).

## Design Claims Checked Against Actual Code (not against the comment restating them)

| Design claim | Verified against | Result |
|---|---|---|
| Single truncation site is RentAdjustment.Confirm (Decision 4) | Read RentAdjustment.Confirm: decimal.Truncate(proposal.UntruncatedNewCanon) is the only Truncate call in the pipeline; Contract.ConfirmAdjustment feeds the already-truncated NewCanon into unmodified ChangeMonthlyRent | Confirmed. tasks.md task 2.11 disagreed with design.md on this point; the implementation correctly followed design.md as instructed and the recorded deviation is accurate |
| Coefficient averages percentage variations, never raw levels | Read AdjustmentMath.Compute: variation = (endLevel/baseLevel - 1) * 100m, rounded once, then averaged | Confirmed. The spec's worked example (IPC 18%, RIPTE 12% -> 15%, not the ~12.04% a level-average would give) is asserted literally in AdjustmentMathTests |
| IndexResolver.Resolve stays a pure domain function despite gaining a catalog parameter (deviation 1) | Read IndexResolver.Resolve: only ArgumentNullException guards and an in-memory IReadOnlyDictionary walk, no I/O, no service dependency | Confirmed pure. The 3-arg shape is required to walk a multi-hop chain at all; 2-arg pseudocode in design/tasks could not have worked |
| AdjustmentClause.Combination always correctly re-derived, never stale (deviation 2) | Read AdjustmentClause.Combination: derived from index count, no setter, no backing field; builder.Ignore(c => c.Combination) in AdjustmentClauseConfiguration | Confirmed. Cannot go stale because there is nowhere to store a stale copy. The missing physical combination column/CHECK is a documentation gap in design.md's table, not a functional one |
| Six tables, not five (deviation 3) | Read the generated migration: exactly 6 CREATE TABLE, 0 ALTER TABLE | Confirmed correct call. design.md's own detailed Decision 3 table lists six; only its summary prose says five. The implementation followed the authoritative detailed table |
| Index levels numeric(18,6), money numeric(14,2), RIPTE never truncated | Read all 6 EF configurations + migration column definitions; SchemaConstraintTests.NumericColumnPrecision_MatchesMoneyAndIndexLevelConventions reads information_schema.columns live | Confirmed at both the code and the running-database level |
| 20260913215911_InitialSchema byte-untouched | git log for that migration file shows only its original creation commit (6ea4d36); no later commit in this change touches it | Confirmed directly from git history, not merely by re-reading the file |
| No real ALTER TABLE in the new migration | Read the full migration file | Confirmed. Zero ALTER TABLE, not even in a comment |
| Four modelBuilder.Ignore calls removed | Read InmobiliariaDbContext.OnModelCreating | Confirmed removed -- none remain |
| CI build job id/name unchanged, core job exists | Read .github/workflows/ci.yml | Confirmed |
| No connection string or secret committed | Searched src/ for connection-string patterns; the one match, DesignTimeDbContextFactory.cs, is a pre-existing (not new to this change) localhost/placeholder fallback gated by an environment variable | Confirmed no secret |
| AttachAdjustmentClause's doc comment ("a clause is created once and never reassigned here") | Read the method body | NOT fully confirmed -- the method has no guard against being called twice; it unconditionally overwrites AdjustmentClause. See WARNING findings |

## Design Coherence

| Deviation (from apply-progress) | Assessment |
|---|---|
| 1. IndexResolver.Resolve gained a catalog parameter | Justified and necessary; stays pure. Recommend updating design.md Decision 7's pseudocode signature at archive |
| 2. AdjustmentClause.Combination not persisted | Justified; functionally correct by construction. Recommend design.md Decision 3's table note this as derived-only, not a stored column |
| 3. Six tables, not five | Implementation correctly followed the authoritative detailed table over the summary prose. Recommend correcting design.md's summary prose at archive, per config.yaml's own archive rule (amend the delta specs to record any rule that was implemented but never specified) |
| 4. Splice-refusal handled by interpretation | The domain rule (AdjustmentMath.Compute) is tested. The adapter's own translation of a splice refusal into a MissingIndexValue entry (DueAdjustmentQuery.ResolveIndices, the baseResolved.Index.Id != endResolved.Index.Id branch) and its defensive Guid.Empty fallback branch are not exercised by any test. This is a real gap, not merely a design-interpretation question -- see WARNING findings |
| 5. EF change-tracking hazard | Confirmed real by reading SchemaConstraintTests.CorrectingAnIndexValue_..., which requires an explicit context.RentAdjustments.Add(correction) to work at all. Correctly identified, fixed in the test, and documented. Genuinely out of scope for this change (no application/use-case layer exists yet) but is a landmine for the next change that adds one -- see WARNING findings |

## Findings

### CRITICAL

1. Spec scenario "One value per index per period" (economic-index / Published Value Stored Per
   Period) has no enforcing test at any layer. The requirement text is explicit: "The system MUST
   reject a second value for the same index and period." The scenario is a full
   Given/When/Then. The implementation has a real mechanism for it --
   IndexValueConfiguration.HasIndex on (EconomicIndexId, Period), IsUnique(), which becomes
   ix_index_values_economic_index_id_period in the migration -- but nothing in
   Inmobiliaria.Domain.Tests or Inmobiliaria.Infrastructure.Tests inserts two IndexValue rows
   for the same index and period and asserts a rejection. The project's own test file
   (SchemaConstraintTests) already has the exact pattern for proving a unique-index rejection
   elsewhere (DuplicateContractUnitCompositeKey_Rejected, DuplicateDni_RejectedByUniqueIndex,
   DuplicateCuil_RejectedByUniqueIndex) -- the equivalent test for index_values was simply never
   written. Per config.yaml's own rule, "a requirement with no enforcing test is a FINDING, even
   when the code is correct." This one is fixable in minutes using the existing test pattern, so it
   should be closed before archive rather than carried forward.

2. Spec requirement "Index Identity"'s explicit "MUST reject creating two active indices with
   the same name" has no enforcing test at any layer, for the same reason as above. The
   mechanism (ix_economic_indices_name_active, a partial unique index on name WHERE
   discontinued_from IS NULL) exists in EconomicIndexConfiguration and is exactly the kind of
   partial-index subtlety (an active "IPC" must block a second active "IPC", but must NOT block a
   new "IPC" succeeding a discontinued one) that most benefits from a real test proving it against
   Postgres rather than being trusted by inspection. This requirement's own written scenario in
   spec.md only covers the positive case (two different-named indices coexisting, which is
   trivially exercised everywhere), so there is no explicit Given/When/Then for the rejection
   clause to fail against -- which is why this is graded one notch below finding 1, but it is the
   same shape of gap and should be closed the same way.

Both above are the reason for the overall verdict below. Per config.yaml's archive rule, this
change must not be archived until both are closed (a test added and passing) and this report is
updated, or a documented, deliberate decision is recorded to accept them as scoped-out (matching
the treatment already given to tests 11/12) -- silence is not an acceptable resolution for either
path.

### WARNING

3. DueAdjustmentQuery.ResolveIndices's splice-refusal branch is untested. The specific lines
   that detect baseResolved.Index.Id != endResolved.Index.Id and turn it into a
   MissingIndexValue entry on the worklist -- and the defensive Guid.Empty-keyed fallback for a
   hypothetical future AdjustmentMath pending reason -- have zero test coverage. The underlying
   arithmetic rule is proven at the domain level
   (AdjustmentMathTests.Compute_BaseAndEndResolveToDifferentIndices_IsRefused), but the adapter's
   own wiring of that outcome into the reminder-ready worklist shape is not. This is not one of the
   18 numbered spec tests and was explicitly flagged as a risk by the implementer, which is the
   right way to carry an honest gap forward -- but it should not remain open indefinitely, since it
   is exactly the kind of "every lookup succeeds and it is still pending for a subtle reason" case
   that a reminder consumer could misinterpret.

4. Contract.AttachAdjustmentClause's XML doc claims a clause "is created once and never
   reassigned here," but the method body has no guard preventing a second call from silently
   overwriting AdjustmentClause with a different, validly-owned clause. In-memory this could
   orphan a previously attached clause before persistence; the database's 1:1 unique FK on
   adjustment_clauses.contract_id only protects the persisted state if both clause rows are ever
   actually saved. No test exercises calling this method twice. Low real-world risk today (nothing
   in this change calls it more than once), but the doc comment overstates what the code enforces --
   exactly the class of design/comment-vs-code mismatch this project's config.yaml explicitly
   asks verify to catch.

5. EF change-tracking hazard is real and documented, but remains a landmine for the next
   change. Confirmed by reading SchemaConstraintTests.CorrectingAnIndexValue_...: appending a
   second RentAdjustment to an already-tracked Contract is not auto-detected as Added by EF's
   DetectChanges, and the fix (an explicit context.RentAdjustments.Add(...)) lives only in a
   test's comment, not in any reusable guidance for the future application/use-case layer that will
   actually call ConfirmAdjustment in production. Recommend promoting this from a code comment to
   a named open question in design.md (alongside the other "touched, not resolved" items) before
   the confirmation UI change begins.

6. Tests 11 and 12 remain only partially provable (carried forward, not a new finding -- see the
   Spec Compliance Matrix above). Explicitly disclosed in three places
   (tasks.md, design.md, apply-progress). The current-account change must re-assert both for
   real once billing exists.

7. Documentation inconsistencies in design.md should be corrected at archive time, per
   config.yaml's own archive rule: the summary prose says "five new EF configurations" / "five
   CREATE TABLEs" where the detailed Decision 3 table (and the implementation) has six; and
   Decision 3's table lists a physical combination column that was not actually created (deviation
   2, justified but undocumented in the table itself).

### SUGGESTION

8. On-disk apply-progress.md is stale relative to the Engram-stored copy (see Completeness
   section) -- sync the two before archive.
9. No test directly exercises AttachAdjustmentClause's guard clause (rejecting a clause whose
   ContractId does not match) or asserts the Contract.AdjustmentClause navigation's value
   directly; coverage is only indirect, through tests that go on to use the attached clause
   successfully. Low priority given the indirect coverage is genuinely exercised on every relevant
   test path.

## Verdict

FAIL -- 2 CRITICAL findings.

The implementation quality is high: build is clean (0 warnings/errors), all 71 tests pass with 0
skips against a real Postgres 17.6 via Testcontainers, every recorded deviation from design.md
was independently verified in the actual code and found to be a justified, correctly-implemented
choice (not a corner cut), and the previously-known partial gaps (tests 11/12, the splice
interpretation, the EF change-tracking hazard) were all honestly disclosed rather than hidden.

However, two spec-mandated database constraints -- one with an explicit, unproven Given/When/Then
scenario -- have no test anywhere in the suite, despite the exact test pattern needed already
existing in the same file for other tables. Per config.yaml's explicit rule ("a requirement with
no enforcing test is a FINDING, even when the code is correct") and this project's own history
("the contract-and-parties verification found two real defects that a green build had hidden"),
this is not a pass. Do not archive until CRITICAL findings 1 and 2 are closed (recommended: two
short SchemaConstraintTests additions using the existing duplicate-key test pattern) and this
report is re-run, or until a team decision to explicitly scope them out is recorded with the same
transparency already given to tests 11/12.
