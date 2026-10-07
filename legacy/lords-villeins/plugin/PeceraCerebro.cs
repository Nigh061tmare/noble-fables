using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using BepInEx;
using HarmonyLib;

namespace Pecera
{
    // =========================================================================
    //  PECERA CEREBRO - le pone cabeza al motor social de Lords & Villeins
    // =========================================================================
    //  El juego ya tiene el motor: relaciones asimetricas, discusiones, peleas,
    //  romances y rumores entre NPCs (EnemyOfYourFriendIsYourEnemy).
    //  Aqui solo le ponemos la voz: cuando dos NPCs se pelean, se discuten o se
    //  enamoran,问一下 a un LLM local que se dicen, en español y en character.
    //
    //  NO toca la simulacion: solo lee y escribe un fichero deialogos.
    // =========================================================================
    [BepInPlugin("pecera.cerebro", "Pecera Cerebro", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string OLLAMA = "http://127.0.0.1:11436/v1/chat/completions";
        public const string MODELO = "qwen9b-silly:latest";
        public static string CarpetaLogs = "";
        public static volatile bool Encendido = true;
        public static int EsperaMs = 25000;
        private Harmony _h;

        private void Awake()
        {
            CarpetaLogs = Path.Combine(Path.GetDirectoryName(Info.Location),
                                       "pecera_datos");
            Directory.CreateDirectory(CarpetaLogs);
            _h = new Harmony("pecera.cerebro");
            _h.PatchAll();

            var cfg = Path.Combine(CarpetaLogs, "pecera.cfg");
            if (File.Exists(cfg))
                Encendido = File.ReadAllText(cfg).Trim().ToLower() != "off";

            Logger.LogInfo("[Pecera] enganchado. modelo=" + MODELO);
            Logger.LogInfo("[Pecera] logs en " + CarpetaLogs);
            Logger.LogInfo("[Pecera] para apagar: crea pecera.cfg con la palabra OFF");

            Banco.Arranca();
        }
    }
}