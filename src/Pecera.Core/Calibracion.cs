using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    // Calibracion con DATOS REALES de la partida del usuario (legacy/datos-muestra/opiniones_*.jsonl, Noble Fates 0.31.5,
    // 300 cambios de opinion, 292 pawns distintos). Medido en esta sesion:
    //   - 93 % de los cambios son por RASGO («X es Orco», «X esta Malo»), solo 7 % por hechos.
    //   - 82 % son negativos.
    //   - |delta| por percentiles: p10 0.51, p25 0.56, p50 0.72, p75 0.98, p90 1.96, p99 2.36 (umbral de registro: 0.5).
    // Antes la magnitud del evento afectivo era tanh(|delta|/2): un cambio mediano daba 0.35 y uno del percentil 99 apenas 0.83.
    // Ahora la magnitud ES el percentil del cambio en la partida real: lo tipico pesa lo tipico y lo excepcional, lo excepcional.
    public static class Calibracion
    {
        public static readonly double[] Nodos = { 0.50, 0.51, 0.56, 0.72, 0.98, 1.96, 2.36, 4.0 };
        public static readonly double[] Percentil = { 0.0, 0.10, 0.25, 0.50, 0.75, 0.90, 0.99, 1.0 };
        public const double FraccionRasgo = 0.93, FraccionNegativa = 0.82;

        public static double Magnitud(double absDelta)
        {
            if (absDelta <= Nodos[0]) return 0.03;
            for (int i = 1; i < Nodos.Length; i++)
                if (absDelta <= Nodos[i])
                    return Math.Max(0.03, Percentil[i - 1] + (Percentil[i] - Percentil[i - 1]) * (absDelta - Nodos[i - 1]) / (Nodos[i] - Nodos[i - 1]));
            return 1.0;
        }

        // Muestra un cambio de opinion con la distribucion real (para el simulador): |delta|, si es por rasgo y si es negativo.
        public static double Muestra(Rng rng, out bool rasgo, out bool negativo)
        {
            rasgo = rng.Chance(FraccionRasgo); negativo = rng.Chance(FraccionNegativa);
            double u = rng.Next();
            for (int i = 1; i < Percentil.Length; i++)
                if (u <= Percentil[i]) return Nodos[i - 1] + (Nodos[i] - Nodos[i - 1]) * (u - Percentil[i - 1]) / Math.Max(1e-9, Percentil[i] - Percentil[i - 1]);
            return Nodos[Nodos.Length - 1];
        }
    }

    // PREJUICIO DE GRUPO. Los datos reales dicen que la mayor parte de la vida social de Noble Fates son reacciones a la raza
    // («es Orco», «es Enano», «es Elfo oscuro») y al alineamiento («esta Bueno/Malo»). Eso no es una relacion entre DOS personas:
    // es una actitud de una persona hacia un GRUPO. Aqui se modela asi:
    //   - actitud[pawn][grupo] en [-1,1], que se mueve con cada evento de rasgo y decae despacio hacia 0 (tau 120 dias);
    //   - el grupo de cada pawn se aprende de los propios motivos del juego («Beto es Orco» => Beto pertenece a «Orco»);
    //   - Sesgo(a, b) = actitud de a hacia el grupo de b: el planificador lo usa para elegir con quien charlar o a quien cortejar,
    //     y la norma «tolerancia» (si el reino la aprueba) lo reduce a la mitad.
    public sealed class Prejuicios
    {
        readonly Dictionary<string, Dictionary<string, double>> actitud = new Dictionary<string, Dictionary<string, double>>();
        readonly Dictionary<string, string> grupoDe = new Dictionary<string, string>();
        public double Tau = 120;
        public double Atenuacion = 1.0;      // 0.5 con la norma de tolerancia

        static readonly string[] Alineamientos = { "bueno", "malo", "neutral" };

        // «Chiyo esta Bueno», «Brigitte es Enano», «Shah Bir es Elfo oscuro | PersistentLocalizedStringContext» -> grupo.
        public static string GrupoDeMotivo(string motivo, out bool alineamiento)
        {
            alineamiento = false;
            if (string.IsNullOrEmpty(motivo)) return null;
            string m = motivo;
            int bar = m.IndexOf(" | ", StringComparison.Ordinal); if (bar >= 0) m = m.Substring(0, bar);
            string mm = " " + Ordenes.SinTildes(m) + " ";
            if (mm.Contains(" ha ") || mm.Contains(" han ")) return null;
            int i = mm.LastIndexOf(" es ", StringComparison.Ordinal), j = mm.LastIndexOf(" esta ", StringComparison.Ordinal);
            string resto;
            if (j > i) resto = mm.Substring(j + 6).Trim(); else if (i >= 0) resto = mm.Substring(i + 4).Trim(); else return null;
            if (resto.Length == 0 || resto.Length > 30) return null;
            foreach (var a in Alineamientos) if (resto == a) { alineamiento = true; return char.ToUpperInvariant(resto[0]) + resto.Substring(1); }
            // conserva la forma original (con tildes y mayusculas) del final del motivo
            string orig = m.Trim();
            string ultimo = orig.Length >= resto.Length ? orig.Substring(orig.Length - resto.Length) : resto;
            return ultimo.Trim();
        }

        // Anota un evento de rasgo: a reacciona (delta) a que b «es <grupo>».
        public void Anota(string a, string b, string grupo, double delta, bool alineamiento)
        {
            if (string.IsNullOrEmpty(grupo)) return;
            if (!alineamiento) grupoDe[b] = grupo;                 // la raza es estable; el alineamiento no define el grupo
            Dictionary<string, double> d;
            if (!actitud.TryGetValue(a, out d)) { d = new Dictionary<string, double>(); actitud[a] = d; }
            double v; d.TryGetValue(grupo, out v);
            double m = Calibracion.Magnitud(Math.Abs(delta)) * 0.3;
            double objetivo = delta < 0 ? -1 : 1;
            d[grupo] = v + m * (objetivo - v);
        }

        public double Actitud(string a, string grupo)
        {
            Dictionary<string, double> d; double v;
            return actitud.TryGetValue(a, out d) && d.TryGetValue(grupo, out v) ? v : 0;
        }

        public string Grupo(string id) { string g; return grupoDe.TryGetValue(id, out g) ? g : null; }

        public double Sesgo(string a, string b)
        {
            string g = Grupo(b);
            if (g == null || (Grupo(a) == g)) return 0;            // dentro del propio grupo no hay prejuicio de grupo
            return Atenuacion * Actitud(a, g);
        }

        public void Avanza(double dias)
        {
            double f = Math.Exp(-dias / Tau);
            foreach (var d in actitud.Values) { var ks = new List<string>(d.Keys); foreach (var k in ks) d[k] *= f; }
        }

        // Los prejuicios mas fuertes del reino (para el informe y la norma de tolerancia): media por grupo de destino.
        public List<KeyValuePair<string, double>> MediaPorGrupo()
        {
            var suma = new Dictionary<string, double>(); var n = new Dictionary<string, int>();
            foreach (var d in actitud.Values) foreach (var kv in d) { double s; suma.TryGetValue(kv.Key, out s); suma[kv.Key] = s + kv.Value; int c; n.TryGetValue(kv.Key, out c); n[kv.Key] = c + 1; }
            var l = new List<KeyValuePair<string, double>>();
            foreach (var kv in suma) l.Add(new KeyValuePair<string, double>(kv.Key, kv.Value / n[kv.Key]));
            l.Sort((x, y) => { int c = x.Value.CompareTo(y.Value); return c != 0 ? c : string.CompareOrdinal(x.Key, y.Key); });
            return l;
        }

        public double Hostilidad()
        {
            double s = 0; int n = 0;
            foreach (var d in actitud.Values) foreach (var v in d.Values) { if (v < 0) s += -v; n++; }
            return n == 0 ? 0 : s / n;
        }

        public IEnumerable<string> Serializa()
        {
            var ids = new List<string>(grupoDe.Keys); ids.Sort(StringComparer.Ordinal);
            foreach (var id in ids) yield return "{\"k\":\"pg\",\"id\":\"" + Json.Escape(id) + "\",\"g\":\"" + Json.Escape(grupoDe[id]) + "\"}";
            var as2 = new List<string>(actitud.Keys); as2.Sort(StringComparer.Ordinal);
            foreach (var a in as2)
                foreach (var kv in actitud[a])
                    if (Math.Abs(kv.Value) > 1e-3) yield return "{\"k\":\"pa\",\"a\":\"" + Json.Escape(a) + "\",\"g\":\"" + Json.Escape(kv.Key) + "\",\"v\":" + Json.Num(kv.Value) + "}";
        }

        public void Carga(object d)
        {
            string k = Json.Str(d, "k");
            if (k == "pg") { string id = Json.Str(d, "id"), g = Json.Str(d, "g"); if (id.Length > 0 && g.Length > 0) grupoDe[id] = g; }
            else if (k == "pa")
            {
                string a = Json.Str(d, "a"), g = Json.Str(d, "g"); if (a.Length == 0 || g.Length == 0) return;
                Dictionary<string, double> m; if (!actitud.TryGetValue(a, out m)) { m = new Dictionary<string, double>(); actitud[a] = m; }
                m[g] = Math.Max(-1, Math.Min(1, Json.Num(d, "v", 0)));
            }
        }
    }
}
