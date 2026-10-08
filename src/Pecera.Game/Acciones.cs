using System;
using System.Reflection;
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
        // CONFIRMADO en binario 0.31.5.3: PetitionManager.Instance.petitionQueue = List<QueuedPetition>
        // y Petition.Complete() existe. El resto (SetPetition/TryTriggerPetition/Complete/CountOf)
        // ESTA LEIDO pero no se ha probado en partida: el adaptador se activa SOLO al verificar que
        // Complete() cierra sin romper.
        //
        // FASE 2 del "Rey dormido": SOLO se habilita cuando peticion_capturada este verificada
        // (ok >= 1). Hasta entonces GobiernoDisponible=false y ResuelvePeticion NO hace nada.
        public static bool GobiernoDisponible
        {
            get
            {
                // Tres llaves: (1) el usuario pidio APLICAR (peticiones_aplicar=1, explicito), (2) la fase 1 (lectura) tiene
                // evidencia, (3) escritura segura. Leer la cola NO prueba que Complete() sea inocuo: por eso (1) no se deduce.
                return Estado.Cfg.Bool("peticiones_aplicar")
                    && Estado.Ev.OkCount("peticion_capturada") >= 1 && Estado.Ev.FailCount("peticion_capturada") == 0;
            }
        }

        // Escribe en el juego la decision de la Compuerta: recibe y completa la peticion activa
        // (PetitionManager.Instance.petition). Se encola en el hilo principal. Solo opera si:
        //   - GobiernoDisponible (fase 2 verificada en partida: peticion_capturada ok>=1),
        //   - escritura segura (version del juego confirmada),
        //   - el interruptor 'peticiones' esta activo y el modo no es observador.
        // 'carga' es el objeto Petition real (via reflexion); 'tipo' es su tipo legible.
        // Hasta que la fase 2 este verificada en partida, es no-op (evidencia placeholder
        // 'peticion_aplicada' que solo se registraria si el codigo llegara a ejecutarse).
        public static void ResuelvePeticion(string tipo, object carga)
        {
            if (!GobiernoDisponible) return;                    // fase 2 sin verificar: nunca escribe
            if (!Estado.EscrituraSegura || !Estado.Cfg.PuedeEscribir("peticiones_aplicar")) return;
            if (carga == null) return;
            Principal.Encola(delegate
            {
                try
                {
                    // Recibe la peticion (la marca como "recibida" por el soberano) y la completa
                    // solo si aun no lo esta. Toda la reflexion en el hilo del juego, como el resto.
                    var t = carga.GetType();
                    var mReceive = t.GetMethod("Receive", Aux.Todos);
                    var mComplete = t.GetMethod("Complete", Aux.Todos);
                    var pCompleta = t.GetProperty("complete", Aux.Todos);
                    if (mReceive != null) mReceive.Invoke(carga, null);
                    if (pCompleta != null)
                    {
                        object v = pCompleta.GetValue(carga, null);
                        if (v is bool && (bool)v) { Estado.Ev.Ok("peticion_aplicada", tipo + " ya estaba completa"); return; }
                    }
                    if (mComplete != null) mComplete.Invoke(carga, null);
                    Estado.Ev.Ok("peticion_aplicada", tipo);
                }
                catch (TargetInvocationException e) { Estado.Ev.Fail("peticion_aplicada", e.GetType().Name + ": " + e.Message); }
            });
        }
    }
}
