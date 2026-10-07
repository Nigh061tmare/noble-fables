# Ideas mas alla de la lista

Para cada una: valor, riesgo, coste de VRAM/CPU y **estado**. «Implementada» = en Core con tests/simulador; ninguna esta probada en el juego.

| Idea | Valor | Riesgo | Coste | Estado |
|---|---|---|---|---|
| Facciones que se forman solas (propagacion de etiquetas sobre el afecto) | Alto: da estructura social legible | Bajo (solo lectura) | CPU O(n²·iter) cada informe; 0 VRAM | **Implementada** (`Sociedad.Facciones`) |
| Lideres por carisma | Alto | Bajo | despreciable | **Implementada** (`Sociedad.Lider`) |
| Rumores con distorsion | Alto: drama sin LLM | Bajo | despreciable | **Implementada** (`RedSecretos`) |
| Tradiciones emergentes | Medio | Bajo | despreciable | **Implementada** (`Costumbres`) |
| Economia de favores (deuda de gratitud) | Medio | Bajo | despreciable | **Implementada** (`Par.Deuda`, `SaldoDeFavores`) |
| Deriva de personalidad (trauma endurece, gratitud suaviza) con tope ±0.2 | Medio | Bajo | despreciable | **Implementada** (`Deriva`), ejercitada en el simulador (deriva maxima observada 0.05), **no cableada** al juego: el tope evita que alguien «se vuelva otro» |
| Soledad / aislamiento | Medio: alimenta metricas de cohesion | Bajo | despreciable | **Implementada** (`Sociedad.Soledad`) |
| Parejas / matrimonios candidatos | Medio | Bajo (solo candidatos) | despreciable | **Implementada** (detecta); el evento de boda en el juego esta **pendiente de firma** |
| Cronica y leyendas | Alto para el jugador | Bajo | despreciable | **Implementada** (`Cronica`) |
| Modo degradado + guarda de version | Alto: robustez | Bajo | 0 | **Implementada** |
| Consola por fichero con deshacer | Medio | Bajo | 0 | **Implementada** |
| Telemetria de verificacion | Alto: reduce idas y vueltas | Bajo | 0 | **Implementada** |
| Justicia y juicios (acusar tras una fuga grave) | Alto | Medio: consecuencias reales | 0 | **Implementada** (`Tribunal`): sesgo del juez medido y explicado, penas irreversibles solo en modo dios, pasa por la `Compuerta` (clase `juicio`, con veto). Adaptador al juego **pendiente** |
| Herencias y sucesion | Medio | Medio | 0 | **Implementada** (`Linaje`, `Sucesion`): herederos por edad/conyuge, reparto que conserva los bienes, disputas con rivalidad. Falta leer la API de familia/titulos del juego |
| Mentoria | Medio | Bajo | 0 | **Implementada** (`Mentoria`): empareja por brecha y simpatia, tope de aprendices, el aprendiz no alcanza al maestro y nace gratitud. Falta leer las habilidades del pawn |
| Religion / cultura emergente | Medio | Bajo | 0 | **Implementada** (`Cultura`): valores y credo salen de lo que pasa, mitos = hitos pesados, fe acotada. Cableada (solo informe y prompts) |
| Espionaje | Bajo-medio | Bajo | 0 | **Implementado** (`RedSecretos.Espia`): exito segun carisma/locuacidad, riesgo de ser descubierto = traicion |
| Dialectos por faccion | Bajo-medio | Bajo | 0 | **Implementados** (`Dialecto`): giros acotados (max 5), solo texto. Cableados al prompt de la voz |
| Estaciones | Bajo-medio | Bajo | 0 | **Implementadas** (`Estaciones`): modificadores suaves 0.7–1.3, usadas en el simulador; sin cablear (la API de calendario del juego esta pendiente) |
| Suenos / inspiraciones del pawn | Medio | Bajo (solo relato) | 0 | **Implementados** (`Suenos`): el sueno sale de la meta de la ficha, hitos 25/50/75/100 %. Sin cablear al juego |
| Voz con modelo mayor solo para eventos clave | Medio | VRAM: compite con el juego | +3–4 GB | **No**: exige medir antes (regla de rendimiento) |

Criterio de implementacion: solo lo que se puede **probar aqui**. Todas las ideas estan en Core con tests y ejercitadas en el simulador; ninguna
escribe en el juego. Las que necesitan datos del juego (familia, habilidades, calendario, esquemas) esperan a la sonda (`sonda.json`).
Unica idea sin implementar: **voz con modelo mayor para eventos clave** (exige medir VRAM/latencia antes).
