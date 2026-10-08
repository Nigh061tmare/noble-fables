# Informe de sesión 3 — v0.5.0 «Vida»

Petición: «haz el juego más autónomo y realista, lleva lo anterior al siguiente nivel, busca referencias (juegos, simuladores, GitHub) e incorpóralas; que los pawns estén vivos y jueguen solos, aunque yo pueda intervenir».

## Qué se hizo (en orden, cada fase con tests en verde)
1. **Fase 1 — Core** (commit «v0.5 fase 1»): bus de sucesos y `Vida`; psique (recuerdos fuertes DF, estrés y crisis CK3, experiencia Voyager, elección Sims, preferencias del jugador);
   calibración con los 300 eventos reales y prejuicio de grupo; órdenes en lenguaje natural; maestro de juego (Concordia); persistencia `EstadoSocial`.
   Simulador con población viva, jugador simulado y órdenes. Calibración del estrés medida con 4 semillas (de 0 crisis → ~100 → 7–20 por año).
2. **Fase 2 — Juego** (commit «v0.5 fase 2»): `Estado` (bus conectado, maestro, `mundo.jsonl` atómico con guarda de versión, preferencias desde la Compuerta vía hilo principal),
   `Gancho` (prejuicio desde los motivos reales), `Mente` (psique por días transcurridos, crisis con bocadillo, maestro con tope diario, currículo, sección Vida en `agentes.md`),
   órdenes desde `directriz.txt` (normas, director, consejo de peticiones), `Observadores` (`ganchos.txt`, solo lectura) y `ganchos.sugeridos.txt` en la sonda. 5 claves nuevas de config.
3. **Fase 3 — Documentación**: CHANGELOG, INVESTIGACION §5 (fuentes comprobadas por búsqueda web), SIMULACION, ARQUITECTURA, VERIFICACION_PENDIENTE, README, AGENTS/CLAUDE.

## Estado honesto
- **HECHO-NUBE**: 274 tests, simulador 4 semillas, `stubs/Pecera.Game.Check` con 0 avisos. **NO VERIFICADO en partida**: todo lo de v0.5.
- **Sin escrituras nuevas al juego.** Lo que ahora es más «vivo» es la mente del mod: qué piensan, recuerdan, sufren, planean y dicen. Que actúen en el mundo (pedir, intrigar, trabajar)
  sigue esperando las firmas A8–A12 y los interruptores de escritura.
- Riesgos a mirar en partida: coste del maestro (acotado por `vida_maestro_dia` y por el reparto circular), bocadillos excesivos, tamaño de `mundo.jsonl` (la crónica tiene tope 5000 hitos).
