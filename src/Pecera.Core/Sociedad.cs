using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    public sealed class Faccion
    {
        public int Id;
        public List<string> Miembros = new List<string>();
        public string Lider = "";
        public double Cohesion;     // media de sentimiento interno
        public string Nombre = "";
    }

    // Sociedad emergente (F): facciones, liderazgo, favores, costumbres, parejas.
    // Todo se DERIVA del modelo afectivo: son lecturas, no escrituras en el juego.
    public static class Sociedad
    {
        static double Simetrico(ModeloAfectivo m, string a, string b)
        {
            return 0.5 * (m.Sentimiento(a, b) + m.Sentimiento(b, a));
        }

        // Propagacion de etiquetas determinista sobre el grafo de afinidades positivas.
        // Orden fijo de ids y desempate por etiqueta menor: mismo mundo -> mismas facciones.
        public static List<Faccion> Facciones(ModeloAfectivo m, IList<string> ids, double umbral, int minMiembros)
        {
            var orden = new List<string>(ids); orden.Sort(StringComparer.Ordinal);
            var etiqueta = new Dictionary<string, int>();
            for (int i = 0; i < orden.Count; i++) etiqueta[orden[i]] = i;
            var pesos = new Dictionary<string, Dictionary<string, double>>();
            foreach (var a in orden)
            {
                var d = new Dictionary<string, double>();
                foreach (var b in orden)
                {
                    if (a == b) continue;
                    double w = Simetrico(m, a, b);
                    if (w >= umbral) d[b] = w;
                }
                pesos[a] = d;
            }
            for (int it = 0; it < 30; it++)
            {
                bool cambio = false;
                foreach (var a in orden)
                {
                    var suma = new Dictionary<int, double>();
                    foreach (var kv in pesos[a])
                    {
                        double v; int e = etiqueta[kv.Key];
                        suma.TryGetValue(e, out v); suma[e] = v + kv.Value;
                    }
                    if (suma.Count == 0) continue;
                    int mejor = etiqueta[a]; double mv = -1;
                    foreach (var kv in suma)
                        if (kv.Value > mv + 1e-12 || (Math.Abs(kv.Value - mv) <= 1e-12 && kv.Key < mejor)) { mv = kv.Value; mejor = kv.Key; }
                    if (mejor != etiqueta[a]) { etiqueta[a] = mejor; cambio = true; }
                }
                if (!cambio) break;
            }
            var grupos = new Dictionary<int, List<string>>();
            foreach (var a in orden) { List<string> l; if (!grupos.TryGetValue(etiqueta[a], out l)) { l = new List<string>(); grupos[etiqueta[a]] = l; } l.Add(a); }
            var res = new List<Faccion>();
            var claves = new List<int>(grupos.Keys); claves.Sort();
            foreach (int k in claves)
            {
                if (grupos[k].Count < minMiembros) continue;
                var f = new Faccion { Id = res.Count + 1, Miembros = grupos[k] };
                double s = 0; int n = 0;
                foreach (var a in f.Miembros) foreach (var b in f.Miembros) if (a != b) { s += m.Sentimiento(a, b); n++; }
                f.Cohesion = n > 0 ? s / n : 0;
                res.Add(f);
            }
            return res;
        }

        // Liderazgo por carisma: mezcla del carisma de la ficha, la estima que le tienen los
        // demas de su faccion y la gratitud (deuda) que se le debe.
        public static string Lider(ModeloAfectivo m, IList<string> miembros, Func<string, Ficha> fichas)
        {
            string mejor = ""; double mp = double.NegativeInfinity;
            foreach (var a in miembros)
            {
                double estima = 0, deuda = 0; int n = 0;
                foreach (var b in miembros)
                {
                    if (a == b) continue;
                    estima += m.Sentimiento(b, a);
                    deuda += m.Get(b, a).Deuda;
                    n++;
                }
                if (n > 0) { estima /= n; deuda /= n; }
                Ficha f = fichas(a);
                double c = f != null ? f.Carisma : 0.5;
                double puntos = 0.4 * c + 0.4 * ((estima + 1) / 2) + 0.2 * ((deuda + 1) / 2);
                if (puntos > mp + 1e-12 || (Math.Abs(puntos - mp) <= 1e-12 && string.CompareOrdinal(a, mejor) < 0)) { mp = puntos; mejor = a; }
            }
            return mejor;
        }

        public static void AsignaLideres(ModeloAfectivo m, List<Faccion> fs, Func<string, Ficha> fichas, Func<string, string> nombre)
        {
            foreach (var f in fs)
            {
                f.Lider = Lider(m, f.Miembros, fichas);
                f.Nombre = "Casa de " + nombre(f.Lider);
            }
        }

        // Economia de favores: saldo neto de gratitud. >0: el reino le debe.
        public static List<KeyValuePair<string, double>> SaldoDeFavores(ModeloAfectivo m, IList<string> ids)
        {
            var saldo = new Dictionary<string, double>();
            foreach (var id in ids) saldo[id] = 0;
            foreach (var kv in m.Todos())
            {
                string a, b; ModeloAfectivo.Separa(kv.Key, out a, out b);
                if (!saldo.ContainsKey(a) || !saldo.ContainsKey(b)) continue;
                saldo[b] += kv.Value.Deuda;      // a debe a b
                saldo[a] -= kv.Value.Deuda;
            }
            var l = new List<KeyValuePair<string, double>>(saldo);
            l.Sort((x, y) => y.Value.CompareTo(x.Value));
            return l;
        }

        // Parejas candidatas (matrimonio): romance y afecto mutuos sostenidos.
        public static List<string[]> ParejasCandidatas(ModeloAfectivo m, double minRomance, double minAfecto)
        {
            var r = new List<string[]>();
            foreach (var kv in m.Todos())
            {
                string a, b; ModeloAfectivo.Separa(kv.Key, out a, out b);
                if (string.CompareOrdinal(a, b) >= 0) continue;
                Par ab = kv.Value, ba = m.Get(b, a);
                if (ab.Romance >= minRomance && ba.Romance >= minRomance && ab.Afecto >= minAfecto && ba.Afecto >= minAfecto)
                    r.Add(new[] { a, b });
            }
            return r;
        }

        // Indice de soledad: 1 = nadie le quiere ni quiere a nadie.
        public static double Soledad(ModeloAfectivo m, string id, IList<string> ids)
        {
            double suma = 0; int n = 0;
            foreach (var o in ids)
            {
                if (o == id) continue;
                suma += Math.Max(0, m.Sentimiento(id, o)) + Math.Max(0, m.Sentimiento(o, id));
                n += 2;
            }
            return n == 0 ? 0 : 1 - Math.Min(1, suma / n * 4);
        }
    }

    // Costumbres y tradiciones: algo que sucede en la misma temporada en >=3 anios
    // distintos se vuelve tradicion. Emerge de lo que pasa, nadie la define.
    public sealed class Costumbres
    {
        readonly Dictionary<string, HashSet<int>> vistas = new Dictionary<string, HashSet<int>>();
        readonly HashSet<string> tradiciones = new HashSet<string>();
        public int AniosParaTradicion = 3;

        // Devuelve true si ESTA observacion convierte el suceso en tradicion.
        public bool Observa(string suceso, int anio, int temporada)
        {
            string k = suceso + "@" + temporada;
            HashSet<int> a;
            if (!vistas.TryGetValue(k, out a)) { a = new HashSet<int>(); vistas[k] = a; }
            a.Add(anio);
            if (a.Count >= AniosParaTradicion && tradiciones.Add(k)) return true;
            return false;
        }

        public IEnumerable<string> Tradiciones { get { return tradiciones; } }
    }

    // Deriva de personalidad: las experiencias duras endurecen (rencor del temperamento
    // sube) y las buenas suavizan, con un tope total para que nadie se vuelva otro.
    public static class Deriva
    {
        public const double TopeTotal = 0.2;

        // base: temperamento de origen. Devuelve el nuevo valor, a lo sumo +-TopeTotal de base.
        public static double Aplica(double actual, double baseT, double trauma, double gratitud)
        {
            double paso = 0.01 * trauma - 0.005 * gratitud;
            double nuevo = actual + paso;
            nuevo = Math.Max(baseT - TopeTotal, Math.Min(baseT + TopeTotal, nuevo));
            return Math.Max(0, Math.Min(1, nuevo));
        }
    }
}
