using System;
using System.Collections.Generic;

namespace Pecera.Core
{
    // ======================================================================================================
    //  GANCHOS DE VIDA (ganchos.txt): que metodos del juego, al ejecutarse, son un suceso de vida (muerte, nacimiento, boda...).
    //  El mod NO adivina firmas: el jugador (o la sonda F11, que deja ganchos.sugeridos.txt) escribe lineas
    //      tipo = Clase.Metodo
    //  y el adaptador les pone un Postfix Harmony de SOLO LECTURA que publica el suceso en el bus. Sin lineas, no se parchea nada.
    //  Aqui solo esta el analisis del fichero y la heuristica de sugerencias, para poder probarlos sin el juego.
    // ======================================================================================================
    public sealed class GanchoVida
    {
        public string Tipo = "", Clase = "", Metodo = "";
        public override string ToString() { return Tipo + " = " + Clase + "." + Metodo; }
    }

    public static class Ganchos
    {
        // Los que entiende Vida.Conecta (mas «trabajo», que solo cuenta).
        public static readonly string[] Tipos = { "muerte", "nacimiento", "boda", "herida", "combate", "llegada", "partida", "trabajo" };

        public const int Max = 24;      // tope de parches: cada uno cuesta algo en un metodo caliente

        public static List<GanchoVida> Parse(string texto, List<string> avisos)
        {
            var r = new List<GanchoVida>();
            if (string.IsNullOrEmpty(texto)) return r;
            int n = 0;
            foreach (var bruta in texto.Split('\n'))
            {
                n++;
                string l = bruta.Trim();
                if (l.Length == 0 || l[0] == '#') continue;
                int eq = l.IndexOf('=');
                if (eq <= 0) { avisos.Add("linea " + n + ": falta 'tipo = Clase.Metodo'"); continue; }
                string tipo = l.Substring(0, eq).Trim().ToLowerInvariant(), dest = l.Substring(eq + 1).Trim();
                if (Array.IndexOf(Tipos, tipo) < 0) { avisos.Add("linea " + n + ": tipo desconocido '" + tipo + "' (validos: " + string.Join(", ", Tipos) + ")"); continue; }
                int punto = dest.LastIndexOf('.');
                if (punto <= 0 || punto == dest.Length - 1) { avisos.Add("linea " + n + ": '" + dest + "' no es Clase.Metodo"); continue; }
                string clase = dest.Substring(0, punto), metodo = dest.Substring(punto + 1);
                if (!Identificador(metodo) || !ClaseValida(clase)) { avisos.Add("linea " + n + ": nombre no valido '" + dest + "'"); continue; }
                if (r.Exists(g => g.Clase == clase && g.Metodo == metodo)) { avisos.Add("linea " + n + ": " + dest + " repetido"); continue; }
                if (r.Count >= Max) { avisos.Add("linea " + n + ": mas de " + Max + " ganchos, se ignora"); continue; }
                r.Add(new GanchoVida { Tipo = tipo, Clase = clase, Metodo = metodo });
            }
            return r;
        }

        static bool Identificador(string s)
        {
            if (s.Length == 0 || !(char.IsLetter(s[0]) || s[0] == '_')) return false;
            foreach (char c in s) if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            return true;
        }

        static bool ClaseValida(string s)
        {
            foreach (var parte in s.Split('.')) if (!Identificador(parte.Replace("+", "_").Replace("`", "_"))) return false;
            return true;
        }

        // Heuristica POR NOMBRE para la sonda: propone un tipo si el nombre del metodo lo sugiere. NO es autoridad: el jugador
        // revisa cada linea y la copia a ganchos.txt solo si la ha visto funcionar. Devuelve null si no sugiere nada.
        public static string Sugiere(string clase, string metodo)
        {
            string m = metodo.ToLowerInvariant();
            if (m.StartsWith("get", StringComparison.Ordinal) || m.StartsWith("can", StringComparison.Ordinal) || m.StartsWith("is", StringComparison.Ordinal)
                || m.StartsWith("has", StringComparison.Ordinal)) return null;
            string tipo = null;
            if (m == "die" || m == "kill" || m.Contains("ondeath") || m.Contains("ondie") || m == "dodeath") tipo = "muerte";
            else if (m.Contains("birth") || m.Contains("born")) tipo = "nacimiento";
            else if (m.Contains("marry") || m.Contains("wedding") || m.Contains("marriage")) tipo = "boda";
            else if (m.Contains("injur") || m.Contains("wound")) tipo = "herida";
            else if (m.Contains("arrive") || m.Contains("recruit") || m == "join" || m.Contains("immigra")) tipo = "llegada";
            else if (m.Contains("depart") || m.Contains("exile") || m.Contains("banish") || m.Contains("leavecolony")) tipo = "partida";
            if (tipo == null) return null;
            return "# " + tipo + " = " + clase + "." + metodo + "    <- SUGERENCIA por nombre, NO VERIFICADA: quita el # solo si lo confirmas";
        }
    }
}
