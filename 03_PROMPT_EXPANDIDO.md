# MISIÓN: PECERA — la simulación social autónoma definitiva (Noble Fates)

Este prompt SUSTITUYE al de `02_PROMPT_CLAUDE_CODE.md` en lo que se contradiga. En
particular: **ya no hay bloqueo por la Fase 1.** Trabajas en un entorno cloud sin el
juego (sin Windows, sin Ollama, sin GPU) y aun así debes avanzar todo lo que se pueda.

Lee primero `01_INFORME_PECERA.md` entero. Contiene lo verificado, las trampas ya
resueltas y la API del juego (§7). No lo redescubras.

## 0. Contexto del entorno (importante)

- Tú: contenedor Linux cloud. Puedes leer, escribir, ejecutar tests y compilar código C#
  que NO dependa del juego (dotnet/mono si están, o instálalos).
- El usuario: Windows 10, RTX 3060 12 GB, Noble Fates + BepInEx 5.4.23.5, Ollama. Solo
  él puede probar en partida. Os comunicáis por ficheros: tú entregas, él vuelve con logs.
- El `.rar` anterior llegó dañado. Si falta un fichero (p. ej. `noble-fates/compilar.ps1`
  llegó vacío), **recréalo** a partir del informe (§4 lista las referencias de compilación)
  y avísalo. Entrega siempre un **.zip** con rutas con `/`, nunca un .rar.

## 1. Reglas innegociables

1. **Honestidad de estado.** Todo lo que escribas y no hayas ejecutado contra el juego
   es NO VERIFICADO. Marca cada cosa: VERIFICADO / LEÍDO / NO VERIFICADO.
2. **No bajes la vara.** Sin supresiones de avisos, sin `catch {}` que oculte fallos
   relevantes, sin stubs en el código de producción, sin tests desactivados.
3. **Seguridad de partidas.** Toda función que escriba en el estado del juego va tras un
   interruptor de `config.txt`, con tope y enfriamiento. Las funciones sin verificar en
   partida van **apagadas por defecto** (`=0`); las verificadas, encendidas. Nunca cierres
   el juego del usuario sin su confirmación (aplica a los scripts que le entregues).
4. **Hilo principal.** Nada del juego se toca fuera de él; usa la cola `Principal`.
5. **Sin secretos ni derechos ajenos.** Ni claves, ni DLL del juego, ni assets de terceros.
6. **Rendimiento.** Sin caída medible de FPS; una voz cada ≥ 25 s por defecto; nunca dos
   llamadas al LLM en vuelo; modelo por defecto ~3 GB; todo lo mayor, justificado con medidas.
7. **Una fase = un objetivo = un commit**, con tests que pasan. No declares «hecho» sin
   criterio de aceptación comprobado **en lo que sí puedes comprobar** (lógica, tests, compilación).
8. **APIs del juego.** Solo usa firmas ya listadas en el informe §7 o leídas del binario.
   Si necesitas otra y no puedes leerla, déjala tras una interfaz y márcala como
   «firma pendiente de confirmar», nunca la inventes.
9. **Lee por rangos** (nada de volcar ficheros de >300 líneas). Un reintento justificado.

## 2. Estrategia: avanzar sin el juego

Divide el código en capas para poder probar en la nube casi todo:

- **`Pecera.Core`** (C# puro, sin Unity ni tipos del juego, apuntando a net48/netstandard2.0):
  toda la lógica: memoria, afectos, rumores, selección de quién habla, cupos, esquemas,
  gobierno, parser JSON, construcción de prompts, configuración. **Esto se prueba aquí.**
- **`Pecera.Tests`**: tests unitarios y de propiedades con semillas fijas.
- **`Pecera.Sim`**: un simulador headless con un mundo falso (pawns, opiniones, eventos) y
  un LLM simulado (determinista) para ejecutar días de juego enteros y medir que el reino
  evoluciona, no se atasca y no oscila. Úsalo para afinar umbrales y topes.
- **`Pecera.Game`** (adaptadores finos al juego, BepInEx/Harmony): se compila solo en el PC
  del usuario con `compilar.ps1`. Para comprobar **tipos** en la nube, crea `Stubs/` con
  declaraciones mínimas de los tipos del juego usados (`Pawn`, `ISubject`, `FeelingReason`,
  `PawnManager`, …) copiadas del informe §7; en CI compila los adaptadores contra los stubs.
- **CI** (GitHub Actions): build + tests en Linux; compilación de adaptadores contra stubs.

## 3. Sistemas a construir (todos; en este orden de prioridad)

**A. Base verificable.** Refactor a las capas de arriba sin cambiar comportamiento.
Sustituye el parser JSON manual por uno robusto y con tests (casos: escapes, `\uXXXX`,
vallas de markdown, JSON truncado). Parametriza todas las rutas.

**B. Identidad y persistencia.** Ficha por pawn con **id estable** (no el nombre; hoy hay
colisiones): personalidad, metas, miedos, voz, estilo de habla. Memoria episódica en disco
con resumen periódico del LLM (corto/medio/largo plazo), con tests de compactación.

**C. Emociones y relaciones.** Estado afectivo por pareja (afecto, confianza, rencor, deuda,
rivalidad, romance), decaimiento, reconciliación, trauma y gratitud. Modelo matemático
documentado y simulado para comprobar que converge y no oscila.

**D. Secretos, rumores y reputación.** Portar `lords-villeins/plugin/Secretos.cs`: secretos
por pawn, propagación por conversaciones, distorsión del rumor, consecuencias; `fugas.jsonl`.
Red de chismes con medidas (alcance, velocidad, fidelidad).

**E. Intención y esquemas.** Rencores y ambiciones sostenidos que disparan esquemas reales
(`SchemeManager.TryTriggerScheme`). Clasifica los tipos por seguridad; arranca solo con los seguros.

**F. Sociedad emergente.** Facciones/bandos, liderazgo por carisma, tradiciones, fiestas y
rituales, economía de favores, matrimonios, herencias, traiciones, justicia y juicios,
mentoría y aprendizaje, cultura y religión que emergen, leyendas.

**G. Gobierno autónomo («Rey dormido»).** Resolver `PetitionManager.petitionQueue`, elegir
investigación (`ResearchManager`), aprobar planos (`PlanManager`), repartir trabajo y
decidir misiones/diplomacia (`MissionManager`), según metas del reino y personalidad de los
consejeros. Veto del jugador siempre. Todo apagado por defecto hasta verificar.

**H. Crecimiento autónomo.** Métricas (población, estabilidad, riqueza, conocimiento,
cohesión) y bucle de metas; el simulador debe demostrar N días sin atascos.

**I. Dirección del jugador.** Directrices globales, por facción y por personaje; consola en
pantalla; modos observador / mano de dios con topes; todo reversible; sueños e inspiraciones
como canal suave de influencia.

**J. Narrativa.** Crónica del reino (diario generado), resúmenes por temporada, hitos,
legible dentro del juego.

**K. Observabilidad.** Grafo de relaciones, línea temporal, métricas de evolución, informe de
sesión larga, A/B formal del coste del mod (ON/OFF), y **telemetría de verificación**: el mod
registra por sí mismo la evidencia de que cada función funcionó (p. ej. «bocadillo mostrado»,
«empujón aplicado y no reentró») para que el usuario te devuelva un solo fichero.

**L. Robustez.** Modo degradado sin LLM, guardado seguro, detección de actualización del
juego, instalador reproducible, migración de formatos de datos.

Añade lo que se te ocurra más allá (estaciones y calendario, deriva de personalidad,
soledad y contagio emocional, ambición y sucesión, espionaje, lenguaje y dialectos, …). Cada
idea: valor, riesgo, coste de VRAM/CPU. Implementa las que pasen tus tests y el simulador.

## 4. Verificación delegada al usuario

Entrega `VERIFICACION_PENDIENTE.md`: una lista numerada, ordenada por riesgo, con
**exactamente** qué hacer en el juego (pasos), qué interruptor activar, qué fichero devolver y
qué resultado indica éxito o fallo. Incluye siempre primero las del informe §8 (bocadillos,
empujón de opinión, reentrada). Cuantas menos idas y vueltas, mejor: agrupa pruebas.

## 5. Cómo trabajar

- Empieza con un **plan de 1 página y riesgos**; avanza sin pedir permiso para lo reversible.
- Por fase entrega: diseño, código, tests, resultados del simulador, lo NO VERIFICADO,
  riesgos nuevos y coste estimado.
- Si te quedas sin presupuesto, **prioriza**: A, B, C, K, luego D, G, E, F, resto. Deja siempre
  el repositorio compilando y con tests en verde.
- Commits pequeños y mensajes claros. Mantén `CHANGELOG.md` e informe al día.

## 6. Entregables finales

1. Repositorio en una rama, compilando y con tests en verde, más un **.zip** con rutas `/`
   que contenga TODO (incluidos `compilar.ps1`, scripts y datos de muestra).
2. `CHANGELOG.md`, informe actualizado, `README` reproducible, `config.txt` documentado.
3. `VERIFICACION_PENDIENTE.md` y un resumen honesto: qué funciona (probado aquí), qué es
   NO VERIFICADO (necesita partida), y qué sigue pendiente.

## 7. Prohibido

Reescribir todo desde cero; inventar firmas del juego; automatizar decisiones irreversibles
sin interruptor y tope; declarar «hecho» sin criterio comprobado; dejar el repo sin compilar.
