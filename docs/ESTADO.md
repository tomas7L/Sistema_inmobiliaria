# Estado del proyecto

_Última actualización: 2026-10-25, archive de `ui-de-cobranza`_

> Este archivo **apunta**, no resume. El estado real de una implementación vive en
> `openspec/changes/<change>/tasks.md`, y el porqué de cada decisión vive en Engram.
> Si este archivo y `tasks.md` se contradicen, **gana `tasks.md`**.

## En qué estamos

**Ningún change activo.** `ui-de-cobranza` quedó **verificado y archivado** el 2026-10-25 (sin commitear todavía):
`openspec/changes/archive/2026-10-25-ui-de-cobranza/`, con su `archive-report.md` (verificación, specs fusionadas,
seguimientos que pasan al próximo change). Capacidades nuevas en `openspec/specs/desktop-shell/`, `cash-desk/` y
`go-live-setup/`; fusionadas en `contract-account`, `access-control` y `receipt`.

## Próximos pasos

1. **Tomás: commit y PR del archive** (archivos y mensaje en la conversación del 2026-10-25).
2. **Change siguiente: pantalla de usuarios (solo Admin), obligatoria antes de entregar.** Alta de Empleado, reseteo y
   baja desde la aplicación. La spec ya lo exige (`openspec/specs/user-registry/spec.md:78`) y la lógica existe
   (`src/Inmobiliaria.Infrastructure/Access/IUserProvisioning.cs`). Crear otro Admin sigue siendo por runbook
   (`docs/runbooks/bootstrap-first-admin.md`). Empieza por la exploración y la propuesta (SDD).
3. **Change después: carga inicial desde el Excel de Nicolás.** Tomás se lo pide el **lunes 2026-10-26**; el asistente lo
   lee desde fuera del repo y arma una lista corta de preguntas para WhatsApp.

## Pendientes / bloqueos

- **Sin commitear:** el archive completo (`openspec/changes/archive/2026-10-25-ui-de-cobranza/`, las seis specs de
  `openspec/specs/`) y este archivo. La rama local `ramatomy` está 1 commit detrás de `origin/develop` (solo el merge
  del #77, sin cambios de contenido).
- **El `appsettings.json` compilado apunta a la base de PRUEBA** (`chygrzoqmcqpncogeiuz`, `aws-1-sa-east-1`), solo en
  `src/Inmobiliaria.Desktop/bin/Release/net10.0-windows/appsettings.json` (ignorado por git). Producción es
  `bwiqgfhokolndygqkchp`, `aws-0-sa-east-1`, que es lo que trae el repo.
- **Base de prueba:** usuario Admin `tomas` y el contrato ficticio "Maria/Juan Prueba". Scripts de esta sesión en el
  scratchpad (no en el repo): `seed-test-database.sql`, `reset-test-database.sql`.
- **Registro de errores en la PC:** `%LOCALAPPDATA%\SistemaInmobiliaria\logs\errores-yyyy-MM-dd.log`.
- **Datos personales, nunca al repo:** el Excel de Nicolás y el PDF de muestra (`C:\Users\buffa\Downloads\abregu.pdf`).
  Logo original en `C:\Users\buffa\Downloads\LogoDelLago.jpg`; el del repo es `src/Inmobiliaria.Desktop/Assets/agency-logo.png`.
- **La impresora no la toma la PC nueva de Tomás** (drivers de Windows, no del sistema).
- **Defecto conocido, no arreglado:** comentario de `src/Inmobiliaria.Domain/Leasing/RentAdjustment.cs:76`.
- **La rama `ramateo`** sigue muy atrás de `develop`, con un stash viejo.

## Precondiciones del próximo paso

| | |
|---|---|
| **Docker** | No para explorar ni proponer; **sí** desde que haya código (Testcontainers `postgres:17.6`) |
| **Supabase** | No hasta la prueba en vivo. La base de prueba se pausa tras unos 7 días sin uso; solo Tomás se conecta |

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

Esperado: **899 pasados, 0 fallas, 0 omitidos** (al merge del #77), build sin advertencias y el modelo en sintonía con
`20261025005832_FixClearMustChangePasswordIdentity`. Si el build de Desktop falla con MSB3027, la aplicación está
abierta y tiene tomadas sus DLL: cerrarla.

**Leer la salida por el contador de errores o el código de salida, nunca por un patrón.** En esta
máquina dotnet responde en español ("Superado", "Con error", "Omitido").

### Archivos clave para abrir primero

| Para qué | Dónde |
|---|---|
| Cómo trabajamos | `CLAUDE.md` (raíz) |
| Lo que exige la pantalla de usuarios | `openspec/specs/user-registry/spec.md`, `openspec/specs/access-control/spec.md` |
| La lógica que ya existe | `src/Inmobiliaria.Infrastructure/Access/IUserProvisioning.cs` |
| Seguimientos heredados | `openspec/changes/archive/2026-10-25-ui-de-cobranza/archive-report.md`, "Follow-ups Carried Over" |

### Herramientas

gentle-ai **4.2.0**, engram **3.4.0**, Docker 29.9.2 con `postgres:17.6`, `gh` 2.102.0 en
`C:\Program Files\GitHub CLI\gh.exe`, logueado como `tomas7L`. En la herramienta Bash, las ediciones de C# van con
scripts de Python escritos al scratchpad: los heredocs se rompen con los apóstrofes. Para `docker exec` desde Git Bash,
`MSYS_NO_PATHCONV=1`. La revisión de gentle-ai necesita `--untracked-scope=select`, el hash del inventario y un
`--intended-untracked` por cada archivo nuevo.
