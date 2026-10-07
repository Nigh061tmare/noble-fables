using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Pecera
{
    // =========================================================================
    //  BURBUJAS  -  que se vea, no solo se lea
    // =========================================================================
    //  El juego ya tiene su propio sistema de bocadillos: el rey habla en el
    // .attributes UIManager.TriggerKingSpeechBubble(String, ...). Reutilizamos
    //  ese mecanismo en vez de fabricar una UI propia: asi se ve igual que todo
    //  lo demas y no se rompe con los cambios de la escena.
    // =========================================================================
    public static class Burbujas
    {
        const int SEGUNDOS = 6;

        public static bool Muestra(string texto)
        {
            try
            {
                if (string.IsNullOrEmpty(texto)) return false;
                texto = Limpia(texto);
                if (texto.Length > 140) texto = texto.Substring(0, 137) + "...";

                var ui = Instancia("UIManager");
                if (ui == null) return false;

                var t = ui.GetType();
                var m = t.GetMethod("TriggerKingSpeechBubble",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null) return false;

                // Los otros dos parametros (lista y definicion de objetivo) los
                // dejamos a null: la burbuja sale igual, sin icono de campaña.
                m.Invoke(ui, new object[] { texto, null, null });

                // Que no se vaya antes de que la lea: el juego la cierra solo,
                // pero si tardas nos quedamos sin ver nada.
                return true;
            }
            catch (Exception e)
            {
                Debug.Log("[Pecera] burbuja fallo: " + e.Message);
                return false;
            }
        }

        static object Instancia(string nombre)
        {
            var t = Tipo(nombre);
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
            return s.Replace("\r", " ").Replace("\n", " ").Replace("\"", "'").Trim();
        }
    }
}