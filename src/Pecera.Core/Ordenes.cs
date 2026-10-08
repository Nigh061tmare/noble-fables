using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    // Ordenes del jugador: frases de directriz.txt que el mod entiende como MANDATOS, no solo como texto para el LLM.
    // Reconoce (sin distinguir mayusculas ni tildes):
    //   «paz» / «nadie intrigue» / «prohibido intrigar» / «prohibo las intrigas»   -> fuerza la norma paz_publica
    //   «ojo por ojo» / «venganza permitida»                                    -> fuerza ojo_por_ojo
    //   «favorece a X» / «favorecer a X» / «apoya a X»                           -> el consejo y el director favorecen a X (pawn o faccion)
    //   «fiesta»                                                               -> el director propone una fiesta en cuanto pueda (una vez)
    //   «mas drama» / «quiero drama» -> director caotico ; «calma» / «tranquilidad» -> director calmo ; «drama normal» -> clasico
    // Lo demas sigue siendo texto libre para la voz. Siempre reversible: borrar la linea anula la orden.
    public sealed class Ordenes
    {
        public string NormaForzada;                     // paz_publica | ojo_por_ojo | null
        public readonly List<string> Favorecidos = new List<string>();
        public bool PideFiesta;
        public EstiloDirector? Estilo;
        public readonly List<string> Entendidas = new List<string>();

        public static string SinTildes(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s.ToLowerInvariant())
            {
                switch (c)
                {
                    case 'á': sb.Append('a'); break; case 'é': sb.Append('e'); break; case 'í': sb.Append('i'); break;
                    case 'ó': sb.Append('o'); break; case 'ú': case 'ü': sb.Append('u'); break; case 'ñ': sb.Append('n'); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        public static Ordenes Parse(string texto)
        {
            var o = new Ordenes();
            string t = " " + SinTildes(Json.UnaLinea(texto)) + " ";
            foreach (char sep in new[] { '.', ',', ';', '!', '?', ':' }) t = t.Replace(sep, ' ');
            t = " " + Json.UnaLinea(t) + " ";
            if (t.Contains(" ojo por ojo ") || t.Contains(" venganza permitida ")) { o.NormaForzada = "ojo_por_ojo"; o.Entendidas.Add("norma: ojo por ojo"); }
            else if (t.Contains(" paz ") || t.Contains(" nadie intrigue ") || t.Contains(" prohibido intrigar ") || t.Contains(" prohibo las intrigas "))
            { o.NormaForzada = "paz_publica"; o.Entendidas.Add("norma: paz publica"); }
            if (t.Contains(" fiesta ")) { o.PideFiesta = true; o.Entendidas.Add("pide una fiesta"); }
            if (t.Contains(" mas drama ") || t.Contains(" quiero drama ")) { o.Estilo = EstiloDirector.Caotico; o.Entendidas.Add("director caotico"); }
            else if (t.Contains(" calma ") || t.Contains(" tranquilidad ")) { o.Estilo = EstiloDirector.Calmo; o.Entendidas.Add("director calmo"); }
            else if (t.Contains(" drama normal ")) { o.Estilo = EstiloDirector.Clasico; o.Entendidas.Add("director clasico"); }
            foreach (var verbo in new[] { " favorece a ", " favorecer a ", " apoya a ", " apoyad a " })
            {
                int i = 0;
                while ((i = t.IndexOf(verbo, i, StringComparison.Ordinal)) >= 0)
                {
                    i += verbo.Length;
                    // el nombre llega hasta la siguiente palabra de corte (y, que, pero) o 4 palabras como mucho
                    var palabras = t.Substring(i).Trim().Split(' ');
                    var nombre = new List<string>();
                    foreach (var w in palabras) { if (w == "y" || w == "que" || w == "pero" || w == "porque" || w.Length == 0 || nombre.Count >= 4) break; nombre.Add(w); }
                    if (nombre.Count > 0 && nombre[0] == "la" && nombre.Count > 1 && nombre[1] == "casa") nombre.RemoveRange(0, 2);
                    if (nombre.Count > 0 && nombre[0] == "de") nombre.RemoveAt(0);
                    if (nombre.Count > 0) { string n = string.Join(" ", nombre.ToArray()); if (!o.Favorecidos.Contains(n)) { o.Favorecidos.Add(n); o.Entendidas.Add("favorece a " + n); } }
                }
            }
            return o;
        }

        // ¿El nombre de este pawn (o de su faccion) esta favorecido? Compara sin tildes y por contencion.
        public bool Favorece(string nombre, string faccion)
        {
            string n = SinTildes(nombre ?? ""), f = SinTildes(faccion ?? "");
            foreach (var x in Favorecidos)
                if ((n.Length > 0 && (n == x || n.Contains(x))) || (f.Length > 0 && (f.Contains(x)))) return true;
            return false;
        }
    }
}
