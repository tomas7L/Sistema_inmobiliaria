# Estado del proyecto

_Última actualización: 2026-11-14 (cierre), con las validaciones de Personas mergeadas (#136)_

> Este archivo **apunta**, no resume. El estado real de una implementación vive en
> `openspec/changes/<change>/tasks.md`, y el porqué de cada decisión vive en Engram.
> Si este archivo y `tasks.md` se contradicen, **gana `tasks.md`**.

## En qué estamos

**Change activo: `gestion-de-alquileres`, Phase 12 (Pantallas).** Hechas: el menú (12.1, 12.2, #134), el tema y la pantalla de
Personas (#135) y sus validaciones (#136). La spec sumó las Decisiones 43 a 55 y los Requirements 26 a 30 (validación de todo lo que
escribe el operador). `limite-de-intentos-de-login` quedó probado en vivo y archivado (#133).

## Próximos pasos

1. **Tarea 12.3e, siguiente pantalla: Propiedades**, con su vista y sus validaciones (Requirement 28: provincia de una lista,
   largos máximos). Antes de mostrarla, revisar las lecciones de Engram `conventions/s12-screen-lessons`.
2. **Mateo:** Tomás le manda el mensaje del 2026-11-14: redibujar **cada pantalla que obliga a bajar** para que entre en ~950 px
   de alto (las de 1000 a 1500 px seguro, las de 900 al límite), el borde rojo en el campo con error y los textos nuevos. **El
   diseño lo cambia Mateo y nosotros lo copiamos:** un achicado propio del tema se probó y se revirtió el mismo día (Engram
   `conventions/s12-screen-fit`). Lo que conteste puede tocar `Themes/Controls.xaml` y `Views/PartiesView.xaml`.
3. **Reunión con Nicolás, la semana del 2026-11-16:** `lectura-reunion-nicolas.md` (raíz, local). Se sumaron N12 a N22 (sección C2;
   quedan ocho, Tomás respondió tres).
4. **Pendiente de la 12.3e:** alinear el control de email del buzón de avisos con la Decisión 54.
5. **Panorama de lo que falta:** Engram `plan/remaining-work-panorama`. `carga-inicial` espera el Excel de la reunión.

## Pendientes / bloqueos

- **La Data API sigue apagada en producción y en prueba, y así se queda.** Desde el 2026-11-13 los roles de la API ya no tienen
  permisos sobre nada de `public`.
- **En producción el único usuario es `nicolas`, no `tomas`.** Tomás le cambió la contraseña al entrar el 2026-11-13, así que ya
  no se la pide al primer ingreso. **Antes de entregar, restablecerla desde Usuarios** para que Nicolás elija la suya, y no guardar
  contraseñas en el chat ni en archivos del repo.
- **Seguimiento sin urgencia del login:** la espera usa el reloj de Windows (archivo del change, `archive-report.md`).
- **La contraseña de aplicación de Gmail de la prueba** (credencial `SistemaInmobiliaria/Gmail` en esta PC): Tomás la revoca en
  su cuenta de Google cuando ya no la necesite.
- **El botón de Traer índices todavía no se apretó en una pantalla:** no hay vista hasta S12, `tasks.md` 12.3b. Se probó contra las fuentes reales con un programa temporal.
- **Producción no tiene backups:** el plan gratis de Supabase no hace backups automáticos. Pro (USD 25/mes) guarda uno por día
  durante 7 días; recomendado con convicción fuerte, para llevar a la charla de precio. Detalle en Engram
  `decisions/notificaciones-c-and-pro`.
- **Falta decidir cómo se cargan los contratos anteriores al sistema en cantidad.** Depende de lo que traiga el Excel:
  `openspec/domain/respuestas-del-dueno.md`, punto 9, y Engram `decisions/carga-inicial-own-change`.
- **Al instalar la próxima versión, todas las PC de la inmobiliaria se actualizan juntas** (`tasks.md`, H.5).
- **`carga-inicial` tiene que rechazar saldos iniciales negativos:** `tasks.md`, Phase 5, punto 5.11.
- **El lienzo de Mateo** es `https://claude.ai/artifact/WDjHN85XeYCXCTxx6pv1UU`. Leerlo con la herramienta Artifact: `list` con
  `scope: files` y después `read` con `paths` de `project/<Nombre>.dc.html` (la lectura normal solo da "Loading…"). Son 80
  pantallas; las oscuras tienen el mismo texto que las claras. **Contar los archivos antes de decir que se revisó todo.** Su guía
  en Word está desactualizada respecto de las últimas siete correcciones: manda el lienzo. Comportamiento y navegación:
  `C:\Users\buffa\Downloads\Comportamiento visual y navegación del sistema de alquileres.docx`. Engram `reference/mateo-canvas`.
- **`C:\Users\buffa\Downloads\Del Lago Alquileres-PantallasPrueba.html` tiene datos reales de inquilinos:** nunca publicarlo ni
  commitearlo. La que se le pasa a Mateo es `C:\Users\buffa\Downloads\Del Lago Maqueta Estilo D.html`, con datos ficticios y una
  copia centrada del logo que Nicolás no aprobó.
- **Producción** es `bwiqgfhokolndygqkchp`, `aws-0-sa-east-1`. El rechazo por usuario o contraseña ya se vio ahí el 2026-11-13.
- **El registro de errores de la versión en producción puede contener contraseñas** de antes de #81 (design F5). Conviene
  revisar y borrar `%LOCALAPPDATA%\SistemaInmobiliaria\logs` en la PC de la inmobiliaria al instalar.
- **Una conexión caída entre el login y la apertura de la ventana principal** todavía cierra la app (`App.xaml.cs:220`).
  Quedó sin tocar: la ventana es muy corta y todavía no hay nada escrito que perder.
- **`engram doctor`** marca `sync_target_closed_space` y `sync_mutation_required_fields` (102 memorias viejas sin título):
  solo afectarían a la sincronización con la nube, que nunca se configuró.
- **El `appsettings.json` compilado apunta a la base de PRUEBA** (`chygrzoqmcqpncogeiuz`, `aws-1-sa-east-1`), solo en
  `src/Inmobiliaria.Desktop/bin/Release/net10.0-windows/appsettings.json` (ignorado por git). Lo que trae el repo es producción.
  **Si git toca el `appsettings.json` del repo, aunque sea un merge que no lo cambia de contenido, el build pisa el compilado con producción.** Antes de abrir la app, comprobar con `grep` a qué base apunta.
- **Base de prueba:** usuario Admin `tomas`, el contrato ficticio "Maria/Juan Prueba" y los Empleados `muñoz` y `muñoz20`,
  los dos desactivados.
- **Registro de errores en la PC:** `%LOCALAPPDATA%\SistemaInmobiliaria\logs\errores-yyyy-MM-dd.log`.
- **Datos personales, nunca al repo:** el Excel de Nicolás y el PDF de muestra (`C:\Users\buffa\Downloads\abregu.pdf`). Las
  copias del Excel se borran al terminar la carga: Descargas, papelera y el chat donde llegue.
  Logo original en `C:\Users\buffa\Downloads\LogoDelLago.jpg`; el del repo es `src/Inmobiliaria.Desktop/Assets/agency-logo.png`.
- **La impresora no la toma la PC nueva de Tomás** (drivers de Windows, no del sistema).
- **Defecto conocido, no arreglado:** comentario de `src/Inmobiliaria.Domain/Leasing/RentAdjustment.cs:76`.
- **Imagen de Docker sobrante:** `mcr.microsoft.com/dotnet/sdk:10.0`, se puede borrar.


## Precondiciones del próximo paso

| | |
|---|---|
| **Docker** | **Sí**, para la suite completa (Testcontainers `postgres:17.6`) |
| **Supabase** | **PRUEBA** solo al final de cada pantalla, cuando Tomás la prueba con capturas. Antes de abrir la app, `grep` del `appsettings.json` compilado |
| **Límite diario** | Tomás pasa el porcentaje al arrancar |

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

Esperado en `develop` (#136): **2297 pasados (Domain 783, Presentation 581, Infrastructure 933), 0 fallas, 0 omitidos**, build
sin advertencias y el modelo en sintonía con `20261113165451_RevokeSupabaseApiRoles`. Si el build de Desktop falla con MSB3027,
la aplicación está abierta y tiene tomadas sus DLL: cerrarla. La app la abre siempre el asistente (Tomás, 2026-11-13).

**Aplicar una migración** (Tomás, en una ventana de PowerShell aparte, nunca con `!`): variable `INMOBILIARIA_DB` con
`Host=aws-1-sa-east-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.chygrzoqmcqpncogeiuz;Password=...;SSL Mode=Require` para PRUEBA y
`aws-0-sa-east-1` con `postgres.bwiqgfhokolndygqkchp` para producción; la contraseña se pide con `Read-Host -AsSecureString`; después `dotnet ef database update -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure`.

**Leer la salida por el contador de errores o el código de salida, nunca por un patrón.** En esta
máquina dotnet responde en español ("Superado", "Con error", "Omitido").

### Archivos clave para abrir primero

| Para qué | Dónde |
|---|---|
| Cómo trabajamos | `CLAUDE.md` (raíz) |
| Lo último cerrado | `openspec/changes/archive/2026-11-14-limite-de-intentos-de-login/archive-report.md` |
| Las pantallas a programar | `gestion-de-alquileres/tasks.md` (S12: 12.3d, 12.3e), `notificaciones/tasks.md` (Phase 4), `mora/tasks.md` (Phase 2) |
| Qué falta de alquileres | `openspec/changes/gestion-de-alquileres/spec.md` y `proposal.md` |
| Reglas de lo que falta | `openspec/specs/lease-contract`, `party-registry`, `unit-registry`, `economic-index`, `rent-adjustment`, `contract-account` |
| Lo que dijo el dueño | `openspec/domain/respuestas-del-dueno.md` |
| Formato de spec a seguir | `openspec/changes/archive/2026-11-04-gestion-de-usuarios/spec.md` |

### Herramientas

gentle-ai **4.4.0** (en Windows se actualiza con `go install github.com/gentleman-programming/gentle-ai/v4/cmd/gentle-ai@<versión>`
y después `gentle-ai sync`), engram **3.7.1** (backup en `~/.engram/backup-20261113-pre-3.7.1/`), Docker 29.9.3 con
`postgres:17.6`, `gh` 2.102.0 en `C:\Program Files\GitHub CLI\gh.exe`, logueado como `tomas7L`. En la herramienta Bash, las
ediciones de C# van con scripts de Python escritos al scratchpad: los heredocs se rompen con los apóstrofes. Para
`docker exec` desde Git Bash, `MSYS_NO_PATHCONV=1`. La revisión de gentle-ai se hace en un clon temporal con un commit "candidate"
que tiene solo el código y los tests, sin documentos (`--base-ref HEAD~1 --committed-only`); un slice grande se parte en código
primero y tests después (`gestion-de-alquileres/tasks.md`, Phase 4, punto 4.9).
