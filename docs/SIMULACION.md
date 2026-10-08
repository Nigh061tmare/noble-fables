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

360 dias × 5 semillas por fila, 20 personajes, **con todas las ideas activas**; `umbral` = `Consejo.UmbralAprobacion`.

| umbral | aprobadas | denegadas | investigaciones | rencor max | oscilacion % | saber final | estabilidad |
|---|---|---|---|---|---|---|---|
| 0.35 | 85 | 40 | 21.8 | 0.930 | 5.51 | 0.724 | 0.764 |
| 0.40 | 66 | 50 | 21.4 | 0.940 | 5.15 | 0.694 | 0.778 |
| **0.45** | 50 | 71 | 20.6 | 0.948 | 5.10 | 0.633 | 0.762 |
| 0.50 | 33 | 85 | 20.6 | 0.946 | 4.92 | 0.613 | 0.758 |
| 0.55 | 16 | 103 | 20.4 | 0.939 | 4.91 | 0.578 | 0.768 |

Max dias seguidos sin progreso: 7 en todas las filas. Lectura honesta: el umbral **casi no cambia la estabilidad ni la oscilacion**; solo cambia cuantas
peticiones se aprueban y, con ello, el saber. Se deja 0.45 (≈ 41 % de aprobacion) como defecto **razonable, no optimo**: el simulador no tiene informacion para
decir que otro valor sea mejor. Reproducir: `-- --barrido --dias 360 --pawns 20`.

## Las ideas dentro del simulador (3 semillas, 360 dias, 24 personajes)

| Idea | Resultado observado | Lectura |
|---|---|---|
| Sucesion | El soberano muere a mitad del periodo; sucede un hijo/pariente; herencia 30/30 repartida; disputa si el margen es < 0.03 | La conservacion de bienes esta ademas probada con tests de propiedades |
| Justicia | 26–41 juicios propuestos, 17–23 condenas y 3–11 absoluciones ejecutadas (tope 1/dia + veto) | Solo penas seguras; los jueces con rencor condenan mas (test) |
| Mentoria | 10–19 lazos; habilidad media 0.41–0.57 → ~0.80–0.82 | Converge sin alcanzar al maestro |
| Espionaje | 11–13 exitos, 4–8 descubiertos por anio | El descubrimiento se vuelve traicion |
| Rumores | 15–23 de 24 secretos llegan a mas de 1 persona, alcance medio 8–11, fidelidad media 0.82–0.87, hasta 8 saltos | **Mas que sin ideas** (la mentoria sube la confianza, y la confianza abre confidencias) |
| Suenos | 7–15 inspiraciones, **0 sueños cumplidos en un anio** | Con estos ritmos los suenos son de largo plazo; no se ha forzado para que salgan bonitos |
| Cultura | Credo «Rito de la Deuda» en las 3 semillas | **Artefacto del simulador**: sus esquemas hostiles (170/anio) dominan el contador de `venganza`. En el juego dependera de lo que pase de verdad |
| Dialectos | 30–37 giros en total (≤ 5 por faccion) | Acotado |
| Deriva | maxima 0.025–0.049 (tope 0.2) | Casi imperceptible en un anio |
| Estaciones | modifican sociabilidad e irritabilidad ±10–25 % | Efecto suave, no cambia la estabilidad |

Defectos elegidos con el simulador (y por que): `ProbBase` de chismorreo = **0.08** (con 0.35/0.15 casi todos los secretos eran de dominio publico en un anio),
`ConfianzaConfidencia 0.55`, `ProbConfidencia 0.05` (con 0.62/0.02 casi ningun secreto salia a la luz sin mentoria).
Los supuestos del simulador (porcentaje de interacciones positivas 62 %, etc.) estan en `Mundo.cs`.

Las tradiciones no llegan a emerger en 360 dias porque hacen falta 3 anios distintos en la misma temporada (`Costumbres`, con test unitario).


## Fase 3 (agentes realistas) — 24 pawns, 360 días, semillas 1–4

| Métrica | Resultado | Criterio del test |
|---|---|---|
| Ambiciones cumplidas al año | 16–18 (≈ 0.7 por pawn) | entre 4 y 72 |
| Necesidad social media / más urgente media | 0.67–0.78 / 0.27–0.34 | social entre 0.2 y 0.95; urgente > 0.05 |
| Pawns-día bajo su umbral de ruptura | 2.1–3.5 % | ≤ 15 % |
| Rupturas al año (retiro/arrebato/hundimiento) | 13–34 (todas retiros) | 1 … 864 |
| Reflexiones / conclusiones | 467–521 / 656–719 | > 24 |
| Tensión media / objetivo medio | 0.42–0.45 / 0.45 | entre 0.30 y 0.60 |
| Días a > 0.2 de la curva objetivo | 20–24 % | ≤ 35 % |
| Eventos del director (escándalos / rivalidades / fiestas / reconciliaciones) | ≈ 70 (18–27 / 17–21 / 13–17 / 15–16) | drama > 10 y alivio > 10 |
| Normas aprobadas / derogadas | 2–3 / 0–1 | derogadas ≤ aprobadas ≤ 6 |
| Oscilación semanal / estabilidad / días sin progreso | 5.2–5.7 % / > 0.5 / ≤ 5 | < 10 % / > 0.5 / ≤ 10 |

Por qué cambió el diseño al medir: (1) los planes de 3 pasos se completaban en 3 días y salían 80–140 ambiciones cumplidas al año → pasos con ≥ 2 días entre sí y plazo mínimo por ambición;
(2) con ánimo = 0.6·necesidades + 0.3·clima el 40 % de los pawns-día estaba bajo su umbral → suelo de contento 0.3; (3) el director no movía la tensión (medía solo cuota global de pares) → añadir la intensidad de los 5 peores rencores
y eventos más fuertes; (4) las normas parpadeaban → margen de 0.10 entre normas rivales y vigencia mínima de 90 días.
Limitaciones (roles casi todos «mediador», hundimientos casi inexistentes): `docs/INVESTIGACION.md` §3.


## v0.5 «Vida» — 24 pawns + población viva, 360 días, semillas 1–4

| Métrica | Resultado | Criterio del test |
|---|---|---|
| Nacimientos / muertes / llegadas / bodas al año | 4–7 / 3–7 / 3 / 0–3 | nacimientos + llegadas > 0 y muertes > 0 |
| Crisis por estrés al año (arrebatos / hundimientos) | 7–20 (5–13 / 0) | entre 3 y 60 (semilla 1) |
| Estrés medio final (0–400) | 13–18 | — |
| Recuerdos fuertes revividos | 359–500 | — |
| Prejuicio de grupo, hostilidad media día 30 → final | 0.14–0.18 → 0.23–0.29 | > 0 y < 0.6 |
| Propuestas silenciadas por lo aprendido del jugador | 65–132 | > 0 |
| Órdenes: favorecido, aprobadas antes → después | 2–4 → 10–17 | después > antes |
| Persistencia (líneas / ida y vuelta idéntica) | 1540–1680 / sí | idéntica |
| Rupturas de ánimo al año | 27–68 | (sube respecto a 0.4: el estrés y el duelo pesan) |
| Tensión media / días fuera de banda | 0.43–0.44 / 21–22 % | 0.30–0.60 / ≤ 35 % |

Calibración hecha al medir: con la disipación inicial (3–8/día) el estrés nunca pasaba de 2 y no había crisis; con fracasos a 4–12 de estrés salían ~100 crisis/año.
Quedó en disipación 1–3/día, fracaso 1–6, duelo hasta 90 y herida hasta 50 (multiplicados por 0.6–1.4 según neuroticismo).
