# Estado del proyecto

_Última actualización: 2026-10-23, cierre de sesión_

> Este archivo **apunta**, no resume. El estado real de una implementación vive en
> `openspec/changes/<change>/tasks.md`, y el porqué de cada decisión vive en Engram.
> Si este archivo y `tasks.md` se contradicen, **gana `tasks.md`**.

## En qué estamos

**Change activo: `ui-de-cobranza`**, implementación **terminada**: los seis slices están mergeados en `develop`
(PR #71 a #76, último merge `0c53409`). Falta solo lo humano (H.4 y H.5) y después cerrar el change.

- Tareas, desviaciones y seguimientos: `openspec/changes/ui-de-cobranza/tasks.md` ("Deviations", bloques Phase 1 a 6; "Human Follow-Ups").
- Spec y design: `openspec/changes/ui-de-cobranza/spec.md`, `openspec/changes/ui-de-cobranza/design.md`.

## Próximos pasos

1. **Preparar la base de pruebas de Supabase** (Tomás la despausó el 2026-10-23). El asistente escribe el script; Tomás lo corre en el SQL Editor:
   - Última migración aplicada: `SELECT migration_id FROM "__EFMigrationsHistory" ORDER BY migration_id DESC LIMIT 1;` → tiene que ser `20261023131755_HardenCutoverDateToAdmin`. Si no, aplicar las que falten con `dotnet ef migrations script <última aplicada> 20261023131755_HardenCutoverDateToAdmin --idempotent -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure`.
   - Un usuario de login según `docs/runbooks/bootstrap-first-admin.md`.
   - Datos de prueba por SQL: un contrato con inquilino, propietario, unidad y cuenta, y el número inicial de recibo (no hay pantalla que los cree todavía).
   - Apuntar el sistema a la base de pruebas cambiando `Supabase:ProjectRef` y `Supabase:Host` solo en `src/Inmobiliaria.Desktop/bin/Release/net10.0-windows/appsettings.json`, nunca en el repo.
2. **H.4 y H.5 juntas** (`tasks.md`, "Human Follow-Ups"): entrar, cobrar, imprimir a PDF con **Microsoft Print to PDF** (M1, M2), anular, reimprimir, y confirmar que cobrar y anular no abren el diálogo (M3). El PDF se puede imprimir en papel desde la PC vieja.
3. **Hecho 2026-10-24:** base de prueba lista, H.4 aprobada, H.5 hecha. H.5 encontró cuatro defectos; los cuatro están arreglados, probados en vivo y revisados por gentle-ai (dos revisiones aprobadas). Detalle: `openspec/changes/ui-de-cobranza/tasks.md`, "Human Follow-Up Results" y "Fixes". Falta: **Tomás commitea y abre el PR**, aplica la migración `20261025005832_FixClearMustChangePasswordIdentity` a **producción** (H.6), y recién después se cierra `ui-de-cobranza` (verify y archive).
4. **Change siguiente: pantalla de usuarios (solo Admin), obligatoria antes de entregar.** Alta de Empleado, reseteo de contraseña y baja, desde la aplicación. La spec ya lo exige (`openspec/specs/user-registry/spec.md:78`) y la lógica existe con tests (`src/Inmobiliaria.Infrastructure/Access/IUserProvisioning.cs`); falta solo la pantalla. Hoy, si se va un empleado, Nicolás no puede dar de alta al reemplazo sin que nosotros corramos SQL. Crear otro **Admin** sigue siendo por runbook, a propósito (`docs/runbooks/bootstrap-first-admin.md`).
5. **Change después: carga inicial desde el Excel de Nicolás.** Tomás le pide una copia (no una reunión) ya mismo, en paralelo; el asistente la lee desde fuera del repo y arma una lista corta de preguntas para mandarle por WhatsApp.

## Pendientes / bloqueos

- **Commiteado en `ramatomy`, sin PR todavía:** `9d6926d` (este archivo, `tasks.md` y el test de seguimiento de `VoidingViewModelTests.cs`), 1 commit por delante de `develop`. Va en el próximo PR.
- **Seguimiento chico, para el próximo change que toque impresión:** envolver el armado de la hoja en `src/Inmobiliaria.Presentation/Recibos/ReciboViewModel.cs` (`PrintAsync`) para que un fallo sea un mensaje de impresión.
- **Datos personales, nunca al repo:** el Excel de Nicolás y el PDF de muestra (`C:\Users\buffa\Downloads\abregu.pdf`). El logo original queda en `C:\Users\buffa\Downloads\LogoDelLago.jpg`; el del repo es `src/Inmobiliaria.Desktop/Assets/agency-logo.png`.
- **La impresora no la toma la PC nueva de Tomás** (drivers de Windows, no del sistema). Para H.4 alcanza con Microsoft Print to PDF.
- **Defecto conocido, no arreglado:** comentario de `src/Inmobiliaria.Domain/Leasing/RentAdjustment.cs:76`.
- **La rama `ramateo`** sigue muy atrás de `develop`, con un stash viejo.

## Precondiciones del próximo paso

| | |
|---|---|
| **Docker** | Sí para correr la suite antes de empezar (Testcontainers `postgres:17.6`); no lo usa la prueba en vivo |
| **Supabase** | **Sí, la base de pruebas despierta** (despausada el 2026-10-23; se pausa tras unos 7 días sin uso). Solo Tomás se conecta; la buena no se toca |

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

Esperado: **888 pasados, 0 fallas, 0 omitidos** (887 al merge del S4b `0c53409`, más el test de seguimiento sin
commitear), build sin advertencias y el modelo en sintonía con `20261023131755_HardenCutoverDateToAdmin`.

**Leer la salida por el contador de errores o el código de salida, nunca por un patrón.** En esta
máquina dotnet responde en español ("Superado", "Con error", "Omitido").

### Archivos clave para abrir primero

| Para qué | Dónde |
|---|---|
| Cómo trabajamos | `CLAUDE.md` (raíz) |
| Lo que queda del change | `openspec/changes/ui-de-cobranza/tasks.md`, "Human Follow-Ups" (H.4, H.5) |
| Cómo se crea el login de prueba | `docs/runbooks/bootstrap-first-admin.md` |
| Qué datos necesita un contrato cobrable | `tests/Inmobiliaria.Infrastructure.Tests/CollectionDeskFixture.cs` (`DeskWorld.SeedAccountAsync`) |

### Herramientas

gentle-ai **4.2.0**, engram **3.4.0**, Docker 29.9.2 con `postgres:17.6`, `gh` 2.102.0 en
`C:\Program Files\GitHub CLI\gh.exe`, logueado como `tomas7L`. En la herramienta Bash, las ediciones de C# van con
scripts de Python escritos al scratchpad: los heredocs se rompen con los apóstrofes.
