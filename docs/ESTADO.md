# Estado del proyecto

_Última actualización: 2026-10-16 (tarde), cierre de sesión_

> Este archivo **apunta**, no resume. El estado real de una implementación vive en
> `openspec/changes/<change>/tasks.md`, y el porqué de cada decisión vive en Engram.
> Si este archivo y `tasks.md` se contradicen, **gana `tasks.md`**.

## En qué estamos

**Change activo: `ui-de-cobranza`**, fase **propuesta escrita, esperando que Tomás la lea**.
Preflight SDD de la sesión: Interactive · Both (openspec + Engram) · Ask me.

- Exploración: `openspec/changes/ui-de-cobranza/exploration.md`.
- Propuesta: `openspec/changes/ui-de-cobranza/proposal.md` — la nota de arriba dice qué leer y qué saltear.
- Todavía no hay `tasks.md` para este change.

## Próximos pasos

1. **Tomás lee la propuesta** y la aprueba o pide cambios.
2. Con la aprobación: Tomás commitea `openspec/changes/ui-de-cobranza/` y abre el PR `ramatomy` → `develop`
   (hito: propuesta cerrada).
3. `/gentle-sdd-continue ui-de-cobranza` → spec y design.
4. Antes de tasks/apply, Tomás elige la estrategia de cadena (`stacked-to-main` o
   `feature-branch-chain`): §6 Open Items de la propuesta.

## Pendientes / bloqueos

- **Sin commitear, a propósito (Tomás no los leyó):** `openspec/changes/ui-de-cobranza/exploration.md`,
  `openspec/changes/ui-de-cobranza/proposal.md`. `docs/ESTADO.md` sí se commitea en cada cierre.
- **`ramatomy` pusheada, sin PR:** 3 commits de docs sobre `develop` (`06e6eb2`, `650f423`, `6f67e5f`).
  El PR se abre por hito, no por cierre.
- **Para Nicolás, cuando se pueda:** H.2 de `openspec/changes/archive/2026-09-21-rent-adjustments/tasks.md`
  (índice que sale tarde) y el papel del recibo (§6 Open Item 2 de la propuesta).
- **Change siguiente ya decidido:** carga inicial desde el Excel de Nicolás (incluye H.2 de
  `cuenta-corriente`). El Excel tiene datos personales: **nunca al repo**.
- **Follow-ups técnicos:** `openspec/changes/archive/2026-10-15-cobranza-y-recibos/archive-report.md`, "Follow-ups Carried Over".
  Más uno nuevo en Engram: facturación futura necesitaría condición IVA + CUIL del propietario.
- **Defecto conocido, no arreglado:** comentario de `src/Inmobiliaria.Domain/Leasing/RentAdjustment.cs:76`
  (dice que es el único que trunca; `AdjustmentProposal.cs:64` también trunca).
- **La rama `ramateo`** sigue muy atrás de `develop`, con un stash viejo.

## Precondiciones del próximo paso

| | |
|---|---|
| **Docker** | No para leer la propuesta ni para spec/design. **Sí** desde apply (tests de Infrastructure con Testcontainers `postgres:17.6`) |
| **Supabase** | No. Sólo cuando haya que aplicar la migración del `cutover_date` (slice S2); la aplica Tomás |

## Para retomar

```bash
# La segunda inspección
git fetch --all && git status --porcelain
git rev-list --left-right --count HEAD...origin/develop
git diff --stat HEAD origin/develop

# La suite completa, con Docker PRENDIDO
dotnet build Inmobiliaria.sln --configuration Release
dotnet test Inmobiliaria.sln --configuration Release

# El modelo de EF sigue coincidiendo con la última migración
dotnet ef migrations has-pending-model-changes -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure
```

Esperado: **598 pasados, 0 fallas, 0 omitidos** (corrido el 2026-10-16 sobre `06e6eb2`; lo posterior es sólo docs), build sin
advertencias y el modelo en sintonía con `20261015204506_AddCollectionsAndRecibos`.

**Leer la salida por el contador de errores o el código de salida, nunca por un patrón.** En esta
máquina dotnet responde en español ("Superado", "Con error", "Omitido").

### Archivos clave para abrir primero

| Para qué | Dónde |
|---|---|
| Cómo trabajamos | `CLAUDE.md` (raíz) |
| Lo que hay que leer ahora | `openspec/changes/ui-de-cobranza/proposal.md` |
| Lo que la UI tiene que llamar | `src/Inmobiliaria.Domain/Collection/ICollectionDesk.cs` |
| Specs vivas de cobro y recibo | `openspec/specs/collection/`, `openspec/specs/receipt/` |

### Herramientas

gentle-ai **4.2.0** (ya no trae `sdd-status`), engram **3.4.0**, Docker 29.8.2 con `postgres:17.6`,
`gh` 2.102.0 en `C:\Program Files\GitHub CLI\gh.exe`, logueado como `tomas7L` (en el PATH de Bash
recién después de reiniciar Claude Code).
