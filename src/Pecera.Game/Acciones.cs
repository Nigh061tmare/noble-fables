using System;
using Pecera.Core;

namespace PeceraNF
{
    // TODAS las escrituras en el estado del juego pasan por aqui. Cada una:
    //   1. pregunta a la config (PuedeEscribir: interruptor + modo observador),
    //   2. comprueba que la version del juego no ha cambiado sin confirmar,
    //   3. se ejecuta en el hilo principal (cola Principal),
    //   4. deja evidencia (ok / fallo) en Estado.Ev.
    public static class Acciones
    {
        // [ThreadStatic]: el hook ve que la llamada es nuestra y no la procesa (reentrada). Si el
        // juego dispara OpinionDelta DENTRO de DeltaOpinionOfSubject, Gancho lo anota como
        // "reentrada_bloqueada"; si nunca ocurre, la evidencia queda en sin_datos (no es un fallo).
        [ThreadStatic] public static bool Propio;

        // VERIFICADO que COMPILA (v0.1). NO VERIFICADO en partida (informe §8.2).
        public static void EmpujaOpinion(Pawn pawn, ISubject sujeto, FeelingReason motivo, double k)
        {
            if (!Estado.Cfg.PuedeEscribir("influencia")) return;
            if (!Estado.EscrituraSegura)
            {
                Estado.Ev.Fail("empujon_opinion", "bloqueado: version del juego cambio y no esta confirmada");
                return;
            }
            Principal.Encola(delegate
            {
                Propio = true;
                try
                {
                    pawn.DeltaOpinionOfSubject(sujeto, (float)k, motivo, FeelingMemoryFlags.None);
                    Estado.Ev.Ok("empujon_opinion", "k=" + Json.Num(k));
                }
                catch (Exception e)
                {
                    Estado.Ev.Fail("empujon_opinion", e.GetType().Name + ": " + e.Message);
                    UnityEngine.Debug.Log("[Pecera] influencia fallo: " + e.Message);
                }
                finally { Propio = false; }
            });
        }

        // ---- firmas PENDIENTES DE CONFIRMAR ----
        // SchemeManager.TryTriggerScheme(SchemeType, ISchemeExecutor, OctScriptContext, out Scheme)
        // figura en el informe §7 (LEIDO) pero no se sabe construir ISchemeExecutor ni OctScriptContext
        // para un pawn. Hasta leerlo del binario con la sonda (F11 -> sonda.json) no se inventa.
        public static bool EsquemasDisponibles { get { return false; } }

        public static bool DisparaEsquema(string tipo, string ejecutorId, string objetivoId)
        {
            Estado.Ev.Fail("esquema_disparo", "firma pendiente de confirmar: TryTriggerScheme (ver sonda.json)");
            return false;
        }

        // PetitionManager.petitionQueue / Petition.Complete(), ResearchManager, PlanManager.AddPlan,
        // MissionManager: nombres en el informe §7 (LEIDO), parametros sin leer. Sin adaptador.
        public static bool GobiernoDisponible { get { return false; } }
    }
}
