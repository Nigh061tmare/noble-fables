using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace PeceraNF
{
    // =========================================================================
    //  EL GANCHO
    // =========================================================================
    //  PawnManager.OpinionDelta(Pawn pawn, ISubject of, float delta,
    //                            FeelingReason reason)
    //
    //  En el binario ese metodo NO hace nada mas que lanzar el delegado
    //  PawnOpinionDeltaCallback. Parcharlo es 100% seguro: no alteramos la
    //  simulacion, solo la observamos en el instante exacto en que alguien
    //  cambia de opinion, de CUANTO y POR QUE.
    //
    //  Reparto del trabajo:
    //    - hilo del juego (Postfix): solo filtra y recoge datos baratos.
    //    - ThreadPool: escribe la linea de datos (nunca espera al LLM).
    //    - hilo de voz (uno solo): la UNICA parte que habla con el LLM.
    // =========================================================================
    public static class Gancho
    {
        // Umbrales por defecto; config.txt puede cambiarlos sin recompilar.
        public static float UmbralDato = 0.5f;    // se registra en opiniones.jsonl
        public static float UmbralHabla = 1.0f;   // un hecho concreto ("ha matado")
        public static float UmbralRasgo = 2.0f;   // un rasgo ("es Orco"): ruido, solo si es fuerte
        public static int GapHablaSegundos = 25;  // una voz cada N s, global

        // Influencia: la actitud que elige el personaje empuja su opinion en el juego.
        public static bool Influencia = true;
        public static float InfluenciaMax = 0.25f;     // tope del empujon por frase
        const int PAREJA_ENFRIAR_SEGUNDOS = 300;       // mismo par: un empujon cada 5 min
        [ThreadStatic] static bool Propio;             // evita reentrar en nuestro propio empujon
        static readonly Dictionary<string, long> ParejaUltima = new Dictionary<string, long>();
        static readonly Dictionary<string, List<string>> Memoria = new Dictionary<string, List<string>>();
        const int FRENO_SEGUNDOS = 20;            // anti-rafaga por pawn

        static long UltimaVoz = 0;
        static int Hablando = 0;                  // 1 mientras una frase esta en vuelo
        static int Llamadas = 0;
        static int Registrados = 0;

        public static int LlamadasTotal { get { return Llamadas; } }
        static readonly Dictionary<int, long> Ultimo = new Dictionary<int, long>();
        static readonly object CerrojoVoz = new object();
        static readonly object CerrojoDisco = new object();
        static readonly Encoding Cfg = new UTF8Encoding(false);

        // Una sola hebra de voz.
        static readonly System.Collections.Concurrent.BlockingCollection<Action> Cola
            = new System.Collections.Concurrent.BlockingCollection<Action>(4);
        static bool HiloArrancado = false;

        [HarmonyPatch(typeof(PawnManager), "OpinionDelta")]
        static class AlCambiarLaOpinion
        {
            static void Postfix(object[] __args)
            {
                try
                {
                    Procesa(__args[0] as Pawn,
                            __args[1],
                            Convert.ToSingle(__args[2], CultureInfo.InvariantCulture),
                            __args[3]);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.Log("[Pecera] fallo: " + e.Message);
                }
            }
        }

        static void Procesa(Pawn pawn, object of, float delta, object reason)
        {
            if (!Plugin.Encendido || Propio) return;

            int n = Interlocked.Increment(ref Llamadas);
            if (n == 1 || n % 2000 == 0)
                UnityEngine.Debug.Log("[Pecera] OpinionDelta llamadas=" + n);

            float abs = Math.Abs(delta);
            if (abs < UmbralDato) return;

            int h = pawn != null ? pawn.GetHashCode() : 0;
            long ahora = DateTime.Now.Ticks;
            lock (Ultimo)
            {
                long prev;
                if (Ultimo.TryGetValue(h, out prev) &&
                    ahora - prev < TimeSpan.TicksPerSecond * FRENO_SEGUNDOS)
                    return;
                Ultimo[h] = ahora;
                if (Ultimo.Count > 4000) Ultimo.Clear();   // memoria acotada
            }

            // Se lee en el hilo del juego: es el unico sitio seguro para tocar
            // objetos del juego. Nada de esto sale del hilo con referencias.
            float? valor = Opinion(pawn, of);
            string a = Nombre(pawn);
            string b = NombreDe(of);
            string porQue = Razon(reason);

            float minimo = EsRasgo(porQue) ? UmbralRasgo : UmbralHabla;
            bool habla = abs >= minimo && TocaVoz();

            // Los recuerdos se leen ANTES de anotar el hecho actual.
            string recuerdos = Recuerdos(a);
            if (abs >= UmbralHabla && !EsRasgo(porQue))
                Recuerda(a, (delta > 0 ? "empezo a apreciar a " : "empezo a desconfiar de ") + b +
                            " (" + porQue + ")");

            if (!habla)
            {
                // Dato sin voz: nunca espera detras del LLM.
                ThreadPool.QueueUserWorkItem(_ => Registra(a, b, porQue, delta, valor, null, null, false, null, 0f));
                return;
            }

            Encola(() =>
            {
                try { Redacta(pawn, of, reason, a, b, porQue, delta, valor, recuerdos); }
                finally { Interlocked.Exchange(ref Hablando, 0); }
            });
        }

        // Cupo de voz: una cada GapHablaSegundos y nunca dos en vuelo.
        static bool TocaVoz()
        {
            lock (CerrojoVoz)
            {
                if (Hablando != 0) return false;
                long t = DateTime.Now.Ticks;
                if (t - UltimaVoz < TimeSpan.TicksPerSecond * GapHablaSegundos) return false;
                UltimaVoz = t;
                Hablando = 1;
                return true;
            }
        }

        // "X es Orco", "Y esta Malo": opiniones por rasgo o raza, no por un hecho.
        // Se detectan porque no llevan verbo de accion ("ha matado", "ha desertado").
        static bool EsRasgo(string porQue)
        {
            if (string.IsNullOrEmpty(porQue)) return true;
            string p = " " + porQue.ToLowerInvariant() + " ";
            if (p.Contains(" ha ") || p.Contains(" han ") || p.Contains(" ha sido ")) return false;
            return p.Contains(" es ") || p.Contains(" son ") ||
                   p.Contains(" está ") || p.Contains(" esta ");
        }

        // ------------------------------------------------------------------
        static void Redacta(Pawn pawn, object of, object reason, string a, string b, string porQue,
                            float delta, float? valor, string recuerdos)
        {
            string frase = null, pensamiento = null, actitud = null;
            float ajuste = 0f;
            try
            {
                string det = valor.HasValue
                    ? "Su opinion actual sobre el otro es " + F(valor.Value) + " (negativo = desagrado, positivo = aprecio; la escala no tiene tope)."
                    : "";
                string mem = string.IsNullOrEmpty(recuerdos)
                    ? "" : " Recuerdos recientes de " + a + ": " + recuerdos + ".";
                string dir = Directriz();
                string ord = string.IsNullOrEmpty(dir)
                    ? "" : " El jugador, que guia el destino del reino, pide: " + dir + ".";

                var sys = "Eres el narrador de un reino medieval de los siglos XIII al XV. " +
                          "Escribes UNA replica breve que un noble pronuncia al descubrir " +
                          "que alguien le cae bien o mal. Responde en espanol, UNA o DOS " +
                          "frases, lenguaje de epoca ('don', 'dona', 'senor', 'vosotros'), " +
                          "sin markdown, sin insultos familiares, sin palabras modernas. " +
                          "Si el cambio es positivo, el noble se alegra; si es negativo, " +
                          "se enfada o se cierra en banda. NO controles al otro personaje. " +
                          "'texto' es lo que dice en voz alta, 'piensa' es UNA sola frase " +
                          "corta de pensamiento interior y 'actitud' es EXACTAMENTE una de: " +
                          "empeora (se aferra al rencor o la admiracion), mantiene, " +
                          "perdona (se calma y suaviza el sentimiento). " +
                          "Nada de listas ni explicaciones. " +
                          "Responde SOLO el JSON {\"texto\":\"...\",\"piensa\":\"...\",\"actitud\":\"...\"}.";

                var user = "Cambio de opinion: " + a + " empieza a " +
                           (delta > 0 ? "RESPETAR Y ADMIRAR" : "DESCONFIAR Y ODIAR") +
                           " a " + b + ". Motivo: " + porQue + ". " + det + mem + ord;

                var txt = Llm.Ask(sys, user, 220);
                if (!string.IsNullOrEmpty(txt))
                {
                    var obj = Llm.Aislar(txt);
                    frase = Llm.Campo(obj, "texto");
                    pensamiento = Llm.Campo(obj, "piensa");
                    actitud = Llm.Campo(obj, "actitud").ToLowerInvariant();
                }
                else if (Llm.UltimoError.Length > 0)
                {
                    UnityEngine.Debug.Log("[Pecera] LLM fallo: " + Llm.UltimoError);
                }
            }
            catch { }

            bool habla = !string.IsNullOrEmpty(frase);
            if (habla) ajuste = Aplica(pawn, of, reason, a, b, delta, actitud);

            Registra(a, b, porQue, delta, valor, frase, pensamiento, true, actitud, ajuste);

            // Bocadillo sobre la cabeza del que habla, visible en el mundo.
            if (habla) Pantalla.Burbuja(pawn, a, frase, delta > 0);
        }

        // Empuja la opinion en el juego segun la actitud elegida. Devuelve el
        // ajuste pedido (0 si no hubo). La llamada real se hace en el hilo del juego.
        static float Aplica(Pawn pawn, object of, object reason, string a, string b,
                            float delta, string actitud)
        {
            if (!Influencia || pawn == null || of == null || reason == null || actitud == null) return 0f;
            int signo = 0;
            if (actitud.StartsWith("empeor")) signo = Math.Sign(delta);
            else if (actitud.StartsWith("perdon")) signo = -Math.Sign(delta);
            if (signo == 0) return 0f;

            string par = a + "|" + b;
            long ahora = DateTime.Now.Ticks;
            lock (ParejaUltima)
            {
                long prev;
                if (ParejaUltima.TryGetValue(par, out prev) &&
                    ahora - prev < TimeSpan.TicksPerSecond * PAREJA_ENFRIAR_SEGUNDOS)
                    return 0f;
                ParejaUltima[par] = ahora;
                if (ParejaUltima.Count > 2000) ParejaUltima.Clear();
            }

            var subj = of as ISubject;
            if (subj == null || !(reason is FeelingReason)) return 0f;
            float k = signo * Math.Min(InfluenciaMax, Math.Abs(delta) * 0.25f);
            var motivo = (FeelingReason)reason;

            Principal.Encola(() =>
            {
                Propio = true;
                try { pawn.DeltaOpinionOfSubject(subj, k, motivo, FeelingMemoryFlags.None); }
                catch (Exception e) { UnityEngine.Debug.Log("[Pecera] influencia fallo: " + e.Message); }
                finally { Propio = false; }
            });
            return k;
        }

        // Ultimos hechos de cada personaje: da continuidad a lo que dice.
        static string Recuerdos(string quien)
        {
            lock (Memoria)
            {
                List<string> l;
                return Memoria.TryGetValue(quien, out l) ? string.Join("; ", l.ToArray()) : "";
            }
        }

        static void Recuerda(string quien, string hecho)
        {
            lock (Memoria)
            {
                if (Memoria.Count > 600) Memoria.Clear();
                List<string> l;
                if (!Memoria.TryGetValue(quien, out l)) { l = new List<string>(); Memoria[quien] = l; }
                l.Add(Limpia(hecho));
                while (l.Count > 3) l.RemoveAt(0);
            }
        }

        // directriz.txt: lo que TU quieres que pase en el reino. Se relee solo
        // cuando cambia el fichero. Las lineas que empiezan por # son comentarios.
        static string _dir = "";
        static DateTime _dirT = DateTime.MinValue;

        static string Directriz()
        {
            try
            {
                var r = Path.Combine(Plugin.Datos, "directriz.txt");
                if (!File.Exists(r)) return "";
                var t = File.GetLastWriteTimeUtc(r);
                if (t == _dirT) return _dir;
                _dirT = t;
                var sb = new StringBuilder();
                foreach (var l in File.ReadAllLines(r, Cfg))
                {
                    var x = l.Trim();
                    if (x.Length == 0 || x.StartsWith("#")) continue;
                    sb.Append(x).Append(' ');
                }
                _dir = Limpia(sb.ToString());
                if (_dir.Length > 400) _dir = _dir.Substring(0, 400);
                return _dir;
            }
            catch { return ""; }
        }

        // Una linea por evento. Esto es el dato, la frase es el adorno.
        // "valor" es la opinion leida en el instante del hook. No se afirma que
        // sea la de antes o la de despues del cambio: no esta verificado.
        static void Registra(string a, string b, string porQue, float delta, float? valor,
                             string frase, string pensamiento, bool intentoVoz,
                             string actitud, float ajuste)
        {
            var linea = string.Format(CultureInfo.InvariantCulture,
                "{{\"t\":\"{0}\",\"a\":\"{1}\",\"b\":\"{2}\",\"delta\":{3}," +
                "\"valor\":{4},\"motivo_juego\":\"{5}\"," +
                "\"dice\":\"{6}\",\"piensa\":\"{7}\",\"actitud\":\"{8}\",\"ajuste\":{9}}}",
                DateTime.Now.ToString("s"), Limpia(a), Limpia(b),
                FN(delta), FN(valor),
                Limpia(porQue),
                Limpia(frase ?? (intentoVoz ? "(sin frase)" : "")),
                Limpia(pensamiento ?? ""),
                Limpia(actitud ?? ""), FN(ajuste));

            try { Escribe("opiniones.jsonl", linea); } catch { }

            int r = Interlocked.Increment(ref Registrados);
            if (intentoVoz)
                UnityEngine.Debug.Log("[Pecera] " + Limpia(a) + " -> " + Limpia(b) +
                    " (" + FN(delta) + ") " + Limpia(porQue) + " | " + Limpia(frase ?? "") +
                    "  [n=" + r + "]");

            // Al panel solo va lo que habla: es lo que el jugador quiere leer.
            if (string.IsNullOrEmpty(frase)) return;
            try
            {
                string cabecera = Limpia(a) + (delta > 0 ? " + " : " - ") + Limpia(b) +
                                  "   (" + FN(delta) + ")  " + Limpia(porQue);
                string cita = "  \"" + Limpia(frase) + "\"";
                Color tono = delta > 0
                    ? new Color(0.62f, 0.93f, 0.66f)
                    : new Color(0.98f, 0.62f, 0.60f);
                Pantalla.Anota(cabecera + "\n" + cita, tono);
            }
            catch { }
        }

        // Escribe sin BOM, serializado, y rota el fichero al pasar de 5 MB
        // (antes crecia sin limite).
        static void Escribe(string archivo, string linea)
        {
            lock (CerrojoDisco)
            {
                var ruta = Path.Combine(Plugin.Datos, archivo);
                var info = new FileInfo(ruta);
                if (info.Exists && info.Length > 5 * 1024 * 1024)
                {
                    var viejo = ruta + ".1";
                    if (File.Exists(viejo)) File.Delete(viejo);
                    File.Move(ruta, viejo);
                }
                File.AppendAllText(ruta, linea + "\n", Cfg);
            }
        }

        static void Encola(Action a)
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
            // Cola acotada: si estuviera llena se libera el cupo de voz y se pierde.
            if (!Cola.TryAdd(a)) Interlocked.Exchange(ref Hablando, 0);
        }

        static void Trabajador()
        {
            foreach (var a in Cola.GetConsumingEnumerable())
            {
                try { a(); } catch { }
            }
        }

        // ------------------------------------------------------------------
        static float F(float v)
        {
            return (float)Math.Round(v, 3, MidpointRounding.AwayFromZero);
        }

        // float -> texto JSON valido
        static string FN(float v)
        {
            return F(v).ToString("0.###", CultureInfo.InvariantCulture);
        }

        // float? -> texto JSON valido ("null" si no hay medida)
        static string FN(float? v)
        {
            return v.HasValue ? FN(v.Value) : "null";
        }

        // Nombre real del pawn (Pawn.character es una PROPIEDAD, de ahi que la
        // busqueda por campo devolviera null).
        static string Nombre(Pawn p)
        {
            if (p == null) return "?";
            string s = null;
            try { s = p.GetShortName(); } catch { }
            if (string.IsNullOrEmpty(s)) { try { s = p.GetName(); } catch { } }
            if (string.IsNullOrEmpty(s)) { try { s = p.GetFullName(); } catch { } }
            if (string.IsNullOrEmpty(s)) { try { s = p.GetLogName(); } catch { } }
            if (string.IsNullOrEmpty(s)) { try { s = p.ToString(); } catch { } }
            if (string.IsNullOrEmpty(s)) return "pawn#" + p.GetHashCode();
            return s;
        }

        static string NombreDe(object subject)
        {
            if (subject == null) return "alguien";
            try
            {
                var p = subject as Pawn;
                if (p != null) return Nombre(p);

                var t = subject.GetType();
                var pr = t.GetProperty("pawn", Aux.TODO);
                if (pr != null)
                {
                    var pp = pr.GetValue(subject, null) as Pawn;
                    if (pp != null) return Nombre(pp);
                }
                for (; t != null; t = t.BaseType)
                    foreach (var f in t.GetFields(Aux.TODO | BindingFlags.DeclaredOnly))
                    {
                        if (!typeof(Pawn).IsAssignableFrom(f.FieldType)) continue;
                        var v = f.GetValue(subject) as Pawn;
                        if (v != null) return Nombre(v);
                    }
                try
                {
                    var s = subject.ToString();
                    if (!string.IsNullOrEmpty(s)) return s;
                }
                catch { }
            }
            catch { }
            return subject.GetType().Name;
        }

        // FeelingReason es un struct con provider / info / text. ToString() ya
        // devuelve algo legible ("Sovereign Chico esta Malo").
        static string Razon(object reason)
        {
            if (reason == null) return "desconocido";
            var partes = new List<string>();
            try { var s = reason.ToString(); if (!string.IsNullOrEmpty(s)) partes.Add(s); }
            catch { return "?"; }
            try
            {
                for (var t = reason.GetType(); t != null; t = t.BaseType)
                {
                    foreach (var f in t.GetFields(Aux.TODO | BindingFlags.DeclaredOnly))
                    {
                        if (!f.Name.EndsWith("k__BackingField")) continue;
                        var v = f.GetValue(reason);
                        if (v == null) continue;
                        if (v.GetType().IsPrimitive) continue;
                        var s = v.ToString();
                        if (string.IsNullOrEmpty(s) || s.Length > 80) continue;
                        if (s == v.GetType().Name || s == v.GetType().FullName) continue;
                        bool ya = false;
                        foreach (var q in partes) if (q == s) { ya = true; break; }
                        if (!ya) partes.Add(s);
                    }
                }
            }
            catch { }
            return partes.Count == 0 ? "?" : string.Join(" | ", partes.ToArray());
        }

        // GetOpinionValue vive en Pawn. Se busca una vez y se cachea: antes se
        // recorria la jerarquia con reflexion en cada evento, en el hilo del juego.
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
                    _mOpinion = typeof(Pawn).GetMethod("GetOpinionValue", Aux.TODO, null,
                        new[] { typeof(ISubjectOrCompound), typeof(bool) }, null);
                }
                var subj = of as ISubjectOrCompound;
                if (subj == null || _mOpinion == null) return null;
                return Convert.ToSingle(_mOpinion.Invoke(pawn, new object[] { subj, false }),
                                        CultureInfo.InvariantCulture);
            }
            catch { }
            return null;
        }

        public static string Limpia(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
                sb.Append(c == '\r' || c == '\n' || c == '\t' || c == '"' || c == '\\' ? ' ' : c);
            return sb.ToString().Trim();
        }
    }

    // =========================================================================
    //  Auxiliar
    // =========================================================================
    public static class Aux
    {
        public const BindingFlags TODO = BindingFlags.Instance | BindingFlags.Static |
                                         BindingFlags.Public | BindingFlags.NonPublic;
    }
}
