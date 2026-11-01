# Estado del proyecto

_Última actualización: 2026-11-01, con la propuesta de `gestion-de-usuarios` aprobada_

> Este archivo **apunta**, no resume. El estado real de una implementación vive en
> `openspec/changes/<change>/tasks.md`, y el porqué de cada decisión vive en Engram.
> Si este archivo y `tasks.md` se contradicen, **gana `tasks.md`**.

## En qué estamos

**Change activo: `gestion-de-usuarios`** (pantalla de usuarios, solo Admin). **Propuesta aprobada**
2026-11-01 (decisiones 4 y 6 de `proposal.md` cerradas con Tomás). Hechos: `exploration.md` y `proposal.md`
en `openspec/changes/gestion-de-usuarios/`. Todavía no hay spec, diseño ni `tasks.md`. Engram:
`sdd/gestion-de-usuarios/explore`, `sdd/gestion-de-usuarios/proposal`, `sdd/gestion-de-usuarios/reactivation`.

## Próximos pasos

1. **Spec** (un solo archivo, `openspec/changes/gestion-de-usuarios/spec.md`) y **diseño**, después `tasks.md`.
2. **Change después: carga inicial desde el Excel de Nicolás.** Tomás se lo pide el **miércoles 2026-11-04**;
   el asistente le ayuda a armar el mensaje, lo lee desde fuera del repo y arma una lista corta de preguntas.

## Pendientes / bloqueos

- La rama `ramatomy` está 2 commits detrás de `origin/develop` (solo los merges #77 y #78, sin diferencia de
  contenido).
- **engram 3.6.0**: backup previo en `~/.engram/backup-20261025-pre-3.6.0/`. `engram doctor` marca
  `sync_target_closed_space` (sync a la nube nunca configurado): no afecta la memoria local.
- **El `appsettings.json` compilado apunta a la base de PRUEBA** (`chygrzoqmcqpncogeiuz`, `aws-1-sa-east-1`),
  solo en `src/Inmobiliaria.Desktop/bin/Release/net10.0-windows/appsettings.json` (ignorado por git). Producción
  es `bwiqgfhokolndygqkchp`, `aws-0-sa-east-1`, que es lo que trae el repo.
- **Base de prueba:** usuario Admin `tomas` y el contrato ficticio "Maria/Juan Prueba". Para la prueba en vivo de
  esta pantalla, `tomas` tiene que crear el Empleado que después resetea.
- **Registro de errores en la PC:** `%LOCALAPPDATA%\SistemaInmobiliaria\logs\errores-yyyy-MM-dd.log`.
- **Datos personales, nunca al repo:** el Excel de Nicolás y el PDF de muestra (`C:\Users\buffa\Downloads\abregu.pdf`).
  Logo original en `C:\Users\buffa\Downloads\LogoDelLago.jpg`; el del repo es `src/Inmobiliaria.Desktop/Assets/agency-logo.png`.
- **La impresora no la toma la PC nueva de Tomás** (drivers de Windows, no del sistema).
- **Defecto conocido, no arreglado:** comentario de `src/Inmobiliaria.Domain/Leasing/RentAdjustment.cs:76`.
- **La rama `ramateo`** sigue muy atrás de `develop`, con un stash viejo.

## Precondiciones del próximo paso

| | |
|---|---|
| **Docker** | No para spec, diseño ni tareas; **sí** desde el slice S1 (Testcontainers `postgres:17.6`) |
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
| La propuesta en curso | `openspec/changes/gestion-de-usuarios/proposal.md` |
| El mapa de código con líneas | `openspec/changes/gestion-de-usuarios/exploration.md`, "Current state (evidence)" |
| Specs que se modifican | `openspec/specs/user-registry/spec.md:78-185`, `openspec/specs/desktop-shell/spec.md:32-56` |
| Formato de spec a seguir | `openspec/changes/archive/2026-10-25-ui-de-cobranza/spec.md` |

### Herramientas

gentle-ai **4.2.0**, engram **3.6.0**, Docker 29.9.2 con `postgres:17.6`, `gh` 2.102.0 en
`C:\Program Files\GitHub CLI\gh.exe`, logueado como `tomas7L`. En la herramienta Bash, las ediciones de C# van con
scripts de Python escritos al scratchpad: los heredocs se rompen con los apóstrofes. Para `docker exec` desde Git Bash,
`MSYS_NO_PATHCONV=1`. La revisión de gentle-ai necesita `--untracked-scope=select`, el hash del inventario y un
`--intended-untracked` por cada archivo nuevo.
