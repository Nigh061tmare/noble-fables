# PECERA — Informe técnico y estado del proyecto

Fecha del informe: 2026-10-07. Autor del proyecto: Jose Luis. Redactado con Claude.

> Regla de este documento: cada afirmación está marcada como **VERIFICADO**
> (visto funcionando o medido en esta máquina), **LEÍDO** (visto en el binario
> o en el código, no ejecutado) o **NO VERIFICADO** (hipótesis). No mezcles las tres.

---

## 1. La idea

Una **pecera social**: un mundo de personajes que viven solos. Cada uno tiene
personalidad, metas, miedos y memoria, y a veces un secreto. Un LLM local
(Ollama en una RTX 3060 de 12 GB, coste 0 €) les da voz y decide cómo reaccionan.
El jugador puede mirar, intervenir o dejarlos: **el mundo debe seguir creciendo
sin él**. Lo que se quiere medir y ver: cómo cambian las relaciones, cuándo se
filtra un secreto, a quién, y qué pasa después.

Objetivo final: una simulación en la que los habitantes **construyen, investigan,
se relacionan, evolucionan y gobiernan** de forma autónoma, con el jugador como
mano opcional (no como motor).

## 2. Qué se ha probado y qué pasó

| Proyecto | Carpeta | Qué se consiguió | Por qué no fue el definitivo |
|---|---|---|---|
| Pecera original (web propia, 3 habitantes: Iris, Teo, Nadia) | `pecera-original/` | Habitantes con ficha, secreto, 3 capas (reflejos / percepción / cognición). 311 decisiones, 78 «oído», 58 reflexiones, 36 inválidas, 6 nacimientos registradas | Mundo diminuto y propio, sin juego detrás |
| AI Society (Godot) | (no incluido, 134 MB) | Traducción al español | Juego ajeno cerrado |
| Kenshi + SentientSands | `kenshi/` | Proxy, informe de secretos, retrofit de secretos | Depende de un mod ajeno; motor difícil de enganchar |
| Going Medieval | `going-medieval/` | Agente autónomo que actúa (prioridades, cocinar) por puente de ficheros | Casi sin vida social: gestiona, pero no hay drama |
| Lords & Villeins | `lords-villeins/` | Módulos: Rey dormido (audiencias, obras, reparto de manos), Secretos, Provocador, Burbujas, Mundo | Compilado e instalado, **nunca probado en partida**; una pelea tarda días de reloj |
| **Noble Fates** | `noble-fates/` | Hook verificado, frases, bocadillos, memoria, influencia, directrices | **Es el mejor candidato** (ver §3) |

## 3. Por qué Noble Fates es el mejor candidato

1. **Volumen de datos real — VERIFICADO.** Más de 700 cambios de opinión en pocos
   minutos de partida, cada uno con nombre, motivo y valor. En Lords & Villeins
   hubo que fabricar peleas con el «Provocador».
2. **Gancho verificado en vivo — VERIFICADO.** `PawnManager.OpinionDelta` dispara.
3. **Se puede escribir de vuelta — LEÍDO.** `Actor.pos`, `Pawn.DeltaOpinionOfSubject`
   (público), `ConversationManager`, `SchemeManager.TryTriggerScheme`,
   `PetitionManager`, `ResearchManager`, `WantsManager`, `MissionManager`.
4. **Moddeable: Unity Mono 2019.4.41f2, sin anticheat — VERIFICADO**, ~520 ficheros
   OCTDAT/OCTSCRIPT sueltos (datos), AppID Steam 1769420.
5. **Sistema social nativo cuantitativo — VERIFICADO**: opiniones con motivo, Moments,
   Schemes, Wants, Conversations.

**Pegas reales:** es el juego más pesado de CPU; con cientos de pawns el LLM no
puede hablar por todos (hace falta un cupo y selección); no hay «secretos»
nativos (hay que construirlos encima).

## 4. Entorno

- Windows 10, 8 hilos lógicos, RTX 3060 12 GB, Ollama 11434, proxy propio 11436
  (inyecta `reasoning_effort:none`).
- Juego: `D:\SteamLibrary\steamapps\common\Noble Fates`. BepInEx 5.4.23.5.
- Datos del mod: `...\BepInEx\plugins\pecera_datos\`.
- Compilación: `compilar.ps1` con `csc.exe` de .NET Framework 4 (no hay SDK moderno).
  Referencias: BepInEx.dll, 0Harmony.dll, Assembly-CSharp.dll y los módulos de
  Unity CoreModule, IMGUIModule, TextRenderingModule, InputLegacyModule.
- Inspección del binario: Mono.Cecil (toma la DLL de BepInEx de otro juego).
  Los scripts están en `herramientas/`.
- **Las rutas están fijas a esta máquina.** Hay que parametrizarlas.

## 5. Arquitectura actual en Noble Fates (VERIFICADO salvo lo indicado)

```
PawnManager.OpinionDelta(Pawn, ISubject, float, FeelingReason)   <- Harmony Postfix
   |  hilo del juego: filtra, lee GetOpinionValue, nombre, motivo
   |-- sin voz  -> ThreadPool -> opiniones.jsonl (nunca espera al LLM)
   '-- con voz  -> cola acotada (4) -> UNA hebra -> Ollama /api/chat
                      -> {texto, piensa, actitud}
                      |-- Registra() -> opiniones.jsonl + panel (F8)
                      |-- Pantalla.Burbuja -> bocadillo sobre el pawn (F7)   [NO VERIFICADO en partida]
                      '-- Aplica() -> Principal.Encola -> hilo del juego
                              pawn.DeltaOpinionOfSubject(...)                [NO VERIFICADO en partida]
```

Piezas: `Gancho.cs` (hook, voz, memoria, influencia, directriz), `Llm.cs` (cliente
Ollama), `Pantalla.cs` (panel, bocadillos, cola al hilo principal), `Pecera.cs`
(plugin, config).

Reglas de la voz:
- Un cambio ≥ 0,5 se registra. Para **hablar**: ≥ 1,0 si es un hecho («ha matado»,
  «ha desertado»); ≥ 2,0 si es un rasgo («es Orco», «está Malo»), que es ruido.
- Una voz cada 25 s en total y nunca dos en vuelo.
- Memoria: últimos 3 hechos por personaje (solo en RAM).
- `directriz.txt`: texto del jugador que se inyecta en el prompt, relectura por fecha.
- La actitud `empeora / mantiene / perdona` empuja la opinión: tope 0,25 por frase y
  un empujón por pareja cada 5 min. Se apaga con `influencia=0`.

## 6. Hallazgos técnicos y trampas (ahórrate esto)

1. **Harmony no puede parchear `Invoke` de un delegado.** El punto real es
   `PawnManager.OpinionDelta`, un método normal que solo lanza el delegado.
2. **`Info.Location` es la ruta del .dll, no la carpeta.** Usarla como carpeta mató
   `Awake` y todo el plugin quedó inerte con el log diciendo «Loading».
3. **Campo ≠ propiedad.** `Pawn.character` es propiedad; su campo es
   `<character>k__BackingField`. Usa `GetShortName()` / `GetName()`.
4. **Ollama `/v1/chat/completions` ignora `keep_alive`** y abre contexto 65536.
   Con la API nativa `/api/chat`: `keep_alive` respetado, `num_ctx=2048`, −0,9 GB
   de VRAM. Con `/v1` hay que mandar `reasoning_effort:"none"` (con solo
   `think:false` el `content` llega vacío).
5. **Benchmark con el prompt real (caliente):** qwen4b-silly 3,4 GB 1,4 s 41 tok/s
   JSON completo → elegido. qwen9b 6,6 GB 2,1 s 29 tok/s (se cortaba a 150 tokens).
   mimo-9b:q4 5,8 GB 2,9 s (pronombres mal). mistral-nemo 7,1 GB 1,9 s, pesado.
6. **Después de benchmarks, descarga el modelo** (`ollama stop`): un modelo de 7 GB
   olvidado dejó 492 MB de VRAM libres.
7. **El lag NO venía del mod.** Era LM Studio (`llama-server`, 7 GB, 38 % SM),
   WeMod `capture.exe` (2 núcleos) y un `fly_server.py` (4,5 núcleos). Con eso
   cerrado, FPS 2-6 → media 61.
8. **`OnGUI` asignaba basura:** un `GUIStyle` por línea y repintado. Estilos
   cacheados y cabecera refrescada cada 0,5 s.
9. **Un freno por pawn no frena nada** si hay 20 pawns: hay que frenar global.
10. **Reemplazos multilínea fallan con CRLF** en scripts de PowerShell: normaliza o
    usa Python con `newline=""`.
11. **`File.AppendAllText` con `Encoding.UTF8` mete BOM.** Usa `UTF8Encoding(false)`.
12. **Parser JSON manual:** `\n` salía como «n» y `\u00e9` como «u00e9». Arreglado
    con un desescape propio. (Pendiente: ¿mejor una librería JSON?)
13. **`compilar.ps1` decía OK con un DLL viejo** aunque fallara la compilación.
    Ahora borra el DLL antes y sale con código 1 si hay `error CS`.
14. **El DLL está bloqueado mientras el juego corre.** Hay que cerrarlo para
    instalar. No lo cierres sin que el usuario haya guardado.
15. **Los valores de opinión no están en −1..1**: se han visto hasta −3,7. Un
    prompt que decía «escala −1 a 1» era falso (corregido).
16. **Semántica de `delta`:** el motivo describe al *objetivo* («Chiyo está Bueno») y
    el signo depende del observador (un pawn malvado pierde estima por alguien
    bueno). El prompt dice «empieza a DESCONFIAR» según el signo, no según el texto.
17. **La API de notificaciones nativa exige claves de localización**; por eso los
    bocadillos se dibujan con `OnGUI` + `WorldToScreenPoint(pawn.character.pos)`.
18. **No sé si `antes`/`despues` eran antes o después** del cambio. El esquema nuevo
    guarda solo `valor`, sin suposiciones.

## 7. API de Noble Fates descubierta (LEÍDO en `Assembly-CSharp.dll`)

- `Pawn`: `GetOpinionValue(ISubjectOrCompound,bool)`, `DeltaOpinionOfSubject(ISubject,float,FeelingReason,FeelingMemoryFlags)`,
  `GetName/GetShortName/GetFullName`, `character` (→ `Character : Actor`, con `pos: Vector3`).
- `FeelingReason` (struct: provider/info/text, `FeelingReason.None`), `FeelingMemoryFlags {None, Reminder}`.
- `ConversationManager`, `ConversationSpokenWords {speaker, listener, lines}`, `ConversationTopic`, `EmoteCommand(Actor,string)`.
- `SchemeManager`: `TryTriggerScheme(SchemeType, ISchemeExecutor, OctScriptContext, out Scheme)`, `DrawScheme`, `IsFitForScheme`.
- `PetitionManager`: `petitionQueue`, `QueuePetition`, `EvaluatePetitions`, `backgroundPetitions`; `Petition.Complete()`.
- `ResearchManager`, `ResearchEntry.AvailableFor`, `ResearchCommand`.
- `PlanManager.AddPlan`, `ZoneManager`, `MissionManager`, `WantsManager`, `KingdomManager.allPawns`.
- `TriggerSchemeOctScriptOperation(partyKey, schemeType)`.
- Notificaciones: `Notification(string|PersistentLocalizedStringContext, Actor, float)`, `NotificationManager.AddNotification`.

## 8. Qué NO está verificado

1. Que los bocadillos se vean en partida (acabo de instalarlos; sin datos).
2. Que el empujón de opinión se aplique, no descontrole la simulación ni rompa tooltips
   (reutiliza el `FeelingReason` original; sin prueba).
3. Que la escala de `delta` coincida con la de `DeltaOpinionOfSubject`.
4. Todo Lords & Villeins en partida.
5. Que las peticiones, la investigación y los planos se puedan automatizar sin corromper partidas.
6. Un A/B del coste del mod (mod ON vs OFF). El lag se resolvió por causa externa.

## 9. Hoja de ruta (cada fase con criterio de aceptación)

**Fase 1 — Cerrar lo instalado.** Verificar bocadillos y empujón con ≥ 5 min de partida.
Aceptación: ≥ 5 frases con bocadillo visible; ninguna excepción en el log; las
opiniones empujadas no superan ±0,25 por frase; FPS sin caída medible.

**Fase 2 — Persistencia.** Memoria por personaje en disco (`memoria.jsonl`), ligada
al id del pawn y no al nombre (hoy hay colisiones de nombre). Resumen periódico de
memoria con el LLM para no crecer. Aceptación: reiniciar el juego conserva recuerdos.

**Fase 3 — Rencores con consecuencias.** Un odio sostenido dispara un esquema real
(`TryTriggerScheme`). Primero leer qué `SchemeType` hay y cuáles son seguros.
Aceptación: un esquema disparado por el mod aparece en la UI del juego y se resuelve.

**Fase 4 — Peticiones autónomas.** Resolver `petitionQueue` cuando el jugador no
actúa, con un LLM que decide según personalidad y memoria; el jugador puede vetar.
Aceptación: una petición completa de principio a fin sin intervención, sin estado inválido.

**Fase 5 — Secretos y rumores.** Portar `Secretos.cs` de Lords & Villeins como capa
propia: secreto por personaje, propagación por conversaciones, registro `fugas.jsonl`.

**Fase 6 — Crecimiento autónomo.** Investigación y planos: elegir qué investigar y
qué construir según metas del reino. Aceptación: el reino crece N días de juego sin
input del jugador sin quedarse atascado.

**Fase 7 — Dirección del jugador.** Directrices más ricas (por personaje, por
facción), consola en pantalla, lectura de `directriz.txt` por pawn.

**Fase 8 — Observabilidad.** Informe de la pecera (como `pecera_informe.py` de
Kenshi): grafo de relaciones, línea temporal, métricas de evolución. Un A/B formal del coste.

## 10. Riesgos

- **Corromper partidas** al escribir en el estado del juego. Siempre copia de
  partida antes de probar; interruptor `influencia=0`; topes bajos.
- **Reentrada:** `DeltaOpinionOfSubject` puede disparar nuestro hook. Protegido con
  `[ThreadStatic] Propio`, hay que comprobarlo en partida.
- **Hilos:** nada del juego se toca fuera del hilo principal (cola `Principal`).
- **Actualizaciones del juego** pueden romper `OpinionDelta` o la reflexión.
- **VRAM:** el LLM comparte tarjeta con el juego. Cupo de voz y `keep_alive` corto.
- **Licencias:** el repositorio no debe incluir DLL del juego ni assets de terceros.

## 11. Uso de créditos en la nube

No sé el coste exacto de cada llamada; consulta el panel de uso antes de lanzar
bucles largos. Recomendaciones prácticas: usar el modelo potente para diseño y
revisión de arquitectura y las fases con riesgo (3, 4, 6); trabajar con sesiones
cortas y un objetivo por sesión; no volcar ficheros enteros al contexto (leer por
rangos); dejar los benchmarks y vigilancias largas a scripts locales.

## 12. Contenido del paquete

```
01_INFORME_PECERA.md          este informe
02_PROMPT_CLAUDE_CODE.md      prompt listo para pegar
README.md / .gitignore        para el repositorio
noble-fates/                  plugin actual (4 .cs + compilar.ps1)
lords-villeins/               módulos del Rey dormido, Secretos, Provocador...
kenshi/ going-medieval/       scripts de intentos anteriores
pecera-original/              agentes y mundo web originales (sin assets gráficos)
herramientas/                 scripts Cecil para leer el binario y monitorizar
datos-muestra/                muestras reales de opiniones, config y respuestas del LLM
```
