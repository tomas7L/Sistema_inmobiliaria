# Sistema_inmobiliaria

Rental-management desktop system for **Del Lago Servicios Inmobiliarios** (Sunchales, Santa Fe).
Owner: Nicolás Demaría. Built by Volkode.

C#/.NET 10, WPF + MVVM (CommunityToolkit.Mvvm), EF Core + Npgsql straight onto Supabase Postgres
with no API layer, xUnit, Testcontainers pinned to `postgres:17.6`.

<!-- protocolo-continuidad: project-owned section, not managed by gentle-ai -->

## Working Agreements

These are the agreements between Tomás and the assistant. They survive nothing but this file — a
`/clear` erases every one of them from context, which is why they are written down rather than
remembered.

### The rhythm, and who touches git

The assistant writes files and leaves them **uncommitted**. Tomás reads, objects or approves, the
assistant corrects — and then **Tomás commits, pushes and opens the PR**. Mateo reviews, Tomás
merges, Tomás says so.

**The assistant never commits, never pushes and never opens a PR.** It hands over the file list and
a commit message, and stops.

### What Tomás reads

**He does not read code or database work.** He reads specs, designs, proposals and task files, and
asks questions about those. For a code slice, the assistant verifies it itself and hands over the
commit — there is nothing to wait for.

For every document, say **exactly what to read and what to skip**.

### Two inspections, looking for different things

1. **On handover** — does it do what the spec says? Tests, build, scope, nothing extra.
2. **At the start of the next session, before continuing** — did it land intact, and does what is
   about to be built still match what is actually there?

The second one is where the expensive mistakes live. It has already caught a field asserted across
three documents that did not exist, and ports placed in the wrong project.

### Preconditions, checked and announced BEFORE starting

- **Docker** must be running for anything that touches `Inmobiliaria.Infrastructure.Tests`. A
  skipped Testcontainers test is an **unrun** test, not a passing one. This cost a whole PR's CI
  once.
- **Supabase pauses on the free plan** after about a week. Anything touching the live database
  needs it restored first, and only Tomás connects to it — the assistant never handles the
  password.

Say which are needed before beginning a phase, not when something fails.

### Deciding things

A question that can be **researched** — law, standard practice, anything technical — is decided
here, with evidence. Only *"how does this particular agency work"* goes to Nicolás, because he
cannot be asked about everything.

State **how strongly a recommendation is held**. Tomás must never act on a weak one thinking it is
firm.

Ask **one question at a time**, then stop and wait.

### Specifications

One **single file** per change, with a Decisions table at the top, a table of contents, and
numbered Tests at the bottom. Never a `specs/` folder.

### Commits

Conventional Commits. **No `Co-Authored-By` and no AI attribution**, ever.

## Session Continuity

This project is worked in short sessions separated by `/clear`. Context lives in three places, and
they do not overlap:

| Where | What it holds |
|---|---|
| `docs/ESTADO.md` | **Only the present**: what is active, the next action, preconditions, what is waiting on Tomás. Overwritten each close, never appended to. |
| `openspec/changes/<change>/tasks.md` | The authoritative state of implementation — checkboxes, deviations recorded beside their task. |
| Engram | Permanent memory: decisions, conventions, bugs, lessons. Per the gentle-ai protocol already in effect. |

**`ESTADO.md` points, it does not summarise.** It must not copy task state, decisions or history:
those have homes above, and a second document claiming to be the present is how a stale file starts
reading as authority. This project has already been burned by one claim repeated across three
documents.

**If `ESTADO.md` and `tasks.md` disagree, `tasks.md` wins.** The SDD phases read and write it;
`ESTADO.md` is a convenience.

### Starting a session (`/retomar`)

1. Read `docs/ESTADO.md`.
2. Ask Engram for this project's recent context, per its own protocol.
3. Run the **second inspection** from Working Agreements above: confirm the last merge landed and
   that what is about to be built still matches the code.
4. Report in 3–5 lines: where we are, what is next, what is blocked, and **which preconditions the
   next step needs**.

Do not re-read the codebase. Open only what the next task requires.

### Closing a session (`/cierre`, or "cerramos", "voy a hacer clear")

1. Rewrite `docs/ESTADO.md` from the template in it. Concrete: real file paths, real commands.
2. Do the Engram session close its own protocol specifies, saving anything not yet saved.
3. Confirm in two lines what was saved, and say `/clear` is safe.

### Verification habits that were learned the hard way

- **Never grep build output for a pattern.** `error CS` misses `error xUnit2029`. Read the
  **error/warning count** or the **exit code**. And this machine's dotnet output is in **Spanish**
  — "Advertencia(s)", "Errores", "Superado", "Omitido" — so English-only greps match nothing and
  look like success.
- A model change and its migration are **atomic** and ship in **one** PR. EF refuses to migrate
  when the model has changes no migration covers, and every Testcontainers test dies at the
  fixture.
- `dotnet ef migrations has-pending-model-changes` is **stronger** than the model validator: the
  validator proves the model builds, this proves it still matches the last migration. It needs no
  database and no Docker.
- `dotnet build` compiles code but **never builds the EF model**.
- `Inmobiliaria.Desktop` is **excluded** from `Inmobiliaria.Core.slnf`, so a test placed there
  cannot gate the Linux CI job. Tests go in `Inmobiliaria.Domain.Tests` or
  `Inmobiliaria.Infrastructure.Tests`.
- **Prove a guard by making it fail.** A guard never seen failing proves nothing.
- `sd` and `fd` are not available in the Bash tool here; `rg` and `bat` are. Use python for
  in-place source edits — a bash heredoc breaks on C# apostrophes.

<!-- /protocolo-continuidad -->
