using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    // Lo que el agente necesita saber del mundo, sin tipos del juego.
    public sealed class ContextoMundo
    {
        public ModeloAfectivo Afectos;
        public IList<string> Vivos;
        public int Dia;
        public MetasReino Metas = new MetasReino();
        public Func<string, Persona> Persona;
        public Func<string, string> Nombre = id => id;
    }

    public sealed class Reflexion
    {
        public string Texto = "";
        public string Foco = "";        // id del otro pawn que mas pesa hoy, o ""
        public bool AmbicionNueva;
    }

    // Presupuesto del LLM local por dia de juego (RTX 3060 compartida con el juego). Prioridades:
    // escribir > planear > narrar > conversar. Cada clase tiene un TECHO propio para que una no
    // se coma a las demas; Escritura puede usar todo lo que quede.
    public enum PrioridadLlm { Escritura = 0, Planeacion = 1, Narrativa = 2, Conversacion = 3 }

    public sealed class PresupuestoLlm
    {
        readonly IClock reloj;
        readonly int[] usadas = new int[4];
        long diaActual = long.MinValue;
        readonly object cerrojo = new object();
        public long DiaTicks = 10 * TimeSpan.TicksPerMinute;
        public int LlamadasPorDia = 40;
        // Fraccion maxima del total para cada prioridad (Escritura = todo).
        public double[] Techo = { 1.0, 0.5, 0.2, 0.2 };

        public PresupuestoLlm(IClock reloj) { this.reloj = reloj; }

        public bool Pide(PrioridadLlm p)
        {
            lock (cerrojo)
            {
                long d = reloj.NowTicks / Math.Max(1, DiaTicks);
                if (d != diaActual) { diaActual = d; for (int i = 0; i < usadas.Length; i++) usadas[i] = 0; }
                int total = 0; foreach (var u in usadas) total += u;
                if (total >= LlamadasPorDia) return false;
                if (usadas[(int)p] >= Math.Ceiling(LlamadasPorDia * Techo[(int)p])) return false;
                usadas[(int)p]++;
                return true;
            }
        }

        public int Usadas(PrioridadLlm p) { lock (cerrojo) { return usadas[(int)p]; } }
        public int Total { get { lock (cerrojo) { int t = 0; foreach (var u in usadas) t += u; return t; } } }
    }

    // El LLM como "mente": una funcion que pregunta y devuelve texto (o null si falla).
    public interface IMente
    {
        string Pregunta(string sistema, string usuario, int maxTokens);
    }

    // Utility-AI de reglas: de personalidad + necesidades + ambiciones + relaciones salen intenciones.
    // Es el PLAN BASE (funciona sin LLM). El LLM solo puede elegir entre lo permitido por Valida.
    public static class Planificador
    {
        static string MejorPor(string yo, ContextoMundo c, Func<Par, double> f, double minimo)
        {
            string mejor = null; double mv = minimo;
            var orden = new List<string>(c.Vivos); orden.Sort(StringComparer.Ordinal);
            foreach (var o in orden)
            {
                if (o == yo) continue;
                double v = f(c.Afectos.Get(yo, o));
                if (v > mv + 1e-12) { mv = v; mejor = o; }
            }
            return mejor;
        }

        static string Vecino(string yo, ContextoMundo c)
        {
            var orden = new List<string>(c.Vivos); orden.Sort(StringComparer.Ordinal);
            orden.Remove(yo);
            if (orden.Count == 0) return "";
            return orden[FichaGen.Hash(yo + "|" + c.Dia) % orden.Count];
        }

        static Intencion Mk(Persona p, TipoIntencion t, string obj, string cat, double prio, string texto, string razon, int dia)
        {
            return new Intencion { Pawn = p.Id, Tipo = t, Objetivo = obj, Categoria = cat, Prioridad = Math.Max(0.05, Math.Min(1, prio)), Texto = texto, Razon = razon, Creada = dia, Vence = dia + 3 };
        }

        public static List<Intencion> Planea(Persona p, ContextoMundo c)
        {
            var r = new List<Intencion>();
            string yo = p.Nombre;
            double v; string nec = p.Needs.Mas(out v);

            // ---- necesidades ----
            if (v < 0.45)
            {
                if (nec == "social")
                {
                    string amigo = MejorPor(p.Id, c, par => par.Afecto, 0.05) ?? Vecino(p.Id, c);
                    if (amigo != null && amigo.Length > 0) r.Add(Mk(p, TipoIntencion.Charlar, amigo, "cohesion", 1 - v, yo + " busca compania y va a charlar con " + c.Nombre(amigo), "necesidad social " + Json.Num(v), c.Dia));
                }
                else if (nec == "descanso") r.Add(Mk(p, TipoIntencion.Descansar, "", "", 1 - v, yo + " necesita descansar", "descanso " + Json.Num(v), c.Dia));
                else if (nec == "seguridad") r.Add(Mk(p, TipoIntencion.Pedir, "", "defensa", 0.9 - v, yo + " pide mas proteccion para el reino", "seguridad " + Json.Num(v), c.Dia));
                else r.Add(Mk(p, TipoIntencion.Aprender, "", "cultura", 0.9 - v, yo + " quiere hacer algo que le llene: aprender", "autorrealizacion " + Json.Num(v), c.Dia));
            }

            // ---- ambiciones ----
            foreach (var a in p.Ambiciones)
            {
                if (a.Cumplida) continue;
                double w = a.Prioridad * (1 - 0.5 * a.Progreso);
                switch (a.Categoria)
                {
                    case "casarse":
                    {
                        string t = MejorPor(p.Id, c, par => par.Romance + 0.3 * par.Afecto, 0.02);
                        if (t != null) r.Add(Mk(p, TipoIntencion.Cortejar, t, "cohesion", w, yo + " corteja a " + c.Nombre(t), "ambicion: " + a.Texto, c.Dia));
                        break;
                    }
                    case "vengar":
                    {
                        string t = MejorPor(p.Id, c, par => par.Rencor, 0.35);
                        if (t != null)
                        {
                            double fuerza = c.Afectos.Get(p.Id, t).Rencor * (0.5 + 0.5 * p.Neuroticismo) * (1 - 0.5 * p.Amabilidad);
                            r.Add(Mk(p, TipoIntencion.Vengarse, t, "cohesion", Math.Max(w * 0.5, fuerza), yo + " trama vengarse de " + c.Nombre(t), "ambicion: " + a.Texto + "; rencor " + Json.Num(c.Afectos.Get(p.Id, t).Rencor), c.Dia));
                        }
                        break;
                    }
                    case "aprender": r.Add(Mk(p, TipoIntencion.Aprender, "", "cultura", w, yo + " se dedica a aprender", "ambicion: " + a.Texto, c.Dia)); break;
                    case "descubrir": r.Add(Mk(p, TipoIntencion.Pedir, "", "cultura", w, yo + " pide que se investigue algo nuevo", "ambicion: " + a.Texto, c.Dia)); break;
                    case "enriquecerse": r.Add(Mk(p, TipoIntencion.Trabajar, "", "economia", w, yo + " trabaja para enriquecerse", "ambicion: " + a.Texto, c.Dia)); break;
                    case "proteger": r.Add(Mk(p, TipoIntencion.Pedir, "", "defensa", w, yo + " pide reforzar la defensa", "ambicion: " + a.Texto, c.Dia)); break;
                    case "mandar":
                    {
                        string rival = MejorPor(p.Id, c, par => par.Rivalidad, 0.3);
                        if (rival != null && p.Amabilidad < 0.6) r.Add(Mk(p, TipoIntencion.Intrigar, rival, "cohesion", w * 0.8, yo + " intriga contra su rival " + c.Nombre(rival), "ambicion: " + a.Texto + "; rivalidad", c.Dia));
                        else r.Add(Mk(p, TipoIntencion.Pedir, "", "crecimiento", w, yo + " pide audiencia para ganar influencia", "ambicion: " + a.Texto, c.Dia));
                        break;
                    }
                    default: // paz
                    {
                        string amigo = MejorPor(p.Id, c, par => par.Afecto, 0.05);
                        if (amigo != null) r.Add(Mk(p, TipoIntencion.Celebrar, amigo, "cohesion", w * 0.6, yo + " propone una reunion con " + c.Nombre(amigo), "ambicion: " + a.Texto, c.Dia));
                        break;
                    }
                }
            }

            // ---- reaccion a lo que siente por otros ----
            string deudor = MejorPor(p.Id, c, par => par.Deuda, 0.4);
            if (deudor != null) r.Add(Mk(p, TipoIntencion.Consolar, deudor, "cohesion", 0.4 + 0.3 * p.Amabilidad, yo + " quiere devolver un favor a " + c.Nombre(deudor), "gratitud", c.Dia));

            r.RemoveAll(i => !Valida(p, i, c));
            r.Sort((x, y) => { int k = y.Prioridad.CompareTo(x.Prioridad); return k != 0 ? k : string.CompareOrdinal(x.Texto, y.Texto); });
            if (r.Count > 3) r.RemoveRange(3, r.Count - 3);
            return r;
        }

        // GUARDARRAILES: lo que valga para las reglas vale para el LLM. Un modelo no puede hacer que un
        // pawn sin rencor trame una venganza ni que corteje a quien no conoce.
        public static bool Valida(Persona p, Intencion i, ContextoMundo c)
        {
            bool conObjetivo = i.Tipo == TipoIntencion.Cortejar || i.Tipo == TipoIntencion.Intrigar || i.Tipo == TipoIntencion.Vengarse || i.Tipo == TipoIntencion.Consolar;
            if (conObjetivo && (i.Objetivo.Length == 0 || i.Objetivo == p.Id || !ContieneId(c.Vivos, i.Objetivo))) return false;
            if (i.Objetivo.Length > 0 && !ContieneId(c.Vivos, i.Objetivo)) return false;
            if (i.Objetivo == p.Id) return false;
            Par par = i.Objetivo.Length > 0 ? c.Afectos.Get(p.Id, i.Objetivo) : new Par();
            switch (i.Tipo)
            {
                case TipoIntencion.Vengarse: return par.Rencor >= 0.35 && p.Amabilidad <= 0.9 && par.Deuda < 0.4;
                case TipoIntencion.Intrigar: return (par.Rencor >= 0.3 || par.Rivalidad >= 0.3) && p.Amabilidad <= 0.9 && par.Deuda < 0.4;
                case TipoIntencion.Cortejar: return par.Afecto >= 0.0 && par.Rencor < 0.3;
                case TipoIntencion.Pedir: return i.Categoria.Length == 0 || Array.IndexOf(MetasReino.Categorias, i.Categoria) >= 0;
                default: return true;
            }
        }

        static bool ContieneId(IList<string> l, string id)
        {
            for (int k = 0; k < l.Count; k++) if (l[k] == id) return true;
            return false;
        }
    }

    // Ciclo vital diario: reflexion -> (planea) -> consolida. Reglas puras y deterministas.
    public static class Agente
    {
        public static Reflexion Reflexiona(Persona p, ContextoMundo c)
        {
            var r = new Reflexion();
            string peor = null, mejor = null; double pr = 0, mr = 0;
            var orden = new List<string>(c.Vivos); orden.Sort(StringComparer.Ordinal);
            foreach (var o in orden)
            {
                if (o == p.Id) continue;
                Par par = c.Afectos.Get(p.Id, o);
                if (par.Rencor > pr + 1e-12) { pr = par.Rencor; peor = o; }
                double q = par.Afecto + par.Romance;
                if (q > mr + 1e-12) { mr = q; mejor = o; }
            }
            if (pr > 0.5 && peor != null)
            {
                r.Foco = peor;
                r.Texto = "Hoy pesa en " + p.Nombre + " el rencor hacia " + c.Nombre(peor) + ".";
                // La ambicion evoluciona con lo vivido: un rencor profundo en alguien inquieto engendra deseo de venganza.
                var v = p.Ambiciones.Find(a => a.Categoria == "vengar" && !a.Cumplida);
                if (v != null) v.Prioridad = Math.Min(1, Math.Max(v.Prioridad, 0.4 + 0.5 * pr));
                else if (p.Neuroticismo >= 0.5 && pr >= 0.7)
                {
                    p.Ambiciones.Add(new Ambicion { Texto = "hacer pagar a " + c.Nombre(peor), Categoria = "vengar", Objetivo = peor, Plazo = Plazo.Medio, Prioridad = 0.4 + 0.4 * pr });
                    r.AmbicionNueva = true;
                }
            }
            else if (mejor != null && mr > 0.4)
            {
                r.Foco = mejor;
                r.Texto = "Hoy " + p.Nombre + " piensa con cariño en " + c.Nombre(mejor) + ".";
                var cs = p.Ambiciones.Find(a => a.Categoria == "casarse" && !a.Cumplida);
                if (cs != null && c.Afectos.Get(p.Id, mejor).Romance > 0.3) cs.Prioridad = Math.Min(1, cs.Prioridad + 0.05);
            }
            else r.Texto = p.Nombre + " tuvo un dia tranquilo.";
            // el rencor/duelo hunde la seguridad percibida
            if (pr > 0.6) p.Needs.Pon("seguridad", p.Needs.Seguridad - 0.02);
            return r;
        }

        static string CategoriaDe(Intencion i)
        {
            switch (i.Tipo)
            {
                case TipoIntencion.Cortejar: return "casarse";
                case TipoIntencion.Aprender: return "aprender";
                case TipoIntencion.Vengarse: case TipoIntencion.Intrigar: return i.Tipo == TipoIntencion.Intrigar ? "mandar" : "vengar";
                case TipoIntencion.Trabajar: return "enriquecerse";
                case TipoIntencion.Pedir:
                    return i.Categoria == "economia" ? "enriquecerse" : i.Categoria == "defensa" ? "proteger" : i.Categoria == "cultura" ? "descubrir" : "mandar";
                default: return "paz";
            }
        }

        // Cierre del dia: restituye necesidades segun lo hecho, avanza ambiciones y envejece las necesidades.
        // Devuelve las ambiciones cumplidas HOY (para la cronica).
        public static List<Ambicion> Consolida(Persona p, Agenda ag, double dias)
        {
            var cumplidas = new List<Ambicion>();
            foreach (var i in ag.De(p.Id))
            {
                if (i.Contada || (i.Estado != EstadoIntencion.Hecha && i.Estado != EstadoIntencion.Fallida)) continue;
                i.Contada = true;
                if (i.Estado == EstadoIntencion.Fallida) { p.Needs.Pon("autorrealizacion", p.Needs.Autorrealizacion - 0.03); continue; }
                switch (i.Tipo)
                {
                    case TipoIntencion.Charlar: p.Needs.Pon("social", p.Needs.Social + 0.25); break;
                    case TipoIntencion.Descansar: p.Needs.Pon("descanso", p.Needs.Descanso + 0.5); break;
                    case TipoIntencion.Celebrar: p.Needs.Pon("social", p.Needs.Social + 0.3); break;
                    case TipoIntencion.Visitar: case TipoIntencion.Cortejar: case TipoIntencion.Consolar: p.Needs.Pon("social", p.Needs.Social + 0.15); break;
                    case TipoIntencion.Trabajar: p.Needs.Pon("autorrealizacion", p.Needs.Autorrealizacion + 0.1); break;
                    case TipoIntencion.Aprender: p.Needs.Pon("autorrealizacion", p.Needs.Autorrealizacion + 0.15); break;
                    case TipoIntencion.Pedir: p.Needs.Pon("seguridad", p.Needs.Seguridad + 0.05); break;
                }
                string cat = CategoriaDe(i);
                foreach (var a in p.Ambiciones)
                {
                    if (a.Cumplida || a.Categoria != cat) continue;
                    a.Progreso = Math.Min(1, a.Progreso + 0.06 * (0.5 + p.Escrupulosidad));
                    if (a.Progreso >= 1) { a.Cumplida = true; cumplidas.Add(a); }
                    break;
                }
            }
            p.Needs.Avanza(dias, p);
            return cumplidas;
        }
    }

    // Planificacion con LLM EN LOTE: una sola llamada para varios pawns (patron del Consejo). La respuesta
    // se valida pawn a pawn con los mismos guardarrailes que las reglas; lo invalido se ignora.
    public static class PlanLlm
    {
        public const string Sistema =
            "Eres el director de un reino medieval. Para cada personaje elige UNA intencion para hoy, coherente con su caracter, " +
            "sus ambiciones y sus relaciones. 'tipo' es EXACTAMENTE uno de: charlar, visitar, cortejar, intrigar, pedir, aprender, " +
            "trabajar, descansar, consolar, vengarse, celebrar. 'objetivo' es el id de otro personaje de la lista o \"\". " +
            "'texto' una frase breve en espanol y 'piensa' un pensamiento interior breve. " +
            "Responde SOLO un JSON: {\"planes\":[{\"id\":\"..\",\"tipo\":\"..\",\"objetivo\":\"..\",\"texto\":\"..\",\"piensa\":\"..\"}]}";

        public static string Usuario(IList<Persona> ps, IList<List<Intencion>> sugeridas, ContextoMundo c)
        {
            var sb = new StringBuilder();
            sb.Append("Dia ").Append(c.Dia).Append(". Personajes:\n");
            for (int k = 0; k < ps.Count; k++)
            {
                var p = ps[k];
                sb.Append("- id=").Append(p.Id).Append(" nombre=").Append(p.Nombre).Append(": ").Append(p.Resumen());
                if (k < sugeridas.Count && sugeridas[k].Count > 0)
                {
                    sb.Append(" | opciones: ");
                    for (int j = 0; j < sugeridas[k].Count; j++) { if (j > 0) sb.Append("; "); sb.Append(sugeridas[k][j].Tipo.ToString().ToLowerInvariant()).Append(' ').Append(sugeridas[k][j].Objetivo); }
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public sealed class Resultado
        {
            public List<Intencion> Intenciones = new List<Intencion>();
            public Dictionary<string, string> Pensamientos = new Dictionary<string, string>();
            public int Rechazadas;
        }

        public static Resultado Parse(string raw, IList<Persona> ps, ContextoMundo c)
        {
            var res = new Resultado();
            var d = Json.ParseObjeto(raw);
            if (d == null) return res;
            var lista = Json.Lista(d, "planes");
            if (lista == null) return res;
            var vistos = new HashSet<string>();
            foreach (var o in lista)
            {
                string id = Json.Str(o, "id");
                Persona p = null; foreach (var q in ps) if (q.Id == id) p = q;
                if (p == null || !vistos.Add(id)) { res.Rechazadas++; continue; }
                TipoIntencion t; bool ok = false; t = TipoIntencion.Charlar;
                string ts = Json.Str(o, "tipo").Trim().ToLowerInvariant();
                foreach (TipoIntencion x in Enum.GetValues(typeof(TipoIntencion))) if (x.ToString().ToLowerInvariant() == ts) { t = x; ok = true; }
                if (!ok) { res.Rechazadas++; continue; }
                var i = new Intencion
                {
                    Pawn = id, Tipo = t, Objetivo = Json.Str(o, "objetivo").Trim(), Texto = Json.UnaLinea(Json.Str(o, "texto")), Origen = "llm",
                    Prioridad = 0.75, Creada = c.Dia, Vence = c.Dia + 3, Razon = "elegida por el director"
                };
                if (i.Texto.Length == 0 || i.Texto.Length > 140) i.Texto = p.Nombre + " " + ts + (i.Objetivo.Length > 0 ? " con " + c.Nombre(i.Objetivo) : "");
                if (t == TipoIntencion.Pedir) i.Categoria = "cohesion";
                if (!Planificador.Valida(p, i, c)) { res.Rechazadas++; continue; }
                res.Intenciones.Add(i);
                string pensar = Json.UnaLinea(Json.Str(o, "piensa"));
                if (pensar.Length > 0 && pensar.Length <= 160) res.Pensamientos[id] = pensar;
            }
            return res;
        }
    }
}
