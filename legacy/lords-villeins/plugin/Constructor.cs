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
    //  EL REY DORMIDO, parte 2:  que la villa CREZCA
    // =========================================================================
    //  Los aldeanos proponen planos y esperan tu aprobacion. Si nadie aprueba,
    //  la villa se estanca: no se levanta ni una casa.
    //
    //  API encontrada en el juego:
    //    WorldManager.unownedBuildableBlueprints  -> AvailableBlueprintsModule
    //      .blueprintDict                          -> los planos pendientes
    //      .MarkBlueprintAsPrioritized(Blueprint)  -> el boton de APROBAR
    // =========================================================================
    public static class Constructor
    {
        public static void AlAmanecer()
        {
            if (!Plugin.Encendido) return;
            var th = new Thread(Obra);
            th.IsBackground = true;
            th.Start();
        }

        static void Obra()
        {
            try
            {
                var wm = Instancia("WorldManager");
                if (wm == null) { Avisa("no encuentro WorldManager"); return; }

                var mod = Parche.Campo(wm, "unownedBuildableBlueprints");
                if (mod == null) { Avisa("el juego no tiene modulo de planos"); return; }

                var dic = Parche.Campo(mod, "blueprintDict") as IDictionary;
                if (dic == null || dic.Count == 0)
                {
                    Log("{\"tipo\":\"obra\",\"nota\":\"ningun plano pendiente\"}");
                    return;
                }

                var marcar = mod.GetType().GetMethod("MarkBlueprintAsPrioritized",
                    BindingFlags.Instance | BindingFlags.Public);
                if (marcar == null) { Avisa("no veo MarkBlueprintAsPrioritized"); return; }

                foreach (DictionaryEntry kv in dic)
                {
                    try
                    {
                        var plano = kv.Value;
                        var clase = NombreDe(plano);
                        var coste = CosteDe(plano);
                        bool ok = Aprueba(clase, coste);

                        if (ok) marcar.Invoke(mod, new object[] { plano });

                        Log(string.Format(CultureInfo.InvariantCulture,
                            "{{\"tipo\":\"obra\",\"plano\":\"{0}\",\"coste\":\"{1}\","
                            + "\"decision\":\"{2}\"}}",
                            clase, coste, ok ? "APROBADO" : "RECHAZADO"));
                    }
                    catch (Exception e) { Avisa(Limpia(e.Message)); }
                }
            }
            catch (Exception e) { Avisa(Limpia(e.Message)); }
        }

        // Regla simple y defendible: se aprueba mientras haya de donde pagar.
        // El LLM Commentary sobre si hace falta, pero el filtro lo pone la villa.
        static bool Aprueba(string clase, string coste)
        {
            int c;
            int.TryParse(coste, out c);
            if (c <= 0) return true;
            return c <= 400;
        }

        static string CosteDe(object plano)
        {
            foreach (var n in new[] { "totalCost", "cost", "requiredFunds", "price" })
            {
                var v = Parche.Campo(plano, n);
                if (v != null) return v.ToString();
            }
            return "?";
        }

        static string NombreDe(object plano)
        {
            foreach (var n in new[] { "blueprintName", "structureName", "name", "displayName" })
            {
                var v = Parche.Campo(plano, n);
                if (v != null) return v.ToString();
            }
            return plano.GetType().Name;
        }

        static object Instancia(string nombre)
        {
            var t = Tipo(nombre);
            if (t == null) return null;
            foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public |
                                           BindingFlags.NonPublic))
            {
                if (f.FieldType == t) return f.GetValue(null);
            }
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

        static void Avisa(string m)
        {
            Log("{\"tipo\":\"error\",\"msg\":\"" + Limpia(m) + "\"}");
        }

        static string Limpia(string s)
        {
            return (s ?? "").Replace("\n", " ").Replace("\"", "'");
        }
    }
}