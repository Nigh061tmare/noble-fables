# INFORME DETALLADO DEL PROYECTO PECERA
## Estado completo para el traspaso a Claude Opus
### Fecha: 2026-10-08 · Autor: sesión previa (Claude) + trabajo de hoy

---

## 1. QUÉ ES PECERA

**Pecera** es un mod para **Noble Fates** (juego de simulación de colonia/estrategia de
Xobermon, LLC, hecho en Unity 2019.4, C#/.NET 4.7, BepInEx 5.4.23.5). El mod da **vida
social autónoma a los personajes (pawns)** del reino usando un **LLM local en la GPU del
usuario** (RTX 3060, Ollama). No sustituye la CPU del juego: **observa eventos**, decide con
lógica propia probada en el Core, y (hoy solo en casos muy restringidos) **escribe en el
estado del juego** para influir en la sociedad.

Filosofía del proyecto (muy importante para ampliarlo):
1. **Todo apagado hasta verificar**: los interruptores que ESCRIBEN en el estado del juego
   vienen `=0` y solo se activan cuando la firma de la API del juego está confirmada con la
   sonda (F11) en partida real.
2. **Lógica pura separada del adaptador**: `Pecera.Core` (C#5, sin dependencias de Unity)
   con 20 ficheros y ~3.485 líneas, probada con tests; `Pecera.Game` (10 ficheros, ~1.809
   líneas) toca los tipos del juego (Unity + Harmony).
3. **Verificación en partida**: un sistema de evidencia (`verificacion.json`) con criterios
   por función (nombre, ok mínimo, fallos máximos). El mod avisa en el log de qué está
   verificado y qué no.

---

## 2. ARQUITECTURA ACTUAL

```
src/
  Pecera.Core/                 (lógica pura, C#5, sin Unity)
    Afectos.cs        Modelo afectivo por pareja (afecto, confianza, rencor, deuda,
                      rivalidad, romance, trauma) con decaimiento exponencial y eventos.
    Compuerta.cs      Compuerta de decisiones: tope diario, enfriamiento por clave, ventana
                      de veto del jugador, disyuntor del LLM (SaludLlm), guarda de versión.
    Config.cs         Esquema de config (schema), parseo, migración, render.
    Cronica.cs        Crónica del reino por días/temporadas/años con hitos y resúmenes.
    Cultura.cs        Sistema de valores emergentes (honor, clemencia, comunidad, saber,
                      venganza) y modificadores por estación.
    Directrices.cs    Directriz del jugador por texto (global/facción/pawn) que condiciona
                      las decisiones; e interface IConsolaHost + Consola.
    Esquemas.cs       Catálogo de "esquemas" (acciones que el reino puede pedir), seguro/
                      riesgo/prohibido, hostil/amistoso, motor que propone esquemas.
    Evidence.cs       Sistema de evidencia (nombre, ok/fail, min_ok, max_fail) -> verificacion.json.
    Gobierno.cs       MetasReino, MetricasReino, Peticion, Veredicto, OpcionElegible, Consejo
                      (evalúa peticiones y elige opciones), Linaje/Sucesion/Justicia (Rey dormido).
    Identidad.cs      Fichas de pawns (trazos, valores), generación determinista o LLM.
    Informe.cs        Resúmenes de sesión (opiniones, fps) + generación de informe.md grafo.dot/json.
    Json.cs           Serializador JSON ligero a medida (C#5) con reparador de cortes.
    Linaje.cs         Reparto de bienes, sucesión con disputas.
    Justicia.cs       Acusaciones, credibilidad, rencor (para el sistema judicial).
    Memoria.cs        Memoria episódica por pawn en disco (memoria.jsonl), resúmenes con LLM.
    Secretos.cs       Secretos por pawn, fugas (rumores), conversaciones.
    Sociedad.cs       Facciones, líderes, saldo de favores, parejas candidatas, soledad, deriva.
    Suenos.cs         Metas/sueños de cada pawn (categoría de meta).
    Voz.cs            Client Ollama (nativo + proxy), parser de réplicas, promps del LLM.
  Pecera.Game/                 (adaptadores, Unity + Harmony + BepInEx)
    Acciones.cs       PUNTO ÚNICO de escritura al juego. EmpujaOpinion (verificado),
                      EsquemasDisponibles/DisparaEsquema (pendiente), GobiernoDisponible +
                      ResuelvePeticion (fase 2 Rey dormido, latente).
    Consola.cs        Consola por fichero (consola.txt -> consola_salida.txt).
    Estado.cs         Estado global del mod (config, reloj, memoria, afectos, evidencias,
                      fichas, facciones, dialecto, cronica, civilizacion, etc.).
    Gancho.cs         Postfix de PawnManager.OpinionDelta (hilo del juego), filtros,
                      lectura de datos, modelo afectivo, memoria, rumores, voz (hebra única LLM).
    Pantalla.cs       Registro de la corte (panel F8/F9), burbujas (F7), cola Principal.
    Peticiones.cs     Rey dormido: Observa() cada 30s (lee petitionQueue + petition activa),
                      dry-run del consejo, ProponeResolver + ProcesaCompuerta (fase 2 latente).
    Plugin.cs         Entry point BepInEx, Update, persiste, informe, sonda, panel, fps.
    Sonda.cs          Sondas de solo lectura (F11): volcado de tipos del juego, firma de
                      PetitionManager/Petition, pawn info.
    stubs/Pecera.Game.Check/   Stubs.cs de tipos falsos para CI (compilación sin el juego).
```

### Flujo del hook (OpinionDelta)
1. El juego llama `PawnManager.OpinionDelta(pawn, subject, delta, reason)`.
2. Postfix: si `Estado.Activo` y no reentrada, filtra por `umbral_dato`, anti-ráfaga por pawn.
3. Lee nombre/valor/razón, identifica con `Identidad`, alimenta `Afectos.DesdeOpinion`.
4. Actualiza memoria y rumores; si hay cupo de voz, lanza una **frase del LLM** (hebra única).
5. `Registra()` escribe una línea en `opiniones.jsonl` y, si hay frase, burbuja en pantalla.
6. Si la actitud es "empeora/perdona/mejora", `Acciones.EmpujaOpinion` (si `influencia=1` y
   `EscrituraSegura`) hace `DeltaOpinionOfSubject` para aplicar el empujón.

---

## 3. ESTADO DE VERIFICACIÓN EN PARTIDA (verificacion.json de hoy)

Versión juego: **0.31.5.3**. Evidencia acumulada:

| Función | Veredicto | Notas |
|---|---|---|
| `hook_opinion` | **insuficiente** (18/50) | El hook corre y cuenta; falta alcanzar 50 para verde |
| `memoria_persistida` | **pasa** | 2 pawns guardados, 12 ok |
| `informe_escrito` | **pasa** | Informe/cronica/grafo generándose |
| `peticion_capturada` | **sin_datos** | Rey dormido fase 1: no ha visto petición real aún |
| `peticion_aplicada` | **sin_datos** | Rey dormido fase 2: latente (requiere la 1) |
| `afectos_modelo` | sin_datos | 20 ok necesarios |
| resto | sin_datos | bocadillo, voto, voz, etc. |

**Lección clave para Opus**: `hook_opinion` llegará a verde con **partida activa larga**;
la **petición** aparece solo cuando el reino genera peticiones (audiencias/obras) — la
columna "activa=no" en el log indica que no se está generando las 24h. Hay que investigar
qué **triggers** crean `Petition` en Noble Fates (probablemente necesitas un Ruler/Kingdom
avanzado, o un evento concreto).

---

## 4. LO QUE SE HA HECHO HOY (2026-10-08) — LA FASE 2 DEL REY DORMIDO

Con la **sonda F11** confirmamos las firmas reales del binario:

**PetitionManager** (Manager<T>.Instance):
```
Void AddType(PetitionType), GetType(String), UpdateLastHeardByTag(String), LastHeard(String),
AddHeardType, HasHeardType, SetPetition(Petition), AddBackgroundPetition, RemoveBackgroundPetition,
EvaluatePetitions(Single), QueuePetition(PetitionType, Pawn, OctScriptContext),
QueuePetition(PetitionType, TimeFrame, Pawn, OctScriptContext), CastPetitioner(...),
CanDrawGuidedExperiencePetition, TryTriggerPetition, ResetGame, Ascend, NewGame, StartPlaying,
GameLoaded, IsGoodTime, CountOf(String), Tick(Single)
prop Petition petition; List`1 backgroundPetitions; TimeStamp nextPetition; List`1 petitionQueue
```

**Petition**:
```
Void Receive(), Complete(), Destroy(), NextStep(), BeginStep(), EndStep(), Tick(Single),
SetMissionTarget, BeginConversation, BeginMission, ...
prop PetitionType type; OctScriptContext context; Int32 step; TimeStamp backgroundExpireAt;
TimeStamp stepStartedAt; Boolean complete; Boolean received; Boolean revealed; Boolean destroying; Boolean destroyed
```

**Qué escribimos HOY** (ficheros tocados):
- `Peticiones.cs` (nuevo): Fase 1 (Observa, dry-run, memoria, evidencia `peticion_capturada`)
  y Fase 2 (ProponeResolver + ProcesaCompuerta).
- `Acciones.cs`: `GobiernoDisponible` dinámico (true cuando `peticion_capturada ok>=1` y
  `fail=0`), `ResuelvePeticion(tipo, object)` que hace Receive+Complete en el hilo principal
  (reflexión, con comprobación de "ya completa"), evidencia `peticion_aplicada`.
- `Estado.cs`: criterios `peticion_capturada` y `peticion_aplicada`.
- `Plugin.cs`: cable `Peticiones.Observa()` cada 30s (en Update).
- `Peticiones.cs`/`Acciones.cs`: eliminada la firma "pendiente" para peticiones.

**El flujo Fase 2**:
1. Cada 30s, `Observa()` lee `petitionQueue` y `petition` (activa).
2. Si hay `petition` activa NO completa ni recibida y `GobiernoDisponible`, `ProponeResolver`
   la propone en la **Compuerta** (`Propone("peticion", ...)`) con veto si `modo=asistente`.
3. `ProcesaCompuerta()` toma las decisiones vencidas (`Listas()`) y llama `ResuelvePeticion`.
4. `ResuelvePeticion` verifica `GobiernoDisponible` + `EscrituraSegura` + `PuedeEscribir`,
   encola en `Principal` (hilo del juego), hace `Receive()` (si existe) y `Complete()` si no estaba completa.

**Seguridad de la Fase 2**: NO toca el juego hasta `peticion_capturada ok>=1` en partida.
El `config.txt` ya tiene `peticiones=1` y `modo=asistente`. DLL instalada = la de `dist/`
(161792 b), idénticas.

---

## 5. LA CONFIG ACTUAL (config.txt, las claves relevantes)

```
activo=1            # el mod está ON
modelo=qwen4b-silly:latest   # Ollama
peticiones=1        # Rey dormido (fase 1 activa, fase 2 latente)
modo=asistente      # asistente: escribe lo habilitado, con veto del jugador
influencia=0        # empuje de opinión APAGADO (no verificado aún a fondo)
esquemas=0 investigacion=0 planos=0 misiones=0   # adaptadores pendientes
burbujas=1 telemetria=1 memoria=1 afectos=1 cronica=1 sociedad=1
```

**Todo lo que escriba en el juego está apagado salvo `peticiones`** (que por diseño actual
escribe solo tras verificar la fase 1).

---

## 6. LA VISIÓN QUE EL USUARIO QUIERE PARA CLAUDE OPUS

> "Que hagan su vida los pj, avancen en el juego, jueguen solos, hagan todo solos y sean
> inteligentes, que hagan su vida aunque yo pueda interactuar, que sepan hacer absolutamente
> de todo, que sean entes vivos, que expandiéramos la pecera de verdad y tengamos esa
> simulación que siempre quise, ahora con la magia de Claude Opus."

Es decir: pasar de "observador social + voz" a **agentes autónomos con propósito, memoria,
relaciones, trabajo, ambiciones y consecuencias**, dentro de Noble Fates, usando el LLM
local (Ollama) para la "mente", e integrándose con las mecánicas reales del juego.

**Referencias de la literatura / proyectos** (para inspirar a Opus):
- **Generative Agents** (Stanford, arxiv 2304.03442): 25 agentes en un pueblo que viven
  días — planifican, recuerdan, reflejan, forman opiniones, celebran fiestas. Arquitectura:
  memoria → reflexión → plan → ejecución → reacción.
- **AI Town** (open-source): pueblito con agentes que hablan y socializan (stack LLM).
- **alife-sdk** (github.com/eurusik/alife-sdk): SDK TypeScript de A-Life: GOAP, facciones,
  economía, amenazas, persistencia.
- **AI-LLM-Driven NPC Generation (Virtual Village)**: aldeanos con LLM que viven e interactúan.
- **MiroFish** (swarm intelligence engine): enjambres de agentes con personalidad y evolución social.
- Noble Fates en sí: juego de simulación con **Wants/Needs**, **Petitions**, **Schemes**,
  **Research**, **Plans**, **Missions** — el juego ya tiene mucha estructura que Pecera puede
  aprovechar (no hay que inventar el simulador, hay que darle alma a los pawns).

---

## 7. QUÉ QUEDA PENDIENTE / QUÉ PUEDE EXPLORAR OPUS

### Pendiente inmediato (verificación)
1. `hook_opinion` a 50 ok (partida larga) — sin bloqueo de código.
2. **`peticion_capturada ok>=1`**: encontrar cómo el juego genera una petición real
   (audiencia, obra, evento) para desbloquear la fase 2. Investigar `PetitionManager.Tick`,
   `PetitionManager.IsGoodTime`, `PetitionType` (definidos en `types`), y los trims.
3. Después: ver `peticion_aplicada` ok>=1 (Complete() se aplicó sin romper).

### Pendiente de diseño (para Opus)
- **Soberano**: hoy `Consejo.Evalua(p, "reino", metas)` usa id "reino" porque no hay id
  escalar de pawn fiable (el informe decía `miembro_id="(ninguno)"`). Con la sonda se puede
  leer `Kingdom`/`Ruler` y obtener un id estable. Eso permite que el consejo discrimine por
  afecto real con el peticionario.
- **Esquemas** (SchemeManager): queremos que los pawns *pidan* cosas (obras, títulos,
  mercenarios, rituales...) usando `TryTriggerScheme`. Firma leída, falta construir
  `ISchemeExecutor`/`OctScriptContext` y confirmar comportamiento.
- **Investigación/Planos/Misiones**: ResearchManager, PlanManager, MissionManager ya están en
  la sonda. Cuando llegue el momento, el Rey dormido debería decidirlos.
- **Voz**: hoy hay una frase por evento de opinión. Para "vida" real hacen falta
  **conversaciones entre pawns**, **acciones autónomas** (trabajar, construir, pedir, viajar),
  y **reflexión/planificación** diaria (como Generative Agents).

---

## 8. ACUERDOS DE CALIDAD (para que Opus los respete)

- Sin `@ts-ignore` ni supresiones de lint.
- `-warnaserror` obligatorio (compila con csc .NET 4 C#5).
- Toda escritura al juego pasa por `Acciones` + Compuerta + `EscrituraSegura` + veto.
- `verificacion.json` es la única verdad de qué está verificado en partida.
- Los tests del Core se ejecutan en la nube (dotnet test de `tests/Pecera.Tests`); este PC
  no tiene dotnet, así que es la CI la que los valida.

---

## 9. CÓMO COMPILAR / INSTALAR (para Opus y el usuario)

En `C:\Users\Jose Luis\pecera-nf`:
```powershell
# Compilar (sin instalar)
.\compilar.ps1 -Juego "D:\SteamLibrary\steamapps\common\Noble Fates"

# Compilar e instalar en BepInEx/plugins (SOLO con el juego cerrado)
.\compilar.ps1 -Juego "D:\SteamLibrary\steamapps\common\Noble Fates" -Instalar
```
- El csc usado es el de .NET Framework 4 (`$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe`).
- El juego está en `D:\SteamLibrary\steamapps\common\Noble Fates`.
- El log del juego: `C:\Users\Jose Luis\AppData\LocalLow\Xobermon, LLC\Noble Fates\Player.log`.
- Los datos del mod se escriben en `...\Noble Fates\BepInEx\plugins\pecera_datos\`.
- F11 en el juego = sonda (vuelca `sonda.json`, `sonda_pawn.json`, `esquemas.sugeridos.txt`).
- F10 = escribe informe `informe.md`+`verificacion.json` ahora.
- Consola por fichero: escribe comandos en `pecera_datos/consola.txt` (estado, pendientes,
  veta <id>, modo observador|asistente|dios, dir..., quita..., deshacer).

---

## 10. ESTRUCTURA QUE SE ENTREGARÁ EN EL RAR

```
info/          <- estos dos documentos + el README/proyecto
  INFORME_DETALLADO.md
  PROMPT_OPUS.md
proyecto/      <- el código fuente completo (sin bin/obj, sin dist viejo)
  src/ tests/ docs/ herramientas/ sim/ sim_out/ stubs/ sesion/ legacy/
  compilar.ps1, Pecera.sln, README.md, AGENTS.md, CLAUDE.md, CHANGELOG.md,
  01_INFORME_PECERA.md, 02_PROMPT_CLAUDE_CODE.md, 03_PROMPT_EXPANDIDO.md,
  VERIFICACION_PENDIENTE.md, .gitignore
```
