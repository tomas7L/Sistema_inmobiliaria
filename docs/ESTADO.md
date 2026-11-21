# Estado del proyecto

_Última actualización: 2026-11-20 (cierre), con #167 a #173 mergeados_

> Este archivo **apunta**, no resume. El estado real de una implementación vive en
> `openspec/changes/<change>/tasks.md`, y el porqué de cada decisión vive en Engram.
> Si este archivo y `tasks.md` se contradicen, **gana `tasks.md`**.

## En qué estamos

**Change activo: `gestion-de-alquileres`, Phase 12 (Pantallas).** El detalle de contrato ya tiene todas sus acciones (Registrar
aviso, Deshacer aviso, Terminar, Deshacer fin, Cancelar por error, Editar, Ver cuenta, Corregir ajuste) y la pantalla **Ajustes**
está en el menú. Detalle de cada una: `openspec/changes/gestion-de-alquileres/tasks.md`, 12.3d (entradas del 2026-11-19 y
2026-11-20). Cómo sumar una acción al detalle: Engram `pattern/contract-action-screen`.

## Próximos pasos

1. **Elegir con Tomás la próxima pantalla** de las que faltan: la vista de Mora (`openspec/changes/mora/tasks.md` 2.2 a 2.4), la
   de Avisos (`openspec/changes/notificaciones/tasks.md` 4.2) y las viejas sin tema (Caja, Cobros recientes, Anular, Avisos,
   Puesta en marcha, Usuarios). Cada pantalla redibujada suma su `NavigationTarget` al disparador del nombre de usuario en
   `src/Inmobiliaria.Desktop/MainWindow.xaml`, y lleva `<Grid Margin="32,20,32,16">` como raíz (Ajustes salió sin ese margen).
2. **Proceso por pantalla:** leer el `.dc.html` del lienzo, decirle a Tomás qué cambia y qué queda, él aprueba, tests primero,
   build, él la prueba en PRUEBA con capturas, entrada en `tasks.md`, textos de commit y PR.
3. **Antes de mostrar cada pantalla**, revisar `memory/wpf-screen-lessons.md`.
4. **Dos detalles vistos y no tocados** (anotados en `tasks.md`, 12.3d): la tarjeta de cláusula de un contrato cancelado dice
   que la cláusula se puede cambiar, y la columna "Fin" de un cancelado muestra el día de la cancelación, anterior a su inicio.
5. **Reunión con Nicolás:** `lectura-reunion-nicolas.md` (raíz, local), preguntas N1 a N22 que siguen abiertas.
6. **Panorama de lo que falta:** Engram `plan/remaining-work-panorama`. `carga-inicial` espera el Excel de la reunión.

## Pendientes / bloqueos

- **Nunca compilar con `--no-incremental`:** copia el `appsettings.json` del repo (producción) sobre el compilado de PRUEBA. El
  2026-11-20 la app se abrió unos segundos contra producción por eso, en el login, sin que nadie entrara. Engram
  `lessons/no-incremental-overwrites-appsettings`.
- **La Data API sigue apagada en producción y en prueba, y así se queda.** Desde el 2026-11-13 los roles de la API ya no tienen
  permisos sobre nada de `public`.
- **En producción el único usuario es `nicolas`, no `tomas`.** Tomás le cambió la contraseña al entrar el 2026-11-13, así que ya
  no se la pide al primer ingreso. **Antes de entregar, restablecerla desde Usuarios** para que Nicolás elija la suya, y no guardar
  contraseñas en el chat ni en archivos del repo.
- **Seguimiento sin urgencia del login:** la espera usa el reloj de Windows (archivo del change, `archive-report.md`).
- **La contraseña de aplicación de Gmail de la prueba** (credencial `SistemaInmobiliaria/Gmail` en esta PC): Tomás la revoca en
  su cuenta de Google cuando ya no la necesite.
- **Producción no tiene backups:** el plan gratis de Supabase no hace backups automáticos. Pro (USD 25/mes) guarda uno por día
  durante 7 días; recomendado con convicción fuerte, para llevar a la charla de precio. Detalle en Engram
  `decisions/notificaciones-c-and-pro`.
- **Falta decidir cómo se cargan los contratos anteriores al sistema en cantidad.** Depende de lo que traiga el Excel:
  `openspec/domain/respuestas-del-dueno.md`, punto 9, y Engram `decisions/carga-inicial-own-change`.
- **Al instalar la próxima versión, todas las PC de la inmobiliaria se actualizan juntas** (`tasks.md`, H.5).
- **`carga-inicial` tiene que rechazar saldos iniciales negativos:** `tasks.md`, Phase 5, punto 5.11.
- **El lienzo de Mateo** es `https://claude.ai/artifact/WDjHN85XeYCXCTxx6pv1UU`. Leerlo con la herramienta Artifact: `list` con
  `scope: files` y después `read` con `paths` de `project/<Nombre>.dc.html` (la lectura normal solo da "Loading…"). Los estilos
  están en `project/caja.css`: una clase sin regla ahí (como `bad` en "Cancelar por error") no pinta nada. Su guía en Word está
  desactualizada: manda el lienzo. Comportamiento y navegación:
  `C:\Users\buffa\Downloads\Comportamiento visual y navegación del sistema de alquileres.docx`. Engram `reference/mateo-canvas`.
- **`C:\Users\buffa\Downloads\Del Lago Alquileres-PantallasPrueba.html` tiene datos reales de inquilinos:** nunca publicarlo ni
  commitearlo. La que se le pasa a Mateo es `C:\Users\buffa\Downloads\Del Lago Maqueta Estilo D.html`, con datos ficticios y una
  copia centrada del logo que Nicolás no aprobó.
- **Producción** es `bwiqgfhokolndygqkchp`, `aws-0-sa-east-1`. El rechazo por usuario o contraseña ya se vio ahí el 2026-11-13.
- **El registro de errores de la versión en producción puede contener contraseñas** de antes de #81 (design F5). Conviene
  revisar y borrar `%LOCALAPPDATA%\SistemaInmobiliaria\logs` en la PC de la inmobiliaria al instalar.
- **Tomás:** imprimir un recibo en papel en PRUEBA para ver que el "$" entra.
- **La app se abre con `Start-Process` de PowerShell, aparte:** abierta como proceso de la terminal del asistente se cerró sola
  el 2026-11-18 (sin error en el registro ni reinicio de Windows).
- **Una conexión caída entre el login y la apertura de la ventana principal** todavía cierra la app (`App.xaml.cs:220`).
  Quedó sin tocar: la ventana es muy corta y todavía no hay nada escrito que perder.
- **`engram doctor`** marca `sync_target_closed_space` y `sync_mutation_required_fields` (102 memorias viejas sin título):
  solo afectarían a la sincronización con la nube, que nunca se configuró.
- **El `appsettings.json` compilado apunta a la base de PRUEBA** (`chygrzoqmcqpncogeiuz`, `aws-1-sa-east-1`), solo en
  `src/Inmobiliaria.Desktop/bin/Release/net10.0-windows/appsettings.json` (ignorado por git). Lo que trae el repo es producción.
  **Si git o un build `--no-incremental` toca el `appsettings.json`, el compilado queda en producción.** Antes de **cada**
  apertura: `grep -oE "chygrzoqmcqpncogeiuz|bwiqgfhokolndygqkchp"` sobre el compilado; si sale `bwiqgfhokolndygqkchp`, no abrir.
- **Base de prueba:** usuario Admin `tomas`, los Empleados `muñoz` y `muñoz20` (desactivados), y los datos ficticios cargados por
  Tomás desde el 2026-11-17. Lo que cambió el 2026-11-20: "Prueba, Maria" tiene honorarios de 7 %; "Sosa, Ana" sobre San Martín 345
  está **cancelado**; "Vera, Marta" sobre Belgrano 613 (desde 01/05/2026, ICL cada 6 meses) tiene un ajuste confirmado y una
  corrección de ese ajuste, los dos a $ 579.053 desde 12/2026. Su recargo de PRUEBA es 2 % diario, por eso las deudas se ven
  altas.
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
| **Docker** | **Sí** si la pantalla toca Infrastructure, y para la suite completa (Testcontainers `postgres:17.6`) |
| **Supabase** | **PRUEBA** solo al final de cada pantalla, cuando Tomás la prueba con capturas. Antes de abrir la app, `grep` del `appsettings.json` compilado. La lógica sin vista no la necesita |
| **Límite diario** | Tomás pasa el porcentaje al arrancar |

## Para retomar

```bash
# La segunda inspección
git fetch --all && git status --porcelain
git rev-list --left-right --count HEAD...origin/develop
git diff --stat HEAD origin/develop

# La suite completa, con Docker PRENDIDO (nunca --no-incremental)
dotnet build Inmobiliaria.sln --configuration Release
dotnet test Inmobiliaria.Core.slnf --configuration Release

# El modelo de EF sigue coincidiendo con la última migración
dotnet ef migrations has-pending-model-changes -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure
```

Esperado en `develop` (#173): **2650 pasados (Domain 867, Presentation 825, Infrastructure 958), 0 fallas, 0 omitidos**, build
sin advertencias y el modelo en sintonía con `20261113165451_RevokeSupabaseApiRoles`. Infrastructure se corrió completa con Docker
el 2026-11-20 sobre #172 (tardó 9 m 38 s, el doble de lo habitual); #173 no la toca. Si el build de Desktop falla con MSB3027,
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
| Lo hecho y lo que falta de pantallas | `openspec/changes/gestion-de-alquileres/tasks.md`, 12.3c a 12.3e |
| Las pantallas a programar | `notificaciones/tasks.md` (Phase 4), `mora/tasks.md` (Phase 2), y las viejas sin tema |
| Qué falta de alquileres | `openspec/changes/gestion-de-alquileres/spec.md` y `proposal.md` |
| Reglas de lo que falta | `openspec/specs/lease-contract`, `party-registry`, `unit-registry`, `economic-index`, `rent-adjustment`, `contract-account` |
| Lo que dijo el dueño | `openspec/domain/respuestas-del-dueno.md` |
| Formato de spec a seguir | `openspec/changes/archive/2026-11-04-gestion-de-usuarios/spec.md` |

### Herramientas

gentle-ai **4.4.0** (en Windows se actualiza con `go install github.com/gentleman-programming/gentle-ai/v4/cmd/gentle-ai@<versión>`
y después `gentle-ai sync`), engram **3.7.1** (backup en `~/.engram/backup-20261113-pre-3.7.1/`), Docker 29.9.3 con
`postgres:17.6`, `gh` 2.102.0 en `C:\Program Files\GitHub CLI\gh.exe`, logueado como `tomas7L`. En la herramienta Bash, las
ediciones de C# van con scripts de Python escritos al scratchpad con la herramienta Write: los heredocs se rompen con los
apóstrofes. Para `docker exec` desde Git Bash, `MSYS_NO_PATHCONV=1`. La revisión de gentle-ai se hace en un clon temporal con un
commit "candidate" que tiene solo el código y los tests, sin documentos (`--base-ref HEAD~1 --committed-only`); un slice grande se
parte en código primero y tests después (`gestion-de-alquileres/tasks.md`, Phase 4, punto 4.9).
