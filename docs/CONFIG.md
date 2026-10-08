# config.txt: referencia

Generado desde el codigo (`dotnet run --project sim/Pecera.Sim -- --doc-config > docs/CONFIG.md`). No lo edites a mano.

Las claves marcadas **escribe en el juego** modifican el estado de la partida. Mientras no esten VERIFICADAS en partida vienen apagadas (`0`) y `modo=observador` las anula todas.

| Clave | Defecto | Rango | Estado | Que hace |
|---|---|---|---|---|
| `config_version` | `2` | 1 .. 99 | VERIFICADO | Version del formato de este fichero. No lo cambies. |
| `modelo` | `qwen4b-silly:latest` | texto | VERIFICADO | Modelo de Ollama. Medidos: qwen4b-silly / qwen9b-silly / mimo-9b:q4 / mistral-nemo. |
| `keep_alive` | `15m` | texto | VERIFICADO | Cuanto sigue el modelo en VRAM tras la ultima peticion. |
| `umbral_dato` | `0.5` | 0.05 .. 10 | VERIFICADO | \|cambio\| minimo para registrar el evento en opiniones.jsonl. |
| `umbral_habla` | `1` | 0.05 .. 50 | VERIFICADO | \|cambio\| minimo para hablar por un HECHO (ha matado, ha desertado). |
| `umbral_rasgo` | `2` | 0.05 .. 50 | VERIFICADO | \|cambio\| minimo para hablar por un RASGO (es Orco, esta Malo): ruido. |
| `gap_voz` | `25` | 5 .. 3600 | VERIFICADO | Segundos minimos entre dos frases (global). Minimo 5. |
| `fps_log` | `0` | 0 .. 1 | VERIFICADO | 1 escribe fps.csv cada 5 s (solo diagnostico). |
| `activo` | `1` | 0 .. 1 | NO VERIFICADO | 0 deja el mod inerte (no parchea nada): para el A/B del coste ON/OFF. fps_log sigue funcionando. |
| `dia_segundos` | `120` | 10 .. 86400 | NO VERIFICADO | Segundos de reloj que equivalen a un dia de juego para el modelo afectivo (la API de tiempo del juego esta pendiente de confirmar). |
| `resumen_gap_s` | `90` | 20 .. 3600 | NO VERIFICADO | Segundos minimos entre dos resumenes de memoria con el LLM. |
| `informe_min` | `5` | 1 .. 240 | NO VERIFICADO | Minutos entre escrituras de informe.md, cronica.md y grafo.dot. |
| `confirmar_version` | `(vacio)` | texto | NO VERIFICADO | Version del juego que el usuario acepta tras una actualizacion (desbloquea las escrituras). |
| `influencia` | `0` | 0 .. 1 | NO VERIFICADO - **escribe en el juego** | 1: la actitud de la frase empuja la opinion en el juego (ESCRIBE). Sin verificar en partida. |
| `influencia_max` | `0.25` | 0 .. 1 | NO VERIFICADO - **escribe en el juego** | Tope del empujon por frase. |
| `influencia_pareja_s` | `300` | 30 .. 86400 | NO VERIFICADO - **escribe en el juego** | Segundos de enfriamiento entre empujones de la misma pareja. |
| `influencia_hora_max` | `30` | 0 .. 1000 | NO VERIFICADO - **escribe en el juego** | Tope global de empujones por hora de reloj. |
| `panel_inicio` | `0` | 0 .. 1 | VERIFICADO | 1: el panel de registro aparece al cargar (F8 lo alterna). No escribe estado. |
| `burbujas` | `1` | 0 .. 1 | NO VERIFICADO | 1: bocadillos sobre los pawns (solo dibuja, no escribe estado). Sin verificar en partida. |
| `telemetria` | `1` | 0 .. 1 | NO VERIFICADO | 1: escribe verificacion.json con la evidencia de cada funcion. |
| `memoria` | `1` | 0 .. 1 | NO VERIFICADO | 1: memoria episodica por pawn en disco (memoria.jsonl). |
| `memoria_recientes` | `6` | 3 .. 50 | NO VERIFICADO | Episodios recientes por pawn antes de resumir. |
| `memoria_resumen` | `1` | 0 .. 1 | NO VERIFICADO | 1: resume episodios viejos con el LLM (1 llamada, comparte cupo de voz). |
| `afectos` | `1` | 0 .. 1 | NO VERIFICADO | 1: modelo afectivo por pareja (afecto, confianza, rencor...). Solo interno. |
| `rumores` | `0` | 0 .. 1 | NO VERIFICADO | 1: secretos y rumores internos (usa el LLM para inventar secretos). |
| `sociedad` | `1` | 0 .. 1 | NO VERIFICADO | 1: facciones, lideres y favores calculados internamente (informe, sin escribir). |
| `cronica` | `1` | 0 .. 1 | NO VERIFICADO | 1: cronica del reino en cronica.md. |
| `esquemas` | `0` | 0 .. 1 | NO VERIFICADO - **escribe en el juego** | 1: dispara esquemas reales (SchemeManager). ESCRIBE. Requiere esquemas.txt. |
| `esquemas_dia_max` | `2` | 0 .. 20 | NO VERIFICADO - **escribe en el juego** | Tope de esquemas disparados por dia de juego. |
| `peticiones` | `0` | 0 .. 1 | NO VERIFICADO - **escribe en el juego** | 1: el Rey dormido LEE la cola de peticiones y anota su decision interna. Para que ademas las aplique hace falta peticiones_aplicar=1. |
| `peticiones_aplicar` | `0` | 0 .. 1 | NO VERIFICADO - **escribe en el juego** | 1: ADEMAS de leer, aplica Receive()+Complete() a la peticion activa (ESCRIBE). Interruptor explicito: peticiones=1 por si solo solo OBSERVA. Sin verificar en partida: haz copia antes. |
| `peticiones_dia_max` | `3` | 0 .. 50 | NO VERIFICADO - **escribe en el juego** | Tope de peticiones resueltas por dia de juego. |
| `peticiones_veto_s` | `30` | 5 .. 3600 | NO VERIFICADO - **escribe en el juego** | Segundos que el jugador tiene para vetar una decision antes de aplicarla. |
| `investigacion` | `0` | 0 .. 1 | NO VERIFICADO - **escribe en el juego** | 1: elige investigacion sola. ESCRIBE. |
| `planos` | `0` | 0 .. 1 | NO VERIFICADO - **escribe en el juego** | 1: aprueba planos solo. ESCRIBE. |
| `misiones` | `0` | 0 .. 1 | NO VERIFICADO - **escribe en el juego** | 1: gestiona misiones/diplomacia solo. ESCRIBE. |
| `modo` | `asistente` | texto | NO VERIFICADO - **escribe en el juego** | observador (kill-switch: NUNCA escribe) \| asistente (escribe lo habilitado, con veto del jugador) \| dios (sin veto, con topes). |
| `datos_dir` | `(vacio)` | texto | NO VERIFICADO | Carpeta de datos. Vacio = junto al DLL del plugin (pecera_datos). |
