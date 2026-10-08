using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Pecera.Core;

namespace PeceraNF
{
    // =========================================================================
    //  PETICIONES  -  adaptador del "Rey dormido" (gobierno autonomo, punto G)
    // =========================================================================
    //  Fase 1: SOLO LECTURA + dry-run. NUNCA escribe en el estado del juego.
    //
    //  Firmas CONFIRMADAS por la sonda F11 (binario 0.31.5.3, sonda.json):
    //    PetitionManager.Instance          (Manager<T>.Instance, estatica)
    //    .petitionQueue   = List<QueuedPetition>   (campos: type, petitioner, context, at)
    //    .petition        = Petition activa        (prop type, context, step, complete; metodos
    //                                                 Complete, Receive, Destroy, ClearMission)
    //    .nextPetition    = TimeStamp
    //    .CountOf(tag)    = Int32
    //  QueuedPetition{ type : PetitionType, petitioner : Pawn, context : OctScriptContext, at : TimeStamp }
    //
    //  Lo que hace en OBSERVADOR:
    //    1. Lee la cola y la peticion activa (reflexion, solo lectura).
    //    2. Cada peticion nueva la apunta en la memoria del mod (memoria.jsonl) y deja una
    //       evidencia "peticion_capturada" en la telemetria: asi verificamos en partida que la
    //       API se comporta como leimos (los campos informan lo que esperamos).
    //    3. Dry-run del consejo: Evalua CON LOS DATOS LEIDOS y deja constancia en el archivo
    //       de cronica/decisiones (solo interna, para el jugador poder verla). NO llama a
    //       ningun metodo del juego (ni SetPetition ni Complete).
    //
    //  Fase 2 (cuando peticion_capturada este verificada): escribir la decision
    //  (SetPetition / Complete) SOLO con peticiones=1 y modo asistente/dios, pasando por
    //  Acciones (Compuerta + veto del jugador). Mientras tanto, GobiernoDisponible=false
    //  y todo lo que escriba queda desactivado.
    // =========================================================================
    public static class Peticiones
    {
        static Type _tMgr;
        static PropertyInfo _propInstance, _propQueue, _propPetition, _propNext;
        static MethodInfo _metCountOf;
        static FieldInfo _fTipo, _fPeticionario, _fContexto, _fAt;
        static readonly Dictionary<int, bool> _vistosPorHash = new Dictionary<int, bool>();
        static bool _cargado;

        static void Carga()
        {
            if (_cargado) return;
            _cargado = true;
            _tMgr = BuscaTipo("PetitionManager");
            if (_tMgr == null) return;
            _propInstance = _tMgr.GetProperty("Instance", Aux.Todos | BindingFlags.FlattenHierarchy);
            _propQueue = _tMgr.GetProperty("petitionQueue", Aux.Todos);
            _propPetition = _tMgr.GetProperty("petition", Aux.Todos);
            _propNext = _tMgr.GetProperty("nextPetition", Aux.Todos);
            _metCountOf = _tMgr.GetMethod("CountOf", Aux.Todos);
            var tQ = BuscaTipo("QueuedPetition");
            if (tQ != null)
            {
                _fTipo = tQ.GetField("type", Aux.Todos);
                _fPeticionario = tQ.GetField("petitioner", Aux.Todos);
                _fContexto = tQ.GetField("context", Aux.Todos);
                _fAt = tQ.GetField("at", Aux.Todos);
            }
        }

        // Se llama desde el hilo principal (Plugin.Update, cada 30 s). No toca el juego:
        // solo lee valores escalares ya existentes y guarda en la memoria del mod.
        public static void Observa()
        {
            if (!Estado.Activo) return;
            Carga();
            if (_tMgr == null || _propInstance == null) return;

            object mgr;
            try { mgr = _propInstance.GetValue(null, null); }
            catch (TargetInvocationException) { return; }
            if (mgr == null) return;

            // ---- cola (List<QueuedPetition>) ----
            object q = _propQueue != null ? Seguro(_propQueue.GetValue(mgr)) : null;
            var tipos = new List<string>();
            int n = 0;
            if (q is System.Collections.IEnumerable)
                foreach (object it in (System.Collections.IEnumerable)q)
                {
                    n++;
                    if (it == null) continue;
                    string tipo = TextoDe(LeeCampo(it, _fTipo));
                    string peti = "?";
                    object pv = _fPeticionario != null ? Seguro(_fPeticionario.GetValue(it)) : null;
                    if (pv != null) peti = NombrePawn(pv);
                    tipos.Add((tipo ?? "?") + "|" + peti);
                }

            // ---- peticion activa (Petition) ----
            object activa = _propPetition != null ? Seguro(_propPetition.GetValue(mgr)) : null;
            string tipoActiva = null;
            if (activa != null)
            {
                var pt = activa.GetType().GetProperty("type", Aux.Todos);
                tipoActiva = pt != null ? TextoDe(Seguro(pt.GetValue(activa, null))) : null;
            }

            // ---- registra en la memoria del mod lo NUEVO (evita duplicar por hash de tipo+peti) ----
            int nuevos = 0;
            var tiposNuevos = new List<string>();
            foreach (string tp in tipos)
            {
                if (tp == "?|?") continue;
                int h = tp.GetHashCode();
                if (_vistosPorHash.ContainsKey(h)) continue;
                _vistosPorHash[h] = true;
                nuevos++;
                tiposNuevos.Add(tp);
                string[] partes = tp.Split('|');
                Estado.Mem.Registra("peticion", partes[0], partes.Length > 1 ? partes[1] : "?",
                                    "el gobierno autonomo observa una peticion", 1);
            }
            if (nuevos > 0 && Estado.Cfg.Bool("telemetria")) Estado.Ev.Ok("peticion_capturada", "cola=" + n);

            // ---- DRY-RUN del consejo: decide SOLO con datos leidos; registra la decision en
            //      la cronica interna (Informe/Cronica), NO llama a nada del juego. ----
            // Solo las NUEVAS: antes se re-anotaba toda la cola cada 30 s y la cronica se llenaba de duplicados.
            if (Estado.Cfg.Bool("peticiones") && !Estado.Cfg.PuedeEscribir("peticiones_aplicar"))
                DryRunConsejo(tiposNuevos);

            // ================= FASE 2 (solo cuando la fase 1 este verificada) =================
            // Si hay una peticion activa NO completa, se propone resolverla en la Compuerta
            // (con veto si modo=asistente). Cuando la ventana de veto pase, se aplica en el
            // hilo principal. GobiernoDisponible=false hoy -> Propone() no llega a escribirse.
            if (Estado.Cfg.Bool("peticiones"))
            {
                ProponeResolver(activa, tipoActiva);
                ProcesaCompuerta();
            }

            // ---- telemetria del tamano y de lo pendiente ----
            if (Estado.Cfg.Bool("telemetria"))
            {
                UnityEngine.Debug.Log("[Pecera] peticiones: cola=" + n + " activa=" + (tipoActiva ?? "no") +
                                      " nuevos=" + nuevos);
            }
        }

        // El consejo (Core) evalua las peticiones LEIDAS. Sigue siendo solo interna: la
        // decision se anota en la cronica del mod (informe.md) para que el jugador la vea y
        // la pueda contrastar. La escritura real al juego (SetPetition/Complete) es Fase 2.
        static void DryRunConsejo(List<string> tipos)
        {
            var consejo = new Consejo(Estado.Afectos);
            var ord = Estado.Ordenes;      // «favorece a X» en directriz.txt: el consejo le da margen (+0.15)
            if (ord.Favorecidos.Count > 0) consejo.Favorecido = sol => ord.Favorece(sol, null);
            var metas = Estado.Metas;      // las metas vivas del reino (se reajustan con las metricas en cada informe)
            foreach (string tp in tipos)
            {
                string[] partes = tp.Split('|');
                if (partes.Length < 1 || partes[0].Length == 0) continue;
                var p = new Peticion { Tipo = partes[0], Solicitante = partes.Length > 1 ? partes[1] : "?", Categoria = "cohesion" };
                // El soberano es quien manda; aun no tenemos id confiable: por ahora el consejo
                // evalua contra un id "reino" (el afecto con el reino no existe -> sentimiento 0).
                Veredicto v = consejo.Evalua(p, "reino", metas);
                Estado.Cronica.Anota(Gancho.DiaActual(), "peticion",
                                     (v.Aprueba ? "aprobada" : "denegada") + " la peticion de " + p.Solicitante +
                                     " (" + p.Tipo + ": " + v.Razon + ")", 1);
            }
        }

        // Propone en la Compuerta resolver la peticion activa. 'activa' es la Petition real
        // del juego (object por reflexion). Si aun no esta completa ni recibida, se propone
        // con veto (asistente) o sin el (dios). GobiernoDisponible=false hoy: el adaptador
        // de escritura (Acciones.ResuelvePeticion) es no-op, asi que esto es inofensivo.
        static void ProponeResolver(object activa, string tipo)
        {
            if (activa == null || !Acciones.GobiernoDisponible) return;
            if (tipo == null || tipo.Length == 0) return;

            // Si la peticion activa ya se completo o se esta resolviendo, no propongas otra vez.
            if (Completa(activa) || Recibida(activa)) return;

            bool dios = Estado.Cfg.Modo == "dios";
            // Carga = la propia Petition: el adaptador la usara al aplicar.
            Estado.Compuerta.Propone(
                "peticion", "activa:" + tipo,
                "Resolver la peticion '" + tipo + "'",
                "el gobierno autonomo la evaluo como favorable", activa, dios);
        }

        // Cuando la ventana de veto vence (o sin veto en dios), Compuerta.Listas() entrega la
        // decision; se aplica encolando en el hilo principal la escritura real (Acciones).
        static void ProcesaCompuerta()
        {
            var listas = Estado.Compuerta.Listas();
            foreach (var d in listas)
            {
                if (d.Clase != "peticion") continue;
                object p = d.Carga as object;                     // la Petition real
                string tipo = TextoDe(LeeProp(p, "type"));
                Acciones.ResuelvePeticion(tipo ?? "?", p);
            }
        }

        static bool Completa(object p)
        {
            object c = LeeProp(p, "complete");
            return c is bool && (bool)c;
        }

        static bool Recibida(object p)
        {
            object r = LeeProp(p, "received");
            return r is bool && (bool)r;
        }

        static object LeeProp(object o, string nombre)
        {
            if (o == null) return null;
            try
            {
                var pr = o.GetType().GetProperty(nombre, Aux.Todos);
                return pr != null ? Seguro(pr.GetValue(o, null)) : null;
            }
            catch (TargetInvocationException) { return null; }
            catch (ArgumentException) { return null; }
        }

        static object Seguro(object o)
        {
            try { return o; } catch (TargetInvocationException) { return null; } catch (ArgumentException) { return null; }
        }

        static object LeeCampo(object obj, FieldInfo f)
        {
            if (f == null || obj == null) return null;
            try { return f.GetValue(obj); } catch (TargetInvocationException) { return null; }
        }

        static string TextoDe(object o)
        {
            if (o == null) return null;
            try { string s = o.ToString(); if (!string.IsNullOrEmpty(s)) return s.Length > 40 ? s.Substring(0, 40) + "…" : s; }
            catch (Exception) { }
            return o.GetType().Name;
        }

        // Nombre legible del Pawn (reutiliza la logica de Gancho sin lanzar).
        static string NombrePawn(object p)
        {
            if (p == null) return "?";
            try
            {
                var pr = p.GetType().GetProperty("GetName", 0);
                object v = pr != null ? Seguro(pr.GetValue(p, null)) : null;
                if (v != null) { string s = v.ToString(); if (!string.IsNullOrEmpty(s)) return s; }
            }
            catch (TargetInvocationException) { }
            return "pawn#" + p.GetHashCode();
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