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
            l.Add(E("modelo", "qwen4b-silly:latest", "Modelo de Ollama. Medidos: qwen4b-silly / qwen9b-silly / mimo-9b:q4 / mistral-nemo.", false, 0, 0, false, Estado.Verificado));
            l.Add(E("keep_alive", "15m", "Cuanto sigue el modelo en VRAM tras la ultima peticion.", false, 0, 0, false, Estado.Verificado));
            l.Add(E("umbral_dato", "0.5", "|cambio| minimo para registrar el evento en opiniones.jsonl.", true, 0.05, 10, false, Estado.Verificado));
            l.Add(E("umbral_habla", "1", "|cambio| minimo para hablar por un HECHO (ha matado, ha desertado).", true, 0.05, 50, false, Estado.Verificado));
            l.Add(E("umbral_rasgo", "2", "|cambio| minimo para hablar por un RASGO (es Orco, esta Malo): ruido.", true, 0.05, 50, false, Estado.Verificado));
            l.Add(E("gap_voz", "25", "Segundos minimos entre dos frases (global). Minimo 5.", true, 5, 3600, false, Estado.Verificado));
            l.Add(E("fps_log", "0", "1 escribe fps.csv cada 5 s (solo diagnostico).", true, 0, 1, false, Estado.Verificado));
            // --- escrituras al juego: SIN verificar -> apagadas por defecto ---
            l.Add(E("influencia", "0", "1: la actitud de la frase empuja la opinion en el juego (ESCRIBE). Sin verificar en partida.", true, 0, 1, true, Estado.NoVerificado));
            l.Add(E("influencia_max", "0.25", "Tope del empujon por frase.", true, 0, 1, true, Estado.NoVerificado));
            l.Add(E("influencia_pareja_s", "300", "Segundos de enfriamiento entre empujones de la misma pareja.", true, 30, 86400, true, Estado.NoVerificado));
            l.Add(E("influencia_hora_max", "30", "Tope global de empujones por hora de reloj.", true, 0, 1000, true, Estado.NoVerificado));
            l.Add(E("burbujas", "1", "1: bocadillos sobre los pawns (solo dibuja, no escribe estado). Sin verificar en partida.", true, 0, 1, false, Estado.NoVerificado));
            // --- capas nuevas internas (no escriben en el juego) ---
            l.Add(E("telemetria", "1", "1: escribe verificacion.json con la evidencia de cada funcion.", true, 0, 1, false, Estado.NoVerificado));
            l.Add(E("memoria", "1", "1: memoria episodica por pawn en disco (memoria.jsonl).", true, 0, 1, false, Estado.NoVerificado));
            l.Add(E("memoria_recientes", "6", "Episodios recientes por pawn antes de resumir.", true, 3, 50, false, Estado.NoVerificado));
            l.Add(E("memoria_resumen", "1", "1: resume episodios viejos con el LLM (1 llamada, comparte cupo de voz).", true, 0, 1, false, Estado.NoVerificado));
            l.Add(E("afectos", "1", "1: modelo afectivo por pareja (afecto, confianza, rencor...). Solo interno.", true, 0, 1, false, Estado.NoVerificado));
            l.Add(E("rumores", "0", "1: secretos y rumores internos (usa el LLM para inventar secretos).", true, 0, 1, false, Estado.NoVerificado));
            l.Add(E("sociedad", "1", "1: facciones, lideres y favores calculados internamente (informe, sin escribir).", true, 0, 1, false, Estado.NoVerificado));
            l.Add(E("cronica", "1", "1: cronica del reino en cronica.md.", true, 0, 1, false, Estado.NoVerificado));
            // --- escrituras de gobierno / esquemas: apagadas ---
            l.Add(E("esquemas", "0", "1: dispara esquemas reales (SchemeManager). ESCRIBE. Requiere esquemas.txt.", true, 0, 1, true, Estado.NoVerificado));
            l.Add(E("esquemas_dia_max", "2", "Tope de esquemas disparados por dia de juego.", true, 0, 20, true, Estado.NoVerificado));
            l.Add(E("peticiones", "0", "1: resuelve peticiones sin el jugador. ESCRIBE.", true, 0, 1, true, Estado.NoVerificado));
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

        public string Str(string key)
        {
            string v;
            if (vals.TryGetValue(key, out v)) return v;
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

        public static string RenderDefault()
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
                sb.Append(e.Key).Append('=').Append(e.Default).Append("\n\n");
            }
            return sb.ToString();
        }
    }
}
