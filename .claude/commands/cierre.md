---
description: Cerrar la sesión antes de un /clear — reescribe el estado y guarda en memoria
---

Ejecutá el procedimiento **"Closing a session"** de la sección *Session Continuity* del
`CLAUDE.md` de este proyecto:

1. Reescribí `docs/ESTADO.md` completo, respetando su plantilla y su regla: **apunta, no resume.**
   - Concreto: rutas de archivo reales, comandos reales, nombres reales.
   - Nada de copiar el estado de las tareas, las decisiones ni el historial — eso vive en
     `openspec/changes/<change>/tasks.md` y en Engram.
   - Incluí siempre la tabla de **precondiciones** del próximo paso (Docker, Supabase).
   - Actualizá la fecha.
2. Hacé el cierre de sesión que indica el protocolo de Engram (`mem_session_summary`), sumando
   cualquier decisión, bug o aprendizaje que haya quedado sin guardar.
3. Si quedó trabajo **sin commitear**, decílo explícitamente con la lista de archivos — Tomás
   commitea, no vos.

Confirmá en **dos líneas** qué guardaste, y decile que ya puede hacer `/clear`.
