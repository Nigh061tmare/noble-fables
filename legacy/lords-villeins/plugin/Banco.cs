using System;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace Pecera
{
    // =========================================================================
    //  BANCO DE PRUEBA
    // =========================================================================
    //  El juego tarda dias de reloj en generar una audiencia o una pelea, asi
    //  que no podemos comprobar que las burbujas funcionan sin esperar. Este
    //  banco dispara un par de frases poco despues de cargar, SOLO para
    //  verificar que se ven. Se apaga solo, y se desactiva escribiendo
    //  "off" en pecera.cfg igual que el resto.
    // =========================================================================
    public static class Banco
    {
        static bool Hecho = false;

        public static void Arranca()
        {
            if (Hecho) return;
            Hecho = true;
            var t = new Thread(new ThreadStart(Dispara));
            t.IsBackground = true;
            t.Start();
        }

        static void Dispara()
        {
            // Le damos tiempo al juego a terminar de cargar la partida.
            System.Threading.Thread.Sleep(System.Math.Max(20000, Plugin.EsperaMs));
            if (!Plugin.Encendido) return;

            var frases = new[] {
                "Banks: muchisima gente y ni una casa nueva.",
                "Doña Maria: ha salido del puerto y hace un frio espantoso.",
                "Don Rocky: la villa crece sin que nadie de fuera la mande.",
                "Ferretero: Tengo el yunque caliente y las manos heladas",
            };
            for (int i = 0; i < frases.Length; i++)
            {
                Parche.A_la_cola(frases[i]);
                System.Threading.Thread.Sleep(9000);
            }
            UnityEngine.Debug.Log("[Pecera] banco de prueba completado");
        }
    }
}