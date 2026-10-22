# Exploration: ui-de-cobranza (2026-10-16)

> Source of truth mirrored in Engram `sdd/ui-de-cobranza/explore` (obs #125).

## Current state (evidence)

- Desktop is a skeleton: `App.xaml.cs` (composition root; only file allowed to touch Npgsql/EF, :1-15), LoginWindow/ViewModel, ChangePasswordWindow/ViewModel, MainWindow = one centered TextBlock (`MainWindow.xaml:9-11`). No navigation, no other screens. MainWindow is built with `new MainWindow()` (`App.xaml.cs:104`): it receives NO session, NO factory, NO role.
- Post-login objects exist only as `AuthenticationResult.Success(IUserSession Session, ISessionDbContextFactory Factory)` (`AuthenticationResult.cs:18`). Role: `IUserSession.Role` (UserRole Admin/Empleado) derived once at login (`IUserSession.cs:23`). `ISessionDbContextFactory.Create()` -> InmobiliariaDbContext (`ISessionDbContextFactory.cs:12`). Pre-login container is deliberately empty (`ApplicationServices.cs`); there is NO session-scoped container yet.
- `CollectionDesk` ctor = (InmobiliariaDbContext, IAgencyClock, RecargoTerms) (`CollectionDesk.cs:44`). AgencyClock exists (`Infrastructure/Persistence/AgencyClock.cs`). RecargoTerms has no default by design (`RecargoTerms.cs:14`): Desktop must supply 0.02 / 0 grace (documented as the agency's leases, :19-29) — from appsettings, not a constant.
- `ICollectionDesk` (`Domain/Collection/ICollectionDesk.cs:44-62`): `PreviewAsync(CollectionRequest)`, `CommitAsync(preview, request)`, `PreviewVoidAsync(Guid reciboId, string reason, UserRole)`, `VoidAsync(VoidingPreview)`.
- Results: `CollectionPreview` (`CollectionPlanner.cs:103`) with Period, ReceivedOn, ResolvedToday, PeriodWasEarly, PreviousAmountOwed, LedgerFigure, ResultingAmountOwed, Locatario/Propietario ReciboDraft, Warnings [ZeroAdministrationRate], Refusal, CanCommit. `CollectionResult` (Succeeded, Refusal, CollectionId, LocatarioNumber, PropietarioNumber). `CollectionRefusal` has 13 members, all with Spanish `OperatorText()` (`CollectionPlanner.cs:31,57`). `VoidingRefusal`: ReasonRequired, AlreadyAnnulled, AdminOnly, WindowClosed, PreviewStale, with OperatorText (`VoidingPlanner.cs:7,25`). `VoidingPreview`(Period, Annulments, Corrections, ResultingAmountOwed, Refusal, ActingRole). Errors that are NOT refusals surface as exceptions (unmapped PostgresException, "No account exists", missing tenant/lessor/unit on header, `CollectionDesk.cs:305,430`): the VM needs one generic error path.
- UI-relevant behaviours: HandedAmount must equal LedgerFigure exactly (`CollectionPlanner.cs:272`, spec Decision 27) — LedgerFigure = rent+instalment+recargo, NOT the recibo total, which also carries typed tasa/seguro/otros (CollectionRequest :14-21; preview doc :102). Preview with HandedAmount=0 returns AmountNotTotal but still carries figures+drafts: usable as the "what is owed" step. Commit re-previews and compares (design D21); any difference = PreviewStale/ConcurrentChange -> UI must re-preview and re-show. ReceivedOn bounds 0..7 days back (`CollectionRules.cs:14`). Void windows: <=7d both roles, 8-30d Admin only, >30d nobody (`CollectionRules.cs:20,26`); UI should pre-gate by role but DB enforces.
- NOT provided by the port (gaps):
  1. Nothing lists/searches accounts or contracts with tenant/unit names to pick from; there are no party/unit/contract screens and no query beyond MoraWorklistQuery/DueAdjustmentQuery.
  2. `CommitAsync` returns only CollectionId + numbers; the ReciboHeader (tenant, lessors, units) lives in a private `Loaded` record (`CollectionDesk.cs:294`) — to print, the UI needs a read port returning the issued Recibo pair (Recibo has Number, Copy, Period, ReceivedOn, Header, Lines, Total, Legend; `Recibo.cs`).
  3. `PreviewVoidAsync` takes the DB Guid recibo id, not the printed number users know; need lookup by number / recent collections list with annulled flag.
  4. No ContractAccount creator exists in `src` (`new ContractAccount(` only in VoidingPlanner replay; INSERT is granted on contract_accounts, `AddContractAccounts.cs:137`): how contracts/accounts reach the live DB before go-live is outside the code today.
- Go-live: `Contract.SetCutoverDate(DateOnly, ContractAccount)` exists (`Contract.cs:199-210`), refuses only when the account has any movement; no bound/validation on the date value; no way to clear; EF writes via named backing field (ContractConfiguration). DB trigger `contracts_cutover_lock` repeats the one-way rule (`AddFeePlanCutoverAndLedgerConstraints.cs:153-173`). No application service loads contract+account and saves it. recibo_numbering: `IReciboNumbering.SetStartingNumberAsync(int)` exists (`ReciboNumbering.cs:24`), >=1, trigger refuses once a recibo exists, column-level GRANT UPDATE(starting_number) to Admin only (`AddCollectionsAndRecibos.cs:408`). But contracts has table-level GRANT INSERT, UPDATE to BOTH roles (`AddUsersAndRoles.cs:161`): an Empleado could change cutover_date via pgAdmin before the first movement — "Admin-only" for cutover would be UI-only unless a small hand-written migration hardens the existing trigger (pg_has_role check). No model change, so no EF snapshot coupling. There is no read of "current starting number / does any recibo exist" for the screen.
- Printing: receipt spec states rendering is the separate `recibo-pdf` change (`receipt/spec.md:7-8`); collection spec says it presents no screen (`collection/spec.md:8`). pdf-library is still an open decision in `openspec/config.yaml`. `Recibo.NonFiscalLegend` = "Documento no válido como factura" must print (accountant confirmation pending, Decision 10).
- Testability: `Core.slnf` = Domain, Infrastructure, Domain.Tests, Infrastructure.Tests only; Desktop excluded; CI `build` job (windows) compiles everything incl. XAML, `core` job (ubuntu) tests the slnf. Precedent: LoginViewModel has no tests; the testable decision logic was pushed into Infrastructure (LoginBootstrap, `ApplicationServices.AddPreLoginServices`) and proved in Infrastructure.Tests (CompositionGuardTests). `Directory.Packages.props` = central package versions. Desktop already references CommunityToolkit.Mvvm; that package is platform-neutral.
- Archive follow-ups relevant to UI: ConcurrentChange e2e proved only by the mapping table test (UI must still handle it as "re-preview"); duplicated materialisation loop (BuildPreview vs AccountMaterialiser) is internal — UI does not depend on it, leave out; H.7 (first instalment rule) closed 2026-10-16 with the default; H.6 = this change's go-live screen.

## Approaches (testability / structure)

| | Approach | Pros | Cons | Effort |
|---|---|---|---|---|
| A | VMs in Desktop only (LoginViewModel precedent) | No new project | All collection/void flow logic (stale re-preview, refusal mapping, role gating) untestable on Linux CI; only the windows `build` job compiles it | Low; risk High for money-moving code |
| B | New non-WPF project `Inmobiliaria.Presentation` (net10.0; CommunityToolkit.Mvvm; references Domain only, ports faked in tests) + `Inmobiliaria.Presentation.Tests`, both in sln and `Core.slnf`; Desktop keeps only XAML + composition | Real VM tests on ubuntu, no Docker; no Infrastructure/EF in VMs | Two csprojs + sln/slnf/Directory.Packages plumbing; config rule requires the proposal to scope new csprojs explicitly | Medium (~100 lines plumbing) |
| C | Pure flow types in Infrastructure/Domain (LoginBootstrap style), thin VMs in Desktop | No new project | UI-shaped state machines in the wrong layer; VM glue (busy flags, commands) still untested | Medium |

**Recommendation: B, held moderately (~65%).** C is the credible fallback if no new projects are wanted.

## Other structural recommendations

- Session-scoped composition: add `AddSessionServices(factory, session)` extension in Infrastructure (like AddPreLoginServices) so the wiring is provable in Infrastructure.Tests; App.xaml.cs just calls it and builds MainWindow with a shell VM. One DbContext per operation (factory.Create() per preview/commit flow) since DbContext is not thread-safe; VM commands disabled while busy. **Held firmly.**
- New ports in Domain (implemented in Infrastructure/Persistence, Testcontainers-tested): account directory (pick/search, with readiness flags: cutover set? honorarios rate entered? frozen?), recibo reader (by collection id, by number; recent collections for voiding with annulled flag), go-live setup (list contracts + cutover state/lock; SetCutoverDate via domain method; starting-number state incl. "any recibo exists"). **Held firmly**: without them neither the picker, print nor void can exist.
- Print: on-screen recibo view + system print through WPF FlowDocument/PrintDialog (no library, so the open pdf-library decision is not forced); PDF stays in `recibo-pdf`. **Held weakly-moderately (~55%).**
- Cash desk UX: pick account -> optional period/received-date/typed lines -> auto-preview -> show previous owed, ledger figure, both recibo drafts, warnings -> operator types handed amount (prefill disabled; show recibo totals AND ledger figure) -> confirm summary (confirm-with-a-summary convention, config.yaml) -> commit -> show numbers + print. Any Stale/Concurrent refusal -> auto re-preview and show diff, never silent retry.

## Scope

- **IN:** shell+navigation+role exposure, session composition, account picker, cash desk (preview/commit), recibo display+print, void flow (find recibo, reason, preview, confirm), Admin go-live screen (cutover per contract, starting recibo number), the new read/setup ports above, VM tests.
- **OUT:** contract/party/unit CRUD screens, account creation/import, opening-balance entry, PDF generation/recibo-pdf, owner payouts, notifications, rounding option, BuildPreview/materialiser dedupe, other archive follow-ups, Admin user management screens.

## Questions that need an answer

1. ~~Tomás: is printing in this change (minimal WPF print) or deferred to recibo-pdf? Recommend in.~~ **Answered by Tomás, 2026-10-16: in** (WPF print, no PDF library).
2. ~~Tomás: how do contracts and ContractAccounts get into the live DB before go-live (no screen or code creates them)? Blocks go-live day, not this change's code.~~ **Answered by Tomás, 2026-10-16: out of this change**; a separate initial-data-load change follows it.
3. ~~Tomás: harden cutover_date to Admin at the DB (small migration extending the trigger) or UI-gate only? Recommend DB hardening, held weakly (~55%).~~ **Answered by Tomás, 2026-10-16: harden at the DB.**
4. Labelling at the desk when tasa/seguro/otros are typed: the ledger figure (spec Decision 27) vs. the locatario recibo total. To be settled in spec/design, not an open owner question unless the design finds a conflict.

## Risks

- Moving money from a UI with VMs untested if approach A.
- Data readiness: honorarios rate (RateNeverEntered), cutover (CutoverNotSet), starting number must exist or every commit refuses; screen must surface readiness up front.
- Supabase needs to be awake for the final live smoke (only Tomás connects).
- Docker required for new Infrastructure port tests.
- SetCutoverDate has no date-value validation; the "domain validation" promise needs a small domain rule (design must define).
- Voiding by number needs a new lookup.
- Size: est 1,900–2,300 authored lines, far above the 400 guard (config review_budget_lines 800): chain.

## Size forecast (authored lines incl. tests) and slicing

| Slice | Content | Lines |
|---|---|---|
| S1 | Shell + session composition + Presentation project plumbing | ~450 |
| S2 | Go-live: ports, domain rule, screen, tests | ~500 |
| S3 | Cash desk: account directory, VM, view, tests | ~650 |
| S4 | Recibo reader + print + void flow | ~550 |

Total ~2,150. Chained PRs recommended (feature-branch-chain or stacked-to-main — Tomás picks).

## Ready for proposal

Yes, after answers to Q1–Q3. Preconditions for later phases: Docker for infra tests; Supabase only for final live smoke.
