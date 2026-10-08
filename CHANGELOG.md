# Changelog

## 0.2.2 (2026-10-08) — traspaso a Opus: se integra el estado real del PC y se endurece el Rey dormido

- Integrado en el repo lo hecho en el PC del usuario (0.2.1 + Rey dormido fases 1 y 2): `Peticiones.cs`, sonda de peticiones, `panel_inicio`,
  `PeceraConfig.VersionDe`, arreglo de `perdona`. El `.rar` del traspaso llego **danado** (17 ficheros vacios); se conservan las versiones intactas del repo
  para `compilar.ps1`, `ConfigTests.cs` y `SistemasTests.cs` y se recrearon los tests de `VersionDe`.
- **Nuevo interruptor `peticiones_aplicar` (0 por defecto).** Antes la fase 2 se activaba sola al ver una peticion en la cola; eso prueba que *leer* funciona,
  no que `Receive()+Complete()` sea inocuo. Ahora hacen falta tres llaves (interruptor explicito + evidencia + escritura segura). `peticiones=1` solo observa.
- Corregido: el dry-run del consejo anotaba la cola ENTERA en la cronica cada 30 s; ahora solo las peticiones nuevas.

## 0.2.1 (2026-10-07) — primera verificacion en el PC real (opencode/Claude local)

**VERIFICADO en el PC del usuario (Windows 10, csc.exe de .NET Framework 4, Noble Fates 0.31.5.3):**
- `compilar.ps1` compila los 29 ficheros con `-warnaserror`, **0 avisos**, 149,5 KB (v0.2.0).
- 159 tests en verde con un SDK .NET 8.0.425 portatil (`C:\Users\Jose Luis\.pecera-tools\dotnet`), no solo en la nube.
- `stubs/Pecera.Game.Check` compila sin avisos; el simulador (360 dias, semilla 1) reproduce el informe de la sesion.
- El plugin carga en el juego real: `Loading [Pecera NobleFates 0.2.0]`, `parcheado: PawnManager.OpinionDelta`, panel listo, sin excepciones.

### Corregido
- **BUG (grave, no cubierto por los 159 tests): la migracion del config v1 nunca se ejecutaba.**
  `Estado.Inicia` decidia migrar con `Parse(texto).Int("config_version") < 2`, pero `Str()` devuelve el DEFECTO del esquema
  (2) cuando la clave falta, asi que un `config.txt` antiguo parecia ya migrado y `influencia=1` (sin verificar, escribe en
  el juego) seguia activa. Nuevo `PeceraConfig.VersionDe(texto)` (1 si no hay clave) + 3 tests de regresion (162 en total).
  **VERIFICADO en el juego:** ahora el log dice `config.txt migrado a la version 2`, `influencia=1 -> 0`, y existe `config.v1.bak.txt`.

- **BUG de semantica en `PoliticaInfluencia`:** `perdona` invertia el signo siempre, tambien con delta positivo, y restaba
  estima a quien acababa de hacer algo bueno. Medido con qwen4b real (12 casos, prompts reales del Core): 3 de 5 eventos
  positivos devolvian `perdona`. Ahora `perdona` solo actua con delta negativo. Test anadido (162 en total).

### Verificado con el modelo real (sin el juego)
- Prompts, `OllamaWire` y `ReplicaParser` del Core contra qwen4b-silly en Ollama: 12/12 frases utiles, 12/12 JSON limpio,
  latencia mediana 1,4 s (max 1,8 s). Cliente de prueba fuera del repo (`%TEMP%\pecera_e2e`).

### Todavia NO VERIFICADO en partida
Bocadillos, empujon de opinion, reentrada, id estable, memoria entre reinicios, A/B de coste: ver `VERIFICACION_PENDIENTE.md`.

## 0.2.0 (2026-10-07) — capas, pruebas, simulador e ideas

Todo lo siguiente esta **probado en la nube (tests + simulador)** y es **NO VERIFICADO en partida** salvo lo indicado.

### Nuevo
- **Arquitectura por capas**: `Pecera.Core` (logica pura, C# 5), `Pecera.Game` (adaptadores), tests, simulador y stubs de tipos.
- **A. Base verificable**: parser JSON propio con escapes, `\uXXXX`, vallas markdown, prosa alrededor y reparacion de JSON truncado;
  cliente Ollama probado (cuerpos nativo/proxy y lectura de respuesta); configuracion tipada y documentada con migracion de v1;
  rutas parametrizadas (`compilar.ps1` busca Steam; `datos_dir`).
- **B. Identidad y persistencia**: id estable por pawn (clave del juego si existe, si no nombre + separacion de homonimos),
  fichas (rasgos, metas, miedos, voz, carisma, rencor, locuacidad) con generacion determinista sin LLM, memoria episodica
  `recientes -> medio -> largo` con resumen del LLM, fallback sin LLM, poda dura, compactacion y recuperacion de lineas corruptas.
- **C. Emociones y relaciones**: afecto, confianza, rencor, deuda, rivalidad, romance y trauma por pareja dirigida; decaimiento,
  reconciliacion, gratitud; estabilidad demostrada por construccion y por test de propiedades.
- **D. Secretos y rumores**: confidencias, propagacion con distorsion, consecuencias afectivas, `fugas.jsonl`, metricas de la red.
- **E. Esquemas**: catalogo `esquemas.txt` con clases de seguridad, motor de propuestas (rencor sostenido / romance). *Adaptador al juego: pendiente.*
- **F. Sociedad**: facciones, liderazgo por carisma, favores, parejas, costumbres/tradiciones, soledad, deriva de personalidad acotada.
- **G/H. Gobierno y crecimiento**: consejo (peticiones, investigacion), metas con bucle de deficit, metricas del reino.
  *Adaptadores al juego: pendientes.*
- **I. Direccion del jugador**: directrices global/faccion/pawn, consola por fichero con deshacer, `modo` observador/asistente/dios.
- **J. Cronica** con hitos, resumen por temporada y leyendas.
- **K. Observabilidad**: `verificacion.json` (evidencia por funcion con criterio), `informe.md`, grafo DOT/JSON, estadistica de FPS, A/B formal (`pecera_ab.py`).
- **L. Robustez**: disyuntor del LLM (modo degradado), guarda de version del juego, formato de datos versionado, escritura atomica.
- **Compuerta de decisiones**: tope diario, enfriamiento y **ventana de veto** para toda escritura al juego.
- Sondas de solo lectura (F11) para cerrar firmas pendientes.

### Ideas anadidas (Core + tests + simulador; ninguna escribe en el juego)
- **Justicia** (`Tribunal`), **sucesion y herencia** (`Linaje`, `Sucesion`), **mentoria**, **cultura/religion emergente** (`Cultura`), **dialectos**,
  **estaciones**, **suenos e inspiraciones**, **espionaje**, **deriva de personalidad** en el simulador.
- Cableado ligero en el juego (sin escribir): facciones, dialectos en el prompt de la voz, credo en el informe.
- `Ficha.Ambicion` (opcional; las fichas antiguas se leen con 0.5). `compilar.ps1` pasa `-codepage:65001` (los literales con tildes del Core ya no dependen de la pagina de codigos).

### Cambiado
- `influencia` pasa a **0 por defecto** (v0.1 la llevaba a 1 sin haberse verificado). La migracion lo apaga y lo avisa.
- Empujones: ademas del tope por frase y la pareja, **tope por hora** (`influencia_hora_max`).
- El hook alimenta tambien el modelo afectivo y la memoria; sigue sin esperar nunca al LLM.
- `OnGUI`/panel conservados de v0.1; los bocadillos dejan evidencia de haberse dibujado.

### Arreglado
- La memoria de v0.1 era por nombre y solo en RAM (informe §9 fase 2: colisiones de nombre): ahora por id y en disco. *Sin ver en partida.*

### Conocido / pendiente
- Ver `VERIFICACION_PENDIENTE.md` y `01_INFORME_PECERA.md` §8.

## 0.1.0
Plugin inicial (`legacy/noble-fates-0.1`): hook verificado, frases, bocadillos, memoria RAM, influencia, directrices.
