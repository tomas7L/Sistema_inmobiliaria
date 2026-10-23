# Estado del proyecto

_Última actualización: 2026-10-22, cierre de sesión_

> Este archivo **apunta**, no resume. El estado real de una implementación vive en
> `openspec/changes/<change>/tasks.md`, y el porqué de cada decisión vive en Engram.
> Si este archivo y `tasks.md` se contradicen, **gana `tasks.md`**.

## En qué estamos

**Change activo: `ui-de-cobranza`**, fase **apply**. PR 1 de 6 (S1) mergeado en `develop` (#71).
Preflight SDD de la sesión: Interactive · Both (openspec + Engram) · Auto (auto-chain), cadena `stacked-to-main`.

- Tareas y su estado: `openspec/changes/ui-de-cobranza/tasks.md` (Phase 1 tildada; sigue Phase 2).
- Spec y design: `openspec/changes/ui-de-cobranza/spec.md`, `openspec/changes/ui-de-cobranza/design.md`.

## Próximos pasos

1. `/gentle-sdd-apply ui-de-cobranza` → **Phase 2 (S2, go-live)** de `tasks.md`, empezando por la segunda
   inspección (2.1) y la tarea 2.1a (salida de la revisión de S1).
2. Revisión de gentle-ai sobre el S2 (con archivos nuevos: ver Engram `pattern/gentle-ai-review-untracked`).
3. Handover del S2 → Tomás commitea, PR, merge.
4. **Después del merge del S2:** Tomás despausa Supabase y aplica la migración `HardenCutoverDateToAdmin` (H.1 de `tasks.md`).

## Pendientes / bloqueos

- **Sin commitear:** `docs/ESTADO.md` (este cierre) y `.atl/skill-registry.md` (sólo la fecha; lo tocó `gentle-ai sync`).
- **Antes de la Phase 6 (impresión):** H.3 de `tasks.md` — membrete y logo. Los datos están en Engram
  `discovery/recibo-paper-layout`; el logo se puede sacar del PDF de muestra (fuera del repo, tiene datos personales).
- **Change futuro anotado:** cobrar la diferencia de un ajuste tardío como opción del operador
  (Out of Scope de `spec.md`). Reemplaza la pregunta H.2 a Nicolás.
- **Change siguiente ya decidido:** carga inicial desde el Excel de Nicolás. El Excel tiene datos personales: **nunca al repo**.
- **Defecto conocido, no arreglado:** comentario de `src/Inmobiliaria.Domain/Leasing/RentAdjustment.cs:76`.
- **La rama `ramateo`** sigue muy atrás de `develop`, con un stash viejo.

## Precondiciones del próximo paso

| | |
|---|---|
| **Docker** | **Sí**, prendido antes de arrancar el S2 (Testcontainers `postgres:17.6`, tests de trigger con roles de login) |
| **Supabase** | No para implementar el S2. **Sí después de su merge**, despausada, para que Tomás aplique la migración |

## Para retomar

```bash
# La segunda inspección
git fetch --all && git status --porcelain
git rev-list --left-right --count HEAD...origin/develop
git diff --stat HEAD origin/develop

# La suite completa, con Docker PRENDIDO
dotnet build Inmobiliaria.sln --configuration Release
dotnet test Inmobiliaria.Core.slnf --configuration Release

# El modelo de EF sigue coincidiendo con la última migración
dotnet ef migrations has-pending-model-changes -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure
```

Esperado: **631 pasados, 0 fallas, 0 omitidos** (corrido el 2026-10-22 sobre el S1, ahora `6506cef`), build sin
advertencias y el modelo en sintonía con `20261015204506_AddCollectionsAndRecibos`.

**Leer la salida por el contador de errores o el código de salida, nunca por un patrón.** En esta
máquina dotnet responde en español ("Superado", "Con error", "Omitido").

### Archivos clave para abrir primero

| Para qué | Dónde |
|---|---|
| Cómo trabajamos | `CLAUDE.md` (raíz) |
| Lo próximo a implementar | `openspec/changes/ui-de-cobranza/tasks.md`, Phase 2 |
| El porqué del S2 | `openspec/changes/ui-de-cobranza/design.md` §6 |
| Lo que dejó el S1 | `src/Inmobiliaria.Infrastructure/Access/SessionServices.cs`, `src/Inmobiliaria.Presentation/` |

### Herramientas

gentle-ai **4.2.0** (sync corrido el 2026-10-22; ya no trae `sdd-status`), engram **3.4.0**, Docker 29.8.2 con
`postgres:17.6`, `gh` 2.102.0 en `C:\Program Files\GitHub CLI\gh.exe`, logueado como `tomas7L`.
