---
description: Retomar el proyecto después de un /clear — lee el estado, consulta la memoria y verifica el último merge
---

Ejecutá el procedimiento **"Starting a session"** de la sección *Session Continuity* del
`CLAUDE.md` de este proyecto, en este orden:

1. Leé `docs/ESTADO.md`.
2. Consultá Engram por el contexto reciente de `sistema_inmobiliaria`, según su propio protocolo
   (`mem_context`, y `mem_search` si necesitás un tema puntual).
3. Corré la **segunda inspección** de *Working Agreements*: que el último merge haya llegado
   entero, y que lo que estás por construir siga coincidiendo con el código que hay.
4. Si `docs/ESTADO.md` y `openspec/changes/<change>/tasks.md` se contradicen, **avisá antes de
   seguir** — y recordá que gana `tasks.md`.

Después reportá en **3 a 5 líneas**: dónde quedamos, qué sigue, qué está bloqueado, y **qué
precondiciones necesita el próximo paso** (Docker, Supabase despausada).

No releas el código. Abrí sólo lo que la próxima tarea necesite.
