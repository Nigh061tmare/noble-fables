# PECERA — Traspaso y plan siguiente (tras v0.5.0 «Vida»)

Repo: nigh061tmare/noble-fables, rama `claude/new-session-1vzpl2`. Entregable: `entregables/pecera-COMPLETO-v0.5.0.zip`. Informe: `sesion/INFORME_SESION_3.md`.
Estado: 274 tests en verde, simulador con 4 semillas, `stubs/Pecera.Game.Check` sin avisos. **Nada de la v0.5 se ha probado en partida.** Nada escribe en el juego.

## Reglas (no negociables, detalle en AGENTS.md)
- Marcar VERIFICADO / LEIDO / NO VERIFICADO.
- Sin `catch {}` que oculte fallos ni avisos suprimidos.
- Core en C# 5: sin `?.`, `$""`, `nameof`, `=>` en miembros ni `out var`.
- Toda escritura al juego va tras un interruptor en config (a 0 si no está verificada), con tope, enfriamiento y veto, a través de `Acciones` + `Compuerta`.
- Nada del juego fuera del hilo principal (`Principal`).
- No inventar firmas: solo las del informe §7 o las de la sonda.
- Una fase = un commit con tests en verde.
- Sin DLL del juego ni secretos; ningún identificador de modelo en el repo.

Comandos:
```
dotnet test tests/Pecera.Tests
dotnet build stubs/Pecera.Game.Check
dotnet run --project sim/Pecera.Sim -- --dias 360 --seed 1 --out sim_out
dotnet run --project sim/Pecera.Sim -- --doc-config > docs/CONFIG.md   # tras tocar el esquema de config
```

## FASE A — Fallos que hay que arreglar primero (los metió la v0.5)
1. **GRAVE: el día vuelve a 0 en cada arranque.** `Gancho.DiaActual()` (`src/Pecera.Game/Gancho.cs:162`) cuenta el tiempo real desde que arranca el mod, pero `mundo.jsonl` guarda días entre sesiones. Al recargar:
   - los enfriamientos de la agenda se quedan bloqueados;
   - las normas no pueden derogarse;
   - los recuerdos con fecha «futura» nunca pasan a largo plazo;
   - la crónica salta hacia atrás.

   Arreglo: guardar el día en `mundo.jsonl` y seguir contando desde ahí, con un test de ida y vuelta. Además ese reloj avanza con el juego en pausa y no acelera con x3: avisarlo en el informe hasta leer el calendario real del juego (API de tiempo, pendiente de la sonda).
2. **El linaje no se guarda:** añadir `Linaje.Serializa`/`Carga` a `EstadoSocial` y a `Estado.Social()`, con test.
3. **Las propuestas de esquemas del juego no rellenan `Decision.Etiqueta`:** las preferencias aprenden `esquema:` en vez de `esquema:Difamar`. Poner Etiqueta = nombre del esquema al proponer, como ya hace el simulador.

## FASE B — Se puede hacer ya, sin el juego (Core + simulador + tests)
1. **Diario del pawn:** una frase al día escrita por el LLM (prioridad baja en `PresupuestoLlm`), a partir de sus recuerdos fuertes, su estrés y su ambición. Va a `diarios.md`.
2. **Familia realista:**
   - los hijos heredan rasgos de los padres (media más ruido);
   - vínculo padre-hijo;
   - celos y rivalidad entre hermanos;
   - las herencias (`Linaje.Reparte`) crean rencor en quien recibe menos.
3. **Chismes de escándalos y crisis:** un arrebato o un hundimiento se comenta a través de `RedSecretos` y afecta a la reputación.
4. **Corregir dos defectos del simulador:**
   - nunca hay hundimientos: revisar los umbrales del nivel 3 y la disipación;
   - los roles salen casi todos «mediador»: dar a consolar y celebrar un coste o un rendimiento distinto.
5. **Memoria por significado:** embeddings de Ollama (`/api/embeddings`) con un modelo pequeño y caché por recuerdo. Medir antes la VRAM que consume en la RTX 3060.
6. **Panel F8:** ver las decisiones pendientes y vetarlas con una tecla, sin editar `consola.txt`.
7. **Tests de integración del cableado de `Gancho`,** que hoy solo se comprueba compilando contra stubs.

## FASE C — Necesita datos de la partida del usuario
Hacer la sesión de `VERIFICACION_PENDIENTE.md`, incluida la sección «Vida v0.5.0», y devolver: `verificacion.json`, `sonda.json`, `sonda_pawn.json`, `sonda_mundo.json`, `esquemas.sugeridos.txt`, `ganchos.sugeridos.txt`, `agentes.md`, `cronica.md` y `mundo.jsonl`.

Después, por prioridad:
1. **A4:** leer las necesidades reales del pawn (hambre, sueño, salud), solo lectura. Sustituyen a las necesidades inventadas en el ánimo y el estrés.
2. **A5–A6:** leer familia, habilidades y edad reales, para que linaje, duelo y currículo sean los del juego.
3. **API de tiempo:** día y estación reales en vez del reloj de pared.
4. **Ganchos de vida:** validar 2–3 líneas de `ganchos.sugeridos.txt` (muerte, boda, nacimiento) con `ganchos=1`, y comprobar que aparece la evidencia `vida_suceso`.
5. **Primera acción real:** la intención «pedir» entra en `PetitionManager` (cola de peticiones), con interruptor `pedir_peticiones=0`, tope, enfriamiento, veto y Compuerta.
6. **Esquemas reales** (`Acciones.DisparaEsquema` con `esquemas.txt` confirmado): la intriga y la venganza dejan de ser solo internas.
7. **Después:** investigación, planos y misiones (A8–A12), según lo que diga la sonda.

## Referencias usadas (detalle en `docs/INVESTIGACION.md`)
- CK3 Dev Diary #31: estrés y crisis según rasgos.
- Wiki de Dwarf Fortress, «Memory (thought)»: 8 + 8 recuerdos, uno por categoría, y deriva de la personalidad.
- GMTK, «The Genius AI Behind The Sims»: elegir al azar entre las mejores opciones.
- Concordia (DeepMind): el maestro de juego.
- Voyager (arXiv 2305.16291): currículo automático.
- Generative Agents: recuperación de memoria 0.5/3/2 y reflexión.
- RimWorld: rupturas 35/20/5 % y narradores.
- Project Sid: normas y roles.
- alife-sdk, AI Town, IAUS.

Datos propios: 300 eventos reales de opinión (93 % por rasgo, 82 % negativos; |delta| p50 0.72, p90 1.96, p99 2.36).
