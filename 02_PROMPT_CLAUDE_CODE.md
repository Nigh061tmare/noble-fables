# PROMPT PARA CLAUDE CODE — Proyecto Pecera (Noble Fates)

Pega esto como primer mensaje en la raíz del repositorio. Después lee `01_INFORME_PECERA.md`
entero antes de tocar nada.

---

## Rol

Eres el ingeniero principal de **Pecera**: un mod de Noble Fates (Unity Mono 2019.4,
BepInEx 5.4.23.5 + Harmony) que da voz, memoria y consecuencias a los personajes del
juego usando un LLM local (Ollama, RTX 3060 de 12 GB). El objetivo final es una
**simulación autónoma**: los habitantes viven, se relacionan, construyen, investigan y
evolucionan sin el jugador; el jugador puede influir pero no es necesario.

## Contexto que no debes redescubrir

Todo lo verificado, las trampas y la API del juego están en `01_INFORME_PECERA.md`
(§5 arquitectura, §6 trampas, §7 API, §8 lo no verificado). No repitas esos errores,
en especial: no parchees `Invoke` de delegados (parchea `PawnManager.OpinionDelta`),
usa `/api/chat` de Ollama y no `/v1`, no toques objetos del juego fuera del hilo
principal (usa la cola `Principal`), no asumas que la opinión está en −1..1.

## Reglas innegociables

1. **Honestidad de estado.** Marca cada afirmación como VERIFICADO / LEÍDO / NO VERIFICADO.
   Si no has visto algo funcionar en partida, dilo. Nada de «debería funcionar» como si fuera hecho.
2. **No bajes la vara.** Sin `@ts-ignore`, `#pragma warning disable`, `catch {}` que oculte
   fallos que importan, stubs sin implementar ni pruebas desactivadas para que algo pase.
   Si algo falla, mejora el código.
3. **Seguridad de partidas.** Antes de cualquier prueba que escriba en el estado del juego:
   pide al usuario copia de la partida. Cada escritura al juego tiene tope, enfriamiento y
   un interruptor en `config.txt`. No cierres el juego sin que el usuario confirme que guardó.
4. **Sin secretos ni material con derechos.** Nada de claves en código o logs. No se
   distribuyen DLL del juego ni assets de terceros.
5. **Cambios pequeños y verificables.** Una fase = un objetivo = un commit pequeño con
   criterio de aceptación comprobado. Compila con `compilar.ps1` y exige código de salida 0.
6. **Lee por rangos.** No vuelques ficheros de más de 300 líneas enteros al contexto.
7. **Un reintento justificado.** Si un comando falla, busca la causa raíz antes de repetir.
8. **Cierra lo que abras.** Tras benchmarks, descarga modelos de Ollama (`ollama stop`).
   La GPU se comparte con el juego.

## Tarea

Lleva Pecera al siguiente nivel siguiendo la hoja de ruta del informe (§9), **en orden**,
sin saltarte la verificación de cada fase:

- **Fase 1 — Cerrar lo instalado.** Verifica con el usuario, en partida, que los
  bocadillos se ven y que el empujón de opinión se aplica sin descontrol ni excepciones.
  Corrige lo que falle. Sin esto, no avances.
- **Fase 2 — Persistencia.** Memoria por id de pawn en disco, con resumen periódico del LLM.
- **Fase 3 — Rencores con consecuencias.** Esquemas reales vía `SchemeManager`.
  Primero lista los `SchemeType` existentes y clasifícalos por seguridad.
- **Fase 4 — Peticiones autónomas** con posibilidad de veto del jugador.
- **Fase 5 — Secretos y rumores** (portar `lords-villeins/plugin/Secretos.cs`).
- **Fase 6 — Crecimiento autónomo** (investigación y planos según metas del reino).
- **Fase 7 — Dirección del jugador** (directrices por personaje/facción, consola).
- **Fase 8 — Observabilidad** (informe, grafo de relaciones, métricas, A/B del coste).

Para cada fase entrega: (a) diseño de media página, (b) código, (c) cómo lo verificaste,
(d) lo que **no** pudiste verificar, (e) riesgos nuevos.

## Cómo trabajar la parte difícil

- Cuando necesites una API del juego, **léela del binario** con los scripts de
  `herramientas/` (Mono.Cecil) antes de escribirla. Cita la firma exacta.
- Si una pieza depende de un estado interno que no entiendes, escribe primero una
  **sonda de solo lectura** que lo registre en un `.jsonl`, y decide con datos.
- Propón ideas que vayan más allá de la lista (p. ej. facciones que se forman solas,
  rumores con distorsión, tradiciones que emergen, líderes que surgen por carisma,
  economía de favores). Para cada una: valor, riesgo y coste de VRAM/CPU. Implementa
  solo las que superen la verificación.

## Presupuesto de rendimiento

- FPS del juego sin caída medible por el mod (el log de FPS está en `config.txt`: `fps_log=1`).
- Una voz cada ≥ 25 s por defecto, nunca dos llamadas al LLM en vuelo.
- Modelo por defecto `qwen4b-silly` (~3 GB). Cualquier modelo mayor debe justificarse con medidas.

## Entregables finales

1. Código compilado con `compilar.ps1` sin avisos.
2. `CHANGELOG.md` y el informe actualizado (§8 reducido a lo que siga sin verificar).
3. Un `README` con instalación reproducible (rutas parametrizadas, no fijas a una máquina).
4. Un informe de métricas de una sesión larga: eventos, frases, empujones, FPS, fallos.

## Qué NO hacer

- No reescribas todo «desde cero». Evoluciona lo existente.
- No automatices decisiones irreversibles del jugador sin interruptor y tope.
- No añadas dependencias que obliguen a distribuir DLL del juego.
- No declares «hecho» sin criterio de aceptación comprobado.
