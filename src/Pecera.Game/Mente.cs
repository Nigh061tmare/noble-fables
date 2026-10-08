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
        static int _rondas, _burbujaRonda = -1;
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
                PuedeHablar = (a, b) => Estado.Freno.Puede(a, b, dia)
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
                    foreach (var a in Agente.Consolida(p, Estado.Agenda, dias))
                    {
                        var sucesora = Agente.Sucesora(p, a, dia);
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
                Estado.Agenda.Caduca(dia);
                PlanLlmEnLote(chunk, reglas, ctx);
                if (_rondas % 10 == 0) Estado.Personas.Guarda();
            }
            catch (Exception e)
            {
                Estado.Ev.Fail("agentes_plan_reglas", e.GetType().Name + ": " + e.Message);
                UnityEngine.Debug.Log("[Pecera] agentes: " + e);
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
                    foreach (var cambio in Estado.Normas.Evalua(dia, Estado.Cultura, ps, id => 1, 0)) Estado.Cronica.Anota(dia, "norma", cambio, 6);
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

        static void Dilo(Intencion i)
        {
            WeakReference w; Pawn p = null;
            if (Pawns.TryGetValue(i.Pawn, out w)) p = w.Target as Pawn;
            if (p != null) Pantalla.Burbuja(p, Estado.Ids.Nombre(i.Pawn), i.Texto, true);
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
            int n = 0;
            foreach (var p in ps)
            {
                if (n++ >= 25) break;
                var hechas = new Dictionary<TipoIntencion, int>(); Dictionary<TipoIntencion, int> hh;
                if (Estado.Agenda.HechasPorPawn.TryGetValue(p.Id, out hh)) hechas = hh;
                string rol = Roles.De(hechas);
                sb.Append("## ").Append(p.Nombre).Append(rol.Length > 0 ? " (" + rol + ")" : "").Append(" — ").Append(p.Resumen()).Append("\n\n");
                sb.Append("Animo: ").Append(AnimoCalc.Nivel(AnimoCalc.Calcula(p, Estado.Afectos, ids), Rupturas.Umbral(p))).Append(".\n");
                foreach (var i in Estado.Agenda.Top(p.Id, 3)) sb.Append("- (").Append(Json.Num(i.Prioridad)).Append(", ").Append(i.Origen).Append(") ").Append(i.Texto).Append(" — ").Append(i.Razon).Append('\n');
                string pen; if (Pensamientos.TryGetValue(p.Id, out pen)) sb.Append("- piensa: ").Append(pen).Append('\n');
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
