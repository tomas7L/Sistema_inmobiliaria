# Estado del proyecto

_Última actualización: 2026-10-13, después de cerrar las dudas de la propuesta_

> Este archivo **apunta**, no resume. El estado real de una implementación vive en
> `openspec/changes/<change>/tasks.md`, y el porqué de cada decisión vive en Engram.
> Si este archivo y `tasks.md` se contradicen, **gana `tasks.md`**.

## En qué estamos

**Change activo: `cobranza-y-recibos`, propuesta cerrada.** Todavía no tiene spec, design ni
`tasks.md`.

- Propuesta: `openspec/changes/cobranza-y-recibos/proposal.md`, leída por Tomás y con sus dudas ya
  incorporadas (§5, Decisions 3, 4, 6, 8 y 15; §6, "Settled" y "Already answered").
- Exploración: sólo en Engram, `sdd/cobranza-y-recibos/explore`.
- El change es el **slice A** de tres: el pago del inquilino y sus dos recibos. Los otros dos son
  changes posteriores, `liquidacion-al-dueno` y `recibo-pdf`.

No hay código nuevo. `ramatomy`, `develop` y `main` están a la par.

## Próximos pasos

1. **Spec de `cobranza-y-recibos`**, un solo archivo: `openspec/changes/cobranza-y-recibos/spec.md`.
   Ya no hay pregunta que la bloquee. Tiene que **reescribir** el requisito vivo
   "A Payment Settles the Oldest Unpaid Period" de `openspec/specs/contract-account/spec.md:179` y su
   Test 14 (Decision 3).
2. **Design**, en paralelo o después: el contador de numeración, el tope de la fecha de pago y quién
   puede ponerla hacia atrás (§6, "Settled — the payment date").

## Pendientes / bloqueos

- **Sin commitear:** `openspec/changes/cobranza-y-recibos/proposal.md` (archivo nuevo) y
  `docs/ESTADO.md` (este archivo).
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
| **Docker** | **No.** Spec y design son documentos |
| **Supabase** | **No.** Nada toca la base todavía |

Para implementar el slice A van a hacer falta las dos: Docker para Testcontainers y Supabase
despausada para la migración. La migración la aplica Tomás.

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
| La propuesta cerrada | `openspec/changes/cobranza-y-recibos/proposal.md` |
| El requisito que la spec reescribe | `openspec/specs/contract-account/spec.md:179` |
| Lo que el dueño respondió, textual | `openspec/domain/respuestas-del-dueno.md` |
| Decisiones abiertas del proyecto | `openspec/config.yaml`, bloque `open_decisions` |

### Herramientas

gentle-ai **4.0.0**, engram **3.3.2**, Go 1.26.5, Docker 29.8.2 con `postgres:17.6` local.
