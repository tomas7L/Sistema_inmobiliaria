# Estado del proyecto

_Última actualización: 2026-11-12 (cierre), con `notificaciones` Phase 1 y 2 mergeadas (#115, #116) y Phase 3 escrita, sin commitear, esperando que Tomás la commitee y la pruebe a mano_

> Este archivo **apunta**, no resume. El estado real de una implementación vive en
> `openspec/changes/<change>/tasks.md`, y el porqué de cada decisión vive en Engram.
> Si este archivo y `tasks.md` se contradicen, **gana `tasks.md`**.

## En qué estamos

**Change activo: `notificaciones`**, en `openspec/changes/notificaciones/`. `tasks.md` tiene el avance. `mora` y
`gestion-de-alquileres` siguen abiertos, esperando las pantallas de Mateo.

## Próximos pasos

1. **`notificaciones` Phase 3: escrita, verificada y revisada por gentle-ai, SIN COMMITEAR, en el árbol de trabajo de
   `ramatomy`** (16 rutas: `git status --porcelain`). El commit y el PR ya se le entregaron a Tomás en el chat; si se
   perdieron, se rearman desde `tasks.md` (Phase 3, Deviations e Issues found). Tomás commitea, abre el PR y mergea.
2. **Prueba a mano de Tomás, `tasks.md` 3.4 y 3.7:** con Supabase PRUEBA despausada y `20261112160850_AddTenantNotices`
   aplicada (H.3 de `gestion-de-alquileres/tasks.md`, todavía pendiente), un contrato de prueba con su correo y deuda.
   Como Admin: Avisos → guardar un Gmail de prueba y su contraseña de aplicación → ver `SistemaInmobiliaria/Gmail` en el
   Administrador de credenciales de Windows → Enviar avisos → comparar el mail con la Caja → probar la contraseña con espacios.
   El resultado se anota en 3.4 y 3.7.
3. **Phase 4 de `notificaciones`, S12 (Pantallas) y M2 de `mora`:** cuando Mateo mande las pantallas. Phase 4 incluye el
   texto final del mail (los centavos ",5"). Lo que S12 tiene que levantar de S9 a S11 está en las notas 9.x a 11.x de
   `gestion-de-alquileres/tasks.md`.
4. **Panorama de lo que falta:** Engram `plan/remaining-work-panorama`. `carga-inicial` espera el Excel de la reunión con
   Nicolás. El change que revoca los permisos de la API de Supabase (ver Pendientes) va antes de entregar.

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
- **Permisos de más en producción, verificado el 2026-11-12:** las 23 tablas, `app_users` incluida, tienen todos los
  permisos para los roles de la API de Supabase (`anon`, `authenticated`, `service_role`), y ninguna tiene seguridad por fila.
  **Hoy no se pueden usar:** la Data API está apagada en producción y en prueba (Tomás lo comprobó). **Nunca prenderla.**
  Arreglo definitivo, antes de entregar: un change propio que revoque tablas y permisos por defecto. Detalle en
  `openspec/changes/notificaciones/design.md`, Finding F2.
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
| **Docker** | **No** para la prueba a mano. **Sí** para la segunda inspección con la suite completa, y para cualquier slice de código (Testcontainers `postgres:17.6`) |
| **Supabase** | **Sí**, PRUEBA despausada, para aplicar `AddTenantNotices` y mandar el aviso real; solo Tomás se conecta |
| **Gmail** | Una cuenta de prueba con verificación en dos pasos y una contraseña de aplicación; la carga Tomás, el asistente nunca la ve |
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

Esperado, con Phase 3 de `notificaciones` en el árbol de trabajo: **2211 pasados (Domain 737, Presentation 553,
Infrastructure 921), 0 fallas, 0 omitidos**, build sin advertencias y el modelo en sintonía con
`20261112160850_AddTenantNotices`. Después del merge de Phase 3, `develop` debe dar lo mismo. Si el build de Desktop falla con MSB3027, la aplicación está
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
| Qué se está construyendo | `openspec/changes/notificaciones/tasks.md`, `design.md` y `spec.md` |
| Qué falta de alquileres | `openspec/changes/gestion-de-alquileres/spec.md` y `proposal.md` |
| Qué existe y qué falta | `openspec/changes/gestion-de-alquileres/exploration.md` |
| Reglas de lo que falta | `openspec/specs/lease-contract`, `party-registry`, `unit-registry`, `economic-index`, `rent-adjustment`, `contract-account` |
| Lo que dijo el dueño | `openspec/domain/respuestas-del-dueno.md` |
| Formato de spec a seguir | `openspec/changes/archive/2026-11-04-gestion-de-usuarios/spec.md` |

### Herramientas

gentle-ai **4.3.1** (en Windows se actualiza con `go install github.com/gentleman-programming/gentle-ai/v4/cmd/gentle-ai@<versión>`
y después `gentle-ai sync`), engram **3.6.2** (backup en `~/.engram/backup-20261104-pre-3.6.2/`), Docker 29.9.3 con
`postgres:17.6`, `gh` 2.102.0 en `C:\Program Files\GitHub CLI\gh.exe`, logueado como `tomas7L`. En la herramienta Bash, las
ediciones de C# van con scripts de Python escritos al scratchpad: los heredocs se rompen con los apóstrofes. Para
`docker exec` desde Git Bash, `MSYS_NO_PATHCONV=1`. La revisión de gentle-ai necesita `--untracked-scope=select`, el hash del
inventario y un `--intended-untracked` por cada archivo nuevo. Un slice grande no entra entero: se revisa en un clon temporal, primero el código sin tests ni documentos y después los tests encima (`tasks.md`, Phase 4, punto 4.9).
