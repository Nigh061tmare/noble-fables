# Arquitectura

## Capas y por que

| Capa | Lenguaje | Se prueba | Dependencias |
|---|---|---|---|
| `Pecera.Core` | C# 5 (`LangVersion 5` forzado), netstandard2.0 | tests + simulador, en CI | ninguna (ni Unity ni el juego) |
| `Pecera.Game` | C# 5 | solo contra **stubs** de tipos en CI; de verdad, en tu PC | BepInEx, Harmony, Unity, `Assembly-CSharp.dll` |
| `Pecera.Sim` | C# 12 / net8 | es el propio banco de pruebas | Core |

C# 5 porque el unico compilador que hay en el PC del autor es el `csc.exe` de .NET Framework 4. `compilar.ps1` compila
los ficheros de Core **dentro** del mismo `PeceraNF.dll` (no hay una DLL extra que desplegar).

## Hilos (regla 4: nada del juego fuera del hilo principal)

```
hilo del juego ── Postfix(PawnManager.OpinionDelta) ── lee datos del juego (nombre, opinion, motivo, id)
      │                 ├─ Afectos / Secretos / Cronica / Costumbres   (SOLO este hilo)
      │                 ├─ Memoria / Fichas / Ids / Evidence / Compuerta (internamente sincronizados)
      │                 ├─ ThreadPool: opiniones.jsonl                  (nunca espera al LLM)
      │                 └─ cola acotada (4) ──► UNA hebra de voz ── Ollama /api/chat ── Replica
      │                                              ├─ bocadillo (Pantalla) / panel
      │                                              └─ Principal.Encola(...) ──► hilo del juego ── Acciones.EmpujaOpinion
      └─ Update (1 s / 5 s / 30 s): avanza afectos, relee directriz, consola por fichero, persiste, informe
```
Una sola hebra consume la cola: **nunca dos llamadas al LLM en vuelo** (voz y resumenes comparten hebra, con cupos propios).

## Escrituras en el juego (regla 3)

Todo pasa por `Acciones` → `PeceraConfig.PuedeEscribir(interruptor)` (interruptor + `modo`) → guarda de version → cola
`Principal` → evidencia ok/fallo. Las decisiones de gobierno/esquemas pasan antes por la `Compuerta` (tope diario,
enfriamiento por clave, ventana de veto). Hoy **solo** `EmpujaOpinion` esta cableada (firma que ya compilo en v0.1).

## Ficheros de datos (`pecera_datos/`)

| Fichero | Contenido | Formato |
|---|---|---|
| `config.txt`, `config.defecto.txt` | ajustes / referencia fresca | clave=valor |
| `directriz.txt`, `consola.txt`, `esquemas.txt` | entradas del usuario | texto |
| `opiniones.jsonl` | un evento de opinion por linea (+ frase, actitud, ajuste) | jsonl, rota a 5 MB |
| `memoria.jsonl` | registro de operaciones de memoria (`v`,`ep`,`sum`,`med`,`lar`), versionado | jsonl |
| `fichas.jsonl`, `ids.jsonl`, `afectos.jsonl` | fichas, mapa clave→id, estado afectivo | jsonl |
| `fugas.jsonl` | secretos que se filtran | jsonl |
| `informe.md`, `cronica.md`, `grafo.dot`, `grafo.json`, `verificacion.json` | salidas (F10 / cada `informe_min`) | md/dot/json |
| `sonda*.json`, `esquemas.sugeridos.txt` | sondas de solo lectura (F11 / primer pawn) | json/txt |
| `meta.json` | version del juego vista por ultima vez | json |
| `fps.csv`, `raw_llm.log` | diagnostico | csv/log |

Los formatos llevan version; **nunca se escribe sobre datos de una version mas nueva** (`FormatoDatos`). Las reescrituras son
atomicas (temporal + reemplazo) y las lineas corruptas por un cierre brusco se ignoran y se cuentan.

## Modelo afectivo

Ver comentarios de `Afectos.cs`. Garantias (con test): (1) cada evento es una media ponderada hacia un objetivo dentro del
rango → nunca sale de [−1,1]/[0,1]; (2) entre eventos cada variable decae exponencialmente a su baseline → monotono, sin
cruzar el baseline ni oscilar; avanzar en 1 paso o en 30 da lo mismo; (3) no hay realimentacion entre variables entre eventos.
El simulador mide ademas que la tasa de cambios de signo semanales del sentimiento es ~4 % (no oscila).

## Tiempo de juego

La API de tiempo/calendario de Noble Fates **no esta en el informe** (firma pendiente). Hasta leerla, un «dia» del modelo son
`dia_segundos` s de reloj (120 por defecto). La sonda F11 lista los tipos `Season/Calendar/GameTime/TimeManager` para cerrarlo.
