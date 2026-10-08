using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Pecera.Core;

namespace PeceraNF
{
    // =========================================================================
    //  OBSERVADORES DE VIDA (ganchos.txt)  -  SOLO LECTURA
    // =========================================================================
    //  Cada linea "tipo = Clase.Metodo" de ganchos.txt instala un Postfix que, al ejecutarse ese metodo del juego, publica un
    //  EventoMundo en el bus (muerte, boda, nacimiento...). El postfix NO cambia argumentos ni resultado: solo mira que Pawn
    //  van como instancia o argumentos (A = el primero, B = el segundo) y encola el suceso al hilo principal.
    //  Mecanismo de Harmony usado: __instance, __args, __originalMethod (documentado; __args ya funciona en el gancho de opinion).
    //  NO VERIFICADO en partida: depende de que el metodo que pongas sea el correcto. La sonda F11 deja ganchos.sugeridos.txt
    //  (heuristica por nombre). Con ganchos=0 (por defecto) no se parchea nada.
    // =========================================================================
    public static class Observadores
    {
        static readonly Dictionary<MethodBase, string> TipoDe = new Dictionary<MethodBase, string>();
        public static int Instalados { get { return TipoDe.Count; } }

        public static void Instala(Harmony h)
        {
            if (!Estado.Cfg.Bool("ganchos")) return;
            string ruta = Path.Combine(Estado.Datos, "ganchos.txt");
            if (!File.Exists(ruta))
            {
                Estado.Escribe(ruta, "# tipo = Clase.Metodo   (tipos: " + string.Join(", ", Ganchos.Tipos) + ")\r\n" +
                                     "# Copia aqui SOLO lineas de ganchos.sugeridos.txt que hayas comprobado. Cada una es un observador de solo lectura.\r\n");
                return;
            }
            var avisos = new List<string>();
            var lista = Ganchos.Parse(File.ReadAllText(ruta, new UTF8Encoding(false)), avisos);
            foreach (var a in avisos) Estado.Avisos.Add("ganchos.txt: " + a);
            var post = new HarmonyMethod(typeof(Observadores).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic));
            foreach (var g in lista)
            {
                Type t = BuscaTipo(g.Clase);
                if (t == null) { Estado.Avisos.Add("ganchos.txt: no existe la clase " + g.Clase); continue; }
                var ms = new List<MethodInfo>();
                foreach (var m in t.GetMethods(Aux.Todos | BindingFlags.DeclaredOnly)) if (m.Name == g.Metodo && !m.IsAbstract && !m.IsGenericMethodDefinition) ms.Add(m);
                if (ms.Count == 0) { Estado.Avisos.Add("ganchos.txt: " + g.Clase + " no tiene un metodo " + g.Metodo); continue; }
                if (ms.Count > 1) Estado.Avisos.Add("ganchos.txt: " + g + " tiene " + ms.Count + " sobrecargas; se observan todas");
                foreach (var m in ms)
                {
                    try { h.Patch(m, postfix: post); TipoDe[m] = g.Tipo; }
                    catch (Exception e)
                    {
                        // Un parche que no entra no rompe nada: se registra y se sigue con el resto.
                        Estado.Ev.Fail("vida_suceso", "no se pudo observar " + g + ": " + e.GetType().Name + ": " + e.Message);
                    }
                }
            }
            UnityEngine.Debug.Log("[Pecera] observadores de vida instalados: " + TipoDe.Count);
        }

        static void Postfix(MethodBase __originalMethod, object __instance, object[] __args)
        {
            try
            {
                string tipo;
                if (__originalMethod == null || !TipoDe.TryGetValue(__originalMethod, out tipo)) return;
                if (!Estado.Activo || !Estado.Cfg.Bool("vida")) return;
                // Se leen los pawns AQUI (hilo del juego que llamo al metodo) y se encola el suceso al hilo principal.
                var pawns = new List<Pawn>();
                Pawn pi = __instance as Pawn; if (pi != null) pawns.Add(pi);
                if (__args != null) foreach (var a in __args) { Pawn pa = a as Pawn; if (pa != null && !pawns.Contains(pa)) pawns.Add(pa); }
                if (pawns.Count == 0) { Estado.Ev.Fail("vida_suceso", tipo + ": " + __originalMethod.Name + " sin ningun Pawn en instancia ni argumentos"); return; }
                string na = Gancho.Nombre(pawns[0]), ida = Identidad.Id(pawns[0], na);
                string idb = "";
                if (pawns.Count > 1) idb = Identidad.Id(pawns[1], Gancho.Nombre(pawns[1]));
                string metodo = __originalMethod.DeclaringType.Name + "." + __originalMethod.Name;
                Principal.Encola(delegate
                {
                    int dia = Gancho.DiaActual();
                    Estado.Eventos.Publica(new EventoMundo { Tipo = tipo, A = ida, B = idb, Dia = dia, Texto = tipo == "nacimiento" ? "" : metodo });
                    Estado.Ev.Ok("vida_suceso", tipo + " " + na + " (" + metodo + ")");
                });
            }
            catch (Exception e)
            {
                Estado.Ev.Fail("vida_suceso", e.GetType().Name + ": " + e.Message);
                UnityEngine.Debug.Log("[Pecera] observador: " + e);
            }
        }

        // Para la sonda: metodos de Assembly-CSharp cuyo nombre sugiere un suceso de vida. Solo lectura de metadatos.
        public static List<string> Sugerencias()
        {
            var r = new List<string> { "# SUGERENCIAS para ganchos.txt (heuristica por NOMBRE, no autoridad). Formato: tipo = Clase.Metodo",
                                       "# Quita el # solo de las que compruebes: observa la partida con ganchos=1 y mira vida_suceso en verificacion.json." };
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!asm.GetName().Name.StartsWith("Assembly-CSharp", StringComparison.Ordinal)) continue;
                Type[] ts;
                try { ts = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { ts = Array.FindAll(e.Types, x => x != null); }
                foreach (var t in ts)
                {
                    if (t.Name.Length > 60) continue;
                    MethodInfo[] ms;
                    try { ms = t.GetMethods(Aux.Todos | BindingFlags.DeclaredOnly); }
                    catch (TypeLoadException) { continue; }
                    foreach (var m in ms)
                    {
                        if (m.IsAbstract || m.IsGenericMethodDefinition || m.IsSpecialName) continue;
                        string s = Ganchos.Sugiere(t.FullName, m.Name);
                        if (s != null && !r.Contains(s)) r.Add(s);
                        if (r.Count > 400) return r;
                    }
                }
            }
            return r;
        }

        static Type BuscaTipo(string nombre)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!asm.GetName().Name.StartsWith("Assembly-CSharp", StringComparison.Ordinal)) continue;
                try { Type t = asm.GetType(nombre); if (t != null) return t; } catch (ReflectionTypeLoadException) { }
            }
            return null;
        }
    }
}
