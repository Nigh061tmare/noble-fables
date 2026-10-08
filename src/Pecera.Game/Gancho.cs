using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using HarmonyLib;
using Pecera.Core;
using UnityEngine;

namespace PeceraNF
{
    // =========================================================================
    //  EL GANCHO  (PawnManager.OpinionDelta -> Postfix)
    // =========================================================================
    //  Informe §6.1: no se parchea Invoke de un delegado sino este metodo normal.
    //  Reparto del trabajo (informe §5, sin cambios):
    //    - hilo del juego (Postfix): filtra, lee datos baratos, alimenta el modelo afectivo
    //      y la memoria (todo en memoria, sin esperar a nadie).
    //    - ThreadPool: escribe la linea de datos.
    //    - UNA hebra de voz: la unica que habla con el LLM (voz, resumenes).
    //  La logica de decision (cupo, rasgo, politica de empujon, parseo, prompts) esta en
    //  Pecera.Core y tiene tests; aqui solo queda lo que toca tipos del juego.
    // =========================================================================
    public static class Gancho
    {
        const int FRENO_SEGUNDOS = 20;               // anti-rafaga por pawn
        static int Llamadas;
        static int Registrados;
        static bool _sondaHecha;
        static readonly Dictionary<int, long> Ultimo = new Dictionary<int, long>();
        static readonly System.Collections.Concurrent.BlockingCollection<Action> Cola
            = new System.Collections.Concurrent.BlockingCollection<Action>(4);
        static bool HiloArrancado;

        public static int LlamadasTotal { get { return Llamadas; } }

        // Para que otros modulos (Mente) usen LA MISMA hebra de LLM: nunca dos llamadas en vuelo.
        public static bool ColaHolgada { get { return Cola.Count < 2; } }
        public static bool EncolaLlm(Action a) { return EncolaBruto(a); }

        [HarmonyPatch(typeof(PawnManager), "OpinionDelta")]
        static class AlCambiarLaOpinion
        {
            static void Postfix(object[] __args)
            {
                try
                {
                    Procesa(__args[0] as Pawn, __args[1],
                            Convert.ToSingle(__args[2], CultureInfo.InvariantCulture), __args[3]);
                }
                catch (Exception e)
                {
                    // Un fallo aqui NUNCA debe llegar al juego, pero tampoco ocultarse: se cuenta.
                    Estado.Ev.Fail("hook_opinion", e.GetType().Name + ": " + e.Message);
                    UnityEngine.Debug.Log("[Pecera] fallo en el hook: " + e);
                }
            }
        }

        static void Procesa(Pawn pawn, object of, float delta, object reason)
        {
            if (!Estado.Activo) return;
            if (Acciones.Propio) { Estado.Ev.Ok("reentrada_bloqueada", ""); return; }

            int n = Interlocked.Increment(ref Llamadas);
            Estado.Ev.Ok("hook_opinion", n == 1 ? "primera llamada" : "");
            if (n == 1 || n % 2000 == 0) UnityEngine.Debug.Log("[Pecera] OpinionDelta llamadas=" + n);

            float abs = Math.Abs(delta);
            if (abs < Estado.Cfg.Num("umbral_dato")) return;

            int h = pawn != null ? pawn.GetHashCode() : 0;
            long ahora = DateTime.Now.Ticks;
            lock (Ultimo)
            {
                long prev;
                if (Ultimo.TryGetValue(h, out prev) && ahora - prev < TimeSpan.TicksPerSecond * FRENO_SEGUNDOS) return;
                Ultimo[h] = ahora;
                if (Ultimo.Count > 4000) Ultimo.Clear();
            }

            // Todo lo que toca objetos del juego se lee AQUI, en el hilo del juego.
            if (!_sondaHecha) { _sondaHecha = true; Sonda.PawnUnaVez(pawn); }
            float? valor = Opinion(pawn, of);
            Pawn otro = PawnDe(of);
            string a = Nombre(pawn), b = otro != null ? Nombre(otro) : NombreDe(of);
            string porQue = Razon(reason);
            string ida = Identidad.Id(pawn, a);
            string idb = otro != null ? Identidad.Id(otro, b) : "x:" + b;
            bool rasgo = Habla.EsRasgo(porQue);
            if (!rasgo && delta > 0) Estado.Cultura.Suceso("comunidad", 0.1);      // trato bueno por un hecho

            // Modelo afectivo, memoria y rumores (solo memoria interna: no escriben en el juego).
            if (pawn != null) Mente.Recuerda(ida, pawn);
            Mente.Observa(ida, idb, delta);
            if (Estado.Cfg.Bool("afectos"))
            {
                Estado.Afectos.DesdeOpinion(ida, idb, delta, rasgo);
                Estado.Ev.Ok("afectos_modelo", "");
            }
            string recuerdos = Estado.Cfg.Bool("memoria") ? Estado.Mem.ParaPrompt(ida, 400) : "";
            if (Estado.Cfg.Bool("memoria") && abs >= Estado.Cfg.Num("umbral_habla") && !rasgo)
                Estado.Mem.Registra(ida, "opinion", idb, (delta > 0 ? "empezo a apreciar a " : "empezo a desconfiar de ") + b + " (" + porQue + ")", abs);
            if (Estado.Cfg.Bool("rumores")) Rumores(ida, idb, a, b);

            bool voz = abs >= (rasgo ? Estado.Cfg.Num("umbral_rasgo") : Estado.Cfg.Num("umbral_habla"))
                       && Estado.Salud.Disponible && Estado.Voz.TryAcquire();
            if (!voz)
            {
                ThreadPool.QueueUserWorkItem(delegate { Registra(a, b, porQue, delta, valor, null, null, false, null, 0); });
                return;
            }

            string ficha = Estado.Fichas.GetOCrea(ida, a).Resumen();
            string faccion; Estado.FaccionDe.TryGetValue(ida, out faccion);
            Dialecto dia; if (faccion != null && Estado.Dialectos.TryGetValue(faccion, out dia) && dia.Count > 0) ficha += "; " + dia.Pista();
            Estado.CargaDirectriz();
            string dir = Estado.Dir.Para(a, faccion);
            Encola(delegate
            {
                try { Redacta(pawn, of, reason, a, b, ida, idb, porQue, delta, valor, ficha, recuerdos, dir); }
                finally { Estado.Voz.Release(); }
            });
        }

        // La confidencia/chisme se dispara con el contacto que ya observamos (la API de
        // ConversationManager esta pendiente de leer). Solo cambia el modelo interno y la memoria.
        static void Rumores(string ida, string idb, string a, string b)
        {
            if (!Estado.Secretos.TieneSecreto(ida)) Estado.Secretos.Asigna(ida, RedSecretos.SecretoDeReserva(ida), 0.4 + 0.5 * ((FichaGen.Hash(ida + "g") % 100) / 100.0));
            if (!Estado.Secretos.TieneSecreto(idb)) Estado.Secretos.Asigna(idb, RedSecretos.SecretoDeReserva(idb), 0.4 + 0.5 * ((FichaGen.Hash(idb + "g") % 100) / 100.0));
            foreach (Fuga f in Estado.Secretos.Conversan(ida, idb))
            {
                Estado.Disco.Append("fugas.jsonl", f.ToJson());
                Estado.Mem.Registra(f.Receptor, "rumor", f.Sujeto, "oyo que " + Estado.Ids.Nombre(f.Sujeto) + " " + f.Texto, 1 + f.Fidelidad);
                Estado.Cronica.Anota(DiaActual(), "rumor", Estado.Ids.Nombre(f.Receptor) + " se entera de un secreto de " + Estado.Ids.Nombre(f.Sujeto), 2);
                Estado.Ev.Ok("rumor_fuga", f.Motivo);
            }
        }

        public static int DiaActual()
        {
            double seg = (Estado.Reloj.NowTicks - Estado.ArranqueTicks) / (double)TimeSpan.TicksPerSecond;
            return (int)(seg / Math.Max(10, Estado.Cfg.Num("dia_segundos")));
        }

        // ------------------------------------------------------------------
        static void Redacta(Pawn pawn, object of, object reason, string a, string b, string ida, string idb,
                            string porQue, float delta, float? valor, string ficha, string recuerdos, string directriz)
        {
            Replica r = new Replica();
            string error = "";
            string user = Prompts.UsuarioVoz(a, b, delta, porQue, valor, ficha, recuerdos, directriz);
            string txt = Llm.Ask(Prompts.SistemaVoz, user, 220);
            if (!string.IsNullOrEmpty(txt)) r = ReplicaParser.Parse(txt);
            else error = Llm.UltimoError;

            if (r.Ok)
            {
                Estado.Ev.Ok("voz_frase", a + ": " + r.Texto);
                if (r.Metodo == "json") Estado.Ev.Ok("voz_json_valido", ""); else Estado.Ev.Fail("voz_json_valido", "metodo=" + r.Metodo);
            }
            else Estado.Ev.Fail("voz_frase", error.Length > 0 ? error : "sin frase util (metodo=" + r.Metodo + ")");

            double ajuste = 0;
            if (r.Ok) Principal.Encola(delegate { Estado.Cultura.Suceso(r.Actitud == "perdona" ? "clemencia" : r.Actitud == "empeora" ? "venganza" : "honor", 0.1); });
            if (r.Ok) ajuste = Aplica(pawn, of, reason, ida, idb, delta, r.Actitud);
            Registra(a, b, porQue, delta, valor, r.Ok ? r.Texto : null, r.Piensa, true, r.Actitud, ajuste);
            if (r.Ok) Pantalla.Burbuja(pawn, a, r.Texto, delta > 0);

            if (Estado.Cfg.Bool("memoria") && Estado.Cfg.Bool("memoria_resumen")) Resumenes();
        }

        // Empuja la opinion segun la actitud. Politica y topes: Core (probada). Escritura: Acciones.
        static double Aplica(Pawn pawn, object of, object reason, string ida, string idb, float delta, string actitud)
        {
            if (!Estado.Cfg.PuedeEscribir("influencia") || pawn == null) return 0;
            var subj = of as ISubject;
            if (subj == null || !(reason is FeelingReason)) return 0;
            double k = Estado.Influencia.Decide(ida + "|" + idb, delta, actitud);
            if (Math.Abs(k) < 1e-9) return 0;
            Acciones.EmpujaOpinion(pawn, subj, (FeelingReason)reason, k);
            return k;
        }

        // Un resumen o una fusion por vez y con su propio cupo: comparte la hebra de voz, asi nunca
        // hay dos llamadas al LLM en vuelo.
        static void Resumenes()
        {
            if (!Estado.Salud.Disponible || !Estado.Resumen.TryAcquire()) return;
            try
            {
                string id = Estado.Mem.SiguienteParaResumir();
                if (id != null)
                {
                    var q = Estado.Mem.PreparaResumen(id);
                    if (q != null)
                    {
                        string s = Llm.Ask(Prompts.SistemaResumen, Prompts.UsuarioResumen(Estado.Ids.Nombre(id), q.Previo, q.Textos), 120);
                        Estado.Mem.AplicaResumen(q, s);
                        if (!string.IsNullOrEmpty(s)) Estado.Ev.Ok("memoria_resumen_llm", id); else Estado.Ev.Fail("memoria_resumen_llm", Llm.UltimoError);
                    }
                    return;
                }
                id = Estado.Mem.SiguienteParaFusionar();
                if (id != null)
                {
                    string s = Llm.Ask(Prompts.SistemaResumen, Prompts.UsuarioResumen(Estado.Ids.Nombre(id), "", new List<string> { Estado.Mem.TextoParaFusion(id) }), 160);
                    Estado.Mem.AplicaFusion(id, s);
                }
            }
            finally { Estado.Resumen.Release(); }
        }

        // Una linea por evento. "valor" es la opinion leida en el instante del hook; no se
        // afirma que sea la de antes ni la de despues del cambio (informe §6.18).
        static void Registra(string a, string b, string porQue, float delta, float? valor,
                             string frase, string pensamiento, bool intentoVoz, string actitud, double ajuste)
        {
            var sb = new StringBuilder();
            sb.Append("{\"t\":\"").Append(DateTime.Now.ToString("s")).Append("\",\"a\":\"").Append(Json.Escape(a)).Append("\",\"b\":\"").Append(Json.Escape(b))
              .Append("\",\"delta\":").Append(Json.Num(delta)).Append(",\"valor\":").Append(valor.HasValue ? Json.Num(valor.Value) : "null")
              .Append(",\"motivo_juego\":\"").Append(Json.Escape(porQue)).Append("\",\"dice\":\"")
              .Append(Json.Escape(frase ?? (intentoVoz ? "(sin frase)" : ""))).Append("\",\"piensa\":\"").Append(Json.Escape(pensamiento ?? ""))
              .Append("\",\"actitud\":\"").Append(Json.Escape(actitud ?? "")).Append("\",\"ajuste\":").Append(Json.Num(ajuste)).Append('}');
            try { Estado.Disco.Append("opiniones.jsonl", sb.ToString()); }
            catch (System.IO.IOException e) { UnityEngine.Debug.Log("[Pecera] opiniones.jsonl: " + e.Message); }

            int r = Interlocked.Increment(ref Registrados);
            if (intentoVoz)
                UnityEngine.Debug.Log("[Pecera] " + a + " -> " + b + " (" + Json.Num(delta) + ") " + porQue + " | " + (frase ?? "") + "  [n=" + r + "]");
            if (string.IsNullOrEmpty(frase)) return;
            string cab = a + (delta > 0 ? " + " : " - ") + b + "   (" + Json.Num(delta) + ")  " + porQue;
            Pantalla.Anota(cab + "\n  \"" + frase + "\"", delta > 0 ? new Color(0.62f, 0.93f, 0.66f) : new Color(0.98f, 0.62f, 0.60f));
        }

        static void Encola(Action a)
        {
            // Cola acotada: si estuviera llena se libera el cupo de voz y se pierde la frase.
            if (!EncolaBruto(a)) Estado.Voz.Release();
        }

        static bool EncolaBruto(Action a)
        {
            lock (typeof(Gancho))
            {
                if (!HiloArrancado)
                {
                    HiloArrancado = true;
                    var t = new Thread(Trabajador);
                    t.IsBackground = true;
                    t.Start();
                }
            }
            return Cola.TryAdd(a);
        }

        static void Trabajador()
        {
            foreach (var a in Cola.GetConsumingEnumerable())
            {
                try { a(); }
                catch (Exception e)
                {
                    // La hebra de voz no puede morir por una excepcion, pero el fallo queda contado.
                    Estado.Ev.Fail("voz_frase", "excepcion en la hebra de voz: " + e.GetType().Name + ": " + e.Message);
                    UnityEngine.Debug.Log("[Pecera] hebra de voz: " + e);
                }
            }
        }

        // ---------------- lectura de datos del juego (hilo del juego) ----------------
        public static string Nombre(Pawn p)
        {
            if (p == null) return "?";
            string s = Seguro(delegate { return p.GetShortName(); });
            if (string.IsNullOrEmpty(s)) s = Seguro(delegate { return p.GetName(); });
            if (string.IsNullOrEmpty(s)) s = Seguro(delegate { return p.GetFullName(); });
            if (string.IsNullOrEmpty(s)) s = Seguro(delegate { return p.GetLogName(); });
            if (string.IsNullOrEmpty(s)) s = Seguro(delegate { return p.ToString(); });
            return string.IsNullOrEmpty(s) ? "pawn#" + p.GetHashCode() : s;
        }

        // Los getters del juego pueden lanzar si el pawn esta a medio construir o destruido:
        // se prueba el siguiente nombre. Es lectura pura, no hay estado que corromper.
        static string Seguro(Func<string> f)
        {
            try { return f(); }
            catch (Exception e) { UnityEngine.Debug.Log("[Pecera] nombre: " + e.GetType().Name); return null; }
        }

        static Pawn PawnDe(object subject)
        {
            if (subject == null) return null;
            var p = subject as Pawn;
            if (p != null) return p;
            try
            {
                var t = subject.GetType();
                var pr = t.GetProperty("pawn", Aux.Todos);
                if (pr != null) { var pp = pr.GetValue(subject, null) as Pawn; if (pp != null) return pp; }
                for (; t != null; t = t.BaseType)
                    foreach (var f in t.GetFields(Aux.Todos | BindingFlags.DeclaredOnly))
                    {
                        if (!typeof(Pawn).IsAssignableFrom(f.FieldType)) continue;
                        var v = f.GetValue(subject) as Pawn;
                        if (v != null) return v;
                    }
            }
            catch (TargetInvocationException) { }
            catch (ArgumentException) { }
            return null;
        }

        static string NombreDe(object subject)
        {
            if (subject == null) return "alguien";
            try { var s = subject.ToString(); if (!string.IsNullOrEmpty(s)) return s; }
            catch (Exception) { }
            return subject.GetType().Name;
        }

        // FeelingReason es un struct con provider / info / text; ToString() ya es legible.
        static string Razon(object reason)
        {
            if (reason == null) return "desconocido";
            var partes = new List<string>();
            try { var s = reason.ToString(); if (!string.IsNullOrEmpty(s)) partes.Add(s); }
            catch (Exception) { return "?"; }
            try
            {
                for (var t = reason.GetType(); t != null; t = t.BaseType)
                    foreach (var f in t.GetFields(Aux.Todos | BindingFlags.DeclaredOnly))
                    {
                        if (!f.Name.EndsWith("k__BackingField", StringComparison.Ordinal)) continue;
                        var v = f.GetValue(reason);
                        if (v == null || v.GetType().IsPrimitive) continue;
                        var s = v.ToString();
                        if (string.IsNullOrEmpty(s) || s.Length > 80 || s == v.GetType().Name || s == v.GetType().FullName) continue;
                        if (!partes.Contains(s)) partes.Add(s);
                    }
            }
            catch (TargetInvocationException) { }
            return partes.Count == 0 ? "?" : string.Join(" | ", partes.ToArray());
        }

        static MethodInfo _mOpinion;
        static bool _mOpinionBuscado;

        static float? Opinion(Pawn pawn, object of)
        {
            if (pawn == null || of == null) return null;
            try
            {
                if (!_mOpinionBuscado)
                {
                    _mOpinionBuscado = true;
                    _mOpinion = typeof(Pawn).GetMethod("GetOpinionValue", Aux.Todos, null, new[] { typeof(ISubjectOrCompound), typeof(bool) }, null);
                }
                var subj = of as ISubjectOrCompound;
                if (subj == null || _mOpinion == null) return null;
                return Convert.ToSingle(_mOpinion.Invoke(pawn, new object[] { subj, false }), CultureInfo.InvariantCulture);
            }
            catch (TargetInvocationException) { return null; }
            catch (InvalidCastException) { return null; }
        }
    }

    public static class Aux
    {
        public const BindingFlags Todos = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    }
}
