# AGENTS.md — contexto para agentes (opencode, Claude Code, etc.)

Proyecto **Pecera**: mod de Noble Fates (Unity Mono 2019.4, BepInEx 5.4.23.5 + Harmony) con LLM local (Ollama).
Lee primero `01_INFORME_PECERA.md` (trampas, API del juego §7, lo no verificado §8) y `VERIFICACION_PENDIENTE.md`.
La mision completa y las reglas estan en `03_PROMPT_EXPANDIDO.md` (sustituye a `02_PROMPT_CLAUDE_CODE.md`).

## Reglas innegociables (resumen)
1. Honestidad: marca VERIFICADO / LEIDO / NO VERIFICADO. Nada se da por hecho si no se vio en partida.
2. No bajar la vara: sin supresion de avisos, sin `catch {}` que oculte fallos, sin stubs en produccion, sin tests desactivados.
3. Toda escritura al juego: tras interruptor de `config.txt` (apagado si no esta verificado), tope, enfriamiento y veto; por `Acciones` + `Compuerta`.
4. Nada del juego fuera del hilo principal (cola `Principal`). `Afectos`, `Secretos`, `Cronica` solo hilo principal.
5. Sin secretos, sin DLL del juego, sin assets de terceros.
6. Solo firmas del informe §7 o leidas del binario (sonda F11 / `herramientas/`). Si no, interfaz + «firma pendiente de confirmar». Nunca inventar.
7. Una fase = un objetivo = un commit con tests en verde. Leer ficheros por rangos.

## Mapa
- `src/Pecera.Core` — logica pura, **C# 5 forzado** (`LangVersion 5`: el `csc.exe` de .NET 4 del autor). Sin `?.`, `$""`, `nameof`, `=>` en miembros.
- `src/Pecera.Game` — adaptadores BepInEx/Harmony (namespace `PeceraNF`); solo se compilan de verdad con `compilar.ps1` en Windows.
- `tests/Pecera.Tests`, `sim/Pecera.Sim` (simulador + `--barrido`, `--doc-config`), `stubs/Pecera.Game.Check` (tipos en CI).
- `legacy/` intentos anteriores y plugin v0.1; `herramientas/` scripts Cecil y `pecera_ab.py`.

## Comandos
```
dotnet test tests/Pecera.Tests                      # debe quedar en verde (incluye docs/CONFIG.md al dia)
dotnet build stubs/Pecera.Game.Check                # adaptadores contra stubs, avisos = errores
dotnet run --project sim/Pecera.Sim -- --dias 360 --seed 1 --out sim_out
dotnet run --project sim/Pecera.Sim -- --doc-config > docs/CONFIG.md   # tras tocar PeceraConfig.Schema
```

## Pendiente (por prioridad)
1. Que el usuario ejecute `VERIFICACION_PENDIENTE.md` y devuelva `verificacion.json`, `sonda.json`, `sonda_pawn.json`, `esquemas.sugeridos.txt`.
2. Con la sonda: adaptadores reales de esquemas (`Acciones.DisparaEsquema`), peticiones, investigacion, planos, misiones, API de tiempo.
3. Tests de integracion del pipeline del hook (hoy la logica esta en Core; el cableado en `Gancho.cs` solo se compila contra stubs).
4. Ideas: todas implementadas en Core salvo la voz con modelo mayor (medir antes). Faltan sus adaptadores al juego (familia, habilidades, calendario). Ver `docs/IDEAS.md`.
