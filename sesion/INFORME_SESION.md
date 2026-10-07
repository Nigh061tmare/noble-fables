# Informe de la sesion (de arriba a abajo)

Fecha: 2026-10-07 · Rama: `claude/new-session-1vzpl2` · Repo: `Nigh061tmare/noble-fables` · Version resultante: **Pecera 0.2.0**

Este documento cuenta TODO lo que paso en la sesion, en orden, con lo que se decidio y por que, lo que quedo hecho,
lo que NO esta verificado y como seguir. Marcas: **VERIFICADO** (ejecutado y visto aqui), **LEIDO** (visto en codigo/binario,
no ejecutado), **NO VERIFICADO** (hipotesis o nunca ejecutado en el juego).

---------------------------------------------------------------------------------------------------

## 1. Punto de partida

* Proyecto del usuario: **Pecera**, mod de **Noble Fates** (Unity Mono 2019.4, BepInEx 5.4.23.5 + Harmony) que da voz, memoria y
  consecuencias a los personajes con un LLM local (Ollama, RTX 3060 12 GB). Meta final: simulacion social **autonoma**.
* El repo solo tenia `01_INFORME_PECERA.md`, `02_PROMPT_CLAUDE_CODE.md` y `README.md`.
* Entorno de la sesion: contenedor Linux en la nube, **sin Windows, sin el juego, sin Ollama, sin GPU**. Solo el usuario puede probar en partida.

## 2. Cronologia

### 2.1 El `.rar` (primer mensaje, sin instrucciones)
* Subio `pecera-paquete.rar`. Instale `7zip` y `unar`; 7z fallo con «Unsupported Method» en ~72 ficheros y `unar` en 12.
  Varios ficheros salieron **vacios** (`compilar.ps1`, `Rey.cs`, `Banco.cs`, `fichas.json`, ...). Conclusion: el `.rar` llegaba danado / con una compresion no soportada.
* Error mio: un `pip download` dejo ficheros `.whl` en el repo; los borre antes de seguir (no se llego a commitear nada).
* Respondi resumiendo el contenido y preguntando que hacer. El prompt original bloqueaba en la «Fase 1» (verificar en partida), imposible en la nube.

### 2.2 Reenvio de los tres `.md` y nuevo prompt
* Reenvio informe, prompt y README (identicos a los del repo) sin instrucciones; volvi a explicar el bloqueo y ofreci opciones A/B.
* Despues subio `pecera-paquete.zip` (completo, **sin ficheros vacios**, incluido `compilar.ps1`) y un **prompt nuevo (`03_PROMPT_EXPANDIDO.md`)**
  que sustituye al anterior: **ya no hay bloqueo por la Fase 1**; trabajar por capas, probar todo lo posible en la nube, entregar `.zip` con rutas `/`,
  `VERIFICACION_PENDIENTE.md`, informe actualizado, README reproducible y CHANGELOG.

### 2.3 Construccion (en este orden de prioridad del prompt: A, B, C, K, luego D, G, E, F, resto)
1. Importe el paquete original a `legacy/` (y `herramientas/` a la raiz); comprobe que no hay secretos (grep de claves: limpio).
2. Instale **dotnet SDK 8** (apt) y monte la solucion: `Pecera.Core` (C# 5 forzado), `Pecera.Tests` (xUnit), `Pecera.Sim`, `stubs/Pecera.Game.Check`.
3. Escribi el Core por bloques, con tests en cada uno, y un commit por bloque (ver §3).
4. Escribi la capa de adaptadores `Pecera.Game` y la comprobe contra **stubs** de tipos (net48, C# 5, avisos = errores).
5. Escribi el simulador, lo use para **calibrar** parametros y para descubrir fallos de diseño (ver §6).
6. Documentacion, `VERIFICACION_PENDIENTE.md`, `AGENTS.md`/`CLAUDE.md` para continuar en opencode, y el zip.
7. Peticion «haz lo de las ideas»: implemente las ideas pendientes de `docs/IDEAS.md` (justicia, sucesion/herencia, mentoria, cultura/religion,
   dialectos, estaciones, suenos, espionaje, deriva) en Core + tests + simulador (ver §5).
8. Peticion final: este zip e informe.

## 3. Commits de la sesion

| Commit | Contenido |
|---|---|
| `190b09f` | Capas Core/Tests: JSON robusto, config documentada, voz, memoria por id, fichas, modelo afectivo (67 tests) |
| `67a5e61` | Compuerta con veto, secretos/rumores, esquemas, sociedad, gobierno, cronica, direccion, informe (111 tests) |
| `bb2e345` | Simulador headless, confidencias como semilla de rumores, enfriamiento por clase (119 tests) |
| `566d1ee` | Capa Game (adaptadores), stubs, `compilar.ps1` parametrizado, CI |
| `2900147` | README, ARQUITECTURA, SIMULACION, IDEAS, CHANGELOG, informe actualizado, VERIFICACION_PENDIENTE |
| `b306a63` | Afectos olvidan pares en baseline, config hilo-seguro, avance por trozos, costes medidos |
| `f025724` | Entregable `pecera-v0.2.0.zip` (primera version) |
| `65e0a86` | `AGENTS.md` / `CLAUDE.md` para opencode |
| `9ac2e3a` | Ideas: justicia, sucesion, mentoria, cultura, dialectos, estaciones, suenos, espionaje, deriva (159 tests) |
| (este) | Informe de sesion, prompt para opencode, originales y zip completo |

## 4. Que hay construido (por capas)

Tamano: ~7.700 lineas C# (Core ~3.800, Game ~1.500, el resto tests/simulador/stubs). **159 tests, todos en verde (VERIFICADO aqui).**

### 4.1 `src/Pecera.Core` — logica pura, C# 5 (se prueba en la nube)
Escrito en C# 5 **a proposito**: el unico compilador del PC del autor es el `csc.exe` de .NET Framework 4. `compilar.ps1` compila estos mismos ficheros
dentro del plugin (no hay DLL extra). `LangVersion 5` esta forzado en el csproj, asi que no se cuela sintaxis moderna.

| Fichero | Contenido |
|---|---|
| `Json.cs` | Parser estricto + tolerante: escapes, `\uXXXX`, vallas markdown, prosa alrededor, **reparacion de JSON truncado**, escritura segura |
| `Config.cs` | Esquema de `config.txt` (rango, defecto, doc, estado de verificacion, «escribe en el juego»), migracion v1→v2, `PuedeEscribir` (interruptor + modo), `Set` en caliente, hilo-seguro |
| `Infra.cs` | Reloj inyectable, almacenamiento (memoria/disco con escritura atomica y rotacion), RNG determinista |
| `Evidence.cs` | **Telemetria de verificacion**: cada funcion con criterio (min exitos / max fallos) → `verificacion.json` |
| `Voz.cs` | Cuerpos Ollama nativo/proxy + lectura de respuesta, parser de la replica (json / reparado / suelto), `EsRasgo`, `CupoVoz`, `PoliticaInfluencia` (tope por frase, por pareja y por hora), prompts |
| `Identidad.cs` | `Ficha` (rasgos, metas, miedos, voz, carisma, rencor, locuacidad, ambicion), generador determinista sin LLM, `RegistroIds` (id estable + homonimos + evidencia de estabilidad entre sesiones), `AlmacenFichas` |
| `Memoria.cs` | Memoria episodica `recientes → medio → largo`, resumen con LLM o fallback, poda dura, compactacion, tolera lineas corruptas |
| `Afectos.cs` | Modelo afectivo dirigido: afecto, confianza, rencor, deuda, rivalidad, romance, trauma; decaimiento; reconciliacion; olvido de pares en baseline |
| `Secretos.cs` | Secretos/rumores: confidencias (semilla), propagacion con **distorsion**, consecuencias afectivas, `fugas.jsonl`, red medida, **espionaje** |
| `Esquemas.cs` | Catalogo `esquemas.txt` con clases de seguridad (seguro/riesgo/prohibido), motor de propuestas, sugeridor por nombre |
| `Compuerta.cs` | **Toda escritura al juego**: tope diario, enfriamiento por clase/clave, ventana de veto; `SaludLlm` (modo degradado), `GuardaVersion`, `FormatoDatos` versionado |
| `Sociedad.cs` | Facciones (propagacion de etiquetas determinista), lider por carisma, favores, parejas, soledad, tradiciones (`Costumbres`), `Deriva` acotada |
| `Gobierno.cs` | Metas del reino (bucle de deficit), metricas, `Consejo` (peticiones, investigacion) |
| `Cronica.cs` | Hitos, resumen por temporada, leyendas, markdown |
| `Directrices.cs` | Directrices global/faccion/pawn, `Consola` con deshacer |
| `Informe.cs` | Resumen de sesion, estadistica de FPS, **A/B formal**, grafo DOT/JSON |
| `Justicia.cs`, `Linaje.cs`, `Cultura.cs`, `Suenos.cs` | Las ideas (ver §5) |

### 4.2 `src/Pecera.Game` — adaptadores finos (namespace `PeceraNF`, solo se compilan de verdad en el PC del usuario)
`Plugin.cs` (arranque, tareas periodicas, informe, version del juego), `Gancho.cs` (hook `PawnManager.OpinionDelta`, pipeline), `Llm.cs` (HTTP Ollama),
`Acciones.cs` (**unico** sitio que escribe en el juego), `Estado.cs` (contenedor y hilos), `Identidad.cs` (clave estable por reflexion), `Sonda.cs` (solo lectura),
`Consola.cs` (consola por fichero), `Pantalla.cs` (panel y bocadillos, de v0.1 + evidencia).

### 4.3 Resto
`tests/Pecera.Tests` (159 tests), `sim/Pecera.Sim` (simulador + `--barrido` + `--doc-config`), `stubs/Pecera.Game.Check` (tipos en CI),
`compilar.ps1` (parametrizado; **no cierra nunca el juego**), `.github/workflows/ci.yml`, `herramientas/` (Cecil + `pecera_ab.py`), `legacy/` (intentos anteriores y plugin v0.1),
`docs/` (CONFIG generado, ARQUITECTURA, SIMULACION, IDEAS), `VERIFICACION_PENDIENTE.md`, `CHANGELOG.md`, `AGENTS.md`/`CLAUDE.md`.

## 5. Las ideas (todas probadas en Core y ejercitadas en el simulador; NINGUNA escribe en el juego)

Justicia (`Tribunal`) · sucesion y herencia (`Linaje`, `Sucesion`, reparto que conserva los bienes) · mentoria · cultura/religion emergente (`Cultura`) ·
dialectos por faccion · estaciones · suenos e inspiraciones · espionaje · deriva de personalidad (tope ±0.2) · facciones y lideres · rumores con distorsion ·
tradiciones · economia de favores · soledad · cronica y leyendas · modo degradado · consola con deshacer · telemetria de verificacion.
**Unica sin implementar:** voz con modelo mayor para eventos clave (exige medir VRAM/latencia primero).
Cableado en el juego, sin escribir: facciones, dialectos en el prompt de la voz y credo en el informe.

## 6. Lo que enseño el simulador (hallazgos y decisiones)

1. **Sin semilla no habia rumores:** nadie contaba secretos propios, asi que nada se filtraba (0 fugas). Se añadio la **confidencia** (con mucha confianza y afecto, el sujeto confia su secreto).
2. **Sesgo de saturacion:** con mentoria la confianza sube y casi todos los secretos acababan de dominio publico → `ProbBase` de chisme bajada a **0.08** (alcance medio 8–11 de 24).
3. **Bug de mi simulador** (no del Core): el saber podia retroceder al sumar bibliotecas; corregido y blindado con test.
4. **Umbral de aprobacion de peticiones:** casi no afecta a estabilidad ni oscilacion; se deja 0.45 como defecto razonable, **no optimo** (tabla en `docs/SIMULACION.md`).
5. **Oscilacion:** ~4–5 % de cambios de signo semanales (criterio del test: < 10 %). Sin atascos: ≤ 7 dias seguidos sin progreso (criterio: ≤ 10).
6. **Sin LLM** (100 % de fallos simulados) el reino sigue creciendo y la memoria sigue acotada.
7. **Artefactos del simulador a no sobreinterpretar:** el credo siempre sale «Rito de la Deuda» (los esquemas hostiles simulados dominan el contador); 0 sueños cumplidos en un año.
8. Costes medidos en la nube (.NET 8): el informe con 200 pawns cuesta ~25 ms en el hilo principal (en Mono ×3–5, **sin medir en el juego**).

## 7. Decisiones de diseño importantes

* **Capas** para poder probar casi todo sin el juego; los adaptadores son finos.
* **Toda escritura al juego** pasa por `Acciones` → `PeceraConfig.PuedeEscribir` (interruptor + `modo`) → guarda de version → cola `Principal` → evidencia ok/fallo.
* **Lo no verificado viene apagado:** `influencia` pasa a **0** por defecto (v0.1 la llevaba a 1 sin verificar); la migracion lo avisa. `modo=observador` anula toda escritura.
* **Firmas pendientes, no inventadas:** `SchemeManager.TryTriggerScheme` (necesita `ISchemeExecutor` y `OctScriptContext`), `PetitionManager`, `ResearchManager`, `PlanManager`, `MissionManager`,
  familia, habilidades y calendario. Quedan tras interfaz y la **sonda F11** vuelca lo necesario para escribirlas.
* **Id estable por pawn:** no hay id documentado → busqueda por reflexion de un miembro escalar tipo id; si no hay, nombre + separacion de homonimos; la estabilidad entre reinicios se **mide** (`id_clave_estable`).
* **Una sola hebra al LLM** (voz y resumenes): nunca dos llamadas en vuelo. Afectos/Secretos/Cronica solo hilo principal.
* **Consola por fichero** (no por IMGUI): IMGUI no puede capturar teclado sin que el juego tambien reciba las teclas.
* **Escritura atomica** y datos **versionados** (nunca se escribe sobre un formato mas nuevo).

## 8. Estado honesto

### VERIFICADO (aqui)
159 tests; el simulador (determinista, 5 semillas); los adaptadores compilan contra stubs en C# 5 con avisos = errores; `pecera_ab.py` coincide con el comparador A/B del Core; `docs/CONFIG.md` generado y comparado por test.

### NO VERIFICADO (necesita tu PC)
1. Que `compilar.ps1` v0.2 y todo el codigo nuevo compilen con tu `csc.exe` real (los stubs no son el juego).
2. Bocadillos en pantalla, empujon de opinion, reentrada del hook, tooltips.
3. Id estable de pawn entre sesiones; persistencia de memoria entre reinicios del juego real.
4. Todo lo marcado «firma pendiente»: esquemas, peticiones, investigacion, planos, misiones, calendario, familia, habilidades.
5. A/B del coste del mod con datos reales; el tiron del informe con muchos pawns.
6. Que las constantes del simulador se parezcan al juego (son supuestos).
7. Lords & Villeins en partida (sin cambios desde antes).

## 9. Como seguir

1. **Tu PC:** hacer `VERIFICACION_PENDIENTE.md` (dos sesiones de juego + A/B) y devolver `verificacion.json`, `sonda.json`, `sonda_pawn.json`, `esquemas.sugeridos.txt`.
2. **Con la sonda:** escribir los adaptadores reales (esquemas → peticiones → investigacion/planos → misiones → tiempo → familia/habilidades) y cablear Justicia, Linaje, Mentoria, Suenos y Estaciones.
3. Tests de integracion del pipeline del hook (hoy la logica esta en Core; el cableado de `Gancho.cs` solo se compila contra stubs).
4. Medir VRAM/latencia para decidir lo de la voz con modelo mayor.
5. En opencode: leer `AGENTS.md` y usar `sesion/PROMPT_OPENCODE.md`.

## 10. Contenido de este zip

`pecera/` = todo el repo (codigo, tests, simulador, stubs, docs, legacy, herramientas, CI) + `sesion/` (este informe, el prompt para opencode, los originales que subiste y un ejemplo de salida del simulador).
Rutas con `/`. Los `.rar`/`.zip` originales estan en `sesion/originales/` (el `.rar` esta danado; se conserva solo como referencia).
