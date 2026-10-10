# Estado del proyecto

_Última actualización: 2026-10-10 (tras actualizar gentle-ai 4.0.0 y engram 3.3.2)_

> Este archivo **apunta**, no resume. El estado real de una implementación vive en
> `openspec/changes/<change>/tasks.md`, y el porqué de cada decisión vive en Engram.
> Si este archivo y `tasks.md` se contradicen, **gana `tasks.md`**.

## En qué estamos

**Entre changes.** `cuenta-corriente` quedó cerrado y archivado en
`openspec/changes/archive/2026-10-10-cuenta-corriente/`, y su especificación ya es parte de la
documentación viva: `openspec/specs/contract-account/spec.md`.

`openspec/changes/` está **vacío** — no hay ningún change en curso.

Lo último que se hizo fue un arreglo suelto, ya mergeado: la convención
`ApplyApplicationSuppliedKeys` en `src/Inmobiliaria.Infrastructure/Persistence/InmobiliariaDbContext.cs`,
que le declara a EF que todas las claves las provee la aplicación.

`main` y `develop` tienen **contenido idéntico**. Nada pendiente de mergear.

## Próximos pasos

1. **Arrancar el change `cobranza-y-recibos`.** Es el que agrega el movimiento de **pago** que
   `cuenta-corriente` dejó deliberadamente afuera, y además es dueño de: tasa municipal, seguros,
   "Otros Conceptos", los recibos y su numeración, la liquidación al dueño y los honorarios de
   administración. Empieza por exploración y propuesta, no por código.
2. Después, **`notificaciones`** — ya tiene su listado de mora esperándolo
   (`IMoraWorklistQuery`). Depende de la decisión abierta `scheduler-mechanism` en
   `openspec/config.yaml`: una aplicación de escritorio cerrada no manda nada.
3. El **plan de pagos** es su propio change, posterior a los dos anteriores.

## Pendientes / bloqueos

- **H.2 de `cuenta-corriente`, decisión de Tomás:** cómo se reparte entre contratos la deuda que
  la planilla de la inmobiliaria tiene por inquilino, para los que tienen más de un contrato. Es
  una decisión de carga de datos.
- **H.3 de `cuenta-corriente`, opcional:** Supabase Cron como disparador extra de la
  materialización. Nunca una dependencia.
- **Pregunta para Nicolás, no bloqueante:** si un contrato arranca a mitad de mes, ¿se cobra la
  parte ocupada? Tomás cree que sí, y que en la práctica se elige el 1° como fecha de inicio.
  El código no cambia en ninguno de los dos casos.
- **Defecto conocido, no arreglado:** `src/Inmobiliaria.Domain/Leasing/RentAdjustment.cs:76` dice
  ser *"el único sitio de truncado de todo el pipeline"*, y `AdjustmentProposal.cs:64` también
  trunca. Inofensivo, pero el comentario es falso.
- **La rama `ramateo` de Mateo está en "Initial commit"**, 87 commits atrás y sin nada propio sin
  mergear. Tiene que traerse `develop` antes de trabajar.
- **Herramientas actualizadas el 2026-10-10:** gentle-ai **4.0.0** y engram **3.3.2**. La memoria
  quedó verificada intacta (88 observaciones, 31 relaciones, leídas directo del SQLite). Respaldo
  completo en `~/.engram/backup-20261010-2030/` — incluye `.db`, `-wal` y `-shm`, porque el WAL
  tenía 3,7 MB de transacciones sin volcar y copiar sólo el `.db` habría respaldado una foto vieja.
- **Pendiente de la actualización, no bloqueante:** `gentle-ai 4.0` **eliminó todos los comandos
  `sdd-*` del CLI** (`sdd-status` ya no existe). Las skills `sdd-*` sobrevivieron, pero
  `~/.claude/skills/_shared/sdd-orchestrator-workflow.md` quedó con fecha del 26-09 y todavía
  manda a correr `gentle-ai sdd-status` para enrutar una fase. Cuando arranque el próximo change,
  leer los artefactos directamente en vez de ese despachador.
- **Defecto de gentle-ai, reproducible:** su propio `gentle-ai upgrade` falla al actualizarse a sí
  mismo. Arma `go install .../gentle-ai/v3/cmd/gentle-ai@v4.0.0` — ruta de módulo v3 con versión
  v4 — y Go lo rechaza con `invalid version: unknown revision`. Se resuelve a mano con
  `go install github.com/gentleman-programming/gentle-ai/v4/cmd/gentle-ai@v4.0.0`.
- **Ocho agentes de revisión no se actualizaron** (`jd-*`, `review-*` en `~/.claude/agents/`).
  gentle-ai los preservó a propósito porque sus bytes estaban modificados, y avisa que hay que
  compararlos y mergearlos a mano. Sin uso hoy.

## Precondiciones del próximo paso

| | |
|---|---|
| **Docker** | **No** — exploración y propuesta son documentos |
| **Supabase** | **No** — nada toca la base todavía |

La migración de `cuenta-corriente` ya está aplicada en producción y verificada.

## Para retomar

```bash
# Verificar que el merge anterior quedó bien (la segunda inspección)
git fetch --all && git status --porcelain
git rev-list --count origin/main..origin/develop

# La suite completa. Con Docker apagado, los tests de Testcontainers quedan OMITIDOS,
# que NO es lo mismo que pasados.
dotnet build Inmobiliaria.sln --configuration Release
dotnet test Inmobiliaria.sln --configuration Release

# El modelo de EF sigue coincidiendo con la última migración (no necesita base ni Docker)
dotnet ef migrations has-pending-model-changes -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure
```

Estado esperado hoy: **241 tests pasados, 0 fallas, 0 omitidos** con Docker prendido
(141 de dominio + 100 de infraestructura), build sin advertencias, y el modelo en sintonía.

### Archivos clave para abrir primero

| Para qué | Dónde |
|---|---|
| Cómo trabajamos | `CLAUDE.md` (raíz) |
| Lo que el dueño respondió, textual | `openspec/domain/respuestas-del-dueno.md` |
| La cuenta corriente, ya viva | `openspec/specs/contract-account/spec.md` |
| El change recién cerrado | `openspec/changes/archive/2026-10-10-cuenta-corriente/archive-report.md` |
