# Simulacion headless

`dotnet run --project sim/Pecera.Sim -- --dias 360 --pawns 24 --seed 1 --out sim_out` ejecuta el Core completo contra un
mundo falso: 24 personajes, interacciones sociales aleatorias con semilla, memoria con resumen por un LLM simulado que falla
el 10 % de las veces, secretos y rumores, esquemas, peticiones, investigacion y un reino con economia sencilla.
Genera `informe.md`, `cronica.md`, `grafo.dot` y `metricas.csv`.

## Que SI demuestra

- La logica no se atasca (≤ 7 dias seguidos sin progreso en las semillas probadas; el criterio del test es ≤ 10), no oscila (~4 % de cambios de signo
  semanales) y no se desboca (rencor siempre en [0,1]; estabilidad final > 0.5).
- El gobierno autonomo importa: con gobierno el saber crece (20–22 investigaciones en un anio); sin gobierno, 0 y la seguridad se erosiona.
- Sin LLM (100 % de fallos) el reino sigue creciendo y la memoria sigue acotada (test).
- Los rumores se propagan con distorsion y sin inundar: 2–5 secretos de 24 salen a la luz en un anio, hasta 8 saltos.
- Todo es determinista con semilla (test).

## Que NO demuestra

Las constantes del mundo (produccion, efecto de obras, coste de investigaciones, probabilidades de interaccion) son **supuestos
del simulador**, no medidas de Noble Fates. Sirve para comprobar la logica y comparar variantes **relativamente**, no para predecir
valores absolutos del juego.

## Barrido de umbrales (evidencia para el defecto)

360 dias × 5 semillas por fila, 20 personajes; `umbral` = `Consejo.UmbralAprobacion`.

| umbral | aprobadas | denegadas | investigaciones | rencor max | oscilacion % | saber final | estabilidad |
|---|---|---|---|---|---|---|---|
| 0.35 | 88 | 38 | 21.4 | 0.921 | 4.53 | 0.713 | 0.779 |
| 0.40 | 73 | 54 | 20.8 | 0.938 | 4.40 | 0.694 | 0.778 |
| **0.45** | 52 | 72 | 20.4 | 0.942 | 4.32 | 0.646 | 0.770 |
| 0.50 | 34 | 90 | 20.2 | 0.906 | 4.00 | 0.605 | 0.770 |
| 0.55 | 16 | 106 | 19.8 | 0.911 | 3.74 | 0.528 | 0.778 |

Lectura honesta: el umbral **casi no cambia la estabilidad ni la oscilacion**; solo cambia cuantas peticiones se aprueban y, con ello,
el saber. Se deja 0.45 (≈ 42 % de aprobacion: ni complaciente ni cerrado) como defecto **razonable, no optimo**: el simulador no tiene
informacion para decir que otro valor sea mejor. Reproducir: `-- --barrido --dias 360 --pawns 20`.

Otro hallazgo del simulador (arreglado): sin una *semilla* de secretos nadie los contaba; se anadio la **confidencia** (con mucha
confianza y afecto el propio sujeto lo confia). Defectos elegidos con el simulador: `ConfianzaConfidencia 0.55`, `ProbConfidencia 0.05`
(con 0.62/0.02 casi ningun secreto salia a la luz en un anio).

Las tradiciones no llegan a emerger en 360 dias porque hacen falta 3 anios distintos en la misma temporada (`Costumbres`, con test unitario);
con `--dias 1080` pueden verse.
