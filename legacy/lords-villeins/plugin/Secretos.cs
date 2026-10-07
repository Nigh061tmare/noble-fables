using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Pecera
{
    // =========================================================================
    //  SECRETOS  -  la pieza que mide la propagacion
    // =========================================================================
    //  Cada aldeano recibe UN secreto que solo el conoce. Lo que interesa no es
    //  el secreto: es CUANDO se filtra, A quien y QUE PASOS despues con la
    //  relacion. Eso es literalmente lo que没有任何 simulador te da.
    //
    //  Archivo:   secretos.json   -> que secreto tiene cada quien
    //             fugas.jsonl      -> cada vez que uno se cuenta otro
    // =========================================================================
    public static class Secretos
    {
        static readonly object Cerrojo = new object();
        static Dictionary<int, string> Mapa = null;   // idNPC -> secreto

        static readonly string SYS =
            "Inventas un secreto breve y ocultable sobre un aldeano de una villa "
          + "medieval. UNA frase, en espanol, sin nombres propios. Debe ser algo "
          + "que de verdad le conviene callar (un crimen, una deuda, un amor "
          + "prohibido, una familia falsa). Nada de magia ni de coisas infantiles.";

        // ------------------------------------------------------------------
        //  Al amanecer: reparte secretos a quien no tenga
        // ------------------------------------------------------------------
        public static void AlAmanecer()
        {
            if (!Plugin.Encendido) return;
            var t = new Thread(new ThreadStart(Reparte));
            t.IsBackground = true;
            t.Start();
        }

        static void Reparte()
        {
            try
            {
                Carga();
                var npcs = NPCs();
                if (npcs.Count == 0) return;

                int nuevos = 0;
                foreach (var kv in npcs)
                {
                    int id = kv.Key;
                    if (Mapa.ContainsKey(id)) continue;

                    var s = Llm.Ask(SYS, "Aldeano " + kv.Value + ". Su secreto:",
                                    60);
                    if (string.IsNullOrEmpty(s)) continue;
                    s = Limpia(s);
                    if (s.Length < 12) continue;      // respuesta vacia o tonta

                    Mapa[id] = s;
                    nuevos++;
                    Log("{\"tipo\":\"secreto\",\"npc\":\"" + Esc(kv.Value) +
                        "\",\"id\":" + id + ",\"secreto\":\"" + Esc(s) + "\"}");
                }
                if (nuevos > 0) Guarda();
            }
            catch (Exception e) { Log("{\"tipo\":\"error\",\"msg\":\"" + Limpia(e.Message) + "\"}"); }
        }

        // ------------------------------------------------------------------
        //  Cuando dos se pelean o se enamoran: chance de que uno se lo suelte
        // ------------------------------------------------------------------
        public static void SeCuenta(int idA, int idB, string tipo, float afinidad)
        {
            try
            {
                Carga();
                if (Mapa.Count == 0) return;

                // Solo se habla de secretos cuando hay confianza O rencor.
                // Un odio fuerte tambien sirve: la gente suelta cosas para
                // hacer dano. Eso es la propagacion que nos interesa.
                if (Math.Abs(afinidad) < 12f) return;
                if (!Mapa.ContainsKey(idA) || !Mapa.ContainsKey(idB)) return;

                // Uno cuenta lo del otro, nunca lo suyo.
                string loDelOtro = Mapa[idB];
                Log("{\"tipo\":\"fuga\",\"de\":\"" + Esc(Mapa[idB].Length > 0
                        ? "npc" + idB : "") + "\",\"a_id\":" + idA +
                    ",\"b_id\":" + idB + ",\"motivo\":\"" + tipo +
                    "\",\"afinidad\":" + afinidad.ToString("0.0",
                       CultureInfo.InvariantCulture) +
                    ",\"secreto\":\"" + Esc(loDelOtro) + "\"}");

                Parche.A_la_cola("Te cuento un secreto: " + loDelOtro);
            }
            catch { }
        }

        // ------------------------------------------------------------------
        static Dictionary<int, string> NPCs()
        {
            var r = new Dictionary<int, string>();
            foreach (var npc in Mundo.Npcs())
            {
                int id = 0;
                try { id = Convert.ToInt32(Mundo.Campo(npc, "id")); } catch { }
                if (id != 0) r[id] = Parche.Nombre(npc);
            }
            return r;
        }

        static string Ruta() { return Path.Combine(Plugin.CarpetaLogs, "secretos.json"); }

        static void Carga()
        {
            lock (Cerrojo)
            {
                if (Mapa != null) return;
                Mapa = new Dictionary<int, string>();
                try
                {
                    if (!File.Exists(Ruta())) return;
                    var t = File.ReadAllText(Ruta());
                    // Formato simple:  "id|secreto" por linea. Sin JSON porque
                    // las comillas y los acentos dan muchos problemas.
                    foreach (var l in t.Split('\n'))
                    {
                        var p = l.Split('|');
                        if (p.Length >= 2)
                        {
                            int id;
                            if (int.TryParse(p[0].Trim(), out id))
                                Mapa[id] = p[1].Trim();
                        }
                    }
                }
                catch { }
            }
        }

        static void Guarda()
        {
            lock (Cerrojo)
            {
                try
                {
                    var sb = new StringBuilder();
                    foreach (var kv in Mapa) sb.AppendLine(kv.Key + "|" + kv.Value);
                    File.WriteAllText(Ruta(), sb.ToString(), Encoding.UTF8);
                }
                catch { }
            }
        }

        static void Log(string l)
        {
            try { File.AppendAllText(Path.Combine(Plugin.CarpetaLogs, "fugas.jsonl"),
                                     l + Environment.NewLine, Encoding.UTF8); }
            catch { }
        }

        static object Instancia(string n)
        {
            var t = Tipo(n);
            if (t == null) return null;
            foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public |
                                          BindingFlags.NonPublic))
                if (f.FieldType == t) return f.GetValue(null);
            return null;
        }

        static Type Tipo(string n)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType(n);
                if (t != null) return t;
            }
            return null;
        }

        static string Limpia(string s)
        {
            return (s ?? "").Replace("\r", " ").Replace("\n", " ")
                    .Replace("\"", "'").Replace("|", "-").Trim();
        }

        static string Esc(string s)
        {
            return (s ?? "").Replace("\"", "'");
        }
    }
}