# Estado del proyecto

_Última actualización: 2026-10-15, cierre de sesión_

> Este archivo **apunta**, no resume. El estado real de una implementación vive en
> `openspec/changes/<change>/tasks.md`, y el porqué de cada decisión vive en Engram.
> Si este archivo y `tasks.md` se contradicen, **gana `tasks.md`**.

## En qué estamos

**No hay change activo.** `cobranza-y-recibos` (slice A) está **archivado** y mergeado entero a
`develop` (#57 a #65).

- Archivo: `openspec/changes/archive/2026-10-15-cobranza-y-recibos/` — leer `archive-report.md`
  (secciones "Tasks at Close" y "Follow-ups Carried Over").
- `ramatomy` tiene el mismo contenido que `origin/develop`; sólo le faltan los 9 commits de merge.
- No queda nada sin commitear.

## Próximos pasos

1. **Nuevo change: la UI de cobranza** (`CollectionDesk` todavía no está conectado a
   `Inmobiliaria.Desktop`). Arranca con `/gentle-sdd-new`, después de la segunda inspección.
2. Antes de abrirlo: actualizar gentle-ai y engram si hay versión nueva (sólo entre changes; ver Engram).

## Pendientes / bloqueos

- **Abiertos de `cobranza-y-recibos`, no cerrables por el asistente:** H.6 y H.7 en
  `openspec/changes/archive/2026-10-15-cobranza-y-recibos/tasks.md`.
- **Follow-ups técnicos:** la lista está en `archive-report.md`, "Follow-ups Carried Over".
- **Specs vivas sin reconciliar:** `archive-report.md`, "Not Placed Cleanly" (Tests de
  `contract-account` y `lease-contract`).
- **Orden de la carga de datos:** plan de honorarios y `cutover_date` de cada contrato **antes** de
  cualquier movimiento (nota de la tarea 2.3 del `tasks.md` archivado).
- **Para el contador o un abogado:** Decision 10 y Decision 3 del `design.md` archivado (ambas **débiles**).
- **H.2 de `cuenta-corriente`, decisión de Tomás:** cómo se reparte entre contratos la deuda que la
  planilla tiene por inquilino.
- **Defecto conocido, no arreglado:** el comentario de
  `src/Inmobiliaria.Domain/Leasing/RentAdjustment.cs:76` dice que es el único lugar que trunca;
  `AdjustmentProposal.cs:64` también trunca.
- **gentle-ai con archivos sin trackear:** `review status` se traba en
  `intended_untracked_selection_required`; el procedimiento está en Engram.
- **La rama `ramateo`** está muy atrás de `develop` y tiene un stash viejo de GitHub Desktop.

## Precondiciones del próximo paso

| | |
|---|---|
| **Docker** | No para explorar ni escribir la propuesta. **Sí** para correr la suite completa en la segunda inspección (Testcontainers `postgres:17.6`) |
| **Supabase** | No. Sólo hace falta cuando haya una migración nueva para aplicar (la aplica Tomás) |

## Para retomar

```bash
# La segunda inspección: que el último merge haya llegado entero
git fetch --all && git status --porcelain
git rev-list --left-right --count HEAD...origin/develop   # "0 N": sólo commits de merge
git diff --stat HEAD origin/develop                       # vacío = mismo contenido

# La suite completa, con Docker PRENDIDO
dotnet build Inmobiliaria.sln --configuration Release
dotnet test Inmobiliaria.sln --configuration Release

# El modelo de EF sigue coincidiendo con la última migración (no necesita base ni Docker)
dotnet ef migrations has-pending-model-changes -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure
```

Esperado: **598 pasados, 0 fallas, 0 omitidos** (último registro, en `1747c75`), build sin
advertencias y el modelo en sintonía con `20261015204506_AddCollectionsAndRecibos`.
Los tests de `tests/Inmobiliaria.Pv1.Tests` se omiten si `INMOBILIARIA_PV1_DB` no está definida.

**Leer la salida por el contador de errores o el código de salida, nunca por un patrón.** En esta
máquina dotnet responde en español ("Superado", "Con error", "Omitido").

### Archivos clave para abrir primero

| Para qué | Dónde |
|---|---|
| Cómo trabajamos | `CLAUDE.md` (raíz) |
| Qué quedó abierto | `openspec/changes/archive/2026-10-15-cobranza-y-recibos/archive-report.md` |
| Lo que la UI tiene que llamar | `src/Inmobiliaria.Domain/Collection/ICollectionDesk.cs` (impl.: `src/Inmobiliaria.Infrastructure/Persistence/CollectionDesk.cs`) |
| Specs vivas de cobro y recibo | `openspec/specs/collection/`, `openspec/specs/receipt/` |

### Herramientas

gentle-ai **4.0.0**, engram **3.3.2**, Docker 29.8.2 con `postgres:17.6` local.
