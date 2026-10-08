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

        static ContextoMundo Ctx(List<string> ids, int dia)
        {
            return new ContextoMundo
            {
                Afectos = Estado.Afectos, Vivos = ids, VivosOrdenados = true, Dia = dia, Metas = Estado.Metas,
                Persona = id => Estado.Personas.Get(id), Nombre = id => Estado.Ids.Nombre(id)
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
                        Estado.Cronica.Anota(dia, "sueno", p.Nombre + " cumple su ambicion: " + a.Texto, 5);
                }
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
            ps.Sort((a, b) => { int c = Estado.Agenda.De(b.Id).Count.CompareTo(Estado.Agenda.De(a.Id).Count); return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id); });
            sb.Append("LLM hoy: ").Append(Estado.Presupuesto.Total).Append(" / ").Append(Estado.Cfg.Int("agentes_llm_dia")).Append(" llamadas (planeacion ").Append(Estado.Presupuesto.Usadas(PrioridadLlm.Planeacion)).Append("). ");
            sb.Append("Intenciones hechas ").Append(Estado.Agenda.Hechas).Append(", fallidas ").Append(Estado.Agenda.Fallidas).Append(".\n\n");
            int n = 0;
            foreach (var p in ps)
            {
                if (n++ >= 25) break;
                sb.Append("## ").Append(p.Nombre).Append(" — ").Append(p.Resumen()).Append("\n\n");
                foreach (var i in Estado.Agenda.Top(p.Id, 3)) sb.Append("- (").Append(Json.Num(i.Prioridad)).Append(", ").Append(i.Origen).Append(") ").Append(i.Texto).Append(" — ").Append(i.Razon).Append('\n');
                string pen; if (Pensamientos.TryGetValue(p.Id, out pen)) sb.Append("- piensa: ").Append(pen).Append('\n');
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
