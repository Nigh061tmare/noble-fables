using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using HarmonyLib;

namespace Pecera
{
    // =========================================================================
    //  EL REY DORMIDO  -  el mayordomo que juega por ti
    // =========================================================================
    //  El juego te pide decidir las audiencias públicas (disputas entre NPCs).
    //  Si no decides, la villa se paraliza. Aqui las resolvemos solas.
    //
    //  Se engancha a OnMorningTick: UNA decision al amanecer de cada dia, que
    //  es justo cuando un rey despacha. Nada mas.
    // =========================================================================
    public static class Rey
    {
        static readonly string[] PERDONAR = {
            "EnemyOfYourFriendIsYourEnemy", "DeeplyInLove", "InServiceOfLove",
            "InFaithWeTrust", "AtYourService", "IndependenceCalls",
            "MilitarySorrow", "HungryPeople"
        };
        static readonly string[] CASTIGAR = {
            "CoupDetat", "GreedIsNeed", "Treachery", "Theft", "Murder"
        };

        // -------------------------------------------------------------------
        //  Se llama una vez por dia de juego
        // -------------------------------------------------------------------
        public static void AlAmanecer()
        {
            if (!Plugin.Encendido) return;
            var th = new Thread(Juzga);
            th.IsBackground = true;
            th.Start();
        }

        static void Juzga()
        {
            try
            {
                var mgr = Instance();
                if (mgr == null) return;

                var enc = Parche.Campo(mgr, "unresolvedEncounters") as IDictionary;
                if (enc == null || enc.Count == 0)
                {
                    Log("{\"tipo\":\"amanecer\",\"nota\":\"sin audiencias pendientes\"}");
                    return;
                }

                foreach (DictionaryEntry kv in enc)
                {
                    try
                    {
                        var presentador = kv.Key;
                        var caso = kv.Value;
                        var nombreTipo = TipoDe(caso);
                        var quien = NombreDe(presentador);

                        string razon = Piensa(nombreTipo, quien);
                        bool acepta = Acepta(nombreTipo);

                        var mi = mgr.GetType();
                        var met = mi.GetMethod("ResolveEncounter",
                            BindingFlags.Instance | BindingFlags.Public);
                        if (met == null) { Log("{\"tipo\":\"error\",\"msg\":\"no veo ResolveEncounter\"}"); return; }

                        met.Invoke(mgr, new object[] { presentador, caso });

                        Log(string.Format(CultureInfo.InvariantCulture,
                            "{{\"tipo\":\"audiencia\",\"asunto\":\"{0}\",\"quien\":\"{1}\","
                            + "\"decision\":\"{2}\",\"motivo\":\"{3}\"}}",
                            nombreTipo, quien, acepta ? "ACEPTADA" : "RECHAZADA", razon));
                    }
                    catch (Exception e)
                    {
                        Log("{\"tipo\":\"error\",\"msg\":\"" + Limpia(e.Message) + "\"}");
                    }
                }
            }
            catch (Exception e)
            {
                Log("{\"tipo\":\"error\",\"msg\":\"" + Limpia(e.Message) + "\"}");
            }
        }

        // El LLM razona, pero la decision la tomamos nosotros con reglas: no
        // queremos que un modelo rechace un golpe de estado a las tres de la tarde.
        static bool Acepta(string tipo)
        {
            foreach (var c in CASTIGAR)
                if (tipo.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0) return false;
            foreach (var p in PERDONAR)
                if (tipo.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return true;   // por defecto,tiles benevolente
        }

        static string Piensa(string tipo, string quien)
        {
            try
            {
                var sys = "Eres el consejero de un rey medieval. En una frase, "
                        + "explica al rey por que conviene resolver asi una peticion "
                        + "de tipo '" + tipo + "' presentada por " + quien + ". "
                        + "Solo espanol, sin comillas, maximo 20 palabras.";
                return Llm.Ask(sys, "Resume el caso en una frase.", 60);
            }
            catch { return "(sin razon)"; }
        }

        static object Instance()
        {
            var t = Tipo("PublicHearingManager");
            if (t == null) return null;
            var f = t.GetField("instance", BindingFlags.Static | BindingFlags.Public |
                                         BindingFlags.NonPublic);
            return f == null ? null : f.GetValue(null);
        }

        static string TipoDe(object enc)
        {
            if (enc == null) return "?";
            var def = Parche.Campo(enc, "publicHearingEncounterDefinition");
            var n = Parche.Campo(def, "encounterName") ?? Parche.Campo(def, "name");
            return n == null ? "?" : n.ToString();
        }

        static string NombreDe(object npc)
        {
            if (npc == null) return "?";
            var a = npc as BaseNPC;
            return a == null ? "?" : Parche.Nombre(a);
        }

        static void Log(string linea)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Plugin.CarpetaLogs, "rey.jsonl"),
                    linea + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        static string Limpia(string s)
        {
            return (s ?? "").Replace("\n", " ").Replace("\"", "'");
        }

        static Type Tipo(string nombre)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType(nombre);
                if (t != null) return t;
            }
            return null;
        }
    }
}