# Estado del proyecto

_Última actualización: 2026-11-04, con `gestion-de-usuarios` archivado y el PR #87 trabado en GitHub_

> Este archivo **apunta**, no resume. El estado real de una implementación vive en
> `openspec/changes/<change>/tasks.md`, y el porqué de cada decisión vive en Engram.
> Si este archivo y `tasks.md` se contradicen, **gana `tasks.md`**.

## En qué estamos

**No hay change activo.** El último archivado está en `openspec/changes/archive/2026-11-04-gestion-de-usuarios/`
(pendientes en `archive-report.md`, "Follow-ups Carried Over"; el login sin red de esa lista ya se resolvió en #86).
Hallazgo a explorar, en Engram: `discovery/missing-contract-and-adjustment-screens`.

## Próximos pasos

1. **Reiniciar Claude Code**: la actualización de engram desconectó su MCP de la sesión anterior.
2. **Destrabar el PR #87** (`docs: record how to recognize port exhaustion in test runs`) y mergearlo. La corrida
   `41861554205` quedó colgada en GitHub con todos sus pasos terminados y bien; la `41864392486` estaba en cola.
3. **Tomás le manda el mensaje a Nicolás el jueves 2026-11-05 temprano.** El texto final está en Engram:
   `mensaje-nicolas/excel`.
4. **Exploración de solo lectura** de las pantallas que faltan (contratos, personas, propiedades, ajustes de alquiler)
   y después una propuesta. Tomás la pidió para después de comer; la carga inicial depende de lo que conteste Nicolás.

## Pendientes / bloqueos

- **Falta decidir cómo se cargan los contratos.** El plan era importarlos del Excel (Engram:
  `decisions/carga-inicial-own-change`), pero `openspec/domain/respuestas-del-dueno.md`, punto 9, lo describe solo como la
  deuda de cada inquilino, y por inquilino, no por contrato.
- **Antes de entregar:** comprobar una vez en producción los rechazos de login. Producción es
  `bwiqgfhokolndygqkchp`, `aws-0-sa-east-1`.
- **El registro de errores de la versión en producción puede contener contraseñas** de antes de #81 (design F5). Conviene
  revisar y borrar `%LOCALAPPDATA%\SistemaInmobiliaria\logs` en la PC de la inmobiliaria al instalar.
- **Una conexión caída entre el login y la apertura de la ventana principal** todavía cierra la app (`App.xaml.cs:220`).
  Quedó sin tocar: la ventana es muy corta y todavía no hay nada escrito que perder.
- **gentle-ai sync** dejó sin actualizar 8 agentes (`~/.claude/agents/jd-*` y `review-*`) porque no pudo comprobar de quién
  son. Sin probar con 4.3.1: la primera revisión dirá si afecta.
- **`engram doctor`** marca `sync_target_closed_space` y `sync_mutation_required_fields` (102 memorias viejas sin título):
  solo afectarían a la sincronización con la nube, que nunca se configuró.
- **El `appsettings.json` compilado apunta a la base de PRUEBA** (`chygrzoqmcqpncogeiuz`, `aws-1-sa-east-1`), solo en
  `src/Inmobiliaria.Desktop/bin/Release/net10.0-windows/appsettings.json` (ignorado por git). Lo que trae el repo es producción.
- **Base de prueba:** usuario Admin `tomas`, el contrato ficticio "Maria/Juan Prueba" y el Empleado `muñoz`, desactivado.
- **Registro de errores en la PC:** `%LOCALAPPDATA%\SistemaInmobiliaria\logs\errores-yyyy-MM-dd.log`.
- **Datos personales, nunca al repo:** el Excel de Nicolás y el PDF de muestra (`C:\Users\buffa\Downloads\abregu.pdf`). Las
  copias del Excel se borran al terminar la carga: Descargas, papelera y el chat donde llegue.
  Logo original en `C:\Users\buffa\Downloads\LogoDelLago.jpg`; el del repo es `src/Inmobiliaria.Desktop/Assets/agency-logo.png`.
- **La impresora no la toma la PC nueva de Tomás** (drivers de Windows, no del sistema).
- **Defecto conocido, no arreglado:** comentario de `src/Inmobiliaria.Domain/Leasing/RentAdjustment.cs:76`.
- **La rama `ramateo`** sigue muy atrás de `develop`, con un stash viejo.
- **Imagen de Docker sobrante:** `mcr.microsoft.com/dotnet/sdk:10.0`, se puede borrar.

## Precondiciones del próximo paso

| | |
|---|---|
| **Docker** | No para la exploración; **sí** cuando se programe algo (Testcontainers `postgres:17.6`) |
| **Supabase** | No para la exploración; la base de PRUEBA se pausa sola, y solo Tomás se conecta |

## Para retomar

```bash
# La segunda inspección
git fetch --all && git status --porcelain
git rev-list --left-right --count HEAD...origin/develop
git diff --stat HEAD origin/develop
gh pr view 87 --json state,mergeStateStatus

# La suite completa, con Docker PRENDIDO
dotnet build Inmobiliaria.sln --configuration Release
dotnet test Inmobiliaria.Core.slnf --configuration Release

# El modelo de EF sigue coincidiendo con la última migración
dotnet ef migrations has-pending-model-changes -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure
```

Esperado: **1013 pasados, 0 fallas, 0 omitidos**, build sin advertencias y el modelo en sintonía con
`20261025005832_FixClearMustChangePasswordIdentity`. Si el build de Desktop falla con MSB3027, la aplicación está
abierta y tiene tomadas sus DLL: cerrarla.

**Leer la salida por el contador de errores o el código de salida, nunca por un patrón.** En esta
máquina dotnet responde en español ("Superado", "Con error", "Omitido").

### Archivos clave para abrir primero

| Para qué | Dónde |
|---|---|
| Cómo trabajamos | `CLAUDE.md` (raíz) |
| Las pantallas que existen | `src/Inmobiliaria.Presentation/Shell/ShellViewModel.cs`, `NavigationEntry.cs` |
| Reglas de lo que falta | `openspec/specs/lease-contract`, `party-registry`, `unit-registry`, `economic-index`, `rent-adjustment` |
| Lo que dijo el dueño | `openspec/domain/respuestas-del-dueno.md` (punto 9, el Excel) |
| Formato de spec a seguir | `openspec/changes/archive/2026-11-04-gestion-de-usuarios/spec.md` |

### Herramientas

gentle-ai **4.3.1** (en Windows se actualiza con `go install github.com/gentleman-programming/gentle-ai/v4/cmd/gentle-ai@<versión>`
y después `gentle-ai sync`), engram **3.6.2** (backup en `~/.engram/backup-20261104-pre-3.6.2/`), Docker 29.9.2 con
`postgres:17.6`, `gh` 2.102.0 en `C:\Program Files\GitHub CLI\gh.exe`, logueado como `tomas7L`. En la herramienta Bash, las
ediciones de C# van con scripts de Python escritos al scratchpad: los heredocs se rompen con los apóstrofes. Para
`docker exec` desde Git Bash, `MSYS_NO_PATHCONV=1`. La revisión de gentle-ai necesita `--untracked-scope=select`, el hash del
inventario y un `--intended-untracked` por cada archivo nuevo.
