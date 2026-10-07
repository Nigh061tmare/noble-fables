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
    //  EL PROVOCADOR  -  fabricar la vida social a demanda
    // =========================================================================
    //  El problema real que nos ha frenado: el juego tarda dias de reloj en
    //  generar una pelea o una audiencia. Con eso no se puede verificar nada
    //  ni experimentallycte un social.
    //
    //  Aqui no esperamos: elegimos una relacion del mundo y le forzamos una
    //  discusion. El hook que ya teniamos hace el resto (el LLM responde, sale
    //  la burbuja, se registran los secretos). El juego cree que ha ocurrido
    //  de verdad porque lo es de verdad.
    // =========================================================================
    public static class Provocador
    {
        public static int CadaCuanto = 45;      // segundos entre eventos
        static DateTime Ultimo = DateTime.MinValue;
        static readonly Random Azar = new Random();
        static readonly List<string> Motivos = new List<string>
            { "pelea", "discusion", "pelea", "discusion", "romance" };

        public static void AlAmanecer()
        {
            // Al amanecer dispara uno de golpe, para que haya datos enseguida.
            if (!Plugin.Encendido) return;
            Dispara("amanecer");
        }

        public static void CadaFrame()
        {
            if (!Plugin.Encendido) return;
            if ((DateTime.Now - Ultimo).TotalSeconds < CadaCuanto) return;
            Ultimo = DateTime.Now;
            var t = new Thread(new ThreadStart(() => Dispara("auto")));
            t.IsBackground = true;
            t.Start();
        }

        static void Dispara(string origen)
        {
            try
            {
                var pares = Relaciones();
                if (pares.Count == 0) return;

                var rel = pares[Azar.Next(pares.Count)];
                var tipo = Motivos[Azar.Next(Motivos.Count)];

                var mi = rel.GetType();
                var met = mi.GetMethod(
                    tipo == "pelea" ? "SetPerformedFight"
                    : tipo == "romance" ? "TryCreateRomance"
                    : "SetPerformedArgument",
                    BindingFlags.Instance | BindingFlags.Public);

                if (met == null) return;
                if (tipo == "romance" && rel is PersonalRelationship)
                    TryRomance(mi, rel);

                met.Invoke(rel, new object[0]);

                Log(string.Format(CultureInfo.InvariantCulture,
                    "{{\"tipo\":\"provocado\",\"origen\":\"{0}\",\"motivo\":\"{1}\"}}",
                    origen, tipo));
            }
            catch (Exception e) { Log("{\"tipo\":\"prov_error\",\"msg\":\"" + Limpia(e.Message) + "\"}"); }
        }

        static void TryRomance(Type mi, object rel)
        {
            try
            {
                // Forzar el interes: sin esto TryCreateRomance casi nunca sale.
                var f = mi.GetField("specialInterestAtoB",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) f.SetValue(rel, true);
                var g = mi.GetField("specialInterestBtoA",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (g != null) g.SetValue(rel, true);
            }
            catch { }
        }

        static List<object> Relaciones()
        {
            var r = new List<object>();
            foreach (var p in Mundo.Relaciones()) r.Add(p);
            return r;
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

        static void Log(string l)
        {
            try { File.AppendAllText(Path.Combine(Plugin.CarpetaLogs, "rey.jsonl"),
                                     l + Environment.NewLine, Encoding.UTF8); }
            catch { }
        }

        static string Limpia(string s)
        {
            return (s ?? "").Replace("\n", " ").Replace("\"", "'").Trim();
        }
    }
}