# Changelog

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
