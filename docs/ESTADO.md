# Estado del proyecto

_Última actualización: 2026-10-14, cierre de sesión_

> Este archivo **apunta**, no resume. El estado real de una implementación vive en
> `openspec/changes/<change>/tasks.md`, y el porqué de cada decisión vive en Engram.
> Si este archivo y `tasks.md` se contradicen, **gana `tasks.md`**.

## En qué estamos

**Change activo: `cobranza-y-recibos`, planificación cerrada; implementación sin empezar.**
Tasks: `openspec/changes/cobranza-y-recibos/tasks.md` (6 PRs, 73 tareas + H.1–H.8), ninguna tildada.

- Propuesta: `openspec/changes/cobranza-y-recibos/proposal.md`, aprobada y mergeada (PR #53).
- Spec: `openspec/changes/cobranza-y-recibos/spec.md`, 49 decisiones y 76 tests.
- Design: `openspec/changes/cobranza-y-recibos/design.md`, D1–D27, 6 slices (~2.400–2.900 líneas).
  Pasó una revisión independiente; sus hallazgos están corregidos en los dos documentos.
- Exploración: sólo en Engram, `sdd/cobranza-y-recibos/explore`.
- El change es el **slice A** de tres: el pago del inquilino y sus dos recibos. Los otros dos son
  changes posteriores, `liquidacion-al-dueno` y `recibo-pdf`.

No hay código nuevo. Spec, design y tasks mergeados a `develop` (PRs #54, #55 y #56).
`ramatomy` tiene el mismo contenido que `develop`; le faltan sólo los commits de merge, así que
hay que traer `develop` antes de empezar.

## Próximos pasos

1. **PR 1 (ledger rules in the domain)**: tareas 1.1 a 1.15 de `tasks.md`. Sólo Domain, sin Docker
   ni Supabase. La tarea 1.2 registra cuántos tests existentes reemplaza (los totales esperados son
   `241 − R + N`, no `241 + N`).
2. Después, PR 2 en adelante, de a uno: cada PR se mergea a `develop` antes de empezar el siguiente.
   PR 2, 5 y 6 necesitan Docker; después de PR 2 y PR 5 Tomás aplica la migración (H.1–H.4).

## Pendientes / bloqueos

- **Para Nicolás, no bloqueantes (design §10):** Q3 (con qué mes arranca la cuota 1 si el contrato
  empieza después del día de vencimiento).
- **Antes de la carga de datos:** cada contrato viejo necesita su fecha de corte (`cutover_date`,
  Decision 41) **antes** de que alguien abra la lista de morosos, o el sistema le genera toda la
  historia.
- **Para la carga de datos:** un mes viejo cargado como deuda inicial no genera recargo solo
  (Decision 31). Cada mes se carga con su cifra real, mora incluida.
- **Para el contador o un abogado, no para Nicolás:**
  - si el recibo puede ser no fiscal (Decision 10, recomendación **débil**);
  - si el art. 900 del CCyCN obliga a pagar primero los recargos de los meses más viejos antes que
    el alquiler del mes que elige el inquilino (Decision 3, **débil**).
- **Para Nicolás, no bloqueantes:** Open Question 4 (cuotas de honorarios contractuales fuera de
  término) y Open Question 7 (si su Excel dice qué meses debe cada inquilino; es de la carga de
  datos).
- **H.2 de `cuenta-corriente`, decisión de Tomás:** cómo se reparte entre contratos la deuda que la
  planilla tiene por inquilino.
- **H.3, opcional:** Supabase Cron como disparador extra de la materialización.
- **Pregunta abierta para Nicolás, no bloqueante:** si un contrato arranca a mitad de mes, ¿se
  cobra la parte ocupada?
- **Defecto conocido, no arreglado:** el comentario de
  `src/Inmobiliaria.Domain/Leasing/RentAdjustment.cs:76` dice que es el único lugar del pipeline
  que trunca, y es falso: `AdjustmentProposal.cs:64` también trunca.
- **gentle-ai 4.0:**
  - Ya no existen los comandos `sdd-*` del CLI; leer los artefactos directamente.
  - Su `upgrade` no puede actualizarse a sí mismo. Sin reportar; decide Tomás.
  - Los agentes `jd-*` y `review-*` de `~/.claude/agents/` quedaron sin mergear.
- **La rama `ramateo`** está 87 commits atrás; tiene que traerse `develop` antes de trabajar.

## Precondiciones del próximo paso

| | |
|---|---|
| **Docker** | **No para el PR 1** (sólo Domain). Sí para PR 2, 5 y 6 |
| **Supabase** | **No para el PR 1.** Sí despausada para aplicar las migraciones de PR 2 y PR 5 (las aplica Tomás) |

## Para retomar

```bash
# La segunda inspección: que el último merge haya llegado entero
git fetch --all && git status --porcelain
git rev-list --count origin/develop..HEAD      # 0 si ramatomy ya está en develop
git diff --stat origin/main origin/develop     # lo que falta pasar a main

# La suite completa. Con Docker APAGADO los tests de Testcontainers quedan OMITIDOS,
# que NO es lo mismo que pasados.
dotnet build Inmobiliaria.sln --configuration Release
dotnet test Inmobiliaria.sln --configuration Release

# El modelo de EF sigue coincidiendo con la última migración (no necesita base ni Docker)
dotnet ef migrations has-pending-model-changes -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure
```

Esperado con Docker prendido: **241 pasados, 0 fallas, 0 omitidos**, build sin advertencias y el
modelo en sintonía con la última migración.

**Leer la salida por el contador de errores o el código de salida, nunca por un patrón.** `error CS`
no encuentra `error xUnit2029`, y en esta máquina dotnet responde en español.

### Archivos clave para abrir primero

| Para qué | Dónde |
|---|---|
| Cómo trabajamos | `CLAUDE.md` (raíz) |
| Qué implementar, en orden | `openspec/changes/cobranza-y-recibos/tasks.md` (Phase 1 = PR 1) |
| Cómo, y por qué | `openspec/changes/cobranza-y-recibos/design.md` (§7 y §8 para el PR 1) |
| Qué debe cumplir | `openspec/changes/cobranza-y-recibos/spec.md` (cada tarea de `tasks.md` nombra los tests que cubre) |
| Lo que el dueño respondió, textual | `openspec/domain/respuestas-del-dueno.md` |
| Decisiones abiertas del proyecto | `openspec/config.yaml`, bloque `open_decisions` |

### Herramientas

gentle-ai **4.0.0**, engram **3.3.2**, Go 1.26.5, Docker 29.8.2 con `postgres:17.6` local.
