# Informe de la sesion 2 (traspaso a Opus) — 2026-10-08

Marcas: **VERIFICADO** (ejecutado y visto aqui o en el PC del usuario segun diga), **LEIDO**, **NO VERIFICADO**.

## 1. Que llego
`PECERA_TRASPASO_OPUS.rar` + `INFORME_DETALLADO.md` + `PROMPT_OPUS.md` (copias en `sesion/originales_traspaso/`). El `.rar` volvio a llegar **danado**:
17 ficheros vacios (`compilar.ps1`, `ConfigTests.cs`, `SistemasTests.cs`, scripts de `herramientas/`, parte de `legacy/`...). Los que eran iguales a los del repo se conservaron
(del repo); lo unico perdido de verdad son los **tests de `VersionDe` que el PC habia anadido**, que recree. **Si cambiaste `compilar.ps1` en tu PC, no he podido verlo**: mandamelo suelto.

## 2. Que habia cambiado en el PC del usuario (integrado)
- v0.2.1 **VERIFICADO en el PC**: compila 29 ficheros con `-warnaserror` y 0 avisos; 159 tests en verde con .NET 8 portatil; el plugin carga en el juego real (0.31.5.3).
- Bug real arreglado alli: la migracion del config v1 **nunca se ejecutaba** (`Parse(...).Int("config_version")` devolvia el defecto 2). Mi error original; `VersionDe` lo corrige.
- Bug de semantica: `perdona` con delta positivo restaba estima. Corregido.
- Rey dormido: `Peticiones.cs` (fase 1 lectura + dry-run, fase 2 `Receive()+Complete()`), `Sonda.Peticiones()`, `panel_inicio`.
- Sonda: firmas reales confirmadas de `PetitionManager`/`Petition`/`QueuedPetition`.

## 3. Revision critica de lo recibido (hallazgos)
1. **Fase 2 se activaba sola**: `GobiernoDisponible` dependia solo de `peticion_capturada ok>=1`, que prueba que se **lee** la cola, no que `Complete()` sea inocuo
   (podria saltarse el dialogo o los pasos de la peticion), y `peticiones=1` ya estaba en tu config. → nuevo interruptor **`peticiones_aplicar=0`** (tres llaves). `peticiones=1` solo observa.
2. El dry-run anotaba la cola **entera** en la cronica cada 30 s (duplicados). → solo las nuevas.
3. `Mem.Registra("peticion", ...)` usa un pawn ficticio «peticion»; inocuo pero contamina la memoria (anotado, no tocado).
4. El consejo evalua contra el id `"reino"` (no hay soberano real todavia): ver confirmacion A3 en `docs/PLAN_OPUS.md`.

## 4. Que se construyo (v0.3.0) — respuesta al PROMPT_OPUS, Fase 0 y 1 en la nube
- **Core de agentes** (C# 5, 28 tests nuevos + 7 de simulador): `Persona`, `Agenda` (topes y enfriamientos), `Planificador`/`Agente` (reflexion, plan por reglas, consolidacion),
  guardarrailes `Valida`, `PlanLlm` en lote, `PresupuestoLlm`, `Dialogo`, `Narrador`.
- **Game**: `Mente.cs` (`agentes=1`, apagado por defecto, no escribe), identidad por tokens con unicidad en vivo, `Sonda.Mundo()` (`sonda_mundo.json`),
  `agentes.md` y `historias.md`.
- **Simulador con agentes** (LLM simulado que falla un 10 % y propone planes invalidos un 20 %): hallazgos en §5.
- **Documentacion**: `docs/PLAN_OPUS.md` (plan por fases, tabla de 13 confirmaciones de API con estimacion de rondas, riesgos, solicitudes al usuario), CHANGELOG 0.3.0, `VERIFICACION_PENDIENTE.md` ampliado.
- **219 tests en verde** (VERIFICADO aqui); `stubs/Pecera.Game.Check` compila sin avisos (C# 5, net48).

## 5. Lo que enseño el simulador esta vez
1. Con las necesidades iniciales todos acababan **agotados y solos** (necesidad mas urgente media ≈ 0): el plan degeneraba en descansar/charlar. → sueno nocturno +0.10 y decaimientos mas suaves (necesidad social media 0.43–0.54, urgente 0.15–0.24).
2. Sin enfriamiento, ~2.000 peticiones y ~1.900 consuelos al anio. → enfriamientos (pedir 14 d por pawn, hostiles 10 d, cortejo 3 d, consolar 4 d): ~600 peticiones/anio.
3. **Los guardarrailes funcionan**: ~350 planes del LLM simulado rechazados al anio; 3–12 intenciones reevaluadas y descartadas al ejecutar (el estado cambio entre planear y ejecutar); ninguna invalida ejecutada en masa.
4. **Presupuesto**: la planificacion usa 1 llamada/dia; el maximo en un dia (incluyendo texto de conversacion) nunca supera el tope (test).
5. Una brecha que cerre: con `Esquemas=false` los agentes aun proponian venganzas (puerta lateral) → ahora fallan la intencion (test).
6. Salud del reino con agentes: sin atascos (≤ 7 dias sin progreso), oscilacion ~5 %, estabilidad > 0.5. **Todo relativo al simulador.**

## 6. Estado honesto
**VERIFICADO (aqui):** 219 tests, simulador, stubs. **VERIFICADO (PC, por el traspaso):** compila, carga, migracion de config, `perdona`.
**NO VERIFICADO:** `Mente` en el juego, identidad por tokens, `sonda_mundo.json`, `peticion_capturada`/`peticion_aplicada`, todo lo de `VERIFICACION_PENDIENTE.md`.
**Sigue sin existir:** ejecucion real de intenciones en el juego (A8–A12), necesidades reales (A4), soberano real (A3), conversaciones reales (A13).

## 7. Que necesito del usuario (resumen; detalle en `docs/PLAN_OPUS.md` §3)
1. Compilar la v0.3.0 y pegarme errores si los hay. 2. Sesion F11+F10 y devolver `sonda.json`, `sonda_pawn.json`, `sonda_mundo.json`, `verificacion.json`.
3. Forzar/esperar una peticion real (fase 1). 4. Sesion `agentes=1` de 20 min y devolver `agentes.md`, `historias.md`. 5. Solo con copia de partida: `peticiones_aplicar=1`.
6. Reenviar `compilar.ps1` si lo modificaste en el PC.


## 8. Ampliacion posterior: Fase 3 «realista y autonoma» (v0.4.0)
Peticion: ampliar la fase 3 tomando como modelo los juegos y simulaciones de ejemplo, investigando en internet. Resultado completo en `docs/INVESTIGACION.md`.
- Investigado (lectura de codigo/README; arXiv y RimWiki bloqueados por el proxy, asi que los numeros de Generative Agents salen de su CODIGO): Generative Agents, AI Town, alife-sdk, Project Sid, RimWorld (rupturas y narrador), IAUS.
- Construido en el Core (+ 30 tests, 249 en total): recuperacion de memoria por relevancia, reflexion por importancia acumulada, guiones de varios pasos, ambiciones con duracion y sucesion, animo y rupturas, director de drama, normas colectivas, roles, etapas de relacion, freno de conversacion, curvas IAUS.
- Cableado en el juego sin escribir: voz con recuperacion, reflexion, normas, director (sugerencias en `agentes.md`), animo informativo.
- El simulador volvio a corregir el diseno cuatro veces (ambiciones triviales, animo demasiado bajo, director sin actuadores, normas parpadeantes). Detalle en `docs/SIMULACION.md`.
- **Sigue siendo cierto** que para que jueguen SOLOS hace falta ejecutar las intenciones en el juego (A8–A12) y leer necesidades/familia/habilidades reales (A4–A6): la logica esta, la API no.
