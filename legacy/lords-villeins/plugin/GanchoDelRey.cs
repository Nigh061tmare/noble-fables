using HarmonyLib;
using UnityEngine;

namespace Pecera
{
    // Al amanecer el rey hace tres cosas, siempre en este orden:
    //   1. juzga las audiencias      (la sociedad avanza)
    //   2. firma las obras           (la villa crece)
    //   3. reparte las manos         (la villa se optimiza)
    //
    // Y ademas, cada fotograma, vacia la cola de frases: el LLM responde en
    // un hilo secundario y Unity obliga a pintar desde aqui.
    [HarmonyPatch(typeof(PublicHearingManager), "OnMorningTick")]
    static class GanchoDelRey
    {
        static void Postfix()
        {
            Rey.AlAmanecer();
            Constructor.AlAmanecer();
            Manos.AlAmanecer();
            Secretos.AlAmanecer();
            Provocador.AlAmanecer();
        }
    }

    [HarmonyPatch(typeof(UnityEngine.Time), "get_deltaTime")]
    static class Latido
    {
        static void Postfix()
        {
            Parche.VaciaCola();
            Provocador.CadaFrame();
        }
    }
}