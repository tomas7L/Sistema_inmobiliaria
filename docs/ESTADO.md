# Estado del proyecto

_Última actualización: 2026-10-14, cierre de sesión_

> Este archivo **apunta**, no resume. El estado real de una implementación vive en
> `openspec/changes/<change>/tasks.md`, y el porqué de cada decisión vive en Engram.
> Si este archivo y `tasks.md` se contradicen, **gana `tasks.md`**.

## En qué estamos

**Change activo: `cobranza-y-recibos` (slice A), implementación en curso: PR 1 a 4 mergeados, más un PR de arreglos.**
Tasks: `openspec/changes/cobranza-y-recibos/tasks.md`. Las Phases 1 a 4 están tildadas, y H.1 y H.2 están hechas.

- Mergeados a `develop`: #57 (PR 1), #58 (PR 2), #59 (PR 3), #60 (PR 4) y #61 (arreglos de dominio previos al PR 5).
- La **migración A** (`20261014204601_AddFeePlanCutoverAndLedgerConstraints`) está **aplicada en Supabase**
  y verificada en el editor SQL (H.2).
- `ramatomy` tiene el mismo contenido que `develop`; sólo le faltan los commits de merge.
- No queda nada sin commitear.

## Próximos pasos

1. **PR 5 (migración B: tablas de cobros y recibos)**: Phase 5 de `tasks.md`. La tarea 5.1 es la
   segunda inspección: confirmar que #61 llegó y que `CollectionPlanner`, `VoidingPlanner`,
   `CollectionRecord`, `ReciboAnnulment` y `Recibo` existen como dice `tasks.md`.
2. Después del merge del PR 5, Tomás hace H.3 (el chequeo de zona horaria) y H.4 (aplicar la migración B).
3. PR 6 (`CollectionDesk`). Después, H.5 (PV1 contra el pooler real).

## Pendientes / bloqueos

- **Orden de la carga de datos (nuevo):** cada contrato viejo se carga con su **plan de honorarios** y
  su **`cutover_date`** **antes** de cualquier movimiento y antes de que alguien abra la lista de
  morosos. Las dos cosas quedan bloqueadas en cuanto la cuenta tiene un movimiento (ver la nota de
  `tasks.md` en 2.3). Si el plan falta, las cuotas no se generan nunca.
- **Antes de la carga de datos:** un mes viejo cargado como deuda inicial no genera recargo solo
  (Decision 31). Cada mes se carga con su cifra real, mora incluida.
- **Riesgo aceptado (nota de la tarea 2.9 en `tasks.md`):** los triggers que bloquean el plan y la
  fecha de corte no toman lock. Hay que revisarlo si alguna pantalla llega a cambiar el plan de un
  contrato vivo.
- **Para el contador o un abogado, no para Nicolás:**
  - si el recibo puede ser no fiscal (Decision 10, **débil**);
  - el orden de imputación del art. 900 del CCyCN (Decision 3, **débil**).
- **Para Nicolás, no bloqueantes:** Q3 (design §10), Open Question 4 y Open Question 7, y si se cobra
  la parte ocupada de un contrato que arranca a mitad de mes.
- **H.2 de `cuenta-corriente`, decisión de Tomás:** cómo se reparte entre contratos la deuda que la
  planilla tiene por inquilino.
- **Defecto conocido, no arreglado:** el comentario de
  `src/Inmobiliaria.Domain/Leasing/RentAdjustment.cs:76` dice que es el único lugar que trunca, y es
  falso: `AdjustmentProposal.cs:64` también trunca.
- **gentle-ai:** cuando hay archivos nuevos sin trackear, `review status` se traba en
  `intended_untracked_selection_required`. Se destraba con `--untracked-scope=select`,
  `--expected-untracked-inventory=<digest>` y un `--intended-untracked=<ruta>` por archivo (el
  procedimiento está en Engram).
- **La rama `ramateo`** está muy atrás de `develop` y tiene un stash viejo de GitHub Desktop.

## Precondiciones del próximo paso

| | |
|---|---|
| **Docker** | **Sí para el PR 5** (Testcontainers `postgres:17.6` aplicando las migraciones A y B). Sin Docker, los tests quedan omitidos, que no es lo mismo que pasados |
| **Supabase** | No para escribir el PR 5. Sí despausada después del merge, para H.3 y H.4 (los aplica Tomás) |

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

Esperado: **506 pasados (Domain 363, Infrastructure 143), 0 fallas, 0 omitidos**, build sin
advertencias y el modelo en sintonía con la última migración.

**Leer la salida por el contador de errores o el código de salida, nunca por un patrón.** En esta
máquina dotnet responde en español ("Superado", "Con error", "Omitido").

### Archivos clave para abrir primero

| Para qué | Dónde |
|---|---|
| Cómo trabajamos | `CLAUDE.md` (raíz) |
| Qué implementar, en orden | `openspec/changes/cobranza-y-recibos/tasks.md` (Phase 5 = PR 5; leer también "Issues found") |
| Cómo, y por qué | `openspec/changes/cobranza-y-recibos/design.md` (§5 para las tablas, los triggers y los grants) |
| Qué debe cumplir | `openspec/changes/cobranza-y-recibos/spec.md` |
| Patrón de migración con triggers | `src/Inmobiliaria.Infrastructure/Persistence/Migrations/20261014204601_AddFeePlanCutoverAndLedgerConstraints.cs` |

### Herramientas

gentle-ai **4.0.0**, engram **3.3.2**, Docker 29.8.2 con `postgres:17.6` local.
