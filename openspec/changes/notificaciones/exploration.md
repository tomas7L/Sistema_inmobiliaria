# Exploration: notificaciones (telling tenants what they owe)

Done 2026-11-11, read-only. Code facts verified by reading the repository; platform facts verified against Supabase's own
documentation, cited by URL. Anything not verified is marked **[inferred]**. Nothing here is decided for the agency: what
the agency wants is Nicolás's, and is listed under **Questions for Nicolás**.

## What to read (Tomás)

| Read | Why |
|---|---|
| **Finding** | The one fact that changes the plan |
| **The three ways to send**, and the recommendation under it | The technical decision this exploration was asked to research, with how strongly it is held |
| **Questions for Nicolás** | What must be answered before a proposal can be written |

**Skip** "What exists" unless you want the detail.

## Finding

**Nothing the agency has said asks for automatic notices.** `openspec/domain/respuestas-del-dueno.md` has no answer about
them. The only trace is `openspec/config.yaml`, open decision `scheduler-mechanism`: "Automated notifications (60/30/15
days before due) must fire while the desktop app is closed", written before any owner meeting, without saying 60 days
before *what* (a rent due date is monthly, so 60 days before it is two rents earlier: the figure reads like a contract's
end, not a rent's due date) **[inferred]**. Tomás asked about email on 2026-11-04, because it is how the agency tells
tenants what they owe today (`gestion-de-alquileres/proposal.md:75`).

So "must fire while the application is closed" is an assumption, not a requirement. It matters, because it is the one
assumption that makes this change expensive and, on Supabase, risky (next section).

## What exists

| Piece | Where | Ready for notices? |
|---|---|---|
| Who owes, how much, how late | `IMoraWorklistQuery` and `MoraAccount` (mora change, PRs #110 and #111): tenant, units, short id, status, days overdue, amount owed, recargo, frozen date | **Yes**, built "reminder-ready" (`contract-account/spec.md:330`) |
| The tenant's email | `Party.Email`, optional (`Domain/Parties/Party.cs:15`), set by `UpdateContactInfo` | **Partly**: optional, so a tenant may have none; whether the ones stored are read is N9 |
| What is about to fall due | `AccrualSchedule.DueDateFor`, `AccrualSchedule.PeriodsDue` | Yes, for a "rent falls due in N days" notice |
| A contract about to end | `Contract.NominalEndDate` (`Domain/Leasing/Contract.cs:27`) | Yes, for a "your contract ends in N days" notice |
| Sending email | Nothing | No code, no provider, no credential |
| Running with the application closed | Nothing | Open decision `scheduler-mechanism` |
| Reliable debt for pre-existing contracts | Not yet | Waits on `carga-inicial`: until opening balances are loaded, an old contract's debt reads wrong, so no debt notice can be switched on before it |

## The three ways to send

### A. Supabase: `pg_cron` calls an Edge Function that sends the mail

How it works: a `pg_cron` job, through `pg_net`, calls an Edge Function on a schedule; the function reads who to notify and
calls an email provider ([Scheduling Edge Functions](https://supabase.com/docs/guides/functions/schedule-functions)).

Against it, all verified:

1. **The secret it needs is readable by every operator.** Supabase's own warning: "Any role that can log in to your
   database can read the secret key while the request waits", because `pg_net` writes it decrypted to
   `net.http_request_queue` ([same page](https://supabase.com/docs/guides/functions/schedule-functions)). In this system
   **every operator is a database login** (`user-registry` spec, users-and-roles). An Empleado could read a key that
   bypasses every GRANT the access-control spec built. That is a security regression, not a detail.
2. **The function cannot use the agency's mailbox.** Outbound ports 25 and 587 are blocked for Edge Functions
   ([Edge Functions limits](https://supabase.com/docs/guides/functions/limits)), so it needs a separate HTTPS email
   service (Resend, SendGrid, Mailgun, SES). Mail would come from that service's domain setup, not from the address
   tenants already know, unless the agency's domain is configured there.
3. **A paused project sends nothing.** Free projects with low activity over 7 days are paused
   ([Project Pausing](https://supabase.com/docs/guides/platform/free-project-pausing)). A paused database runs no job
   **[inferred]**: notices would stop silently, for example over the January holidays. Paid projects are never paused.
4. Code in a second language and runtime (TypeScript on Deno), deployed separately from the desktop app.

### B. A console program run by Windows Task Scheduler on the agency's PC

How it works: a small .NET program, sharing this repository's domain code, run daily by Windows.

Against it:

1. **It needs a database credential stored on that PC**, which `user-registry` forbids outright: "The system MUST NOT
   persist any database credential in a configuration file, an environment variable ... or any other on-disk location"
   (`openspec/specs/user-registry/spec.md:9-11`). Doing it means amending that requirement and creating a dedicated
   database role for the job.
2. It runs only while that PC is on **[inferred]**: a PC switched off overnight or over a weekend skips that day.
3. In favour: it can use the agency's own mailbox over SMTP, and it is C#, tested like the rest.

### C. The application sends them, when an operator asks

How it works: no scheduler. The Mora screen (or one beside it) lists who would be notified today and why; the operator
reviews it and presses "Enviar avisos". The application, already logged in as that operator, sends through the agency's
own mailbox.

1. No new credential in the database and no database credential on disk. The mailbox password is the one new secret; where
   it lives (typed per session, or stored for the agency) is a proposal decision.
2. Mail comes from the address tenants already know.
3. A person sees every notice before it goes: nothing goes out on a wrong figure, which matters until `carga-inicial`
   has loaded old balances.
4. Against it: **it does not fire with the application closed.** If nobody opens the application, nobody is notified. A
   notice "3 days before" sent on the next working day is late.
5. All C#, tested with the existing Testcontainers suite; the email sending itself is behind a port, faked in tests.

### Recommendation

**C first, held medium.** It is the only option that adds no security exposure, needs no amendment of an existing
requirement, and sends from the agency's own address. Its one cost, firing only when someone opens the application, is
small for an office that opens it every working day, and is exactly the assumption Nicolás has not confirmed. If he needs
notices on days nobody works, B is the fallback (with the `user-registry` amendment and a dedicated role); **A is not
recommended while operators are database logins**, held strong, because of the readable secret key.

`scheduler-mechanism` is not resolved by this exploration: under C it is not needed. If Nicolás confirms C, the open
decision closes as "no scheduler: notices are sent from the application by an operator".

## Questions for Nicolás

These decide the proposal. None can be researched.

| # | Question | Why it matters |
|---|---|---|
| N5 | **Do you want the system to send notices to tenants at all?** Today they are sent by email by hand | Whether this change exists |
| N6 | **Which notices?** Rent about to fall due; rent overdue; contract about to end; adjustment applied | Each is a different trigger and text |
| N7 | **When?** How many days before or after; and is it enough that they go out when the office opens the system, or must they go out on days nobody works? | Decides C versus B |
| N8 | **From which email address**, and which provider is it (Gmail, Outlook, the domain's hosting)? | The sending setup and its password |
| N9 | **Does every tenant have an email you trust?** And do co-debtors or guarantors get notices too? | Who receives them |
| N10 | **Should someone see each notice before it goes**, or send them without review? | C's review step, or a fully automatic send |
| N11 | **Would it work for you if the notices went out on their own, without anyone pressing a button?** And on days nobody opens the system? | Whether automatic sending is added on top of C (`proposal.md` §10) |

Numbered after N1 to N4 of the `gestion-de-alquileres` spec, so one list can go to the meeting.

### What Tomás recalls (2026-11-12), to confirm with Nicolás

None of this is Nicolás's answer yet. It is what Tomás remembers from earlier conversations, or his own view.

| # | Tomás | Status |
|---|---|---|
| N5 | "Si, eso le habiamos dicho que se podia agregar para que ellos no tengan que hacerlo a mano." Nicolás also said it "would be good" if notices sent themselves | Recalled; confirm whether *automatic* is a wish or a need |
| N6 | Today they send one when a tenant asks how much they owe that month. Other notices "no estarian de mas", but pressing a button per email "se hace muy extenso. Son muchos avisos por inquilino" | Open. Note: C sends the whole day's list with **one** press, not one per email |
| N7 | Tomás's own view, not discussed with Nicolás: on day 1, or on the due date, saying the late charge (the 2%) starts from then | Open |
| N8 | Gmail; Nicolás showed it | Recalled. Gmail allows 500 messages a day on a personal account ([Gmail sending limits](https://support.google.com/mail/answer/22839)); sending from an application needs an app password, which needs 2-Step Verification on that account **[to verify in the proposal]** |

**Tomás's decision (2026-11-12): option C is built whatever Nicolás answers.** Whether automatic sending is added on
top, and how, waits on N7 and on whether the agency moves to Supabase Pro.

### Mixing C with automatic sending

Two different problems, with different costs:

1. **Staff too busy to press the button.** Solved without any scheduler: the application, when open on or after the
   sending day, sends whatever nobody sent by hand. Needs a sent-notices record (with a uniqueness rule so two PCs never
   send the same notice), the mailbox password stored for the agency, and must stay off until `carga-inicial` has loaded
   opening balances, because nobody reviews what it sends.
2. **Nobody opens the application that day** (weekends, holidays). Needs something running in the cloud:
   - GitHub Actions running option B's program: **not recommended, held strong.** "In a public repository, scheduled
     workflows are automatically disabled when no repository activity has occurred in 60 days"; runs "can be delayed"
     and "some queued jobs may be dropped" under load; and a public repository's logs are public
     ([GitHub docs](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows)).
   - An Edge Function triggered by an outside scheduler instead of `pg_cron`: avoids A's readable key **[to verify]**,
     but rewrites the debt calculation in TypeScript, a second copy of the money logic, and still needs an HTTPS email
     service. Supabase Pro removes pausing only, not these.

## Not in this change, whatever Nicolás answers

- Debt notices on contracts loaded before go-live, until `carga-inicial` has loaded their opening balances.
- WhatsApp or SMS: nobody has asked.
- Notices to owners (`liquidacion-al-dueno`).
