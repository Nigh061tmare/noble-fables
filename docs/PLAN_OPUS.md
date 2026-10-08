# Plan de trabajo — «La verdadera Pecera» (respuesta al PROMPT_OPUS)

Estado de partida (08-oct-2026): v0.2.2 tras integrar el traspaso. Marcas: **HECHO-NUBE** (codigo + tests + simulador, nunca ejecutado en el juego),
**VERIFICADO-PC** (visto en tu PC), **PENDIENTE-API** (necesita confirmar una firma del juego), **PENDIENTE-PC** (necesita una sesion de juego tuya).

## 0. Lo que ya hace esta version (v0.3.0, HECHO-NUBE)

| Pieza del prompt | Donde | Estado |
|---|---|---|
| `Persona` (Big Five + oraculo + necesidades + ambiciones) | `Core/Persona.cs` | HECHO-NUBE, 6 tests, generacion determinista sin LLM y refinable con LLM |
| `Agenda` (cola de intenciones, topes, enfriamientos) | `Core/Agenda.cs` | HECHO-NUBE; sin spam de peticiones/venganzas (test) |
| `Agente`: reflexion → plan → consolida | `Core/Agente.cs` | HECHO-NUBE; las ambiciones evolucionan con lo vivido |
| Planificador (utility-AI) + guardarrailes `Valida` | `Core/Agente.cs` | HECHO-NUBE; **el LLM no puede saltarse lo que las reglas prohiben** |
| Plan con LLM **en lote** (1 llamada, N pawns) | `Core/Agente.cs` (`PlanLlm`) | HECHO-NUBE; JSON invalido / tipo inventado / objetivo fantasma / id repetido se rechazan |
| Presupuesto de LLM por dia y prioridad | `Core/Agente.cs` (`PresupuestoLlm`) | HECHO-NUBE; escribir > planear > narrar > conversar, con techos |
| Conversaciones (resultado por reglas, texto por LLM) | `Core/Conversa.cs` | HECHO-NUBE; el mundo no depende de que el modelo acierte |
| Narrativa (historias por temporada) | `Core/Narrativa.cs` | HECHO-NUBE; plantilla siempre, LLM opcional y validado |
| `Mente` en el juego (plan por reglas + lote LLM + observacion) | `Game/Mente.cs` | **compila contra stubs; PENDIENTE-PC** (`agentes=1`) |
| Panel de ambiciones | `agentes.md`, `historias.md` | PENDIENTE-PC |
| Identidad estable de pawn | `Game/Identidad.cs` + `Core/NombreMiembro.cs` | Busqueda por tokens + unicidad en vivo; **PENDIENTE-PC** (`sonda_pawn.json`) |
| Sonda ampliada | `Game/Sonda.cs` (`Mundo()`) | F11 ahora tambien escribe `sonda_mundo.json`; **PENDIENTE-PC** |

El simulador (24 pawns, 360 dias) demuestra con 4 semillas: ambiciones que se cumplen (13–16 por anio) y avanzan (progreso medio ~0.5), necesidades sanas
(social ~0.45–0.55), 1 llamada de planificacion al dia, **nunca por encima del presupuesto**, el LLM simulado cometiendo errores a proposito (~20 %) sin que nada
invalido llegue a ejecutarse, y el reino sigue creciendo/estable. **Todo es relativo al simulador, no al juego.** Detalle: `docs/SIMULACION.md`.

## 1. Confirmaciones de API que faltan (el cuello de botella)

Cada fila es una «ronda»: yo escribo la sonda/adaptador, tu juegas ~15 min y me devuelves un fichero.

| # | Necesito | Para | Como se confirma | Rondas |
|---|---|---|---|---|
| A1 | Id escalar estable del pawn | todo lo de relaciones a largo plazo | `sonda_pawn.json` (`candidatos_id`) + evidencias `id_clave_unica` / `id_clave_estable` | 1–2 |
| A2 | Reloj/calendario real (dia, estacion) | sustituir `dia_segundos` | `sonda_mundo.json` (managers `Time/Calendar/Season`) | 1 |
| A3 | Soberano / reino (`Kingdom`, `Ruler`) | que el consejo juzgue por el afecto REAL con el peticionario (hoy usa el id `"reino"`) | `sonda_mundo.json` | 1 |
| A4 | Necesidades/deseos por pawn (`Wants/Needs`) | sustituir las necesidades estimadas | `sonda_mundo.json` (`pawn_metodos`) | 1 |
| A5 | Habilidades y trabajo del pawn | `Mentoria`, ambicion «aprender/enriquecerse» | `sonda_mundo.json` | 1 |
| A6 | Familia / matrimonio / titulos | `Linaje`, `Sucesion`, ambicion «casarse» | `sonda_mundo.json` | 1–2 |
| A7 | `Petition.Complete()` inocuo (ya escrito) | Rey dormido fase 2 | evidencia `peticion_aplicada` con `peticiones_aplicar=1` **y copia de partida** | 1 |
| A8 | Crear una peticion: `QueuePetition(PetitionType, Pawn, OctScriptContext)` | que los pawns PIDAN | construir `OctScriptContext` y `PetitionType` por nombre (`GetType(string)`) | 2 |
| A9 | `SchemeManager.TryTriggerScheme(type, ISchemeExecutor, OctScriptContext, out Scheme)` | intrigas, cortejos, fiestas reales | `ISchemeExecutor`/`OctScriptContext` del pawn | 2–3 |
| A10 | `ResearchManager` (elegir investigacion) | consejo decide el saber | sonda de firmas + 1 prueba | 2 |
| A11 | `PlanManager.AddPlan` (aprobar obras) | consejo decide obras | idem | 2–3 |
| A12 | `MissionManager` (diplomacia/expediciones) | diplomacia | idem | 3 |
| A13 | Eventos de conversacion (`ConversationManager`) | mostrar dialogos y alimentar chismes con conversaciones REALES | sonda + gancho de solo lectura | 1–2 |

Estimacion: **~17–24 rondas** si se hacen una a una; se pueden **agrupar**: A1–A6 caben en 2 sesiones (una sonda), A7 en otra, A8–A13 en ~8–10.
Orden recomendado por valor/riesgo: A1 → A2/A3 → A7 → A8 (peticiones propias) → A9 (esquemas) → A10/A11 → A4–A6 → A13 → A12.

## 2. Fases

**Fase 0 — Endurecer la base (casi hecha en la nube).**
- HECHO-NUBE: identidad por tokens + unicidad, sonda ampliada, `peticiones_aplicar` (tres llaves), bug del dry-run duplicado, tests de regresion de `VersionDe`.
- PENDIENTE-PC: ronda A1+A2+A3+A4+A5+A6 (una sola sesion de F11 → `sonda_pawn.json`, `sonda_mundo.json`, `sonda.json`).
- PENDIENTE-PC: forzar una peticion real para verificar la fase 1 del Rey dormido (ver §3, solicitud 3).

**Fase 1 — Agentes con proposito.** HECHO-NUBE salvo: ejecucion de intenciones en el juego (depende de A8/A9) y necesidades reales (A4).
Hasta entonces `Mente` **solo observa**: da una intencion por hecha cuando el juego produce el evento que esperaba (evidencia `agentes_intencion_observada`).

**Fase 2 — Actuacion en el mundo.** `NoblezApi.cs` (reflexion centralizada, hoy repartida en `Peticiones.cs`/`Sonda.cs`/`Identidad.cs`) + `AgenteDriver.cs`
(intencion → `QueuePetition` / `TryTriggerScheme` / investigacion / plano). Un adaptador por ronda (A8–A12), cada uno tras su interruptor, tope diario,
enfriamiento, veto y evidencia, **apagado por defecto**.

**Fase 3 — Drama emergente.** La logica (sucesion, justicia, facciones, cultura, conversaciones, historias) esta HECHA-NUBE; falta conectarla a datos reales (A6, A13)
y al `AgenteDriver`. Panel F8 de ambiciones: pendiente (hoy `agentes.md`).

**Fase 4 — Pulido.** Presupuesto adaptativo segun la latencia medida del LLM, `informe.md` con las historias de la semana (ya existe `historias.md`), test de
integracion del pipeline del hook, medicion real del tiron del informe con muchos pawns.

## 3. Solicitudes concretas al usuario (ordenadas)

1. **Compila e instala la v0.3.0** (`.\compilar.ps1 -Instalar`, juego cerrado). Pegame la salida si hay errores: es la primera vez que `Mente.cs`, `Identidad.cs` y `Sonda.Mundo()` ven el compilador real.
2. **Sesion de sonda (15 min)**: carga la partida, juega 5 min, pulsa **F11** y luego **F10**. Devuelveme: `sonda.json`, `sonda_pawn.json`, **`sonda_mundo.json`** (nuevo), `verificacion.json`.
   Mira tambien en `verificacion.json` las evidencias `id_clave_unica` e `id_clave_estable` y el campo `miembro_id`.
3. **Para verificar la fase 1 del Rey dormido** (`peticion_capturada`): con `peticiones=1` (y `peticiones_aplicar=0`) juega hasta que aparezca una peticion o audiencia del reino.
   Si en 20 min el log dice siempre `activa=no`, dime que haces tu en el juego (construir, reclutar, tener un rey/reina asignado, etc.) y la forzamos; la sonda ya lista `TryTriggerPetition` e `IsGoodTime`.
4. **Sesion de agentes (20 min)**: pon `agentes=1` (no escribe en el juego), juega y devuelveme `agentes.md`, `historias.md`, `verificacion.json`
   (criterios `agentes_plan_reglas` ≥ 10, `agentes_plan_llm` ≥ 3, `agentes_intencion_observada` ≥ 1) y dime si notas un tiron cada ~2 minutos (el reparto circular lo acota a 40 pawns por dia de juego).
5. **Solo cuando quieras probar la fase 2** (`peticiones_aplicar=1`): copia de partida primero. Una peticion activa se completara ~30 s despues (ventana de veto: `veta <id>` en `consola.txt`).

## 4. Riesgos nuevos de esta version

- `Mente` hace una llamada al LLM por dia de juego (lote de 6): comparte hebra con la voz (nunca dos en vuelo) pero **suma ~1,4 s de GPU por dia** → sube `dia_segundos` o baja `agentes_llm_dia` si el juego se resiente.
- El plan por reglas recalcula 40 pawns por dia: coste O(40·n) en el hilo principal (medido solo en la nube).
- La identidad por tokens puede elegir un miembro que NO sea un id (p. ej. un contador de instancia); la unicidad en vivo lo detecta si colisiona, pero **no** si cambia entre sesiones: eso lo mide `id_clave_estable`.
- Las necesidades son estimadas: hasta A4 no reflejan el hambre/sueño reales del pawn.
