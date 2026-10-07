using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Pecera
{
    // =========================================================================
    //  EL REY DORMIDO, parte 3:  repartir las manos
    // =========================================================================
    //  La villa no se estanca por falta de trabajo: cada NPC elige por su
    //  cuenta (GOAP). Lo que el jugador decide es CUANTOS seitoral a cada
    //  labor. Eso es exactamente WorkGroup.SetMaxMembers.
    //
    //  API (leida del binario del juego):
    //    WorldOrganization.GetWorkGroups()               -> Dictionary<int,WorkGroup>
    //    WorldOrganization.SetWorkGroupMaxMembers(wg, n) -> el deslizador
    //    WorldOrganization.RedistributeMembersIntoWorkGroups() -> aplicar
    // =========================================================================
    public static class Manos
    {
        // Reparto base cuando no sabemos nada. Las claves se comparan contra
        // el workGroupName (que viene en ingles, del juego).
        static readonly Dictionary<string, int> BASE = new Dictionary<string, int>
        {
            {"farm", 6}, {"food", 6}, {"cook", 2}, {"bakery", 2},
            {"hunt", 3}, {"butcher", 1}, {"wood", 5}, {"forest", 4},
            {"mine", 3}, {"stone", 2}, {"build", 5}, {"carpentry", 3},
            {"smith", 1}, {"craft", 3}, {"tailor", 2}, {"haul", 4},
            {"clean", 3}, {"research", 1}, {"heal", 1}, {"medic", 1},
            {"relig", 1}, {"priest", 1}, {"guard", 3}, {"soldier", 4},
        };

        public static void AlAmanecer()
        {
            if (!Plugin.Encendido) return;
            var th = new Thread(new ThreadStart(Reparte));
            th.IsBackground = true;
            th.Start();
        }

        static void Reparte()
        {
            try
            {
                var org = Organizacion();
                if (org == null) { Log("{\"tipo\":\"manos\",\"error\":\"no veo la organizacion\"}"); return; }

                var mi = org.GetType();
                var getWg = mi.GetMethod("GetWorkGroups");
                var setMax = mi.GetMethod("SetWorkGroupMaxMembers",
                    BindingFlags.Instance | BindingFlags.Public);
                if (getWg == null || setMax == null) {
                    Log("{\"tipo\":\"manos\",\"error\":\"no veo la API de grupos\"}");
                    return;
                }

                var grupos = getWg.Invoke(org, null) as IDictionary;
                if (grupos == null || grupos.Count == 0) return;

                int total = 0;
                foreach (DictionaryEntry kv in grupos)
                {
                    var wg = kv.Value;
                    var nombre = Nombre(wg);
                    int minimo = Minimo(nombre);
                    try {
                        var met = wg.GetType().GetMethod("SetMaxMembers",
                            BindingFlags.Instance | BindingFlags.Public);
                        if (met != null) { met.Invoke(wg, new object[] { minimo }); total++; }
                    } catch { }
                }

                // Con los grupos actualizados hay que repartirse de verdad.
                var red = mi.GetMethod("RedistributeMembersIntoWorkGroups",
                    BindingFlags.Instance | BindingFlags.Public);
                if (red != null) red.Invoke(org, null);

                Log(string.Format(CultureInfo.InvariantCulture,
                    "{{\"tipo\":\"manos\",\"grupos\":{0},\"repartidos\":true}}", total));
            }
            catch (Exception e) { Log("{\"tipo\":\"manos\",\"error\":\"" + Limpia(e.Message) + "\"}"); }
        }

        static int Minimo(string nombre)
        {
            var n = (nombre ?? "").ToLowerInvariant();
            int mejor = 2;
            int largo = -1;
            foreach (var kv in BASE)
            {
                if (n.Contains(kv.Key) && kv.Key.Length > largo)
                {
                    mejor = kv.Value;
                    largo = kv.Key.Length;
                }
            }
            return mejor;
        }

        static string Nombre(object wg)
        {
            var v = Parche.Campo(wg, "workGroupName");
            if (v != null) return v.ToString();
            var d = Parche.Campo(wg, "workGroupDefinition");
            var n = Parche.Campo(d, "name");
            return n == null ? "?" : n.ToString();
        }

        // Busca la organizacion del jugador probando los sitios habituales.
        static object Organizacion()
        {
            return Mundo.OrgMundo();
        }

        static object Instancia(Type t)
        {
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

        static void Log(string l)
        {
            try { File.AppendAllText(Path.Combine(Plugin.CarpetaLogs, "rey.jsonl"),
                                     l + Environment.NewLine, Encoding.UTF8); }
            catch { }
        }

        static string Limpia(string s)
        {
            return (s ?? "").Replace("\n", " ").Replace("\"", "'");
        }
    }
}