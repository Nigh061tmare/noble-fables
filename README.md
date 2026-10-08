# Pecera

Mod de **Noble Fates** (Unity Mono 2019.4, BepInEx 5.4.23.5 + Harmony) que da voz, memoria y consecuencias a los
personajes con un LLM local (Ollama). Meta: una simulacion social **autonoma**: los habitantes viven, se relacionan, se
entreran de secretos, forman bandos y el reino crece solo, con el jugador como mano opcional.

> **Estado honesto (v0.5.0).** Toda la *logica* esta hecha y probada aqui (mas de 240 tests + simulador). **Nada de lo nuevo se ha
> visto funcionar dentro del juego.** El hook y las frases de v0.1 estaban verificados en partida; bocadillos, empujon de
> opinion, memoria persistente, modelo afectivo, rumores y consola son **NO VERIFICADOS**. Los adaptadores de esquemas,
> peticiones, investigacion, planos y misiones estan **pendientes de confirmar firmas** y no hacen nada todavia.
> v0.4.0: los pawns tienen ambiciones, planes de varios pasos, animo, normas y un director de drama (`agentes=1`, solo observa). Investigacion: `docs/INVESTIGACION.md`. Plan: `docs/PLAN_OPUS.md`.
> v0.5.0 «Vida»: estrés y crisis según carácter (CK3), recuerdos que marcan (Dwarf Fortress), prejuicio de grupo calibrado con tus 300 eventos reales,
> maestro de juego que resuelve conversaciones por dentro, el reino aprende de tus vetos, órdenes en lenguaje natural y estado que sobrevive al reinicio (`mundo.jsonl`). Sigue sin escribir en el juego.
> Lee `VERIFICACION_PENDIENTE.md`: son 2 sesiones de juego para cerrar casi todo.

## Instalacion reproducible

Requisitos: Noble Fates (Steam, AppID 1769420) con BepInEx 5.4.23.5 instalado · Ollama en `127.0.0.1:11434` con el modelo
`qwen4b-silly:latest` (o el que pongas en `config.txt`) · .NET Framework 4 (`csc.exe`, ya viene con Windows) · PowerShell.

```powershell
# 1. COPIA de tu partida. 2. Cierra el juego TU MISMO. 3.
.\compilar.ps1 -Instalar                              # busca Steam solo
.\compilar.ps1 -Juego "D:\SteamLibrary\steamapps\common\Noble Fates" -Instalar
$env:NOBLE_FATES_DIR = "D:\...\Noble Fates"           # alternativa: variable de entorno
```

Sin rutas fijas a ninguna maquina. `compilar.ps1` sale con codigo 1 si falla (y borra el DLL viejo), con `-warnaserror`,
y **nunca cierra el juego**: con `-Instalar` y el juego abierto aborta y te lo dice.

Los datos viven en `BepInEx\plugins\pecera_datos\` (o donde diga `datos_dir`). El primer arranque crea `config.txt` (y migra uno
antiguo dejando copia). **Todo lo que escribe en el estado del juego viene apagado** hasta que lo verifiques.

## Uso en partida

| Tecla | Efecto |
|---|---|
| F7 | bocadillos on/off |
| F8 / F9 | panel de registro / limpiar |
| F10 | escribe `informe.md`, `cronica.md`, `grafo.dot/json` y **`verificacion.json`** |
| F11 | sonda de solo lectura: `sonda.json`, `sonda_pawn.json`, `esquemas.sugeridos.txt`, `ganchos.sugeridos.txt` |

* `config.txt`: referencia completa en [`docs/CONFIG.md`](docs/CONFIG.md) (generada del codigo).
* `directriz.txt`: lo que quieres que pase. Global, o por `[faccion:Nombre]` y `[pawn:Nombre]`. Se relee sola. Con `ordenes=1` entiende además
  «quiero paz», «ojo por ojo», «favorece a Ana», «haced una fiesta», «quiero drama» / «quiero calma».
* `ganchos.txt` (opcional, `ganchos=0`): `tipo = Clase.Metodo` para que la vida del mod oiga muertes, bodas o nacimientos reales (solo lectura).
* `consola.txt`: comandos (`estado`, `pendientes`, `veta <id>`, `modo observador|asistente|dios`, `dir ...`, `quita ...`, `deshacer`).
  Se ejecutan solos; respuesta en `consola_salida.txt`.
* `modo=observador` es un interruptor general: **ninguna** escritura al juego, pase lo que pase.

## Arquitectura (resumen; detalle en [`docs/ARQUITECTURA.md`](docs/ARQUITECTURA.md))

```
src/Pecera.Core    C# 5 puro, sin Unity ni tipos del juego. TODA la logica. Se prueba aqui.
src/Pecera.Game    Adaptadores finos BepInEx/Harmony (solo se compilan en tu PC con compilar.ps1).
tests/             mas de 240 tests xUnit (JSON, config, memoria, afectos, rumores, sociedad, gobierno, simulador...)
sim/Pecera.Sim     Simulador headless: dias de juego con mundo falso y LLM simulado determinista.
stubs/             Comprueba en CI que Game compila (C# 5, net48, avisos=errores) contra tipos de mentira.
legacy/            Intentos anteriores (Kenshi, Going Medieval, Lords & Villeins, pecera web) y plugin v0.1.
herramientas/      Scripts Mono.Cecil para LEER la API del juego, monitores y pecera_ab.py (A/B del coste).
```

## Desarrollo

```bash
dotnet test tests/Pecera.Tests                     # mas de 240 tests, incluye simulador y docs al dia
dotnet build stubs/Pecera.Game.Check               # tipos de los adaptadores contra stubs
dotnet run --project sim/Pecera.Sim -- --dias 360 --seed 1 --out sim_out
```

Aviso: no se distribuyen DLL ni assets del juego. Proyecto no afiliado a los autores de Noble Fates.
Documentos: [`01_INFORME_PECERA.md`](01_INFORME_PECERA.md) (estado y trampas) · [`CHANGELOG.md`](CHANGELOG.md) ·
[`docs/IDEAS.md`](docs/IDEAS.md) · [`docs/SIMULACION.md`](docs/SIMULACION.md) · [`VERIFICACION_PENDIENTE.md`](VERIFICACION_PENDIENTE.md).
