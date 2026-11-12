# Estado del proyecto

_Última actualización: 2026-11-11 (cierre), con `mora` mergeado hasta la tarea 2.0 (#111) y la exploración de `notificaciones` escrita_

> Este archivo **apunta**, no resume. El estado real de una implementación vive en
> `openspec/changes/<change>/tasks.md`, y el porqué de cada decisión vive en Engram.
> Si este archivo y `tasks.md` se contradicen, **gana `tasks.md`**.

## En qué estamos

**Change activo: `mora`**, en `openspec/changes/mora/`. La propuesta entró con el PR #108. `spec.md` y `design.md` entraron con el
PR #109; `tasks.md` tiene el avance. `gestion-de-alquileres` sigue abierto, esperando S12;
S11 entró con el PR #107.

## Próximos pasos

1. **`mora` está completo hasta donde no depende de Mateo** (M1 en #110, la tarea 2.0 en #111). El resto de Phase 2 de
   `openspec/changes/mora/tasks.md`, desde 2.1, va con sus pantallas.
2. **S12 (Pantallas) y M2 de `mora`:** cuando Mateo mande las pantallas. Lo que S12 tiene que levantar de S9 a S11 está en las
   notas 9.x a 11.x de `gestion-de-alquileres/tasks.md`.
3. **`notificaciones`: exploración y propuesta escritas** (`openspec/changes/notificaciones/`). Tomás decidió el 2026-11-12
   que la opción C se hace sí o sí; lo siguiente es la spec. Si no están en `develop`, Tomás todavía no las commiteó.
4. **Panorama de lo que falta:** armado el 2026-11-11, en Engram `plan/remaining-work-panorama`. Lo próximo que no espera a
   Mateo es `carga-inicial`, que espera el Excel de la reunión con Nicolás.

## Pendientes / bloqueos

- **El botón de Traer índices todavía no se apretó en una pantalla:** no hay vista hasta S12, `tasks.md` 12.3b. Se probó contra las fuentes reales con un programa temporal.
- **Precio para la reunión con Nicolás:** sin decidir. Lo discutido está en Engram, sesión del 2026-11-05.
- **Reunión con Nicolás: la semana del 2026-11-16** (Tomás, 2026-11-11). Lo que queda para esa reunión: la copia del Excel, si
  no llega antes, el logo en vector, el precio y las preguntas de abajo. Nada de `mora` la espera.
- **Para preguntarle a Nicolás en la reunión:** N1 a N4 de `gestion-de-alquileres/spec.md` (N1 y N2 confirman valores que la spec
  dejó supuestos), N5 a N11 de `notificaciones/exploration.md` (N11: si ve viable el envío automático sin apretar nada), el
  Excel, el logo, el precio, Supabase Pro y qué priorizar entre avisos, contratos firmados y liquidación al dueño.
  **Antes de la reunión, el asistente le muestra a Tomás todas las preguntas, con cómo hacerlas y por qué importa cada una**,
  para que las estudie (Tomás, 2026-11-12).
- **Producción no tiene backups:** el plan gratis de Supabase no hace backups automáticos. Pro (USD 25/mes) guarda uno por día
  durante 7 días; recomendado con convicción fuerte, para llevar a la charla de precio. Detalle en Engram
  `decisions/notificaciones-c-and-pro`.
- **Falta decidir cómo se cargan los contratos anteriores al sistema en cantidad.** Depende de lo que traiga el Excel:
  `openspec/domain/respuestas-del-dueno.md`, punto 9, y Engram `decisions/carga-inicial-own-change`.
- **Al instalar la próxima versión, todas las PC de la inmobiliaria se actualizan juntas** (`tasks.md`, H.5).
- **`carga-inicial` tiene que rechazar saldos iniciales negativos:** `tasks.md`, Phase 5, punto 5.11.
- **Mateo diseña las pantallas por su cuenta**, sin fecha acordada. La lógica no lo espera. Su maqueta se revisa antes de
  mergear: el repo es público y no pueden entrar datos reales. Sus dudas de Índices y Ajustes ya tienen respuesta en Engram
  `decisions/mockup-indices-ajustes`; que baje `develop`. **Su pantalla de Índices tiene que mostrar el registro de correcciones** (spec Decisión 42).
  El 2026-11-07 se le respondió la segunda tanda (historial, honorarios contractuales, día de vencimiento, propietarios, fecha de corte, textos).
- **`C:\Users\buffa\Downloads\Del Lago Alquileres-PantallasPrueba.html` tiene datos reales de inquilinos:** nunca publicarlo ni
  commitearlo. La que se le pasa a Mateo es `C:\Users\buffa\Downloads\Del Lago Maqueta Estilo D.html`, con datos ficticios y una
  copia centrada del logo que Nicolás no aprobó.
- **Antes de entregar:** comprobar una vez en producción los rechazos de login. Producción es
  `bwiqgfhokolndygqkchp`, `aws-0-sa-east-1`.
- **El registro de errores de la versión en producción puede contener contraseñas** de antes de #81 (design F5). Conviene
  revisar y borrar `%LOCALAPPDATA%\SistemaInmobiliaria\logs` en la PC de la inmobiliaria al instalar.
- **Una conexión caída entre el login y la apertura de la ventana principal** todavía cierra la app (`App.xaml.cs:220`).
  Quedó sin tocar: la ventana es muy corta y todavía no hay nada escrito que perder.
- **`engram doctor`** marca `sync_target_closed_space` y `sync_mutation_required_fields` (102 memorias viejas sin título):
  solo afectarían a la sincronización con la nube, que nunca se configuró.
- **El `appsettings.json` compilado apunta a la base de PRUEBA** (`chygrzoqmcqpncogeiuz`, `aws-1-sa-east-1`), solo en
  `src/Inmobiliaria.Desktop/bin/Release/net10.0-windows/appsettings.json` (ignorado por git). Lo que trae el repo es producción.
  **Si git toca el `appsettings.json` del repo, aunque sea un merge que no lo cambia de contenido, el build pisa el compilado con producción.** Antes de abrir la app, comprobar con `grep` a qué base apunta.
- **Base de prueba:** usuario Admin `tomas`, el contrato ficticio "Maria/Juan Prueba" y el Empleado `muñoz`, desactivado.
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
| **Docker** | **Sí** para cualquier slice de código: S12, M2 de `mora`, `carga-inicial` (Testcontainers `postgres:17.6`). **No** para armar la lista de preguntas |
| **Supabase** | **No** para lo próximo (nada lleva migración); las bases se pausan solas, y solo Tomás se conecta |
| **Límite diario** | Tomás pasa el porcentaje al arrancar; el verificador y la revisión de gentle-ai son lo más caro |

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

Esperado: **2133 pasados, 0 fallas, 0 omitidos**, build sin advertencias y el modelo en sintonía con
`20261107213120_HardenClearMustChangePassword`. Si el build de Desktop falla con MSB3027, la aplicación está
abierta y tiene tomadas sus DLL: cerrarla.

**Aplicar una migración** (Tomás, en una ventana de PowerShell aparte, nunca con `!`): `tasks.md` H.3 y H.4; variable `INMOBILIARIA_DB` con
`Host=aws-1-sa-east-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.chygrzoqmcqpncogeiuz;Password=...;SSL Mode=Require` para PRUEBA y
`aws-0-sa-east-1` con `postgres.bwiqgfhokolndygqkchp` para producción; la contraseña se pide con `Read-Host -AsSecureString`; después `dotnet ef database update -p src/Inmobiliaria.Infrastructure -s src/Inmobiliaria.Infrastructure`.

**Leer la salida por el contador de errores o el código de salida, nunca por un patrón.** En esta
máquina dotnet responde en español ("Superado", "Con error", "Omitido").

### Archivos clave para abrir primero

| Para qué | Dónde |
|---|---|
| Cómo trabajamos | `CLAUDE.md` (raíz) |
| Qué se va a construir | `openspec/changes/gestion-de-alquileres/spec.md` y `proposal.md` |
| Qué existe y qué falta | `openspec/changes/gestion-de-alquileres/exploration.md` |
| Reglas de lo que falta | `openspec/specs/lease-contract`, `party-registry`, `unit-registry`, `economic-index`, `rent-adjustment`, `contract-account` |
| Lo que dijo el dueño | `openspec/domain/respuestas-del-dueno.md` |
| Formato de spec a seguir | `openspec/changes/archive/2026-11-04-gestion-de-usuarios/spec.md` |

### Herramientas

gentle-ai **4.3.1** (en Windows se actualiza con `go install github.com/gentleman-programming/gentle-ai/v4/cmd/gentle-ai@<versión>`
y después `gentle-ai sync`), engram **3.6.2** (backup en `~/.engram/backup-20261104-pre-3.6.2/`), Docker 29.9.2 con
`postgres:17.6`, `gh` 2.102.0 en `C:\Program Files\GitHub CLI\gh.exe`, logueado como `tomas7L`. En la herramienta Bash, las
ediciones de C# van con scripts de Python escritos al scratchpad: los heredocs se rompen con los apóstrofes. Para
`docker exec` desde Git Bash, `MSYS_NO_PATHCONV=1`. La revisión de gentle-ai necesita `--untracked-scope=select`, el hash del
inventario y un `--intended-untracked` por cada archivo nuevo. Un slice grande no entra entero: se revisa en un clon temporal, primero el código sin tests ni documentos y después los tests encima (`tasks.md`, Phase 4, punto 4.9).
