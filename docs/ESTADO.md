# Estado del proyecto

_Última actualización: 2026-11-10, con S11 terminada, verificada y revisada, entregada sin commitear_

> Este archivo **apunta**, no resume. El estado real de una implementación vive en
> `openspec/changes/<change>/tasks.md`, y el porqué de cada decisión vive en Engram.
> Si este archivo y `tasks.md` se contradicen, **gana `tasks.md`**.

## En qué estamos

**Change activo: `gestion-de-alquileres`**, en `openspec/changes/gestion-de-alquileres/`. Tiene `exploration.md`, `proposal.md`
`spec.md`, `design.md` y `tasks.md`, que tiene el avance real. La spec cambió cosas de la propuesta: leer sus Findings.

## Próximos pasos

1. **Tomás commitea S11 y abre el PR**, junto con el commit de las notas de M7 que ya hizo (archivos, commit y PR en el último mensaje
   de la sesión del 2026-11-10). S11 no lleva migración.
2. **S12 (Pantallas)** espera las maquetas de Mateo (`tasks.md`, Phase 12). Si se demora, Tomás puede elegir vistas simples para
   algunas pantallas y reestilizarlas después (proposal §7). Lo que S12 tiene que levantar de S9 a S11 está anotado en las notas 9.x a
   11.x de `tasks.md` (por ejemplo, mostrar los meses de la diferencia en caja, nota 10.11).

## Pendientes / bloqueos

- **El botón de Traer índices todavía no se apretó en una pantalla:** no hay vista hasta S12, `tasks.md` 12.3b. Se probó contra las fuentes reales con un programa temporal.
- **Precio para la reunión con Nicolás:** sin decidir. Lo discutido está en Engram, sesión del 2026-11-05.
- **Esperando a Nicolás:** respondió el 2026-11-05 que está complicado y quiere verlo cuando esté más tranquilo, en unas 2 semanas.
  Lo que queda para esa reunión: la copia del Excel, si no llega antes, y el logo en vector. Nada de este change lo espera.
- **Para preguntarle a Nicolás en la reunión:** las preguntas N1 a N4 de `spec.md`. N1 y N2 (qué meses y qué día usa en cada índice)
  confirman valores que la spec dejó supuestos.
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
| **Docker** | **Sí** para S12 (Testcontainers `postgres:17.6`) |
| **Supabase** | **No** para S11 (no lleva migración); las bases se pausan solas, y solo Tomás se conecta |
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

Esperado: **1987 pasados, 0 fallas, 0 omitidos**, build sin advertencias y el modelo en sintonía con
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
