# Verificacion pendiente (la haces tu, en tu PC)

Solo tu puedes probar el mod en partida. Esta lista esta **ordenada de menor a mayor riesgo** y agrupada para
minimizar idas y vueltas: con **dos sesiones de juego** (una corta de 15 min y una larga de 60 min) se cierra casi todo.
Al final de cada sesion pulsa **F10** y devuelveme **un solo fichero**: `pecera_datos/verificacion.json`
(el mod mide por si mismo cada criterio y dice `pasa` / `falla` / `insuficiente` / `sin_datos`).
Si algo falla, anade `pecera_datos/informe.md` y `BepInEx/LogOutput.log`.

Leyenda: **[INT]** interruptor de `pecera_datos/config.txt` · **[EVID]** nombre de la evidencia en `verificacion.json`.

## 0. Preparacion (una vez)

1. Haz **copia de tu partida** (carpeta de guardados) antes de nada.
2. Cierra el juego tu mismo (el DLL esta bloqueado mientras corre). Nunca lo cierro yo.
3. Compila e instala: `.\compilar.ps1 -Instalar` (detecta tu carpeta de Steam; o `-Juego "D:\...\Noble Fates"`).
   - Criterio de exito: sale `=== OK ... KB`, codigo de salida 0 y **cero avisos** (`-warnaserror`).
   - Si falla: pegame la salida completa. Es la primera vez que este codigo toca el compilador real.
4. Arranca el juego. El primer arranque **migra** tu `config.txt` antiguo (deja `config.v1.bak.txt`) y pone
   `influencia=0` aunque antes valiese 1 (estaba sin verificar). Mira el log: debe decir
   `[Pecera] parcheado: PawnManager.OpinionDelta` y `v0.2.0`.

## SESION CORTA (15 min, sin escribir en el juego) — riesgo bajo

Config: la de defecto (todo lo que escribe en el juego esta a 0). Juega normal 15 min en una partida con muchos pawns.

| # | Que hacer | Que mirar | Exito / fallo |
|---|---|---|---|
| 1 | Jugar 15 min. Pulsa **F10** al final. | [EVID] `hook_opinion` | `pasa` si >= 50 llamadas y 0 fallos. `falla`: el hook de v0.1 ya no funciona (¿el juego se actualizo?). |
| 2 | Pulsa **F7** hasta que las burbujas esten **on**. Espera >= 5 frases. | [EVID] `bocadillo_dibujado` y **tu vista**: ¿ves el bocadillo encima del pawn que habla? | `pasa` (>=5 y 0 fallos). Si la evidencia pasa pero no ves nada: captura de pantalla (el dibujo se ejecuta pero fuera de campo). |
| 3 | (nada, pasa solo) | [EVID] `voz_frase`, `voz_json_valido` y `raw_llm.log` | `voz_json_valido` deberia pasar: si falla pero `voz_frase` pasa, el modelo devuelve JSON roto y el parser lo repara (dime cuanto). |
| 4 | Pulsa **F11** una vez. | Se crean `sonda.json`, `sonda_pawn.json`, `esquemas.sugeridos.txt`. | **Devuelveme esos 3 ficheros**: con ellos escribo los adaptadores de esquemas, peticiones, investigacion y planos (hoy son «firma pendiente de confirmar»). `sonda_pawn` ok = hay datos. |
| 5 | Mira `verificacion.json` → `miembro_id`. | Si pone `Pawn.id` / `Character.guid`…: hay un id candidato. `(ninguno)`: no hay y se usa el nombre. | Informativo. Decide como identificamos pawns. |
| 6 | **Con el juego abierto, mata Ollama** (cierra el proceso) 2 min, luego arrancalo. | [EVID] `voz_frase` falla; `informe.md` avisa «modo degradado»; **el juego no se congela**; al volver Ollama, vuelven las frases. | `pasa` si el FPS no cae y se recupera solo. |
| 7 | **Guarda**, sal del juego tu mismo, vuelve a entrar con la misma partida, juega 5 min, **F10**. | [EVID] `memoria_persistida` y `id_clave_estable` (campos `ids_coinciden` / `ids_discrepan`). | `id_clave_estable=pasa`: los ids sobreviven al reinicio. `falla`: el juego renumera → la memoria por id no vale y hay que usar otra clave. |

Prueba de la consola (30 s): en `pecera_datos/consola.txt` escribe `estado` y espera 5 s; el resultado esta en `consola_salida.txt`.

## SESION LARGA (60 min) — riesgo medio (escribe en el juego)

**Antes de empezar: copia de partida otra vez.** Pon en `config.txt`: `influencia=1`, `fps_log=1`. Reinicia el juego.

| # | Que hacer | Que mirar | Exito / fallo |
|---|---|---|---|
| 8 | Jugar 60 min normales. **F10** cada ~20 min y al final. | [EVID] `empujon_opinion` (>=3, 0 fallos) | `falla`: excepcion al llamar `DeltaOpinionOfSubject`. Pega `ultimo_error`. |
| 9 | Durante la sesion, **pasa el raton por la opinion** de un pawn que haya recibido empujon (tooltip de opiniones). | Que el tooltip no se rompa ni muestre cosas raras. | Anota si ves duplicados o nombres raros: el motivo se reutiliza tal cual. |
| 10 | (pasa solo) | [EVID] `reentrada_bloqueada` | `sin_datos` = el juego **no** re-dispara el hook dentro de nuestro empujon (bien). `pasa` = lo re-disparaba y la guarda lo evito. Ambos validos. |
| 11 | Abre `opiniones.jsonl` (o `informe.md`). | Ningun `ajuste` > 0.25 en valor absoluto, y no mas de 30 empujones/hora. | Si los supera, la politica falla: pegame las lineas. |
| 12 | Mira el **FPS** (panel F8 o `fps.csv`). | Sin caidas visibles. | Ver A/B abajo para la medida formal. |

### A/B formal del coste (se hace UNA vez, 2 x 10 min)

1. Carga la **misma partida y la misma escena** (camara quieta, misma velocidad). `fps_log=1`.
2. Mod ON: `activo=1`. Juega 10 min. Guarda `fps.csv` como `fps_on.csv` y borra el original.
3. Mod OFF: `activo=0` (el mod queda inerte; `fps_log` sigue midiendo). Reinicia. 10 min. Guarda `fps_off.csv`.
4. `python herramientas\pecera_ab.py fps_on.csv fps_off.csv` → veredicto. **Devuelvemelo.**
   Exito: `sin caida medible` (delta < 3 % o |t| < 2).

## Pruebas OPCIONALES (cada una enciende una funcion; solo si quieres probarla)

| # | Interruptor | Que hacer | Evidencia / exito |
|---|---|---|---|
| 13 | `rumores=1` (solo modelo interno y memoria, no escribe en el juego) | Jugar 30 min. | [EVID] `rumor_fuga` >= 1; `fugas.jsonl` con lineas; en `informe.md` aparecen en la cronica. |
| 14 | `modo=observador` | Con `influencia=1`, comprobar que **no** hay empujones. | [EVID] `empujon_opinion` sigue `sin_datos`. |
| 15 | Actualizacion del juego | Tras la siguiente actualizacion de Noble Fates, arranca. | El log dice «escrituras BLOQUEADAS»; pon `confirmar_version=<version>` y se desbloquean. |

## NO se puede probar todavia (necesita que escriba antes los adaptadores, con tus sondas)

- **Esquemas** (`esquemas=1`), **investigacion**, **planos**, **misiones**:
  la logica de decision esta hecha y probada en el simulador (ver `docs/SIMULACION.md`), pero los adaptadores al juego estan
  marcados «firma pendiente de confirmar» y **no hacen nada** hasta que me devuelvas `sonda.json` (paso 4).
  Con esos interruptores a 1 hoy **no ocurre nada** (el mod lo avisa en el log al arrancar). No es un fallo: es lo pendiente.

## Rey dormido (punto G) — FASE 1 (ya escrita, solo observa) y FASE 2 (instalada, latente)

Con las firmas confirmadas por tu `sonda.json` escribi el adaptador de peticiones en dos fases
(`src/Pecera.Game/Peticiones.cs` + el punto unico de escritura en `Acciones.cs`). Ambas estan
compiladas e instaladas (DLL en `plugins/`, tamano ~158 KB).

**FASE 1 (solo lectura + dry-run, SIEMPRE activa con `peticiones=1`):**
- Cada 30 s lee `PetitionManager.Instance.petitionQueue` y la peticion activa (`petition`).
- Cada peticion NUEVA la apunta en `memoria.jsonl` y deja evidencia `peticion_capturada`.
- Evalua la peticion con el consejo y anota la decision en `cronica.md`/`informe.md` (interna).

**FASE 2 (escribir la decision con `Complete()`, INACTIVA salvo que lo pidas con `peticiones_aplicar=1`):**
- `GobiernoDisponible` pasa a `true` SOLO con las TRES llaves: `peticiones_aplicar=1` (explicito, **apagado por defecto**), `peticion_capturada ok>=1` (fase 1 con evidencia) y escritura segura.
  Leer la cola no prueba que `Complete()` sea inocuo (podria saltarse el dialogo/pasos de la peticion), por eso `peticiones=1` por si solo **solo observa**.
- Cuando hay una peticion ACTIVA no completa, la `Compuerta` propone resolverla (veto en asistente,
  sin veto en dios). Pasado `peticiones_veto_s`, se aplica en el hilo principal: `Receive()` +
  `Complete()` si no estaba completa, con evidencia `peticion_aplicada`. Todo en `Acciones.cs`
  (respeta config `PuedeEscribir`, modo observador, y `EscrituraSegura`).

**Como verificar:**
1. **Copia de la partida.** `config.txt`: `peticiones=1` (deja `modo=asistente`) y **reinicia**. Para la FASE 1 basta eso. Para la FASE 2 anade `peticiones_aplicar=1`.
2. Juega con la partida cargada hasta que aparezca una peticion activa (audiencia, obra...).
3. Log cada 30 s: `[Pecera] peticiones: cola=N activa=... nuevos=M`. Cuando `activa=<tipo>` y un
   `nuevos=1`, mira `verificacion.json`: `peticion_capturada ok=1` **= FASE 1 verificada**.
4. **SIN reiniciar**: cuando `GobiernoDisponible` pasa a true en vivo, la SIGUIENTE peticion activa
   se resolvera sola (veras `peticion_aplicada ok=1` en `verificacion.json`) **= FASE 2 verificada**.
5. Si en 15-20 min ves siempre `activa=no`, la partida no genera peticiones a ese ritmo; dinos y
   forzamos una con una accion del reino.

## Que me devuelves en total

1. `pecera_datos/verificacion.json` de la sesion corta y de la larga.
2. `sonda.json`, `sonda_pawn.json`, `esquemas.sugeridos.txt`.
3. Salida de `compilar.ps1` si algo fallo.
4. El veredicto de `pecera_ab.py`.


## Agentes con proposito (v0.3.0) — `agentes=1` (NO escribe en el juego)

1. `config.txt`: `agentes=1` (los demas `agentes_*` con su defecto). Reinicia. Juega 20 min.
2. Mira `agentes.md` (se reescribe cada `informe_min` min y con **F10**): cada pawn con su rasgo, su ambicion principal con % de progreso y sus 3 intenciones.
3. `verificacion.json`: `agentes_plan_reglas` ≥ 10 (reglas sin excepciones), `agentes_plan_llm` ≥ 3 (el LLM devuelve planes validos; algun fallo es normal), `agentes_intencion_observada` ≥ 1 (el juego produjo lo esperado).
4. Si ves un bocadillo con una intencion («Ana corteja a Beto»), cuenta para `bocadillo_dibujado`.
5. **Sin tirones**: anota si notas un hitch cada `dia_segundos` s (120 por defecto). Si lo hay, baja `agentes_pawns_tick`.
6. Devuelveme `agentes.md`, `historias.md`, `verificacion.json`, `sonda_pawn.json` y `sonda_mundo.json` (F11).

### Qué debería verse en `agentes.md` con la v0.4.0 (si no, dímelo)
- Cabecera: «Normas vigentes», «Tensión del reino» y «Última sugerencia» del director (solo texto: no ejecuta nada).
- Por pawn: rol (tras ≥ 10 intenciones hechas), nivel de ánimo (sereno/inquieto/al límite…), ambición con %, y sus 3 intenciones.
- `verificacion.json`: además `agentes_reflexion` ≥ 1 (la importancia acumulada dispara conclusiones).
- Tras unos días de juego, la crónica (`cronica.md`) debe mezclar «cumple su ambición…», «El director sugiere…» y, si cambian los valores del reino, una norma.
- `director_estilo=ninguno` apaga las sugerencias; `calmo` / `caotico` cambian el ritmo.
