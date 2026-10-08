using System;
using System.Collections.Generic;
using System.Text;
using Pecera.Core;

namespace PeceraNF
{
    // =========================================================================
    //  MENTE  -  los pawns como agentes con proposito (Persona / Agenda / plan en lote)
    // =========================================================================
    //  NO escribe en el estado del juego. Hace tres cosas:
    //    1. Cada dia de juego recalcula (por trozos, reparto circular) la reflexion y el plan base POR REGLAS
    //       de un grupo de pawns y lo anota en la Agenda.
    //    2. Con presupuesto y salud del LLM, pide UNA llamada en lote para planear a varios pawns; la respuesta
    //       se valida con los mismos guardarrailes que las reglas.
    //    3. Observa: cuando el juego produce por su cuenta el evento que una intencion esperaba (p. ej. una
    //       opinion positiva hacia el objetivo de un "charlar"), la da por HECHA y alimenta necesidades y
    //       ambiciones. Es el unico "ejecutor" que existe hasta que se confirmen las APIs de accion del juego.
    //  Hilos: Agenda, Personas y los objetos que mutan viven en el hilo principal. La unica llamada al LLM va
    //  por la hebra de voz (cola acotada), asi nunca hay dos en vuelo.
    // =========================================================================
    public static class Mente
    {
        static int _cursor;
        static readonly Dictionary<string, int> UltimoDia = new Dictionary<string, int>();
        static readonly Dictionary<string, string> Pensamientos = new Dictionary<string, string>();
        static readonly Dictionary<string, WeakReference> Pawns = new Dictionary<string, WeakReference>();
        static int _rondas, _burbujaRonda = -1, _diaMaestro = -1, _maestroHoy;
        static readonly Rng _rng = new Rng(Environment.TickCount);
        static bool _planEnVuelo;

        public static void Recuerda(string id, Pawn p) { if (p != null) Pawns[id] = new WeakReference(p); }

        static int _ultimoMes = -1;

        static ContextoMundo Ctx(List<string> ids, int dia)
        {
            return new ContextoMundo
            {
                Afectos = Estado.Afectos, Vivos = ids, VivosOrdenados = true, Dia = dia, Metas = Estado.Metas,
                Persona = id => Estado.Personas.Get(id), Nombre = id => Estado.Ids.Nombre(id),
                UmbralRencorHostil = 0.35 + Estado.Normas.UmbralHostilExtra, BonoHospitalidad = Estado.Normas.BonoHospitalidad,
                PuedeHablar = (a, b) => Estado.Freno.Puede(a, b, dia),
                Exp = Estado.Cfg.Bool("vida") && Estado.Maestro != null ? Estado.Maestro.Exp : null,
                Sesgo = Estado.Cfg.Bool("vida") ? new Func<string, string, double>(Estado.Prejuicios.Sesgo) : null
            };
        }

        // Hilo principal, una vez por dia de juego.
        public static void Tick()
        {
            if (!Estado.Activo || !Estado.Cfg.Bool("agentes")) return;
            try
            {
                var fichas = Estado.Fichas.Todas();
                if (fichas.Count < 2) return;
                var ids = new List<string>();
                foreach (var f in fichas) ids.Add(f.Id);
                ids.Sort(StringComparer.Ordinal);
                int dia = Gancho.DiaActual();
                var ctx = Ctx(ids, dia);
                int n = Math.Min(ids.Count, Estado.Cfg.Int("agentes_pawns_tick"));
                var chunk = new List<Persona>();
                for (int k = 0; k < n; k++)
                {
                    string id = ids[(_cursor + k) % ids.Count];
                    Persona p = Estado.Personas.GetOCrea(Estado.Fichas.GetOCrea(id, Estado.Ids.Nombre(id)));
                    chunk.Add(p);
                }
                _cursor = (_cursor + n) % ids.Count;
                _rondas++;

                var reglas = new List<List<Intencion>>();
                foreach (var p in chunk)
                {
                    Agente.Reflexiona(p, ctx);
                    var plan = Planificador.Planea(p, ctx);
                    reglas.Add(plan);
                    foreach (var i in plan) if (Estado.Agenda.Anade(i, dia) != null) Estado.Ev.Ok("agentes_plan_reglas", "");
                }

                // Consolidacion: las necesidades envejecen los dias transcurridos desde la ultima vez de ESE pawn.
                foreach (var p in chunk)
                {
                    int ult; if (!UltimoDia.TryGetValue(p.Id, out ult)) ult = dia - 1;
                    double dias = Math.Max(1, dia - ult); UltimoDia[p.Id] = dia;
                    if (Estado.Cfg.Bool("vida")) VidaInterior(p, ult, dia, ctx);
                    foreach (var a in Agente.Consolida(p, Estado.Agenda, dias))
                    {
                        var sucesora = Agente.Sucesora(p, a, dia, Estado.Maestro != null ? Estado.Maestro.Exp : null);
                        Estado.Cronica.Anota(dia, "sueno", p.Nombre + " cumple su ambicion (" + a.Texto + ") y ahora aspira a " + sucesora.Texto, 5);
                    }
                    // Reflexion por importancia acumulada (Generative Agents): conclusiones que vuelven al recuperar recuerdos.
                    if (Estado.Cfg.Bool("memoria") && Estado.Mem.ToqueReflexion(p.Id))
                    {
                        var ins = Agente.Insights(p, Estado.Mem, ctx);
                        if (ins.Count > 0) Estado.Ev.Ok("agentes_reflexion", ins[0]);
                    }
                }
                MensualYDirector(ids, ctx, dia);
                if (Estado.Cfg.Bool("vida") && Estado.Cfg.Bool("vida_maestro")) Maestro(chunk, ids, ctx, dia);
                Estado.Agenda.Caduca(dia);
                PlanLlmEnLote(chunk, reglas, ctx);
                if (_rondas % 10 == 0) { Estado.Personas.Guarda(); Estado.GuardaMundo(); Estado.Ev.Ok("mundo_persistido", ""); }
            }
            catch (Exception e)
            {
                Estado.Ev.Fail("agentes_plan_reglas", e.GetType().Name + ": " + e.Message);
                UnityEngine.Debug.Log("[Pecera] agentes: " + e);
            }
        }

        // Psique de los dias transcurridos desde la ultima vez de ESTE pawn (como mucho 30): disipacion del estres, recuerdos fuertes
        // que maduran o vuelven, y la crisis si se cruzo un nivel (CK3). Todo interno; la crisis se ve en la cronica y en un bocadillo.
        static void VidaInterior(Persona p, int ult, int dia, ContextoMundo ctx)
        {
            int desde = Math.Max(ult + 1, dia - 29);
            for (int d = desde; d <= dia; d++)
            {
                string revivido;
                var cr = Psique.Dia(p, Estado.Fuertes, d, out revivido);
                if (revivido.Length > 0 && d == dia && Estado.Cfg.Bool("memoria")) Estado.Mem.Registra(p.Id, "recuerdo", "", revivido, 2);
                if (cr == Ruptura.Ninguna) continue;
                string texto = Rupturas.Aplica(cr, p, ctx, _rng);
                Estado.Fuertes.Anota(p.Id, "crisis", texto, -0.6, 6, d);
                Estado.Cronica.Anota(d, "crisis", texto + " (estres)", cr == Ruptura.Retiro ? 2 : 4);
                Estado.Ev.Ok("vida_crisis", p.Nombre + ": " + cr);
                Dilo(p.Id, texto, false);
            }
        }

        // Maestro de juego (Concordia): de las intenciones sociales vivas de este trozo, elige como The Sims (al azar entre las mejores)
        // y las resuelve POR DENTRO sobre el modelo afectivo del mod. No toca la opinion del juego. Tope diario vida_maestro_dia.
        static void Maestro(List<Persona> chunk, List<string> ids, ContextoMundo ctx, int dia)
        {
            if (Estado.Maestro == null) return;
            if (dia != _diaMaestro) { _diaMaestro = dia; _maestroHoy = 0; }
            int tope = Estado.Cfg.Int("vida_maestro_dia");
            foreach (var p in chunk)
            {
                if (_maestroHoy >= tope) return;
                var cand = new List<Intencion>();
                foreach (var i in Estado.Agenda.Top(p.Id, 3))
                    if (i.Tipo == TipoIntencion.Charlar || i.Tipo == TipoIntencion.Visitar || i.Tipo == TipoIntencion.Cortejar || i.Tipo == TipoIntencion.Consolar || i.Tipo == TipoIntencion.Celebrar || i.Tipo == TipoIntencion.Descansar)
                        cand.Add(i);
                var elegida = Eleccion.Elige(cand, _rng, 0.12);
                if (elegida == null || !Planificador.Valida(p, elegida, ctx)) continue;
                var r = Estado.Maestro.Resuelve(elegida, dia, _rng, ids);
                if (!r.Resuelta) continue;
                _maestroHoy++;
                Estado.Agenda.Marca(elegida, r.Exito ? EstadoIntencion.Hecha : EstadoIntencion.Fallida);
                Estado.Ev.Ok("vida_maestro", elegida.Tipo + " " + (r.Exito ? "ok" : "fallo"));
                if (elegida.Tipo == TipoIntencion.Celebrar) Estado.Cultura.Suceso("comunidad", 0.3);
                if (r.Crisis != Ruptura.Ninguna)
                {
                    string t = Rupturas.Aplica(r.Crisis, p, ctx, _rng);
                    Estado.Cronica.Anota(dia, "crisis", t + " (estres)", 3);
                    Estado.Ev.Ok("vida_crisis", p.Nombre + ": " + r.Crisis);
                }
                if (r.Conversacion != null && Estado.Cfg.Bool("agentes_burbujas"))
                {
                    // las dos primeras lineas de la conversacion, cada una sobre quien la dice
                    for (int k = 0; k < r.Conversacion.Lineas.Count && k < 2; k++)
                    {
                        var l = r.Conversacion.Lineas[k];
                        Dilo(l.Hablante, l.Texto, r.Exito);
                    }
                    if (r.Conversacion.Lineas.Count == 0) Dilo(p.Id, r.Texto, r.Exito);
                }
                if (r.Conversacion != null && r.Conversacion.Exito && elegida.Tipo != TipoIntencion.Descansar) Estado.Cronica.Anota(dia, "charla", r.Texto, 1);
            }
        }

        // Normas (cada 30 dias) y director de drama (cada dia). Ni una ni otro ejecutan nada en el juego: las normas solo mueven
        // los umbrales del planificador y el director deja SUGERENCIAS en agentes.md/cronica (su ejecucion real esperaria a A8-A12).
        static void MensualYDirector(List<string> ids, ContextoMundo ctx, int dia)
        {
            if (dia / 30 != _ultimoMes)
            {
                _ultimoMes = dia / 30;
                var ps = new List<Persona>(); foreach (var id in ids) { var p = Estado.Personas.Get(id); if (p != null) ps.Add(p); }
                if (ps.Count >= 5)
                {
                    double hostilGrupal = Estado.Cfg.Bool("vida") ? Estado.Prejuicios.Hostilidad() : 0;
                    foreach (var cambio in Estado.Normas.Evalua(dia, Estado.Cultura, ps, id => 1, 0, hostilGrupal)) Estado.Cronica.Anota(dia, "norma", cambio, 6);
                    Estado.Prejuicios.Atenuacion = Estado.Normas.AtenuacionPrejuicio;
                    Estado.Prejuicios.Avanza(30);
                    Estado.Agenda.EnfriaHostil = (int)Math.Round(10 * Estado.Normas.FactorEnfriaHostil);
                }
            }
            if (Estado.Director == null) return;
            double t = Director.Mide(Estado.Afectos, ids, Estado.Agenda, Estado.Cronica, dia);
            var s = Estado.Director.Decide(dia, t, Estado.Afectos, ids, id => Estado.Personas.Get(id));
            if (s == null) return;
            var nombres = new List<string>(); foreach (var id in s.Pawns) nombres.Add(Estado.Ids.Nombre(id));
            Estado.UltimaSugerencia = "dia " + dia + ": " + s.Tipo + (nombres.Count > 0 ? " (" + string.Join(", ", nombres.ToArray()) + ")" : "") + " - " + s.Razon + " [tension " + Json.Num(s.TensionAntes) + " frente a objetivo " + Json.Num(s.Objetivo) + "]";
            Estado.Cronica.Anota(dia, "director", "El director sugiere: " + Estado.UltimaSugerencia, 2);
        }

        static void PlanLlmEnLote(List<Persona> chunk, List<List<Intencion>> reglas, ContextoMundo ctx)
        {
            if (_planEnVuelo || !Estado.Salud.Disponible || !Gancho.ColaHolgada) return;
            var ps = new List<Persona>(); var sug = new List<List<Intencion>>();
            int lote = Estado.Cfg.Int("agentes_lote");
            for (int k = 0; k < chunk.Count && ps.Count < lote; k++)
            {
                // prioridad: quien no tiene nada vivo; el resto, por turnos
                bool vacio = Estado.Agenda.Top(chunk[k].Id, 1).Count == 0;
                if (vacio || (k + _rondas) % 5 == 0) { ps.Add(chunk[k]); sug.Add(reglas[k]); }
            }
            if (ps.Count == 0 || !Estado.Presupuesto.Pide(PrioridadLlm.Planeacion)) return;
            string user = PlanLlm.Usuario(ps, sug, ctx);
            _planEnVuelo = true;
            bool encolado = Gancho.EncolaLlm(delegate
            {
                string raw = null;
                try { raw = Llm.Ask(PlanLlm.Sistema, user, 450); }
                finally
                {
                    string r = raw;
                    Principal.Encola(delegate { _planEnVuelo = false; AplicaPlan(r, ps, ctx); });
                }
            });
            if (!encolado) _planEnVuelo = false;       // la cola se lleno entre la comprobacion y el alta: no queda nada en vuelo
        }

        // Hilo principal (via Principal).
        static void AplicaPlan(string raw, List<Persona> ps, ContextoMundo ctx)
        {
            if (string.IsNullOrEmpty(raw)) { Estado.Ev.Fail("agentes_plan_llm", Llm.UltimoError); return; }
            var r = PlanLlm.Parse(raw, ps, ctx);
            if (r.Intenciones.Count == 0) { Estado.Ev.Fail("agentes_plan_llm", "ningun plan valido (rechazados " + r.Rechazadas + ")"); return; }
            Estado.Ev.Ok("agentes_plan_llm", r.Intenciones.Count + " planes, " + r.Rechazadas + " rechazados");
            foreach (var i in r.Intenciones)
            {
                var guardada = Estado.Agenda.Anade(i, ctx.Dia);
                string pensar;
                if (r.Pensamientos.TryGetValue(i.Pawn, out pensar)) Pensamientos[i.Pawn] = pensar;
                if (guardada != null && Estado.Cfg.Bool("agentes_burbujas") && _burbujaRonda != _rondas) { _burbujaRonda = _rondas; Dilo(i); }
            }
        }

        static void Dilo(Intencion i) { Dilo(i.Pawn, i.Texto, true); }

        static void Dilo(string id, string texto, bool positivo)
        {
            WeakReference w; Pawn p = null;
            if (Pawns.TryGetValue(id, out w)) p = w.Target as Pawn;
            if (p != null && !string.IsNullOrEmpty(texto)) Pantalla.Burbuja(p, Estado.Ids.Nombre(id), texto, positivo);
        }

        // Hilo principal, desde el hook de opinion. Si el juego produjo lo que la intencion esperaba, se da por hecha.
        public static void Observa(string ida, string idb, float delta)
        {
            if (!Estado.Cfg.Bool("agentes") || delta <= 0) return;
            foreach (var i in Estado.Agenda.Top(ida, 6))
            {
                bool social = i.Tipo == TipoIntencion.Charlar || i.Tipo == TipoIntencion.Visitar || i.Tipo == TipoIntencion.Cortejar || i.Tipo == TipoIntencion.Consolar || i.Tipo == TipoIntencion.Celebrar;
                if (!social || i.Objetivo != idb) continue;
                Estado.Agenda.Marca(i, EstadoIntencion.Hecha);
                Estado.Ev.Ok("agentes_intencion_observada", i.Tipo + " " + ida + "->" + idb);
                break;
            }
            Persona p = Estado.Personas.Get(ida);
            if (p != null) p.Needs.Pon("social", p.Needs.Social + 0.04);
        }

        public static string InformeMd()
        {
            var sb = new StringBuilder("# Ambiciones y planes\n\n");
            if (!Estado.Cfg.Bool("agentes")) return sb.Append("`agentes=0`: desactivado.\n").ToString();
            var ps = Estado.Personas.Todas();
            var ids = new List<string>(); foreach (var f in Estado.Fichas.Todas()) ids.Add(f.Id);
            ps.Sort((a, b) => { int c = Estado.Agenda.De(b.Id).Count.CompareTo(Estado.Agenda.De(a.Id).Count); return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id); });
            sb.Append("LLM hoy: ").Append(Estado.Presupuesto.Total).Append(" / ").Append(Estado.Cfg.Int("agentes_llm_dia")).Append(" llamadas (planeacion ").Append(Estado.Presupuesto.Usadas(PrioridadLlm.Planeacion)).Append("). ");
            var vigentes = new List<string>(); foreach (var nm in Estado.Normas.Activas) vigentes.Add(nm.Id);
            sb.Append("Normas vigentes: ").Append(vigentes.Count == 0 ? "(ninguna)" : string.Join(", ", vigentes.ToArray())).Append(". ");
            if (Estado.Director != null) sb.Append("Tension del reino ").Append(Json.Num(Estado.Director.Tension)).Append(" (director ").Append(Estado.Director.Estilo.ToString().ToLowerInvariant()).Append("). Ultima sugerencia: ").Append(Estado.UltimaSugerencia.Length > 0 ? Estado.UltimaSugerencia : "(ninguna)").Append(". ");
            sb.Append("Intenciones hechas ").Append(Estado.Agenda.Hechas).Append(", fallidas ").Append(Estado.Agenda.Fallidas).Append(".\n\n");
            if (Estado.Cfg.Bool("vida"))
            {
                sb.Append("**Vida.** Prejuicio medio por grupo: ");
                var pg = Estado.Prejuicios.MediaPorGrupo();
                if (pg.Count == 0) sb.Append("(aun sin datos)");
                for (int k = 0; k < pg.Count && k < 6; k++) sb.Append(k > 0 ? ", " : "").Append(pg[k].Key).Append(' ').Append(Json.Num(pg[k].Value));
                sb.Append(". Lo que el reino ha aprendido de tus vetos: ").Append(Estado.Prefs.Resumen().Length > 0 ? Estado.Prefs.Resumen() : "(nada aun)");
                sb.Append(". Ordenes entendidas: ").Append(Estado.Ordenes.Entendidas.Count > 0 ? string.Join(", ", Estado.Ordenes.Entendidas.ToArray()) : "(ninguna)");
                sb.Append(". Sucesos de vida oidos: ");
                bool alguno = false;
                foreach (var kv in Estado.Eventos.Cuentas) { if (kv.Key == "opinion") continue; sb.Append(alguno ? ", " : "").Append(kv.Key).Append('=').Append(kv.Value); alguno = true; }
                if (!alguno) sb.Append(Observadores.Instalados == 0 ? "(ninguno: sin ganchos.txt solo se oyen opiniones)" : "(ninguno todavia)");
                if (Estado.Eventos.Errores > 0) sb.Append(". ERRORES en el bus: ").Append(Estado.Eventos.Errores).Append(" (ultimo: ").Append(Estado.Eventos.UltimoError).Append(')');
                sb.Append(".\n\n");
            }
            int n = 0;
            foreach (var p in ps)
            {
                if (n++ >= 25) break;
                var hechas = new Dictionary<TipoIntencion, int>(); Dictionary<TipoIntencion, int> hh;
                if (Estado.Agenda.HechasPorPawn.TryGetValue(p.Id, out hh)) hechas = hh;
                string rol = Roles.De(hechas);
                sb.Append("## ").Append(p.Nombre).Append(rol.Length > 0 ? " (" + rol + ")" : "").Append(" — ").Append(p.Resumen()).Append("\n\n");
                sb.Append("Animo: ").Append(AnimoCalc.Nivel(AnimoCalc.Calcula(p, Estado.Afectos, ids), Rupturas.Umbral(p)));
                if (Estado.Cfg.Bool("vida"))
                {
                    sb.Append(". Estres ").Append((int)p.Estres).Append(" (nivel ").Append(Estres.Nivel(p.Estres)).Append(')');
                    RecuerdoFuerte mf = null;
                    foreach (var r in Estado.Fuertes.De(p.Id)) if (mf == null || r.Intensidad > mf.Intensidad) mf = r;
                    if (mf != null) sb.Append(". Le marco: ").Append(mf.Texto).Append(mf.Largo ? " (para siempre)" : "");
                }
                sb.Append(".\n");
                foreach (var i in Estado.Agenda.Top(p.Id, 3)) sb.Append("- (").Append(Json.Num(i.Prioridad)).Append(", ").Append(i.Origen).Append(") ").Append(i.Texto).Append(" — ").Append(i.Razon).Append('\n');
                string pen; if (Pensamientos.TryGetValue(p.Id, out pen)) sb.Append("- piensa: ").Append(pen).Append('\n');
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
