using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Pecera.Core
{
    public enum Estado { Verificado, Leido, NoVerificado }

    public sealed class ConfigEntry
    {
        public string Key;
        public string Default;
        public string Doc;
        public double Min = double.NegativeInfinity;
        public double Max = double.PositiveInfinity;
        public bool Numeric;
        // Escribe en el estado del juego? Si es true y Estado != Verificado el defecto debe ser 0.
        public bool EscribeJuego;
        public Estado Estado = Estado.NoVerificado;
    }

    // config.txt: pares clave=valor documentados. Un valor invalido no rompe nada:
    // se usa el defecto y se anota un aviso que la telemetria recoge.
    public sealed class PeceraConfig
    {
        static readonly List<ConfigEntry> schema = Build();
        static readonly Dictionary<string, ConfigEntry> byKey = Index();
        readonly Dictionary<string, string> vals = new Dictionary<string, string>();
        public readonly List<string> Avisos = new List<string>();

        public static IList<ConfigEntry> Schema { get { return schema; } }

        static ConfigEntry E(string k, string d, string doc, bool num, double min, double max, bool escribe, Estado e)
        {
            return new ConfigEntry { Key = k, Default = d, Doc = doc, Numeric = num, Min = min, Max = max, EscribeJuego = escribe, Estado = e };
        }

        static List<ConfigEntry> Build()
        {
            var l = new List<ConfigEntry>();
            // --- voz (verificado en partida: hook y frases) ---
            l.Add(E("config_version", "2", "Version del formato de este fichero. No lo cambies.", true, 1, 99, false, Estado.Verificado));
            l.Add(E("modelo", "qwen4b-silly:latest", "Modelo de Ollama. Medidos: qwen4b-silly / qwen9b-silly / mimo-9b:q4 / mistral-nemo.", false, 0, 0, false, Estado.Verificado));
            l.Add(E("keep_alive", "15m", "Cuanto sigue el modelo en VRAM tras la ultima peticion.", false, 0, 0, false, Estado.Verificado));
            l.Add(E("umbral_dato", "0.5", "|cambio| minimo para registrar el evento en opiniones.jsonl.", true, 0.05, 10, false, Estado.Verificado));
            l.Add(E("umbral_habla", "1", "|cambio| minimo para hablar por un HECHO (ha matado, ha desertado).", true, 0.05, 50, false, Estado.Verificado));
            l.Add(E("umbral_rasgo", "2", "|cambio| minimo para hablar por un RASGO (es Orco, esta Malo): ruido.", true, 0.05, 50, false, Estado.Verificado));
            l.Add(E("gap_voz", "25", "Segundos minimos entre dos frases (global). Minimo 5.", true, 5, 3600, false, Estado.Verificado));
            l.Add(E("fps_log", "0", "1 escribe fps.csv cada 5 s (solo diagnostico).", true, 0, 1, false, Estado.Verificado));
            l.Add(E("activo", "1", "0 deja el mod inerte (no parchea nada): para el A/B del coste ON/OFF. fps_log sigue funcionando.", true, 0, 1, false, Estado.NoVerificado));
            l.Add(E("dia_segundos", "120", "Segundos de reloj que equivalen a un dia de juego para el modelo afectivo (la API de tiempo del juego esta pendiente de confirmar).", true, 10, 86400, false, Estado.NoVerificado));
            l.Add(E("resumen_gap_s", "90", "Segundos minimos entre dos resumenes de memoria con el LLM.", true, 20, 3600, false, Estado.NoVerificado));
            l.Add(E("informe_min", "5", "Minutos entre escrituras de informe.md, cronica.md y grafo.dot.", true, 1, 240, false, Estado.NoVerificado));
            l.Add(E("confirmar_version", "", "Version del juego que el usuario acepta tras una actualizacion (desbloquea las escrituras).", false, 0, 0, false, Estado.NoVerificado));
            // --- escrituras al juego: SIN verificar -> apagadas por defecto ---
            l.Add(E("influencia", "0", "1: la actitud de la frase empuja la opinion en el juego (ESCRIBE). Sin verificar en partida.", true, 0, 1, true, Estado.NoVerificado));
            l.Add(E("influencia_max", "0.25", "Tope del empujon por frase.", true, 0, 1, true, Estado.NoVerificado));
            l.Add(E("influencia_pareja_s", "300", "Segundos de enfriamiento entre empujones de la misma pareja.", true, 30, 86400, true, Estado.NoVerificado));
            l.Add(E("influencia_hora_max", "30", "Tope global de empujones por hora de reloj.", true, 0, 1000, true, Estado.NoVerificado));
            l.Add(E("panel_inicio", "0", "1: el panel de registro aparece al cargar (F8 lo alterna). No escribe estado.", true, 0, 1, false, Estado.Verificado));
            l.Add(E("burbujas", "1", "1: bocadillos sobre los pawns (solo dibuja, no escribe estado). Sin verificar en partida.", true, 0, 1, false, Estado.NoVerificado));
            // --- capas nuevas internas (no escriben en el juego) ---
            l.Add(E("telemetria", "1", "1: escribe verificacion.json con la evidencia de cada funcion.", true, 0, 1, false, Estado.NoVerificado));
            l.Add(E("memoria", "1", "1: memoria episodica por pawn en disco (memoria.jsonl).", true, 0, 1, false, Estado.NoVerificado));
            l.Add(E("memoria_recientes", "6", "Episodios recientes por pawn antes de resumir.", true, 3, 50, false, Estado.NoVerificado));
            l.Add(E("memoria_resumen", "1", "1: resume episodios viejos con el LLM (1 llamada, comparte cupo de voz).", true, 0, 1, false, Estado.NoVerificado));
            l.Add(E("afectos", "1", "1: modelo afectivo por pareja (afecto, confianza, rencor...). Solo interno.", true, 0, 1, false, Estado.NoVerificado));
            l.Add(E("agentes", "0", "1: los pawns tienen ambiciones, necesidades y planes (Persona/Agenda). Usa el LLM en lote bajo presupuesto. NO escribe en el juego: solo muestra intenciones (informe, burbujas) y las da por cumplidas al VER que el juego produce el evento.", true, 0, 1, false, Estado.NoVerificado));
            l.Add(E("agentes_llm_dia", "12", "Llamadas maximas al LLM por dia de juego para planear (techos: planeacion 50 %, narrativa 20 %, conversacion 20 %). Con dia_segundos=120 son ~una cada 10 s: la voz ya usa una cada 25 s, ajusta a tu GPU.", true, 0, 500, false, Estado.NoVerificado));
            l.Add(E("agentes_lote", "6", "Pawns que se planean con UNA sola llamada al LLM.", true, 1, 20, false, Estado.NoVerificado));
            l.Add(E("agentes_pawns_tick", "40", "Pawns que se recalculan por dia de juego (reparto circular): acota el coste en el hilo principal.", true, 5, 1000, false, Estado.NoVerificado));
            l.Add(E("director_estilo", "clasico", "Director de drama (solo informa en agentes.md, no ejecuta nada): ninguno | calmo | clasico | caotico. Mide la tension del reino y sugiere conflicto si hay calma o alivio si hay demasiada tension.", false, 0, 0, false, Estado.NoVerificado));
            l.Add(E("agentes_burbujas", "1", "1: al adoptar un plan elegido por el LLM, el pawn lo dice en un bocadillo (max 1 por ronda). Solo dibuja.", true, 0, 1, false, Estado.NoVerificado));
            l.Add(E("rumores", "0", "1: secretos y rumores internos (usa el LLM para inventar secretos).", true, 0, 1, false, Estado.NoVerificado));
            l.Add(E("sociedad", "1", "1: facciones, lideres y favores calculados internamente (informe, sin escribir).", true, 0, 1, false, Estado.NoVerificado));
            l.Add(E("cronica", "1", "1: cronica del reino en cronica.md.", true, 0, 1, false, Estado.NoVerificado));
            // --- escrituras de gobierno / esquemas: apagadas ---
            l.Add(E("esquemas", "0", "1: dispara esquemas reales (SchemeManager). ESCRIBE. Requiere esquemas.txt.", true, 0, 1, true, Estado.NoVerificado));
            l.Add(E("esquemas_dia_max", "2", "Tope de esquemas disparados por dia de juego.", true, 0, 20, true, Estado.NoVerificado));
            l.Add(E("peticiones", "0", "1: el Rey dormido LEE la cola de peticiones y anota su decision interna. Para que ademas las aplique hace falta peticiones_aplicar=1.", true, 0, 1, true, Estado.NoVerificado));
            l.Add(E("peticiones_aplicar", "0", "1: ADEMAS de leer, aplica Receive()+Complete() a la peticion activa (ESCRIBE). Interruptor explicito: peticiones=1 por si solo solo OBSERVA. Sin verificar en partida: haz copia antes.", true, 0, 1, true, Estado.NoVerificado));
            l.Add(E("peticiones_dia_max", "3", "Tope de peticiones resueltas por dia de juego.", true, 0, 50, true, Estado.NoVerificado));
            l.Add(E("peticiones_veto_s", "30", "Segundos que el jugador tiene para vetar una decision antes de aplicarla.", true, 5, 3600, true, Estado.NoVerificado));
            l.Add(E("investigacion", "0", "1: elige investigacion sola. ESCRIBE.", true, 0, 1, true, Estado.NoVerificado));
            l.Add(E("planos", "0", "1: aprueba planos solo. ESCRIBE.", true, 0, 1, true, Estado.NoVerificado));
            l.Add(E("misiones", "0", "1: gestiona misiones/diplomacia solo. ESCRIBE.", true, 0, 1, true, Estado.NoVerificado));
            l.Add(E("modo", "asistente", "observador (kill-switch: NUNCA escribe) | asistente (escribe lo habilitado, con veto del jugador) | dios (sin veto, con topes).", false, 0, 0, true, Estado.NoVerificado));
            l.Add(E("datos_dir", "", "Carpeta de datos. Vacio = junto al DLL del plugin (pecera_datos).", false, 0, 0, false, Estado.NoVerificado));
            return l;
        }

        static Dictionary<string, ConfigEntry> Index()
        {
            var d = new Dictionary<string, ConfigEntry>();
            foreach (var e in schema) d[e.Key] = e;
            return d;
        }

        public static PeceraConfig Parse(string text)
        {
            var c = new PeceraConfig();
            if (text == null) text = "";
            foreach (string linea in text.Split('\n'))
            {
                string l = linea.Trim();
                if (l.Length == 0 || l[0] == '#') continue;
                int eq = l.IndexOf('=');
                if (eq <= 0) { c.Avisos.Add("linea sin '=': " + l); continue; }
                string k = l.Substring(0, eq).Trim().ToLowerInvariant();
                string v = l.Substring(eq + 1).Trim();
                ConfigEntry e;
                if (!byKey.TryGetValue(k, out e)) { c.Avisos.Add("clave desconocida: " + k); continue; }
                if (e.Numeric)
                {
                    double d;
                    if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) || d < e.Min || d > e.Max)
                    {
                        c.Avisos.Add("valor invalido para " + k + ": '" + v + "' (uso " + e.Default + ")");
                        continue;
                    }
                }
                c.vals[k] = v;
            }
            return c;
        }

        // Cambio en caliente (consola). Valida igual que Parse. No toca el fichero.
        public bool Set(string key, string valor, out string error)
        {
            error = "";
            ConfigEntry e;
            if (!byKey.TryGetValue(key, out e)) { error = "clave desconocida: " + key; return false; }
            if (e.Numeric)
            {
                double d;
                if (!double.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out d) || d < e.Min || d > e.Max)
                { error = "valor invalido para " + key + ": " + valor; return false; }
            }
            lock (vals) { vals[key] = valor; }
            return true;
        }

        public string Str(string key)
        {
            string v;
            lock (vals) { if (vals.TryGetValue(key, out v)) return v; }
            ConfigEntry e;
            if (!byKey.TryGetValue(key, out e)) throw new ArgumentException("clave no registrada: " + key);
            return e.Default;
        }

        public double Num(string key)
        {
            double d;
            if (!double.TryParse(Str(key), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new ArgumentException("clave no numerica: " + key);
            return d;
        }

        public int Int(string key) { return (int)Math.Round(Num(key)); }
        public bool Bool(string key) { return Num(key) >= 0.5; }

        // Modo efectivo: 'observador' anula TODA escritura al juego aunque el interruptor este a 1.
        public string Modo
        {
            get
            {
                string m = Str("modo").ToLowerInvariant();
                return m == "dios" || m == "observador" ? m : "asistente";
            }
        }

        // Unico punto por el que una funcion que escribe en el juego pregunta si puede.
        public bool PuedeEscribir(string interruptor)
        {
            ConfigEntry e;
            if (!byKey.TryGetValue(interruptor, out e) || !e.EscribeJuego)
                throw new ArgumentException("no es un interruptor de escritura: " + interruptor);
            if (Modo == "observador") return false;
            return Bool(interruptor);
        }

        public const int VersionActual = 2;

        // Version REAL de un config.txt: 1 si no trae la clave config_version. No se puede usar
        // Parse(texto).Int("config_version") porque Str() devuelve el DEFECTO del esquema (2) cuando la
        // clave falta, y un config antiguo parecia ya migrado (bug: influencia=1 sin verificar seguia activa).
        public static int VersionDe(string texto)
        {
            if (texto == null) return 1;
            foreach (string linea in texto.Split('\n'))
            {
                string l = linea.Trim();
                if (l.Length == 0 || l[0] == '#') continue;
                int eq = l.IndexOf('=');
                if (eq <= 0) continue;
                if (l.Substring(0, eq).Trim().ToLowerInvariant() != "config_version") continue;
                int v;
                return int.TryParse(l.Substring(eq + 1).Trim(), out v) ? v : 1;
            }
            return 1;
        }

        // Migracion de un config.txt de la version 1 (sin config_version): se conservan los
        // valores de las claves que NO escriben en el juego y se descartan las que si
        // (influencia venia a 1 por defecto, sin haberse verificado en partida: vuelve a 0).
        public static string Migra(string textoViejo, IList<string> cambios)
        {
            var viejo = Parse(textoViejo);
            var over = new Dictionary<string, string>();
            foreach (var e in schema)
            {
                if (e.Key == "config_version") continue;
                string v;
                if (!viejo.vals.TryGetValue(e.Key, out v)) continue;
                if (e.EscribeJuego && v != e.Default)
                {
                    if (cambios != null) cambios.Add(e.Key + "=" + v + " -> " + e.Default + " (escribe en el juego y no esta verificado)");
                    continue;
                }
                over[e.Key] = v;
            }
            return Render(over);
        }

        public static string RenderDefault() { return Render(null); }

        public static string Render(Dictionary<string, string> over)
        {
            var sb = new StringBuilder();
            sb.Append("# Ajustes de Pecera. Edita y reinicia el juego.\n");
            sb.Append("# Las funciones que ESCRIBEN en el estado del juego y no estan verificadas en partida\n");
            sb.Append("# vienen apagadas (=0). Activalas de una en una, con copia de la partida hecha.\n\n");
            foreach (var e in schema)
            {
                sb.Append("# ").Append(e.Doc);
                sb.Append(" [").Append(e.Estado == Estado.Verificado ? "VERIFICADO" : e.Estado == Estado.Leido ? "LEIDO" : "NO VERIFICADO");
                if (e.EscribeJuego) sb.Append(", escribe en el juego");
                sb.Append("]\n");
                string v;
                if (over == null || e.Key == "config_version" || !over.TryGetValue(e.Key, out v)) v = e.Default;
                sb.Append(e.Key).Append('=').Append(v).Append("\n\n");
            }
            return sb.ToString();
        }
    }
}
