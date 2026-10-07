using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    public static class Valores
    {
        public static readonly string[] Nombres = { "honor", "clemencia", "comunidad", "saber", "venganza" };
    }

    // Cultura y religion emergentes (F). Nadie define el credo: sale de lo que PASA en el reino.
    //  - Los valores culturales se calculan de contadores de sucesos (perdones, traiciones
    //    castigadas, fiestas, descubrimientos, venganzas).
    //  - Los mitos son hitos de mucho peso que se cuentan una y otra vez; el credo toma nombre
    //    del valor dominante y de su mito mas pesado.
    //  - La fe de cada pawn sube con fiestas compartidas y con el trauma superado y baja con
    //    la soledad; esta acotada en [0,1].
    public sealed class Cultura
    {
        readonly Dictionary<string, double> contadores = new Dictionary<string, double>();
        readonly Dictionary<string, double> fe = new Dictionary<string, double>();
        readonly List<Hito> mitos = new List<Hito>();
        public double PesoMito = 8;

        public void Suceso(string valor, double cantidad)
        {
            if (Array.IndexOf(Valores.Nombres, valor) < 0) throw new ArgumentException("valor cultural desconocido: " + valor);
            double v; contadores.TryGetValue(valor, out v); contadores[valor] = v + Math.Max(0, cantidad);
        }

        public bool Observa(Hito h)
        {
            if (h.Peso < PesoMito) return false;
            foreach (var m in mitos) if (m.Texto == h.Texto) return false;
            mitos.Add(h);
            if (mitos.Count > 20) mitos.RemoveAt(0);
            return true;
        }

        public IList<Hito> Mitos { get { return mitos; } }

        // Reparto normalizado (suma 1). Sin sucesos: uniforme.
        public Dictionary<string, double> Reparto()
        {
            double tot = 0; foreach (var n in Valores.Nombres) { double v; contadores.TryGetValue(n, out v); tot += v; }
            var r = new Dictionary<string, double>();
            foreach (var n in Valores.Nombres) { double v; contadores.TryGetValue(n, out v); r[n] = tot > 0 ? v / tot : 1.0 / Valores.Nombres.Length; }
            return r;
        }

        public string Dominante()
        {
            var r = Reparto(); string mejor = Valores.Nombres[0];
            foreach (var n in Valores.Nombres) if (r[n] > r[mejor] + 1e-12) mejor = n;
            return mejor;
        }

        public string Credo()
        {
            string dom = Dominante();
            var map = new Dictionary<string, string> {
                { "honor", "Camino del Juramento" }, { "clemencia", "Fe del Perdon" }, { "comunidad", "Culto del Hogar" },
                { "saber", "Orden de la Lampara" }, { "venganza", "Rito de la Deuda" } };
            string mito = "";
            if (mitos.Count > 0) { var top = mitos[0]; foreach (var m in mitos) if (m.Peso > top.Peso) top = m; mito = ", nacido de «" + top.Texto + "»"; }
            return map[dom] + mito;
        }

        public double Fe(string id) { double v; return fe.TryGetValue(id, out v) ? v : 0.3; }

        public void ActualizaFe(string id, double fiestas, double traumaSuperado, double soledad)
        {
            double f = Fe(id);
            double objetivo = Math.Max(0, Math.Min(1, 0.3 + 0.4 * Math.Min(1, fiestas) + 0.3 * Math.Min(1, traumaSuperado) - 0.3 * soledad));
            fe[id] = f + 0.2 * (objetivo - f);        // media ponderada: sin saltos ni salida de rango
        }
    }

    // Dialectos (F): cada faccion desarrolla giros propios que se pegan con el tiempo. Solo
    // cambia el TEXTO que se muestra o se pide al LLM; nunca la logica.
    public sealed class Dialecto
    {
        static readonly string[,] Pool = {
            { "amigo", "hermano de fuego" }, { "pan", "migaja" }, { "rey", "el de la corona" }, { "gracias", "que el hogar os guarde" },
            { "hola", "buen fuego" }, { "adios", "hasta la ceniza" }, { "casa", "cobijo" }, { "mal", "ceniza" }, { "bien", "brasa" } };
        readonly List<string[]> giros = new List<string[]>();
        public int Max = 5;

        public int Count { get { return giros.Count; } }

        // Cada llamada puede añadir UN giro nuevo (nunca duplicado) hasta Max.
        public bool Deriva(Rng rng)
        {
            if (giros.Count >= Max) return false;
            int n = Pool.GetLength(0);
            for (int i = 0; i < n; i++)
            {
                int k = (rng.Next(n) + i) % n;
                bool ya = false; foreach (var g in giros) if (g[0] == Pool[k, 0]) ya = true;
                if (!ya) { giros.Add(new[] { Pool[k, 0], Pool[k, 1] }); return true; }
            }
            return false;
        }

        public string Aplica(string texto)
        {
            if (string.IsNullOrEmpty(texto)) return texto ?? "";
            string t = texto;
            foreach (var g in giros) t = ReemplazaPalabra(t, g[0], g[1]);
            return t;
        }

        static string ReemplazaPalabra(string t, string de, string a)
        {
            var sb = new StringBuilder(); int i = 0;
            while (i < t.Length)
            {
                int j = t.IndexOf(de, i, StringComparison.OrdinalIgnoreCase);
                if (j < 0) { sb.Append(t, i, t.Length - i); break; }
                bool izq = j == 0 || !char.IsLetter(t[j - 1]), der = j + de.Length >= t.Length || !char.IsLetter(t[j + de.Length]);
                sb.Append(t, i, j - i);
                sb.Append(izq && der ? a : t.Substring(j, de.Length));
                i = j + de.Length;
            }
            return sb.ToString();
        }

        // Frase para el prompt de la voz: «en su faccion dicen X por Y».
        public string Pista()
        {
            if (giros.Count == 0) return "";
            var p = new List<string>(); foreach (var g in giros) p.Add("'" + g[1] + "' en vez de '" + g[0] + "'");
            return "su gente dice " + string.Join(", ", p.ToArray());
        }
    }

    public sealed class ModEstacion
    {
        public string Nombre = "";
        public double Sociabilidad = 1;     // multiplica las interacciones positivas
        public double Irritabilidad = 1;    // multiplica los agravios
        public double Fiesta = 1;           // multiplica la probabilidad de fiesta
    }

    // Estaciones (F): un ciclo fijo de dias; el invierno encierra e irrita, la primavera y el
    // otoño (cosecha) invitan a las fiestas. Son MODIFICADORES suaves (0.7–1.3), nunca interruptores.
    public static class Estaciones
    {
        public static ModEstacion De(int dia, int diasPorTemporada)
        {
            int t = ((dia / Math.Max(1, diasPorTemporada)) % 4 + 4) % 4;
            switch (t)
            {
                case 0: return new ModEstacion { Nombre = "primavera", Sociabilidad = 1.15, Irritabilidad = 0.9, Fiesta = 1.2 };
                case 1: return new ModEstacion { Nombre = "verano", Sociabilidad = 1.1, Irritabilidad = 1.1, Fiesta = 1.0 };
                case 2: return new ModEstacion { Nombre = "otono", Sociabilidad = 1.0, Irritabilidad = 0.95, Fiesta = 1.3 };
                default: return new ModEstacion { Nombre = "invierno", Sociabilidad = 0.8, Irritabilidad = 1.25, Fiesta = 0.7 };
            }
        }
    }
}
