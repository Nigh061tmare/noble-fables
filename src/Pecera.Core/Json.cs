using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Pecera.Core
{
    public sealed class JsonException : Exception
    {
        public JsonException(string msg) : base(msg) { }
    }

    // JSON propio, sin dependencias: dentro de un juego instalado es mas robusto que
    // referenciar otra DLL. Valores: Dictionary<string,object>, List<object>, string,
    // double, bool, null.
    public static class Json
    {
        // ---------- parseo estricto ----------
        public static object Parse(string s)
        {
            if (s == null) throw new JsonException("nulo");
            var p = new Parser(s);
            p.Ws();
            object v = p.Valor();
            p.Ws();
            if (!p.Fin) throw new JsonException("texto sobrante en " + p.Pos);
            return v;
        }

        public static bool TryParse(string s, out object v)
        {
            try { v = Parse(s); return true; }
            catch (JsonException) { v = null; return false; }
        }

        // ---------- parseo tolerante para respuestas de LLM ----------
        // Quita vallas de markdown y prosa, recorta el primer objeto balanceado y, si
        // el JSON viene cortado (num_predict agotado), lo repara cerrando lo abierto.
        // Devuelve null si no hay nada recuperable.
        public static Dictionary<string, object> ParseObjeto(string texto)
        {
            string obj = Aislar(texto);
            if (obj.Length == 0) return null;
            object v;
            if (TryParse(obj, out v)) return v as Dictionary<string, object>;
            string rep = Reparar(obj);
            if (rep != null && TryParse(rep, out v)) return v as Dictionary<string, object>;
            return null;
        }

        // Primer objeto { ... } balanceado. Si esta cortado devuelve desde la llave.
        public static string Aislar(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int ini = s.IndexOf('{');
            if (ini < 0) return "";
            int nivel = 0;
            bool enStr = false, esc = false;
            for (int i = ini; i < s.Length; i++)
            {
                char c = s[i];
                if (enStr)
                {
                    if (esc) { esc = false; continue; }
                    if (c == '\\') { esc = true; continue; }
                    if (c == '"') enStr = false;
                    continue;
                }
                if (c == '"') { enStr = true; continue; }
                if (c == '{') nivel++;
                else if (c == '}')
                {
                    nivel--;
                    if (nivel == 0) return s.Substring(ini, i - ini + 1);
                }
            }
            return s.Substring(ini);
        }

        // Cierra comillas y llaves abiertas. Si aun no parsea (clave o coma colgando)
        // retrocede hasta la ultima coma fuera de cadena y reintenta.
        public static string Reparar(string cortado)
        {
            string t = cortado;
            for (int intento = 0; intento < 40; intento++)
            {
                string cand = Cerrar(t);
                object v;
                if (TryParse(cand, out v)) return cand;
                int coma = UltimaComaFueraDeCadena(t);
                if (coma < 0) return null;
                t = t.Substring(0, coma);
            }
            return null;
        }

        static string Cerrar(string t)
        {
            var pila = new List<char>();
            bool enStr = false, esc = false;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (enStr)
                {
                    if (esc) esc = false;
                    else if (c == '\\') esc = true;
                    else if (c == '"') enStr = false;
                    continue;
                }
                if (c == '"') enStr = true;
                else if (c == '{') pila.Add('}');
                else if (c == '[') pila.Add(']');
                else if ((c == '}' || c == ']') && pila.Count > 0) pila.RemoveAt(pila.Count - 1);
            }
            var sb = new StringBuilder(t);
            if (enStr)
            {
                // Un escape a medias (\ o \u12) se quita para no romper la cadena.
                int b = 0;
                while (sb.Length > 0 && sb[sb.Length - 1] == '\\') { sb.Length--; b++; }
                if (b % 2 == 1) { /* la barra impar se descarta */ } else sb.Append('\\', b);
                int u = sb.ToString().LastIndexOf("\\u", StringComparison.Ordinal);
                if (u >= 0 && sb.Length - u < 6) sb.Length = u;
                sb.Append('"');
            }
            string cur = sb.ToString().TrimEnd();
            if (cur.EndsWith(":", StringComparison.Ordinal)) cur += "null";
            sb = new StringBuilder(cur);
            for (int i = pila.Count - 1; i >= 0; i--) sb.Append(pila[i]);
            return sb.ToString();
        }

        static int UltimaComaFueraDeCadena(string t)
        {
            bool enStr = false, esc = false;
            int ultima = -1;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (enStr)
                {
                    if (esc) esc = false;
                    else if (c == '\\') esc = true;
                    else if (c == '"') enStr = false;
                    continue;
                }
                if (c == '"') enStr = true;
                else if (c == ',') ultima = i;
            }
            return ultima;
        }

        // ---------- acceso comodo ----------
        public static string Str(object o, string clave)
        {
            var d = o as Dictionary<string, object>;
            object v;
            if (d == null || !d.TryGetValue(clave, out v) || v == null) return "";
            var s = v as string;
            if (s != null) return s;
            if (v is double) return ((double)v).ToString("R", CultureInfo.InvariantCulture);
            if (v is bool) return (bool)v ? "true" : "false";
            return "";
        }

        public static double Num(object o, string clave, double defecto)
        {
            var d = o as Dictionary<string, object>;
            object v;
            if (d == null || !d.TryGetValue(clave, out v)) return defecto;
            if (v is double) return (double)v;
            double r;
            var s = v as string;
            if (s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out r)) return r;
            return defecto;
        }

        public static List<object> Lista(object o, string clave)
        {
            var d = o as Dictionary<string, object>;
            object v;
            if (d != null && d.TryGetValue(clave, out v)) return v as List<object>;
            return null;
        }

        // Texto de una sola linea para mostrar: \n, \t, \r pasan a espacio.
        public static string UnaLinea(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            bool esp = false;
            foreach (char c in s)
            {
                bool blanco = c == '\n' || c == '\r' || c == '\t' || c == ' ';
                if (blanco) { if (!esp) sb.Append(' '); esp = true; }
                else { sb.Append(c); esp = false; }
            }
            return sb.ToString().Trim();
        }

        // ---------- escritura ----------
        public static string Escape(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static string Num(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "null";
            double r = Math.Round(v, 3, MidpointRounding.AwayFromZero);
            return r.ToString("0.###", CultureInfo.InvariantCulture);
        }

        sealed class Parser
        {
            readonly string s;
            int i;
            public Parser(string s) { this.s = s; }
            public bool Fin { get { return i >= s.Length; } }
            public int Pos { get { return i; } }

            public void Ws()
            {
                while (i < s.Length && (s[i] == ' ' || s[i] == '\n' || s[i] == '\r' || s[i] == '\t')) i++;
            }

            public object Valor()
            {
                if (i >= s.Length) throw new JsonException("fin inesperado");
                char c = s[i];
                if (c == '{') return Objeto();
                if (c == '[') return Lista();
                if (c == '"') return Cadena();
                if (c == 't') { Lit("true"); return true; }
                if (c == 'f') { Lit("false"); return false; }
                if (c == 'n') { Lit("null"); return null; }
                return Numero();
            }

            void Lit(string l)
            {
                if (string.CompareOrdinal(s, i, l, 0, l.Length) != 0) throw new JsonException("literal invalido en " + i);
                i += l.Length;
            }

            object Numero()
            {
                int ini = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                double d;
                if (i == ini || !double.TryParse(s.Substring(ini, i - ini), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out d))
                    throw new JsonException("numero invalido en " + ini);
                return d;
            }

            Dictionary<string, object> Objeto()
            {
                var d = new Dictionary<string, object>();
                i++;
                Ws();
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws();
                    if (i >= s.Length || s[i] != '"') throw new JsonException("clave esperada en " + i);
                    string k = Cadena();
                    Ws();
                    if (i >= s.Length || s[i] != ':') throw new JsonException("':' esperado en " + i);
                    i++;
                    Ws();
                    d[k] = Valor();
                    Ws();
                    if (i >= s.Length) throw new JsonException("objeto sin cerrar");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new JsonException("',' o '}' esperado en " + i);
                }
            }

            List<object> Lista()
            {
                var l = new List<object>();
                i++;
                Ws();
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (true)
                {
                    Ws();
                    l.Add(Valor());
                    Ws();
                    if (i >= s.Length) throw new JsonException("lista sin cerrar");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new JsonException("',' o ']' esperado en " + i);
                }
            }

            string Cadena()
            {
                var sb = new StringBuilder();
                i++;
                while (true)
                {
                    if (i >= s.Length) throw new JsonException("cadena sin cerrar");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (i >= s.Length) throw new JsonException("escape cortado");
                    char n = s[i++];
                    switch (n)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case '/': sb.Append('/'); break;
                        case '\\': sb.Append('\\'); break;
                        case '"': sb.Append('"'); break;
                        case 'u':
                            int cp;
                            if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture, out cp))
                                throw new JsonException("\\u invalido en " + i);
                            sb.Append((char)cp);   // pares sustitutos: cada mitad se anade tal cual
                            i += 4;
                            break;
                        default: throw new JsonException("escape desconocido \\" + n);
                    }
                }
            }
        }
    }
}
