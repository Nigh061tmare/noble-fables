using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    public sealed class Sueno
    {
        public string Id = "", Texto = "", Categoria = "";
        public double Progreso;          // 0..1
        public bool Cumplido;
    }

    // Suenos e inspiraciones (I). Cada pawn tiene UN sueño derivado de su ficha (su meta). El
    // progreso lo mueven sucesos reales del reino que casan con la categoria del sueño; al
    // cruzar un hito (25/50/75/100 %) llega una inspiracion y, al cumplirse, un hito de cronica.
    // Nada de esto escribe en el juego: es relato y un empujon interno al afecto.
    public sealed class Suenos
    {
        readonly Dictionary<string, Sueno> por = new Dictionary<string, Sueno>();
        readonly Dictionary<string, int> hitos = new Dictionary<string, int>();

        public static string CategoriaDeMeta(string meta)
        {
            string m = (meta ?? "").ToLowerInvariant();
            if (m.Contains("saber") || m.Contains("descubrir")) return "cultura";
            if (m.Contains("enriquec")) return "economia";
            if (m.Contains("proteger") || m.Contains("vengar")) return "defensa";
            if (m.Contains("amor")) return "cohesion";
            return "crecimiento";
        }

        public Sueno De(Ficha f)
        {
            Sueno s;
            if (por.TryGetValue(f.Id, out s)) return s;
            string meta = f.Metas.Count > 0 ? f.Metas[0] : "vivir en paz";
            s = new Sueno { Id = f.Id, Texto = meta, Categoria = CategoriaDeMeta(meta) };
            por[f.Id] = s;
            return s;
        }

        // Devuelve las inspiraciones nuevas (texto) que produjo este avance.
        public List<string> Avanza(Ficha f, string categoriaSuceso, double cantidad, ModeloAfectivo m)
        {
            var r = new List<string>();
            Sueno s = De(f);
            if (s.Cumplido || categoriaSuceso != s.Categoria || cantidad <= 0) return r;
            double antes = s.Progreso;
            s.Progreso = Math.Min(1, s.Progreso + cantidad * (0.5 + f.Ambicion));      // el ambicioso avanza mas deprisa
            int h; hitos.TryGetValue(f.Id, out h);
            int nuevoH = (int)Math.Floor(s.Progreso * 4 + 1e-9);
            while (h < nuevoH)
            {
                h++;
                r.Add(h >= 4 ? f.Nombre + " cumple su sueno: " + s.Texto : f.Nombre + " siente que se acerca a su sueno (" + s.Texto + "): " + (h * 25) + " %");
            }
            hitos[f.Id] = h;
            if (s.Progreso >= 1) s.Cumplido = true;
            return r;
        }

        public int Cumplidos { get { int n = 0; foreach (var s in por.Values) if (s.Cumplido) n++; return n; } }
        public int Total { get { return por.Count; } }
    }
}
