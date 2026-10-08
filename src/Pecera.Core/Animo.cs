using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    public enum Ruptura { Ninguna, Retiro, Arrebato, Hundimiento }

    // Animo y rupturas mentales. Modelo tomado del de RimWorld (umbrales publicados en su wiki): se rompe quien cae por
    // debajo de 3 umbrales (menor / mayor / extremo), con un tiempo medio entre rupturas cada vez mas corto:
    //     menor  35 %  media 10 dias      mayor  20 % (4/7 del menor)  media 3 dias      extremo  5 % (1/7)  media 0.7 dias
    // y el temperamento desplaza el umbral (el nervioso se rompe antes, el sereno despues). Aqui cada ruptura es una
    // CONSECUENCIA INTERNA (eventos afectivos, necesidades, intenciones): no escribe en el juego.
    public static class AnimoCalc
    {
        // 0..1: necesidades (60 %) + clima social (30 %) - trauma (hasta 10 puntos).
        public static double Calcula(Persona p, ModeloAfectivo m, IList<string> vivos)
        {
            double needs = (p.Needs.Descanso + p.Needs.Social + p.Needs.Seguridad + p.Needs.Autorrealizacion) / 4;
            double clima = 0.5, trauma = 0; int n = 0;
            double suma = 0;
            foreach (var o in vivos)
            {
                if (o == p.Id) continue;
                suma += m.Sentimiento(p.Id, o);
                trauma = Math.Max(trauma, m.Get(p.Id, o).Trauma);
                n++;
            }
            if (n > 0) clima = 0.5 + 0.5 * (suma / n) * 2;             // sentimiento medio -1..1 -> 0..1 (acotado abajo)
            clima = Math.Max(0, Math.Min(1, clima));
            // 0.3 es el «suelo de contento» de vivir en paz; sin el, un reino normal estaba siempre por debajo del umbral y se rompia el 40 % del tiempo (simulador).
            return Math.Max(0, Math.Min(1, 0.5 * needs + 0.2 * clima + 0.3 - 0.1 * trauma));
        }

        public static string Nivel(double animo, double umbral)
        {
            if (animo < umbral / 7) return "al borde del colapso";
            if (animo < umbral * 4 / 7) return "al limite";
            if (animo < umbral) return "inquieto";
            return animo > 0.85 ? "radiante" : "sereno";
        }
    }

    public static class Rupturas
    {
        public const double UmbralBase = 0.35, MediaMenor = 10, MediaMayor = 3, MediaExtrema = 0.7;

        // Neuroticismo 0..1 desplaza el umbral +-0.15 (RimWorld: sereno -18 %, volatil +15 %). Acotado a [0.05, 0.5].
        public static double Umbral(Persona p) { return Math.Max(0.05, Math.Min(0.5, UmbralBase + 0.15 * (2 * p.Neuroticismo - 1))); }

        // Probabilidad de ruptura en 'dias' dado el animo: se mira el nivel mas grave en el que se esta.
        public static Ruptura Evalua(Persona p, double animo, Rng rng, double dias)
        {
            double u = Umbral(p);
            double media; Ruptura r;
            if (animo < u / 7) { media = MediaExtrema; r = Ruptura.Hundimiento; }
            else if (animo < u * 4 / 7) { media = MediaMayor; r = Ruptura.Arrebato; }
            else if (animo < u) { media = MediaMenor; r = Ruptura.Retiro; }
            else return Ruptura.Ninguna;
            return rng.Chance(1 - Math.Exp(-dias / media)) ? r : Ruptura.Ninguna;
        }

        // Consecuencias internas. Devuelve el texto para la cronica.
        public static string Aplica(Ruptura r, Persona p, ContextoMundo c, Rng rng)
        {
            string yo = p.Nombre;
            switch (r)
            {
                case Ruptura.Retiro:
                    p.Needs.Pon("social", p.Needs.Social - 0.1); p.Needs.Pon("descanso", p.Needs.Descanso + 0.15);
                    return yo + " se aisla un dia, harto de todos.";
                case Ruptura.Arrebato:
                {
                    // Se desahoga con quien peor lleva; si no hay nadie, con alguien cercano al azar (la rabia no siempre es justa).
                    string objetivo = null; double mr = 0.05;
                    var orden = Planificador.Orden(c);
                    foreach (var o in orden) { if (o == p.Id) continue; double rr = c.Afectos.Get(p.Id, o).Rencor; if (rr > mr + 1e-12) { mr = rr; objetivo = o; } }
                    if (objetivo == null) { var otros = new List<string>(orden); otros.Remove(p.Id); if (otros.Count == 0) return yo + " grita al vacio."; objetivo = otros[rng.Next(otros.Count)]; }
                    c.Afectos.Evento(objetivo, p.Id, TipoEvento.Agravio, 0.5);
                    c.Afectos.Evento(p.Id, objetivo, TipoEvento.Perdon, 0.2);                 // desahogarse alivia un poco
                    p.Needs.Pon("seguridad", p.Needs.Seguridad - 0.05);
                    return yo + " estalla contra " + c.Nombre(objetivo) + " delante de todos.";
                }
                case Ruptura.Hundimiento:
                    p.Needs.Pon("seguridad", p.Needs.Seguridad - 0.15); p.Needs.Pon("autorrealizacion", p.Needs.Autorrealizacion - 0.1);
                    return yo + " se hunde y no sale de su cuarto.";
                default: return "";
            }
        }

        // Inspiracion: lo opuesto. Un animo muy alto da, de vez en cuando, un empujon creativo (media 15 dias).
        public static bool Inspira(Persona p, double animo, Rng rng, double dias)
        {
            if (animo <= 0.85) return false;
            if (!rng.Chance(1 - Math.Exp(-dias / 15.0))) return false;
            p.Needs.Pon("autorrealizacion", p.Needs.Autorrealizacion + 0.2);
            var a = p.Principal();
            if (a != null) a.Progreso = Math.Min(1, a.Progreso + 0.05);
            return true;
        }
    }
}
