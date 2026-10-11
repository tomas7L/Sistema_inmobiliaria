# Estado del proyecto

_Última actualización: 2026-10-10, cierre de sesión_

> Este archivo **apunta**, no resume. El estado real de una implementación vive en
> `openspec/changes/<change>/tasks.md`, y el porqué de cada decisión vive en Engram.
> Si este archivo y `tasks.md` se contradicen, **gana `tasks.md`**.

## En qué estamos

**Entre changes. No hay ninguno en curso** — `openspec/changes/` sólo contiene `archive/`.

`cuenta-corriente` quedó cerrado y archivado, y su especificación ya es documentación viva en
`openspec/specs/contract-account/spec.md`. Lo último que se hizo después fue un arreglo suelto, ya
mergeado: la convención `ApplyApplicationSuppliedKeys` en
`src/Inmobiliaria.Infrastructure/Persistence/InmobiliariaDbContext.cs`.

Nueve capacidades vivas, 72 requisitos. **241 tests, 0 fallas, 0 omitidos.** Cuatro migraciones
aplicadas en producción.

**La UI casi no existe:** `src/Inmobiliaria.Desktop` tiene sólo `LoginWindow`,
`ChangePasswordWindow` y `MainWindow`. Ninguna pantalla de ninguna capacidad. El motor está
avanzado; lo que Nicolás tocaría, no.

## Próximos pasos

1. **Arrancar `cobranza-y-recibos`**, por exploración y propuesta — no por código. Agrega el
   movimiento de **pago** que `cuenta-corriente` dejó afuera a propósito, y es dueño de: recibos y
   su numeración, tasa municipal, seguros, "Otros Conceptos", la liquidación al dueño y los
   honorarios de administración.
2. **`notificaciones`** después. Ya tiene su listado de mora esperándolo (`IMoraWorklistQuery`).
   Bloqueado por dos decisiones abiertas en `openspec/config.yaml`: `scheduler-mechanism` (una app
   de escritorio cerrada no manda nada) y la librería de PDF, sin elegir.
3. El **plan de pagos** es su propio change, posterior a los dos.
4. La **pasada de UI**, grande y casi entera por delante.

## Pendientes / bloqueos

- **Sin commitear:** `docs/ESTADO.md` (este archivo) y `.atl/skill-registry.md` (lo dejó
  modificado `gentle-ai sync`). **Faltan 2 commits de `develop` a `main`** — ahí va el `CLAUDE.md`
  que explica cómo se trabaja el proyecto, que es lo que necesita Mateo si saca una rama de ahí.
- **H.2 de `cuenta-corriente`, decisión de Tomás:** cómo se reparte entre contratos la deuda que la
  planilla tiene por inquilino, para quien tenga más de uno. Decisión de carga de datos.
- **H.3, opcional:** Supabase Cron como disparador extra de la materialización. Nunca una
  dependencia.
- **Pregunta abierta para Nicolás, no bloqueante:** si un contrato arranca a mitad de mes, ¿se
  cobra la parte ocupada? Tomás cree que sí, y que en la práctica se elige el 1° como inicio. El
  código no cambia en ninguno de los dos casos.
- **Defecto conocido, no arreglado:** `src/Inmobiliaria.Domain/Leasing/RentAdjustment.cs:76` dice
  ser *"el único sitio de truncado de todo el pipeline"*, y `AdjustmentProposal.cs:64` también
  trunca. Inofensivo; el comentario es falso.
- **`gentle-ai 4.0` eliminó los comandos `sdd-*` del CLI.** Las skills `sdd-*` siguen, pero
  `~/.claude/skills/_shared/sdd-orchestrator-workflow.md` quedó en su versión del 26-09 y todavía
  manda a correr `gentle-ai sdd-status`, que ya no existe. **Leer los artefactos directamente.**
- **Defecto de gentle-ai, reproducible:** su `upgrade` no puede actualizarse a sí mismo; arma
  `.../gentle-ai/v3/cmd/gentle-ai@v4.0.0` (ruta v3 con versión v4) y Go lo rechaza. Se resuelve con
  `go install github.com/gentleman-programming/gentle-ai/v4/cmd/gentle-ai@v4.0.0`.
  **Sin reportar todavía** — Tomás tiene que decidir si se reporta.
- **Ocho agentes de revisión** (`jd-*`, `review-*` en `~/.claude/agents/`) no se actualizaron:
  gentle-ai los preservó porque sus bytes estaban modificados y avisa que hay que mergearlos a
  mano. Sin uso hoy.
- **La rama `ramateo` de Mateo está en "Initial commit"**, 87 commits atrás y sin nada propio sin
  mergear. Tiene que traerse `develop` antes de trabajar.

## Precondiciones del próximo paso

| | |
|---|---|
| **Docker** | **No** — exploración y propuesta son documentos |
| **Supabase** | **No** — nada toca la base todavía |

La migración de `cuenta-corriente` ya está aplicada y verificada en producción.

## Para retomar

```bash
# La segunda inspección: que el último merge haya llegado entero
git fetch --all && git status --porcelain
git rev-list --count origin/main..origin/develop

# La suite completa. Con Docker APAGADO los tests de Testcontainers quedan OMITIDOS,
# que NO es lo mismo que pasados.
dotnet build Inmobiliaria.sln --configuration Release
dotnet test Inmobiliaria.sln --configuration Release

# El modelo de EF sigue coincidiendo con la última migración (no necesita base ni Docker)
dotnet ef migrations has-pending-model-changes -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure
```

Esperado con Docker prendido: **241 pasados, 0 fallas, 0 omitidos** (141 dominio + 100
infraestructura), build sin advertencias, modelo en sintonía.

**Leer la salida por el contador de errores o el código de salida, nunca por un patrón:**
`error CS` no encuentra `error xUnit2029`, y en esta máquina dotnet responde en español.

### Archivos clave para abrir primero

| Para qué | Dónde |
|---|---|
| Cómo trabajamos | `CLAUDE.md` (raíz) |
| Lo que el dueño respondió, textual | `openspec/domain/respuestas-del-dueno.md` |
| La cuenta corriente, ya viva | `openspec/specs/contract-account/spec.md` |
| El change recién cerrado | `openspec/changes/archive/2026-10-10-cuenta-corriente/archive-report.md` |
| Decisiones abiertas del proyecto | `openspec/config.yaml`, bloque `open_decisions` |

### Herramientas

gentle-ai **4.0.0**, engram **3.3.2**, Go 1.26.5, Docker 29.8.2 con `postgres:17.6` local.
Respaldo completo de la memoria en `~/.engram/backup-20261010-2030/` (`.db`, `-wal` y `-shm` — el
WAL tenía 3,7 MB sin volcar, así que copiar sólo el `.db` habría guardado una foto vieja).
