# Pecera

Mod de **Noble Fates** que da voz, memoria y consecuencias a los personajes del juego
con un LLM local (Ollama). Objetivo: una simulación autónoma en la que los habitantes
viven, se relacionan, construyen y evolucionan solos, con el jugador como mano opcional.

> Estado: prototipo. El hook y las frases están verificados en partida; los bocadillos
> y la influencia sobre las opiniones están instalados pero **sin verificar**. Lee
> `01_INFORME_PECERA.md` antes de nada.

## Qué contiene

| Carpeta | Contenido |
|---|---|
| `noble-fates/` | Plugin BepInEx actual (`Gancho`, `Llm`, `Pantalla`, `Pecera`) y `compilar.ps1` |
| `lords-villeins/` | Módulos para Lords & Villeins (Rey dormido, Secretos, Provocador, Burbujas) |
| `kenshi/`, `going-medieval/` | Scripts de intentos anteriores |
| `pecera-original/` | Mundo web y agentes del primer prototipo (sin assets gráficos) |
| `herramientas/` | Scripts Mono.Cecil para leer la API del juego, benchmarks y monitores |
| `datos-muestra/` | Muestras reales de eventos, config y respuestas del modelo |

## Requisitos

- Noble Fates (Steam, AppID 1769420) y BepInEx 5.4.23.5 instalado en su carpeta.
- Ollama con un modelo (por defecto `qwen4b-silly:latest`) en `127.0.0.1:11434`.
- .NET Framework 4 (`csc.exe`) para compilar con `noble-fates/compilar.ps1`.
- **Las rutas están fijas a la máquina del autor**: edita `$G` en `compilar.ps1`.

## Uso en partida

- **F7** bocadillos on/off · **F8** panel de registro · **F9** limpiar.
- `pecera_datos/config.txt`: modelo, `keep_alive`, umbrales de voz, `gap_voz`, `influencia`, `fps_log`.
- `pecera_datos/directriz.txt`: lo que quieres que pase en el reino (se relee solo).
- `influencia=0` apaga cualquier escritura en el estado del juego.

## Aviso

No se distribuyen DLL ni assets del juego. Haz copia de tus partidas antes de probar.
Proyecto no afiliado a los autores de Noble Fates.
