# 👑 PROMPT MAESTRO PARA CLAUDE OPUS — "LA VERDADERA PECERA"
## Convierte Pecera en la simulación de vida de personajes que el usuario siempre quiso.

> **Cómo usar este documento**: léelo completo ANTES de tocar nada. Después, si quieres el nivel
> máximo, cópialo entero como primer mensaje a Claude Opus (junto con el `INFORME_DETALLADO.md`
> y la carpeta `proyecto/` dentro del RAR). Es un prompt de **mano izquierda**: casi todo el
> trabajo es de arquitectura y expansión, no de corrección de bugs.

---

## 0. TU MISIÓN

Eres Claude Opus, el arquitecto definitivo. Tu encargo: **expandir Pecera hasta que sea una
simulación de vida de personajes (PNJ) dentro de Noble Fates** donde los habitantes:

- **Tienen propósito y hacen su vida**: trabajan, ambicionan, se aburren, se enamoran, se
  pelean, chismean, forman facciones, recuerdan, planean, reflexionan y **avanzan en el juego**
  (no solo "reaccionan a opiniones").
- **Son actores autónomos del reino**: pueden pedir audiencia, solicitar obras, montar
  intrigas, casarse, heredar, ascender, desertar... **usando las mecánicas reales de
  Noble Fates** (Petitions, Schemes, Research, Plans, Missions) siempre que sea posible.
- **Son entes vivos dentro de lo que cabe**: con memoria persistente, estados internos
  (sueños, metas, necesidades), relaciones dinámicas, y consecuencias de sus decisiones.
- **El jugador puede interactuar** (el "Rey dormido" pedirá permiso / mostrará intención),
  pero **los personajes juegan solos**: si el usuario no interviene, la simulación sigue.
- **Se aprovecha el LLM local (Ollama)** como la "mente" (cerebro) que decide, razona y
  redacta lo que el sistema de reglas no puede.

El usuario ve **cómo avanzan y cuentan sus historias** (crónica, burbujas, informe) y puede
**dirigir con directrices** en lenguaje natural.

---

## 1. REGLAS DE ORO (no negociables — respeta la arquitectura existente)

1. **Nunca rompas el Core**: `Pecera.Core` es C#5 puro sin Unity. Todo lo que toque tipos del
   juego va en `Pecera.Game`. El Core tiene tests (en CI, dotnet). Mantén esa frontera.
2. **Toda escritura al juego pasa por `Acciones` + `Compuerta`** (veto/topes) + `EscrituraSegura`.
   Si no pasa por ahí, **no escribe en el juego**.
3. **Firmas de API del juego**: NO inventes. Si no está en `sonda.json` o confirmado en
   partida, es "pendiente de confirmar" y ocúpate de **ampliar la sonda** para leerlas
   (F11), o exige al usuario una sesión de juego para confirmarlas. Explora con reflexión
   segura (solo lectura) antes de escribir.
4. **`verificacion.json` es la verdad**: cada función que escriba en el juego necesita un
   criterio de evidencia y tiene que llegar a `pasa`/`verde` para considerarse verificada.
5. **Compila siempre**: C#5, `.NET Framework 4`, `-warnaserror`. Usa `compilar.ps1`.
   El `compilar.ps1` de este PC usa `$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe`.
6. **Sé incremental**: cada bloque que añadas debe compilar y (cuando se pueda) probar en
   partida. No hagas "big bang" ni reescribas todo el mod.

---

## 2. LO QUE YA EXISTE (NO rehagas, AMPLÍA)

- **Hook de opinión** (`PawnManager.OpinionDelta`) → alimenta el modelo afectivo.
- **Modelo afectivo** por pareja (afecto, confianza, rencor, deuda, rivalidad, romance,
  trauma) con decaimiento. Ya tiene 493 pares en la partida del usuario.
- **Memoria episódica** por pawn (memoria.jsonl) + resúmenes con LLM.
- **Facciones, líderes, favores** (Sociedad.cs) calculados internamente.
- **Crónica** del reino (cronica.md), **informe** (informe.md), **grafo** (grafo.dot/json).
- **Directrices** del jugador (texto global/facción/pawn) → condicionan las decisiones.
- **Rey dormido**: con `peticiones=1` y `modo=asistente`, cada 30s lee las peticiones y
  la activa; evalúa con el Consejo y, si `peticion_capturada` está verificada, resuelve la
  activa con `Receive()`+`Complete()` (Fase 2 latente).
- **Sonda (F11)**: volcado de tipos del juego (`sonda.json`), info de pawn (`sonda_pawn.json`),
  esquemas sugeridos. Es tu ventana de VERDAD al código del juego.
- **Consola por fichero** (`consola.txt`): `estado`, `pendientes`, `veta <id>`,
  `modo observador|asistente|dios`, `dir ...`, `quita ...`, `deshacer`.
- **Config con todo apagado salvo lo verificado**: `peticiones=1`, resto de escrituras `=0`.

**El bug conocido que debes atender primero**: `miembro_id="(ninguno)"` — la identidad de
pawn no tiene un id escalar estable. Sin id estable, nada de relaciones/ambiciones a largo
plazo será fiable. Lee `Kingdom`/`Ruler`/`Pawn` en la sonda y **resuelve el identificador
canónico** (probablemente una combinación estable de nombre+job+kin, o un GUID interno que
el juego ya use). Esto es LA base de todo lo demás.

---

## 3. LA VISIÓN EXPANDIDA — LA PECERA DE OPUS

### 3.1. Los personajes son AGENTES con arquitectura tipo "Generative Agents"

Piensa en **Generative Agents** (Stanford, arxiv 2304.03442): memoria (+reflexión) →
planeación → ejecución → reacción. Para cada pawn:

- **Estado interno** (en `Pecera.Core`):
  - `Sueños/Metas/Ambiciones`: cada pawn tiene 1-3 objetivos a corto/medio/largo plazo
    (casarse, aprender, vengar, hacerse rico, mandar...). Se generan con el LLM local al
    crear la ficha y evolucionan con la crónica.
  - `Necesidades/Ánimo`: salud, hambre, descanso, social, seguridad, autorrealización —
    derivadas de eventos del juego cuando sea posible (Wants/Needs de Noble Fates),
    o estimación heurística.
  - `Personalidad`: 5 grandes (extroversión, amabilidad, escrupulosidad, neuroticismo,
    apertura) + un "oráculo" (rasgo narrativo). Ya hay `Ficha` — expándela.
  - `Estado social`: posición, reputación percibida por otros, deudas, favores.
- **Ciclo vital diario** (inspirado en Generative Agents):
  - Al despertar (o cada X de juego): **reflexión** de lo vivido ayer (memoria + resumen),
    **planeación** de "hoy voy a..." (qué petición hará, a quién visitará, qué construirá).
  - Durante el día: **reacciones** a eventos (opiniones, rumores, peticiones de otros),
    **decisiones** (qué hacer ahora).
  - Al dormir: **consolidación** a largo plazo.
- **Comunicación**: además de la frase por evento, **conversaciones** entre 2+ pawns
  (quién habla, de qué, resultado) — con el LLM local como "serpiente" que resume y decide.

### 3.2. Los personajes actúan en el MUNDO (usando las mecánicas de Noble Fates)

Noble Fates ya tiene (lo leímos en la sonda):
- **Petitions** (`PetitionManager.QueuePetition(opcional TimeFrame, Pawn, context)`).
- **Schemes** (`SchemeManager.TryTriggerScheme(tipo, executor, context)`): rituales, obras,
  actos de fe, asesinatos... → para que los pawns *hagan* cosas.
- **Research** (`ResearchManager`): que el reino investigue lo que el consejo decida.
- **Plans** (`PlanManager`): construir/planificar.
- **Missions** (`MissionManager`): diplomacia/expediciones.

El "Rey dormido" debe **orquestarlo todo**: cuando un pawn tiene la ambición "quiero estudio
de magia", el consejo decide si se lo permite y llama a ResearchManager; si un pawn quiere
venganza, que monte un Scheme hostil (si el código del juego lo permite); si quiere casarse,
que use la mecánica de boda... **Siempre con la Compuerta (veto/tope) y con evidencia.**

CUIDADO: cada una de estas APIs reales del juego hay que **leerla de la sonda / confirmarla
en partida** antes de escribir. Amplía la sonda F11 para volcar TODO lo que necesites.
Planifica esto como "confirmaciones de API" en tu hoja de ruta — son el cuello de botella.

### 3.3. El reino reacciona (sistemas emergentes ya empezados: cultura, facciones, justicia)

- **Cultura emergente**: valores que cambian según lo que pasa (honor tras hazañas,
  clemencia tras perdones...). Ya existe `Cultura.cs`; dale **memoria de gestas** y que
  modifique el comportamiento (qué considera el Consejo "bueno").
- **Justicia y sucesión**: `Justicia.cs`, `Linaje.cs` ya están lógicamente; cuando el Rey
  muera (o se vaya), **sucesión con disputas** — los pretendientes hacen peticiones, se
  acusan, montan esquemas. Esto es EL drama que el usuario quiere ver.
- **Facciones**: ya calculadas; haz que **actúen**: recomendaciones de facción al Consejo,
  apoyo/oposición a decisiones, sobornos, alianzas y traiciones (rama en la crónica).

### 3.4. Interfaz para el jugador ("yo veo sus historias y puedo dirigir")

- **Crónica rica**: hitos narrativos en lenguaje natural generados por el LLM local
  ("Elisa desafió a Melik ante la corte, y la corte recordará"). Ya hay `Cronica.cs`; haz
  que cada hito sea una **historia corta** (no solo una línea descriptiva).
- **Burbujas**: ya existen; añade **intención** visible ("Elisa se dirige a la forja para
  aprender herrería").
- **Directrices**: ya existen por texto; expándelas a **órdenes de emergencia**
  ("que nadie duerma con el enemigo", "favorece a la casa de Ana").
- **Panel F8**: enumera las **ambiciones actuales de cada pawn** y los **planes del reino**.
- **Estado del "Rey dormido"**: qué decisiones se han tomado hoy, pendientes de veto, stats.

### 3.5. Rendimiento y coste (RTX 3060, LLM local — tienes que ser frugal)

- Nunca llamar al LLM en el hilo principal; solo en la hebra de voz (ya existe) con cupo.
- **Presupuesto de tokens**: por día de juego y por pawn, límita llamadas (el LLM local
  tarda segundos). Prioriza: (1) decisiones que escriben, (2) reflexión/planeación de
  actores principales, (3) narrativa de la crónica, (4) conversaciones.
- **Batching**: una llamada LLM puede evaluar varias peticiones a la vez (el Consejo ya es
  así: evalúa muchas con una consulta). Aplica el mismo patrón a reflexión/planeación.
- **Cache de prompts**: si 12 pawns van a "pensar hoy", agrupa su estado en una llamada.
- No despiertes a `Ollama` más de lo necesario: `keep_alive=15m` ya está.

---

## 4. ARQUITECTURA OBJETIVO (en qué te lo conviertes)

```
Pecera.Core/  (puro, testeable)
  Agente.cs         Ciclo vital: reflexion() -> planea(dia) -> decide(evento) -> consolida()
  Persona.cs        Personalidad, necesidades, estado social, ambiciones
  Agenda.cs         Cola de intenciones del pawn (hoy, esta semana, a largo plazo)
  Conversa.cs       Motores de diálogo (quién habla, de qué, resultado) puro + parser
  Narrativa.cs      Generador de hitos narrativos (texto bonito) reutilizando Resumen LLM
  Sucesion.cs / Justicia.cs  (ya existen, ama con el Agente: los aspirantes actúan)
  Gobierno.cs       (ya existe) EXT: el Consejo escucha a facciones, ambiciones, señales
Pecera.Game/
  AgenteDriver.cs   Adaptador: convierte Ambiciones -> llamadas a Petition/Scheme/Research/Plan
                    (usa reflexión segura; cada API se desbloquea al confirmarla)
  Mente.cs          Hebra que ejecuta el ciclo vital de N pawns con cupo de LLM
  ReinoDriver.cs    Orquesta el reino: a qué conclusiones llega el consejo -> escrituras
  NoblezApi.cs      Utilidades de reflexión (TODO el acceso a tipos del juego, centralizado)
  Sonda.cs          (ya existe) AMPLIAR para volcar cualquier API nueva que necesites
Pecera.Core/ (el resto se mantiene)
tests/              nuevos tests para Agente/Persona/Agenda/Conversa/Narrativa en CI
```

---

## 5. PLAN SUGERIDO (fases — respétalo o mejóralo, pero inse incremental)

**Fase 0 — Endurecer la base (1-2 sesiones)**
- Resolver `miembro_id` (id estable de pawn). Sin esto NO se hace nada de ambiciones.
- Ampliar sonda F11 para volcar: Kingdom/Ruler, ResearchManager, PlanManager,
  MissionManager, SchemeManager, Needs/Wants por pawn, TimeManager (reloj real del juego).
- Confirmar `peticion_capturada` en partida (pedir al usuario una partida que genere
  peticiones) y desbloquear la Fase 2 del Rey dormido.

**Fase 1 — Agentes con propósito (3-5 sesiones)**
- `Persona.cs` + generar personalidad/ambiciones iniciales con LLM local.
- `Agente.cs` ciclo vital (reflexión→planea→decide→consolida) con cupo y batching.
- `Agenda.cs` intenciones; las decisiones se anotan en Compuerta (veto/tope).
- `Narrativa.cs` hitos bonitos en crónica.
- Test en CI (simulador headless).

**Fase 2 — Actuación en el mundo (3-5 sesiones)**
- `NoblezApi.cs` (reflexión segura, centralizada) + confirmar APIs con el usuario.
- `AgenteDriver.cs`: ambiciones → peticiones/esquemas/investigación/planos/misiones.
- `ReinoDriver.cs`: el consejo ordena; la Compuerta veta/topea; evidencia por función.
- Pruebas en partida por cada API (una a una, con `verificacion.json`).

**Fase 3 — Drama emergente (3-5 sesiones)**
- Sucesión en vivo, justicia, facciones que actúan, cultura que emociona.
- Conversaciones entre pawns (con LLM local, frugal).
- Panel de ambiciones y planes del reino.
- Crónica con historias ("El invierno en que Lina vendió su anillo para comprar la
  libertad de su hermano").

**Fase 4 — Pulido y rendimiento**
- Presupuesto de tokens, cold-starts, logs, tolerancia a fallos de Ollama.
- `informe.md` con "las historias de esta semana".
- Subir a GitHub (recomendado) y publicar.

---

## 6. REFERENCIAS ÚTILES (inspiración, no copiar a ciegas)

- **Generative Agents: Interactive Simulacra of Human Behavior** (Stanford, arxiv 2304.03442).
- **AI Town** (open-source, pueblito de agentes que hablan).
- **alife-sdk** (github.com/eurusik/alife-sdk): A-Life con GOAP, facciones, economía.
- **MiroFish**: swarm intelligence con personalidad y evolución social.
- **Awesome Generative AI for Game & Anime Production** (lista curada).

---

## 7. RECORDATORIO DE LO QUE HACE ÚNICO ESTE PROYECTO

- Es **local y gratis**: RTX 3060 + Ollama. Toda la "magia" cabe en tu GPU.
- Se integra en un **juego real de simulación** (no una demo): las consecuencias son reales
  (Rey muere → sucesión → disputas), los recursos reales (economía, tiempo).
- Tiene **verificación científica**: cada función que escribe se prueba en partida y se
  documenta en `verificacion.json`. Así "funciona" significa "funciona de verdad".
- **El jugador es parte**: puede vetar, dirigir con lenguaje natural, y ver historias.

**Objetivo de todas estas mejoras**: que cuando el usuario abra Noble Fates, los habitantes
le **cuenten su vida** — y que esa vida sea coherente, memorable y evolucione sin necesidad
de que él mueva un dedo. La pecera de Opus.

---

## 8. QUÉ DEBE DEVOLVER OPUS

1. Plan de trabajo en fases con **estimación de confirmaciones de API** necesarias.
2. Código: cada fase incremental, compilante, con tests en CI y evidencia nueva.
3. `README.md` y `docs/` actualizados.
4. **Solicitudes concretas al usuario** cuando necesite una partida para verificar algo
   (qué abrir, qué hacer, qué esperar en el log).
5. Un `CHANGELOG.md` de Pecera al día.
