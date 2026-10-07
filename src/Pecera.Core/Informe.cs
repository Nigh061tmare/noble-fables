using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Pecera.Core
{
    public sealed class ResumenSesion
    {
        public int Eventos, ConVoz, SinFrase, Empujones, Errores;
        public double SumaAbsDelta, MaxEmpujon;
        public string Desde = "", Hasta = "";
        public readonly Dictionary<string, int> PorPawn = new Dictionary<string, int>();
        public readonly Dictionary<string, int> PorPareja = new Dictionary<string, int>();
        public int Lineas, LineasInvalidas;
    }

    public sealed class EstadisticaFps
    {
        public int N;
        public double Media, P5, P1, Min, Desv;
    }

    public sealed class ComparacionAB
    {
        public EstadisticaFps On, Off;
        public double DeltaPct, T;
        public string Veredicto = "";
    }

    // Observabilidad (K): informe de sesion, estadistica de FPS, A/B del coste y grafo.
    public static class Informe
    {
        public static ResumenSesion Resume(IEnumerable<string> lineasOpiniones)
        {
            var r = new ResumenSesion();
            foreach (var l in lineasOpiniones)
            {
                r.Lineas++;
                object d;
                if (!Json.TryParse(l, out d)) { r.LineasInvalidas++; continue; }
                string a = Json.Str(d, "a"), b = Json.Str(d, "b");
                if (a.Length == 0) { r.LineasInvalidas++; continue; }
                r.Eventos++;
                string t = Json.Str(d, "t");
                if (r.Desde.Length == 0 || string.CompareOrdinal(t, r.Desde) < 0) r.Desde = t;
                if (string.CompareOrdinal(t, r.Hasta) > 0) r.Hasta = t;
                string dice = Json.Str(d, "dice");
                if (dice == "(sin frase)") r.SinFrase++;
                else if (dice.Length > 0) r.ConVoz++;
                double aj = Json.Num(d, "ajuste", 0);
                if (Math.Abs(aj) > 1e-9) { r.Empujones++; r.MaxEmpujon = Math.Max(r.MaxEmpujon, Math.Abs(aj)); }
                r.SumaAbsDelta += Math.Abs(Json.Num(d, "delta", 0));
                Cuenta(r.PorPawn, a);
                Cuenta(r.PorPareja, a + " -> " + b);
            }
            return r;
        }

        static void Cuenta(Dictionary<string, int> d, string k) { int n; d.TryGetValue(k, out n); d[k] = n + 1; }

        static List<KeyValuePair<string, int>> Top(Dictionary<string, int> d, int n)
        {
            var l = new List<KeyValuePair<string, int>>(d);
            l.Sort((x, y) => { int c = y.Value.CompareTo(x.Value); return c != 0 ? c : string.CompareOrdinal(x.Key, y.Key); });
            if (l.Count > n) l.RemoveRange(n, l.Count - n);
            return l;
        }

        // fps.csv: hora,fps_medio,fps_instantanea,llamadas_opinion
        public static double[] LeeFps(IEnumerable<string> lineas)
        {
            var v = new List<double>();
            foreach (var l in lineas)
            {
                var p = l.Split(',');
                double x;
                if (p.Length >= 3 && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out x)) v.Add(x);
            }
            return v.ToArray();
        }

        public static EstadisticaFps Fps(double[] v)
        {
            var e = new EstadisticaFps { N = v.Length };
            if (v.Length == 0) return e;
            var s = (double[])v.Clone(); Array.Sort(s);
            double suma = 0; foreach (var x in s) suma += x;
            e.Media = suma / s.Length; e.Min = s[0];
            e.P5 = s[(int)Math.Floor(0.05 * (s.Length - 1))];
            e.P1 = s[(int)Math.Floor(0.01 * (s.Length - 1))];
            double q = 0; foreach (var x in s) q += (x - e.Media) * (x - e.Media);
            e.Desv = s.Length > 1 ? Math.Sqrt(q / (s.Length - 1)) : 0;
            return e;
        }

        // A/B formal: misma partida/escena con el mod ON y OFF. Welch t sobre FPS medios.
        // Veredicto: "sin caida medible" solo si la diferencia es <3 % Y |t| < 2 con >=30 muestras por brazo.
        public static ComparacionAB CompararAB(double[] on, double[] off)
        {
            var c = new ComparacionAB { On = Fps(on), Off = Fps(off) };
            if (c.On.N < 30 || c.Off.N < 30) { c.Veredicto = "insuficiente (hacen falta >=30 muestras por brazo)"; return c; }
            c.DeltaPct = c.Off.Media > 0 ? 100.0 * (c.On.Media - c.Off.Media) / c.Off.Media : 0;
            double se = Math.Sqrt(c.On.Desv * c.On.Desv / c.On.N + c.Off.Desv * c.Off.Desv / c.Off.N);
            c.T = se > 0 ? (c.On.Media - c.Off.Media) / se : 0;
            if (c.DeltaPct < -3 && c.T < -2) c.Veredicto = "caida medible";
            else if (Math.Abs(c.DeltaPct) < 3 || Math.Abs(c.T) < 2) c.Veredicto = "sin caida medible";
            else c.Veredicto = "mejora (revisar el montaje)";
            return c;
        }

        public static string Markdown(string titulo, ResumenSesion r, EstadisticaFps fps, string evidenciaJson, MetricasReino metricas, IList<string> avisos)
        {
            var sb = new StringBuilder();
            sb.Append("# ").Append(titulo).Append("\n\n");
            sb.Append("Periodo: ").Append(r.Desde).Append(" -> ").Append(r.Hasta).Append("\n\n");
            sb.Append("## Eventos\n\n");
            sb.Append("- Cambios de opinion registrados: ").Append(r.Eventos).Append('\n');
            sb.Append("- Con frase: ").Append(r.ConVoz).Append(" | intento sin frase (LLM fallo): ").Append(r.SinFrase).Append('\n');
            sb.Append("- Empujones a la opinion: ").Append(r.Empujones).Append(" (maximo ").Append(Json.Num(r.MaxEmpujon)).Append(")\n");
            sb.Append("- |delta| medio: ").Append(Json.Num(r.Eventos > 0 ? r.SumaAbsDelta / r.Eventos : 0)).Append('\n');
            sb.Append("- Lineas ilegibles: ").Append(r.LineasInvalidas).Append(" de ").Append(r.Lineas).Append("\n\n");
            if (r.PorPawn.Count > 0)
            {
                sb.Append("## Quien mas cambia de opinion\n\n");
                foreach (var kv in Top(r.PorPawn, 5)) sb.Append("- ").Append(kv.Key).Append(": ").Append(kv.Value).Append('\n');
                sb.Append("\n## Parejas mas tensas\n\n");
                foreach (var kv in Top(r.PorPareja, 5)) sb.Append("- ").Append(kv.Key).Append(": ").Append(kv.Value).Append('\n');
                sb.Append('\n');
            }
            if (fps != null && fps.N > 0)
            {
                sb.Append("## FPS\n\n");
                sb.Append("- Muestras: ").Append(fps.N).Append(" | media ").Append(Json.Num(fps.Media)).Append(" | p5 ").Append(Json.Num(fps.P5))
                  .Append(" | p1 ").Append(Json.Num(fps.P1)).Append(" | min ").Append(Json.Num(fps.Min)).Append("\n\n");
            }
            if (metricas != null) sb.Append("## Metricas del reino\n\n```\n").Append(metricas.ToJson()).Append("\n```\n\n");
            if (avisos != null && avisos.Count > 0)
            {
                sb.Append("## Avisos\n\n");
                foreach (var a in avisos) sb.Append("- ").Append(a).Append('\n');
                sb.Append('\n');
            }
            if (!string.IsNullOrEmpty(evidenciaJson)) sb.Append("## Evidencia de verificacion\n\n```json\n").Append(evidenciaJson).Append("\n```\n");
            return sb.ToString();
        }

        // Grafo de relaciones: aristas dirigidas con |sentimiento| >= umbral.
        public static string GrafoDot(ModeloAfectivo m, IList<string> ids, Func<string, string> nombre, double umbral)
        {
            var sb = new StringBuilder("digraph pecera {\n  rankdir=LR; node [shape=ellipse];\n");
            foreach (var id in ids) sb.Append("  \"").Append(id).Append("\" [label=\"").Append(nombre(id).Replace("\"", "'")).Append("\"];\n");
            foreach (var a in ids) foreach (var b in ids)
            {
                if (a == b) continue;
                double s = m.Sentimiento(a, b);
                if (Math.Abs(s) < umbral) continue;
                sb.Append("  \"").Append(a).Append("\" -> \"").Append(b).Append("\" [color=").Append(s > 0 ? "darkgreen" : "red")
                  .Append(", penwidth=").Append(Json.Num(1 + 3 * Math.Abs(s))).Append(", label=\"").Append(Json.Num(s)).Append("\"];\n");
            }
            return sb.Append("}\n").ToString();
        }

        public static string GrafoJson(ModeloAfectivo m, IList<string> ids, Func<string, string> nombre, double umbral)
        {
            var sb = new StringBuilder("{\"nodos\":[");
            for (int i = 0; i < ids.Count; i++) { if (i > 0) sb.Append(','); sb.Append("{\"id\":\"").Append(Json.Escape(ids[i])).Append("\",\"nombre\":\"").Append(Json.Escape(nombre(ids[i]))).Append("\"}"); }
            sb.Append("],\"aristas\":[");
            bool primera = true;
            foreach (var a in ids) foreach (var b in ids)
            {
                if (a == b) continue;
                double s = m.Sentimiento(a, b);
                if (Math.Abs(s) < umbral) continue;
                if (!primera) sb.Append(','); primera = false;
                sb.Append("{\"de\":\"").Append(Json.Escape(a)).Append("\",\"a\":\"").Append(Json.Escape(b)).Append("\",\"peso\":").Append(Json.Num(s)).Append('}');
            }
            return sb.Append("]}").ToString();
        }
    }
}
