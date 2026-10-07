using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    // Direccion del jugador (I). directriz.txt admite secciones:
    //   texto suelto                -> global
    //   [faccion:Casa de Ana]       -> para los miembros de esa faccion
    //   [pawn:Ana]                  -> para ese personaje (por nombre; se resuelve a id fuera)
    // Compatible con el directriz.txt antiguo (todo global).
    public sealed class Directrices
    {
        public string Global = "";
        public readonly Dictionary<string, string> PorFaccion = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> PorPawn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public const int MaxChars = 400;

        public static Directrices Parse(string texto)
        {
            var d = new Directrices();
            var g = new StringBuilder();
            Dictionary<string, string> dest = null; string clave = null;
            var acum = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
            if (texto == null) texto = "";
            foreach (string linea in texto.Split('\n'))
            {
                string l = linea.Trim();
                if (l.Length == 0 || l[0] == '#') continue;
                if (l[0] == '[' && l[l.Length - 1] == ']')
                {
                    string cab = l.Substring(1, l.Length - 2);
                    int c = cab.IndexOf(':');
                    string tipo = c > 0 ? cab.Substring(0, c).Trim().ToLowerInvariant() : "";
                    string nom = c > 0 ? cab.Substring(c + 1).Trim() : "";
                    if (tipo == "faccion" && nom.Length > 0) { dest = d.PorFaccion; clave = nom; }
                    else if (tipo == "pawn" && nom.Length > 0) { dest = d.PorPawn; clave = nom; }
                    else { dest = null; clave = null; }
                    continue;
                }
                if (dest == null) { g.Append(l).Append(' '); continue; }
                StringBuilder sb;
                string ak = (dest == d.PorFaccion ? "f:" : "p:") + clave;
                if (!acum.TryGetValue(ak, out sb)) { sb = new StringBuilder(); acum[ak] = sb; }
                sb.Append(l).Append(' ');
            }
            d.Global = Cut(g.ToString());
            foreach (var kv in acum)
            {
                if (kv.Key.StartsWith("f:", StringComparison.Ordinal)) d.PorFaccion[kv.Key.Substring(2)] = Cut(kv.Value.ToString());
                else d.PorPawn[kv.Key.Substring(2)] = Cut(kv.Value.ToString());
            }
            return d;
        }

        static string Cut(string s)
        {
            s = Json.UnaLinea(s);
            return s.Length > MaxChars ? s.Substring(0, MaxChars) : s;
        }

        // Texto efectivo para un personaje: global + su faccion + el suyo.
        public string Para(string nombrePawn, string nombreFaccion)
        {
            var p = new List<string>();
            if (Global.Length > 0) p.Add(Global);
            string x;
            if (!string.IsNullOrEmpty(nombreFaccion) && PorFaccion.TryGetValue(nombreFaccion, out x)) p.Add(x);
            if (!string.IsNullOrEmpty(nombrePawn) && PorPawn.TryGetValue(nombrePawn, out x)) p.Add(x);
            string r = string.Join(" ", p.ToArray());
            return r.Length > MaxChars ? r.Substring(0, MaxChars) : r;
        }
    }

    public interface IConsolaHost
    {
        string Estado();
        bool Veta(int id);
        string Modo { get; set; }
        void PonDirectriz(string ambito, string clave, string texto);   // ambito: global|faccion|pawn
        string QuitaDirectriz(string ambito, string clave);             // devuelve el texto previo o null
        string Pendientes();
    }

    // Consola en pantalla (I): comandos de una linea. Pura y probada; el adaptador solo
    // le pasa lo que el jugador escribe.
    public sealed class Consola
    {
        readonly IConsolaHost host;
        readonly Stack<string[]> deshacer = new Stack<string[]>();   // [ambito, clave, previo]
        public Consola(IConsolaHost host) { this.host = host; }

        public const string Ayuda =
            "estado | pendientes | veta <id> | modo <observador|asistente|dios> | " +
            "dir global <texto> | dir faccion <nombre>: <texto> | dir pawn <nombre>: <texto> | " +
            "quita global | quita faccion <nombre> | quita pawn <nombre> | deshacer";

        public string Ejecuta(string linea)
        {
            if (string.IsNullOrWhiteSpace(linea)) return Ayuda;
            string l = linea.Trim();
            string cmd = Primera(ref l);
            switch (cmd.ToLowerInvariant())
            {
                case "estado": return host.Estado();
                case "pendientes": return host.Pendientes();
                case "ayuda": return Ayuda;
                case "veta":
                {
                    int id;
                    if (!int.TryParse(l, out id)) return "uso: veta <id>";
                    return host.Veta(id) ? "vetada #" + id : "no hay decision #" + id + " pendiente";
                }
                case "modo":
                {
                    string m = l.ToLowerInvariant();
                    if (m != "observador" && m != "asistente" && m != "dios") return "uso: modo observador|asistente|dios";
                    host.Modo = m;
                    return "modo " + m + (m == "dios" ? " (sin ventana de veto, con topes)" : "");
                }
                case "dir":
                {
                    string ambito = Primera(ref l).ToLowerInvariant();
                    if (ambito == "global") return Pon("global", "", l);
                    if (ambito != "faccion" && ambito != "pawn") return "uso: dir global|faccion|pawn ...";
                    int c = l.IndexOf(':');
                    if (c <= 0) return "uso: dir " + ambito + " <nombre>: <texto>";
                    return Pon(ambito, l.Substring(0, c).Trim(), l.Substring(c + 1).Trim());
                }
                case "quita":
                {
                    string ambito = Primera(ref l).ToLowerInvariant();
                    if (ambito != "global" && ambito != "faccion" && ambito != "pawn") return "uso: quita global|faccion|pawn <nombre>";
                    string clave = ambito == "global" ? "" : l.Trim();
                    if (ambito != "global" && clave.Length == 0) return "falta el nombre";
                    string previo = host.QuitaDirectriz(ambito, clave);
                    if (previo == null) return "no habia directriz";
                    deshacer.Push(new[] { ambito, clave, previo });
                    return "quitada (usa 'deshacer' para recuperarla)";
                }
                case "deshacer":
                {
                    if (deshacer.Count == 0) return "nada que deshacer";
                    var u = deshacer.Pop();
                    if (u[2] == "\u0000") host.QuitaDirectriz(u[0], u[1]); else host.PonDirectriz(u[0], u[1], u[2]);
                    return "deshecho";
                }
                default: return "comando desconocido. " + Ayuda;
            }
        }

        string Pon(string ambito, string clave, string texto)
        {
            if (texto.Length == 0) return "falta el texto";
            string previo = host.QuitaDirectriz(ambito, clave);
            deshacer.Push(new[] { ambito, clave, previo ?? "\u0000" });
            host.PonDirectriz(ambito, clave, texto);
            return "directriz " + ambito + (clave.Length > 0 ? " '" + clave + "'" : "") + " fijada";
        }

        static string Primera(ref string l)
        {
            l = l.TrimStart();
            int i = l.IndexOf(' ');
            string p = i < 0 ? l : l.Substring(0, i);
            l = i < 0 ? "" : l.Substring(i + 1).Trim();
            return p;
        }
    }
}
