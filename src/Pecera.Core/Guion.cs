using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    public sealed class Paso
    {
        public TipoIntencion Tipo;
        public string Objetivo = "";
        public string Categoria = "";
    }

    // Plan de VARIOS pasos hacia una ambicion (GOAP-lite / plan jerarquico de Generative Agents: el dia sale de metas largas, no de
    // impulsos sueltos). «Casarse» no es una intencion: es charlar -> cortejar -> celebrar -> pedir la boda. El guion avanza cuando el
    // paso actual se cumple, y se reconstruye si el objetivo desaparece o si lleva demasiado tiempo sin avanzar.
    public sealed class Guion
    {
        public string Ambicion = "";
        public List<Paso> Pasos = new List<Paso>();
        public int Actual;
        public int Dia;                         // dia de creacion
        public int UltimoAvance;
        public bool Terminado { get { return Actual >= Pasos.Count; } }
        public Paso PasoActual { get { return Terminado ? null : Pasos[Actual]; } }
        public static int Paciencia = 15;       // dias sin avanzar antes de rehacerlo
        public static int DiasEntrePasos = 2;   // un paso no se encadena con el siguiente el mismo dia: las cosas llevan tiempo
    }

    public static class Guiones
    {
        static Paso P(TipoIntencion t, string obj, string cat) { return new Paso { Tipo = t, Objetivo = obj ?? "", Categoria = cat ?? "" }; }

        // Devuelve el guion vigente de esa ambicion (lo crea o lo rehace si hace falta). null = no hay guion posible ahora.
        public static Guion De(Persona p, Ambicion a, ContextoMundo c)
        {
            Guion g;
            if (p.Guiones.TryGetValue(a.Categoria, out g))
            {
                bool objetivoPerdido = false;
                foreach (var s in g.Pasos) if (s.Objetivo.Length > 0 && !Contiene(c.Vivos, s.Objetivo)) objetivoPerdido = true;
                bool estancado = c.Dia - g.UltimoAvance > Guion.Paciencia;
                if (!g.Terminado && !objetivoPerdido && !estancado) return g;
                p.Guiones.Remove(a.Categoria);
            }
            g = Construye(p, a, c);
            if (g != null) { g.Dia = c.Dia; g.UltimoAvance = c.Dia; p.Guiones[a.Categoria] = g; }
            return g;
        }

        static bool Contiene(IList<string> l, string id) { for (int i = 0; i < l.Count; i++) if (l[i] == id) return true; return false; }

        static Guion Construye(Persona p, Ambicion a, ContextoMundo c)
        {
            var g = new Guion { Ambicion = a.Categoria };
            switch (a.Categoria)
            {
                case "casarse":
                {
                    string t = Planificador.MejorPor(p.Id, c, par => par.Romance + 0.3 * par.Afecto, 0.02) ?? Planificador.MejorPor(p.Id, c, par => par.Afecto, 0.0);
                    if (t == null) return null;
                    g.Pasos.Add(P(TipoIntencion.Charlar, t, "cohesion")); g.Pasos.Add(P(TipoIntencion.Cortejar, t, "cohesion"));
                    g.Pasos.Add(P(TipoIntencion.Celebrar, t, "cohesion")); g.Pasos.Add(P(TipoIntencion.Pedir, "", "cohesion"));
                    // Se empieza donde de verdad esta la relacion: quien ya se trata con cariño no vuelve a «charlar» para conocerse.
                    Par pt = c.Afectos.Get(p.Id, t);
                    if (pt.Afecto >= 0.25 || pt.Romance >= 0.05) g.Actual = 1;
                    if (pt.Romance >= 0.5 && pt.Afecto >= 0.4) g.Actual = 2;
                    break;
                }
                case "vengar":
                {
                    string t = (a.Objetivo.Length > 0 && Contiene(c.Vivos, a.Objetivo)) ? a.Objetivo : Planificador.MejorPor(p.Id, c, par => par.Rencor, c.UmbralRencorHostil);
                    if (t == null) return null;
                    g.Pasos.Add(P(TipoIntencion.Intrigar, t, "cohesion")); g.Pasos.Add(P(TipoIntencion.Vengarse, t, "cohesion"));
                    break;
                }
                case "aprender":
                {
                    string m = Planificador.MejorPor(p.Id, c, par => par.Afecto, 0.05);
                    if (m != null) g.Pasos.Add(P(TipoIntencion.Visitar, m, "cultura"));
                    g.Pasos.Add(P(TipoIntencion.Aprender, "", "cultura")); g.Pasos.Add(P(TipoIntencion.Aprender, "", "cultura"));
                    break;
                }
                case "enriquecerse": g.Pasos.Add(P(TipoIntencion.Trabajar, "", "economia")); g.Pasos.Add(P(TipoIntencion.Trabajar, "", "economia")); g.Pasos.Add(P(TipoIntencion.Pedir, "", "economia")); break;
                case "proteger": g.Pasos.Add(P(TipoIntencion.Pedir, "", "defensa")); g.Pasos.Add(P(TipoIntencion.Trabajar, "", "defensa")); break;
                case "descubrir": g.Pasos.Add(P(TipoIntencion.Aprender, "", "cultura")); g.Pasos.Add(P(TipoIntencion.Pedir, "", "cultura")); break;
                case "mandar":
                {
                    string aliado = Planificador.MejorPor(p.Id, c, par => par.Afecto, 0.1);
                    if (aliado != null) g.Pasos.Add(P(TipoIntencion.Celebrar, aliado, "cohesion"));
                    g.Pasos.Add(P(TipoIntencion.Pedir, "", "crecimiento"));
                    string rival = Planificador.MejorPor(p.Id, c, par => par.Rivalidad, 0.3);
                    if (rival != null && p.Amabilidad < 0.6) g.Pasos.Add(P(TipoIntencion.Intrigar, rival, "cohesion"));
                    break;
                }
                default:
                {
                    string amigo = Planificador.MejorPor(p.Id, c, par => par.Afecto, 0.05);
                    if (amigo == null) return null;
                    g.Pasos.Add(P(TipoIntencion.Celebrar, amigo, "cohesion")); g.Pasos.Add(P(TipoIntencion.Consolar, amigo, "cohesion"));
                    break;
                }
            }
            return g.Pasos.Count == 0 ? null : g;
        }

        // Llamado al cerrar una intencion HECHA: si es el paso actual de algun guion, lo avanza y devuelve ese guion (o null).
        // Orden fijo por categoria para que sea determinista.
        public static Guion Avanza(Persona p, Intencion hecha, int dia)
        {
            var cats = new List<string>(p.Guiones.Keys); cats.Sort(StringComparer.Ordinal);
            foreach (var cat in cats)
            {
                var g = p.Guiones[cat];
                var s = g.PasoActual;
                if (s == null || s.Tipo != hecha.Tipo || s.Objetivo != hecha.Objetivo) continue;
                if (g.Actual > 0 && dia - g.UltimoAvance < Guion.DiasEntrePasos) continue;      // demasiado pronto: cuenta como practica, no como paso
                g.Actual++; g.UltimoAvance = dia;
                return g;
            }
            return null;
        }
    }
}
