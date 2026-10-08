# Investigación: qué se miró, qué se tomó y qué no

Pregunta del usuario: *¿se puede hacer que los pawns sean más realistas y autónomos y que «jueguen solos», tomando como modelo los juegos y simulaciones de ejemplo?*
Método: lectura de código y documentación de los proyectos citados. **Límite honesto:** el proxy de esta sesión bloquea `arxiv.org` y `rimworldwiki.com`; los números de
Generative Agents salen del **código fuente** del repositorio (no del artículo), y los de RimWorld de los resultados de búsqueda de la wiki (no de la página completa). Todo
lo que no pude leer está marcado.

## 1. Fuentes y qué aportaron

| Fuente | Qué leí | Idea concreta |
|---|---|---|
| [Generative Agents (código)](https://github.com/joonspk-research/generative_agents) — `retrieve.py`, `reflect.py`, `plan.py` | Código real | **Recuperación** de memoria: puntuación = recencia×0.5 + relevancia×3 + importancia×2, cada una normalizada min-max a [0,1] (si todas iguales, 0.5), top-30; **reflexión** cuando se agota un contador de importancia (no «una vez al día»), 3 focos × hasta 5 ideas, guardadas como recuerdos con caducidad a 30 días; plan **jerárquico** (día → horas → sub-tareas ~2 h por delante); **decidir hablar** se bloquea si alguno duerme, si ya está en conversación o si la pareja tiene `chatting_with_buffer` > 0 |
| [AI Town (a16z)](https://github.com/a16z-infra/ai-town) | README | Límite de memorias recuperadas (`NUM_MEMORIES_TO_SEARCH`), pausa por inactividad, ejecutar LLM local (Ollama) por defecto, sin límites de gasto explícitos (los «palancas» son parar el motor) |
| [alife-sdk](https://github.com/eurusik/alife-sdk) | README | Actualización **con presupuesto** y reparto circular (ya lo hacemos con `agentes_pawns_tick`), pipeline ordenado (decaer → decidir → mover → moral → limpiar), **puertos y adaptadores** (ya lo hacemos: Core/Game), bus de eventos tipado, parámetros de personalidad por NPC (umbrales de pánico), lugares con capacidad y trabajos (*smart terrains*), GOAP en el núcleo |
| [Project Sid / PIANO (Altera)](https://github.com/altera-al/project-sid) y [su resumen](https://digitalhumanity.substack.com/p/project-sid-many-agent-simulations) | README + resúmenes (el PDF de arXiv está bloqueado) | Módulos **concurrentes** coherentes entre sí, módulo de *action awareness* (comparar lo esperado con lo observado), roles que **emergen**, normas colectivas que se adoptan y **cambian**, transmisión de cultura y religión, hitos de «civilización» como benchmark |
| RimWorld — [mental breaks](https://rimworldwiki.com/wiki/Mental_Break_Threshold) y [narradores](https://rimworldwiki.com/wiki/AI_storytellers) (vía resultados de búsqueda) | Extractos | Umbrales de ruptura **35 % / 20 % / 5 %** (4/7 y 1/7 del menor), tiempos medios **10 / 3 / 0.7 días**, rasgos que desplazan el umbral (−18 %…+15 %); el narrador no es azar: mide y elige el incidente (modelo: AI Director de Left 4 Dead), con *presets* de ritmo (Cassandra, Phoebe, Randy) |
| IAUS / utility AI — [Dave Mark](https://www.gameai.com/manuals/index.php/IAUS) y [wiki IAUS](https://github.com/ProjectBorealis/IAUS/wiki) (extractos) | Resúmenes | Consideraciones **multiplicadas** con curvas de respuesta, **inercia** para no oscilar entre comportamientos, añadir comportamientos sin reescribir transiciones |
| Mods LLM con BepInEx (Valheim, 7 Days to Die, Casualties) — resultados de búsqueda | Solo listados | Confirma el patrón BepInEx + Ollama; **no encontré** un mod de colonia con agentes completos al estilo Pecera (no afirmo que no exista) |

## 2. Qué se adoptó (todo en `Pecera.Core`, con tests y simulador)

| Idea de la fuente | Implementación | Resultado medido en el simulador (24 pawns, 360 días, 4 semillas) |
|---|---|---|
| Recuperación recencia/relevancia/importancia | `Memoria.Recupera` (relevancia por solapamiento de palabras; incluye resúmenes) y úsala la voz del juego | Test: la relevante gana a la reciente; la importante desempata |
| Reflexión por **importancia acumulada** | `Memoria.ToqueReflexion` + `Agente.Insights` (conclusiones guardadas como recuerdos de peso 8) | ~470–520 reflexiones/año (≈ una cada 17 días por pawn), 650–720 conclusiones |
| Planes jerárquicos | `Guiones` (casarse = charlar → cortejar → celebrar → pedir…), empiezan donde está la relación, avanzan con paso exacto y ≥ 2 días entre pasos, se rehacen si el objetivo desaparece o se estancan (15 d) | Las ambiciones dejaron de cumplirse en días: **16–18/año** (antes 80–140) |
| `chatting_with_buffer` | `FrenoConversacion` (2 días por pareja) | Evita bucles A↔B |
| Rupturas de RimWorld | `AnimoCalc`, `Rupturas` (umbrales 35/20/5, tiempos medios 10/3/0.7, neuroticismo desplaza ±0.15) + inspiración (animo > 0.85, media 15 d) | Test Monte-Carlo reproduce 9.5 % / 28.3 % / 76 % por día; en la simulación 2–3.5 % de pawns-día bajo el umbral, **13–34 rupturas/año** |
| Narrador / AI Director | `Director` (3 estilos, curva objetivo, mide tensión con 5 componentes, enfriamiento, banda ±0.12) | Tensión media **0.42–0.45** frente a objetivo medio 0.45; **20–24 %** de días a > 0.2 de la curva; mezcla equilibrada drama (≈ 37) / alivio (≈ 33) |
| Normas que se adoptan y cambian (Sid) | `Normas`: paz pública, ojo por ojo, hospitalidad; apoyo ponderado por líderes, histéresis 0.55/0.40, vigencia mínima 90 d, incompatibles entre sí | 2–3 aprobadas y 0–1 derogadas por año (sin parpadeo) |
| Roles emergentes (Sid) | `Roles.De` a partir de lo HECHO | Aparecen, pero ver limitación 3 |
| IAUS: curvas e inercia | `Curvas.Logistica` para urgencias; los duplicados refuerzan en vez de reiniciar | Necesidad social media 0.67–0.78 (antes 0.3–0.5) |
| *Action awareness* (Sid) | `Mente.Observa`: una intención se da por hecha solo cuando el juego produce el evento esperado | Evidencia `agentes_intencion_observada` (pendiente de partida) |
| Presupuesto de LLM / pausa (AI Town) | `PresupuestoLlm` por prioridad, `SaludLlm`, una sola hebra | 1 llamada de planificación/día; máximo 4 en un día incluyendo texto de conversación |

## 3. Limitaciones y artefactos (no los vendo como logros)
1. Las constantes del mundo son **supuestos del simulador**; valen para comprobar que la lógica no se atasca ni oscila, no para predecir Noble Fates.
2. La relevancia de recuerdos usa **palabras**, no embeddings. Siguiente paso: `POST /api/embeddings` de Ollama con un modelo pequeño (mide VRAM antes) y cachear el vector por recuerdo.
3. **Roles:** en el simulador casi todos acaban «mediador» (≈ 20 de 24) porque consolar/celebrar son las intenciones más baratas de completar; en el juego dependerá de qué se pueda ejecutar de verdad. Es un artefacto de la economía de intenciones del simulador.
4. Ruptura de tipo *Hundimiento* y *Arrebato* casi no aparecen (los ánimos del simulador son sanos); el modelo está probado por Monte-Carlo pero no «vivido».
5. Todo lo anterior vive en el Core y en el simulador. En el juego solo está cableado **sin escribir**: recuperación de memoria en la voz, reflexión, normas y director (sugerencias en `agentes.md`), ánimo (informativo). Las rupturas se aplican solo en el simulador hasta tener las necesidades reales del pawn (A4).

## 4. Ideas de la investigación que NO se implementaron (y por qué)
| Idea | Motivo |
|---|---|
| *Smart terrains* (lugares con capacidad y trabajos) | Requiere leer zonas/edificios del juego (no hay firma confirmada) |
| Separación online/offline por distancia a la cámara | Hoy el coste ya está acotado por reparto circular; se reevalúa tras medir en partida |
| Embeddings de recuerdos | Ver limitación 2 |
| Bus de eventos tipado | El acoplamiento actual (Estado + Gancho + Mente) es manejable; no compensa el cambio todavía |
| Conversaciones multi-turno largas con LLM | Cuesta GPU y el resultado ya se decide por reglas; el texto es opcional |
| Hitos de «civilización» como benchmark | Parcialmente cubierto con roles, normas, cultura y crónica; falta definir metas medibles en el juego real |
