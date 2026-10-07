# Ideas mas alla de la lista

Para cada una: valor, riesgo, coste de VRAM/CPU y **estado**. «Implementada» = en Core con tests/simulador; ninguna esta probada en el juego.

| Idea | Valor | Riesgo | Coste | Estado |
|---|---|---|---|---|
| Facciones que se forman solas (propagacion de etiquetas sobre el afecto) | Alto: da estructura social legible | Bajo (solo lectura) | CPU O(n²·iter) cada informe; 0 VRAM | **Implementada** (`Sociedad.Facciones`) |
| Lideres por carisma | Alto | Bajo | despreciable | **Implementada** (`Sociedad.Lider`) |
| Rumores con distorsion | Alto: drama sin LLM | Bajo | despreciable | **Implementada** (`RedSecretos`) |
| Tradiciones emergentes | Medio | Bajo | despreciable | **Implementada** (`Costumbres`) |
| Economia de favores (deuda de gratitud) | Medio | Bajo | despreciable | **Implementada** (`Par.Deuda`, `SaldoDeFavores`) |
| Deriva de personalidad (trauma endurece, gratitud suaviza) con tope ±0.2 | Medio | Bajo | despreciable | **Implementada** (`Deriva`), **no cableada** al juego: el tope evita que alguien «se vuelva otro» |
| Soledad / aislamiento | Medio: alimenta metricas de cohesion | Bajo | despreciable | **Implementada** (`Sociedad.Soledad`) |
| Parejas / matrimonios candidatos | Medio | Bajo (solo candidatos) | despreciable | **Implementada** (detecta); el evento de boda en el juego esta **pendiente de firma** |
| Cronica y leyendas | Alto para el jugador | Bajo | despreciable | **Implementada** (`Cronica`) |
| Modo degradado + guarda de version | Alto: robustez | Bajo | 0 | **Implementada** |
| Consola por fichero con deshacer | Medio | Bajo | 0 | **Implementada** |
| Telemetria de verificacion | Alto: reduce idas y vueltas | Bajo | 0 | **Implementada** |
| Justicia y juicios (acusar al traidor tras una fuga grave) | Alto | Medio: consecuencias reales | 0 | **No implementada**: necesita esquemas/peticiones reales |
| Herencias y sucesion | Medio | Medio | 0 | **No implementada**: necesita API de familia/titulos |
| Mentoria | Medio | Bajo | 0 | **No implementada**: necesita habilidades del pawn |
| Religion / cultura emergente | Medio | Bajo | 0 | **No implementada** (las tradiciones son la base) |
| Espionaje, dialectos, estaciones | Bajo-medio | Bajo | 0 | **No implementadas** |
| Sueños/inspiraciones del pawn | Medio | Medio: afectan a su estado | 0 | **No implementados** |
| Voz con modelo mayor solo para eventos clave | Medio | VRAM: compite con el juego | +3–4 GB | **No**: exige medir antes (regla de rendimiento) |

Criterio de implementacion: solo lo que se puede **probar aqui** y no escribe en el juego. Lo que necesita esquemas, peticiones o
familia espera a las sondas (`sonda.json`).
