# Exploration: limite-de-intentos-de-login (failed logins lock the whole office out)

Written 2026-11-13. Evidence comes from Supabase's pooler source, this repository, and a test run on the PRUEBA database
the same day. Nothing here was assumed from documentation alone.

**What to read (Tomás):** **Finding** and **What the test proved**. Skip the rest unless you want the detail.

---

## Finding

Supabase's shared pooler (Supavisor) has two circuit breakers that refuse **every new connection**, not only the
failing user's. The application shows both as "No se pudo conectar con el sistema. Revise la conexión a internet e
intente de nuevo…", which reads as a network problem and invites the retries that keep the breaker closed.

| Breaker | Fires on | Threshold | Blocks | For |
|---|---|---|---|---|
| `auth_error` | A wrong password | 10 failures in 150 s | The project, from that IP address | 120 s |
| `get_secrets` | A user the pooler cannot look up (unknown or deactivated) | 5 failures in 300 s | The project, from every IP address | 30 s |

Source, `supabase/supavisor` on `main`, read 2026-11-13:

- `lib/supavisor/circuit_breaker.ex:20-23` (`get_secrets`) and `:36-40` (`auth_error`): the thresholds above.
- `lib/supavisor/client_handler.ex:364` and `:960`: `auth_error` is checked and recorded under `{data.tenant, data.peer_ip}`.
- `lib/supavisor/client_authentication.ex:237` and `:246`: `get_secrets` is checked and recorded under `tenant.external_id`
  alone.
- Supabase's troubleshooting page "Supavisor error: 'Circuit breaker open' after password rotation" says the same of
  `auth_error`: the origin IP is blocked for up to 2 minutes.

**Why it matters at the agency.** The office's PCs reach the internet through one router, so they share one public IP
address. One employee who mistypes about ten times in two and a half minutes locks every PC in the office out for two
minutes. An employee who is deactivated and keeps trying locks out everyone, from anywhere, for thirty seconds.

Sessions already open keep working on the connections they hold. A session that needs a new physical connection during
the block fails too.

## What the test proved

On PRUEBA (`chygrzoqmcqpncogeiuz`), from Tomás's PC, recorded in `%LOCALAPPDATA%\SistemaInmobiliaria\logs\errores-2026-11-13.log`:

1. Wrong passwords for the active Empleado `muñoz20` until the message changed; then `tomas` with the correct password
   was refused as well. Log 19:33:01 to 19:33:33: `XX000: (ECIRCUITBREAKER) too many authentication failures, new
   connections are temporarily blocked`. **The block is not per user.**
2. Wrong passwords for the deactivated `muñoz`: log 19:24:33, `(ECIRCUITBREAKER) failed to retrieve database credentials
   after multiple attempts`. A deactivated user trips the other breaker.

The number of attempts was not counted exactly (Tomás: "5 o 6"). Failures from an earlier round, less than 150 s
before, most likely counted too: the application opens exactly one connection per attempt
(`NpgsqlAuthenticator.cs:66`, "The one and only physical connection this method ever opens (spec test 2)"), so each
attempt is one failure.

## What the code does today

- `NpgsqlAuthenticator.AuthenticateAsync` (`src/Inmobiliaria.Infrastructure/Access/NpgsqlAuthenticator.cs`):
  - wrong password, unknown user and deactivated user all return `Rejected` (`IsLoginRefusal`, lines 29-33);
  - any other `NpgsqlException`, the breaker included (`SqlState XX000`), returns `Unavailable`.
- `LoginViewModel.LoginAsync` (`src/Inmobiliaria.Desktop/LoginViewModel.cs:58-96`): `Rejected` shows "Usuario o contraseña
  incorrectos."; `Unavailable` logs the cause and shows the connection message. Nothing counts failures, and the button
  is enabled again at once.
- The password change does not open a new connection (`PostgresPasswordService.cs:39-44`), so it never feeds either
  breaker.
- `user-registry` spec, Requirement "Login Is the Connection Attempt", scenario "A login with no verdict keeps the window
  open": today the breaker falls under it, and it requires the check-your-connection message.

## Options

| Option | What | Verdict |
|---|---|---|
| A | **The application slows itself down**: after a few failed logins on one PC, the button waits | Recommended. The only lever this system holds |
| B | **The application names the block**: when the pooler answers `ECIRCUITBREAKER`, say so and say to wait, instead of the connection message | Recommended, together with A. Stops the retries that extend the block |
| C | Connect without the shared pooler (direct connection or Supabase's dedicated pooler) | Rejected. The direct connection is IPv6 only unless the paid IPv4 add-on is bought, and the dedicated pooler exists only on the Pro plan and above (Supabase, "Connect to your database"); both ban the IP after repeated failures too (Supabase's troubleshooting page above) |
| D | Ask Supabase to change the thresholds | Rejected. They are fixed in the shared pooler's code for every project |

## Open points for the proposal

- How many failures per PC, and how long to wait, so that several PCs together stay under 10 in 150 s.
- Whether the count must survive closing and reopening the application.
- Whether the `get_secrets` breaker (5 in 300 s, every IP) can be avoided at all, given that the application cannot tell
  the operator that a username does not exist.
- How a session already open reports the block when it needs a new connection.
