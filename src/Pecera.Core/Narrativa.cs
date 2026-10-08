using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    public sealed class Historia
    {
        public string Titulo = "", Texto = "";
        public int Dia;
        public double Peso;
        public string Origen = "plantilla";    // plantilla | llm
    }

    // Narrativa (J): de hitos sueltos a historias cortas. La plantilla funciona siempre; el LLM, con
    // presupuesto, la reescribe con mas gracia. El LLM NO inventa hechos: se le dan los hitos y se le pide
    // que no anada ninguno (y se descarta la salida si es larga, lista o markdown).
    public static class Narrador
    {
        static string Nombre(Cronica c, int dia) { return c.NombreTemporada(dia); }

        public static Historia Plantilla(Cronica c, Hito h)
        {
            string t = h.Texto.TrimEnd('.');
            string cuerpo;
            switch (h.Tipo)
            {
                case "sucesion": cuerpo = t + ". El reino contuvo el aliento y la corte tomo partido."; break;
                case "juicio": cuerpo = t + ". La sala quedo en silencio y nadie volvio a mirar igual al acusador."; break;
                case "rumor": cuerpo = t + ". Antes del anochecer lo sabia media plaza."; break;
                case "esquema": cuerpo = t + ". Lo que se trama en la sombra rara vez se queda en ella."; break;
                case "faccion": cuerpo = t + ". Desde entonces los bandos se miran de reojo."; break;
                case "saber": cuerpo = t + ". Los sabios lo anotaron para quien venga despues."; break;
                case "tradicion": cuerpo = t + ". Lo que empezo como un gesto se volvio costumbre."; break;
                case "sueno": cuerpo = t + ". No siempre se cumple lo que se anhela; esta vez si."; break;
                case "peticion": cuerpo = t + ". Los cortesanos tomaron nota de a quien favorece el trono."; break;
                default: cuerpo = t + "."; break;
            }
            return new Historia { Titulo = char.ToUpperInvariant(Nombre(c, h.Dia)[0]) + Nombre(c, h.Dia).Substring(1), Texto = cuerpo, Dia = h.Dia, Peso = h.Peso };
        }

        // Las n historias mas pesadas de una temporada, en orden cronologico.
        public static List<Historia> DeTemporada(Cronica c, int temporada, int n)
        {
            var sel = c.Hitos.Where2(h => c.Temporada(h.Dia) == temporada);
            sel.Sort((a, b) => { int k = b.Peso.CompareTo(a.Peso); return k != 0 ? k : a.Dia.CompareTo(b.Dia); });
            if (sel.Count > n) sel.RemoveRange(n, sel.Count - n);
            sel.Sort((a, b) => a.Dia.CompareTo(b.Dia));
            var r = new List<Historia>();
            foreach (var h in sel) r.Add(Plantilla(c, h));
            return r;
        }

        public const string Sistema =
            "Eres el cronista de un reino medieval. Reescribes UNA historia de 2 a 3 frases, en espanol y con tono de cronica, " +
            "usando SOLO los hechos dados: no inventes personajes, cifras ni sucesos. Sin markdown ni listas. Responde SOLO el texto.";

        public static string Usuario(Historia h, Hito origen) { return "Hecho: " + origen.Texto + " (" + origen.Tipo + ", " + h.Titulo + ")."; }

        // Acepta la reescritura del LLM solo si es una prosa razonable; si no, null (se queda la plantilla).
        public static string Valida(string raw, Hito origen)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string t = Json.UnaLinea(raw);
            if (t.Length < 20 || t.Length > 420) return null;
            if (t.IndexOf('#') >= 0 || t.IndexOf("**", StringComparison.Ordinal) >= 0 || t.StartsWith("- ", StringComparison.Ordinal) || t.IndexOf('{') >= 0) return null;
            // debe conservar al menos un nombre/palabra larga del hecho original
            bool ancla = false;
            foreach (var w in origen.Texto.Split(' ')) if (w.Length >= 5 && t.IndexOf(w.Trim('.', ',', '«', '»'), StringComparison.OrdinalIgnoreCase) >= 0) { ancla = true; break; }
            return ancla ? t : null;
        }

        public static string Markdown(IList<Historia> hs, string reino)
        {
            var sb = new StringBuilder("# Historias de ").Append(reino).Append("\n\n");
            if (hs.Count == 0) return sb.Append("Aun no hay historias que contar.\n").ToString();
            foreach (var h in hs) sb.Append("## ").Append(h.Titulo).Append(" (dia ").Append(h.Dia).Append(")\n\n").Append(h.Texto).Append("\n\n");
            return sb.ToString();
        }
    }

    static class ListaExt
    {
        // C# 5 sin LINQ: filtro simple sobre IList.
        public static List<Hito> Where2(this IList<Hito> l, Func<Hito, bool> f)
        {
            var r = new List<Hito>();
            foreach (var x in l) if (f(x)) r.Add(x);
            return r;
        }
    }
}
