using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    // ---- Cliente de Ollama: cuerpos y lectura de respuesta (sin red) ----
    public static class OllamaWire
    {
        // API NATIVA /api/chat: respeta keep_alive y num_ctx (informe §6.4). Nunca /v1 como primera opcion.
        public static string BodyNativo(string modelo, string keepAlive, int numCtx, int maxTokens, string sistema, string usuario)
        {
            return "{\"model\":\"" + Json.Escape(modelo) + "\",\"stream\":false,\"think\":false,"
                 + "\"keep_alive\":\"" + Json.Escape(keepAlive) + "\","
                 + "\"options\":{\"num_ctx\":" + numCtx + ",\"num_predict\":" + maxTokens + "},"
                 + "\"messages\":[{\"role\":\"system\",\"content\":\"" + Json.Escape(sistema) + "\"},"
                 + "{\"role\":\"user\",\"content\":\"" + Json.Escape(usuario) + "\"}]}";
        }

        // Red de seguridad por el proxy /v1: aqui SI hace falta reasoning_effort:none.
        public static string BodyProxy(string modelo, string keepAlive, int maxTokens, string sistema, string usuario)
        {
            return "{\"model\":\"" + Json.Escape(modelo) + "\",\"reasoning_effort\":\"none\",\"think\":false,\"stream\":false,"
                 + "\"keep_alive\":\"" + Json.Escape(keepAlive) + "\",\"temperature\":0.85,\"max_tokens\":" + maxTokens + ","
                 + "\"messages\":[{\"role\":\"system\",\"content\":\"" + Json.Escape(sistema) + "\"},"
                 + "{\"role\":\"user\",\"content\":\"" + Json.Escape(usuario) + "\"}]}";
        }

        // {"message":{"content":..}} (nativa) o {"choices":[{"message":{"content":..}}]} (/v1). null si vacio.
        public static string Contenido(string raw)
        {
            object v;
            if (!Json.TryParse(raw, out v)) return null;
            string c = Dentro(v, "message", "content");
            if (string.IsNullOrEmpty(c))
            {
                var ch = Json.Lista(v, "choices");
                if (ch != null && ch.Count > 0) c = Dentro(ch[0], "message", "content");
            }
            c = (c ?? "").Trim();
            return c.Length == 0 ? null : c;
        }

        static string Dentro(object o, string a, string b)
        {
            var d = o as Dictionary<string, object>;
            object x;
            if (d == null || !d.TryGetValue(a, out x)) return "";
            return Json.Str(x, b);
        }
    }

    // ---- Respuesta estructurada de la voz ----
    public sealed class Replica
    {
        public string Texto = "", Piensa = "", Actitud = "";
        public bool Ok { get { return Texto.Length > 0; } }
        public string Metodo = "";   // json | reparado | suelto | vacio
    }

    public static class ReplicaParser
    {
        public static Replica Parse(string raw)
        {
            var r = new Replica();
            if (string.IsNullOrEmpty(raw)) { r.Metodo = "vacio"; return r; }
            string aislado = Json.Aislar(raw);
            object v;
            if (aislado.Length > 0 && Json.TryParse(aislado, out v) && v is Dictionary<string, object>)
            {
                Rellena(r, v); r.Metodo = "json";
            }
            else
            {
                var d = Json.ParseObjeto(raw);
                if (d != null) { Rellena(r, d); r.Metodo = "reparado"; }
                else
                {
                    // Ultimo recurso: comillas internas sin escapar o JSON roto.
                    r.Texto = Json.UnaLinea(Suelto(aislado.Length > 0 ? aislado : raw, "texto"));
                    r.Piensa = Json.UnaLinea(Suelto(aislado.Length > 0 ? aislado : raw, "piensa"));
                    r.Actitud = Json.UnaLinea(Suelto(aislado.Length > 0 ? aislado : raw, "actitud")).ToLowerInvariant();
                    r.Metodo = r.Texto.Length > 0 ? "suelto" : "vacio";
                }
            }
            r.Actitud = NormalizaActitud(r.Actitud);
            return r;
        }

        static void Rellena(Replica r, object d)
        {
            r.Texto = Json.UnaLinea(Json.Str(d, "texto"));
            r.Piensa = Json.UnaLinea(Json.Str(d, "piensa"));
            r.Actitud = Json.UnaLinea(Json.Str(d, "actitud")).ToLowerInvariant();
        }

        // La actitud valida es una de tres; cualquier otra cosa cuenta como "mantiene".
        public static string NormalizaActitud(string a)
        {
            if (a == null) return "mantiene";
            a = a.Trim().ToLowerInvariant();
            if (a.StartsWith("empeor", StringComparison.Ordinal)) return "empeora";
            if (a.StartsWith("perdon", StringComparison.Ordinal) || a.StartsWith("perdón", StringComparison.Ordinal)) return "perdona";
            return "mantiene";
        }

        // Extrae "campo": "valor" aunque el valor tenga comillas sin escapar: acaba en la
        // comilla que va seguida de coma/llave y otra clave o del final.
        public static string Suelto(string s, string campo)
        {
            string clave = "\"" + campo + "\"";
            int i = s.IndexOf(clave, StringComparison.Ordinal);
            if (i < 0) return "";
            int j = s.IndexOf(':', i + clave.Length);
            if (j < 0) return "";
            j++;
            while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
            if (j >= s.Length || s[j] != '"') return "";
            int ini = j + 1;
            int fin = -1;
            for (int k = ini; k < s.Length; k++)
            {
                if (s[k] != '"' || (k > 0 && s[k - 1] == '\\')) continue;
                int m = k + 1;
                while (m < s.Length && char.IsWhiteSpace(s[m])) m++;
                if (m >= s.Length || s[m] == '}' || (s[m] == ',' && SiguienteEsClave(s, m + 1))) { fin = k; break; }
            }
            if (fin < 0) return "";
            string crudo = s.Substring(ini, fin - ini).Replace("\\\"", "\"").Replace("\\n", " ").Replace("\\\\", "\\");
            return crudo.Trim();
        }

        static bool SiguienteEsClave(string s, int pos)
        {
            while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
            if (pos >= s.Length || s[pos] != '"') return false;
            int q = s.IndexOf('"', pos + 1);
            if (q < 0) return false;
            int c = q + 1;
            while (c < s.Length && char.IsWhiteSpace(s[c])) c++;
            return c < s.Length && s[c] == ':';
        }
    }

    // ---- Cuando hablar ----
    public static class Habla
    {
        // "X es Orco", "Y esta Malo": opiniones por rasgo, no por un hecho (informe §5).
        public static bool EsRasgo(string porQue)
        {
            if (string.IsNullOrEmpty(porQue)) return true;
            string p = " " + porQue.ToLowerInvariant() + " ";
            if (p.Contains(" ha ") || p.Contains(" han ") || p.Contains(" ha sido ")) return false;
            return p.Contains(" es ") || p.Contains(" son ") || p.Contains(" está ") || p.Contains(" esta ");
        }
    }

    // Cupo de voz: una cada GapSegundos, nunca dos en vuelo. Hilo-seguro.
    public sealed class CupoVoz
    {
        readonly IClock clock;
        readonly object cerrojo = new object();
        long ultima;
        bool vuelo;
        bool hubo;
        public int GapSegundos;

        public CupoVoz(IClock clock, int gapSegundos) { this.clock = clock; GapSegundos = gapSegundos; }

        public bool TryAcquire()
        {
            lock (cerrojo)
            {
                if (vuelo) return false;
                long t = clock.NowTicks;
                if (hubo && t - ultima < TimeSpan.TicksPerSecond * GapSegundos) return false;
                ultima = t; hubo = true; vuelo = true;
                return true;
            }
        }

        public void Release() { lock (cerrojo) { vuelo = false; } }
        public bool EnVuelo { get { lock (cerrojo) { return vuelo; } } }
    }

    // Politica de empujones a la opinion del juego. Pura: el adaptador solo ejecuta el
    // numero que devuelva. Tope por frase, enfriamiento por pareja y tope por hora global.
    public sealed class PoliticaInfluencia
    {
        readonly IClock clock;
        readonly Dictionary<string, long> ultimaPareja = new Dictionary<string, long>();
        readonly Queue<long> hora = new Queue<long>();
        readonly object cerrojo = new object();
        public double Max = 0.25;
        public int ParejaSegundos = 300;
        public int HoraMax = 30;

        public PoliticaInfluencia(IClock clock) { this.clock = clock; }

        // Devuelve el empujon firmado (0 = ninguno). Registra el uso si es != 0.
        public double Decide(string parejaClave, double delta, string actitud)
        {
            int signo = 0;
            if (actitud == "empeora") signo = Math.Sign(delta);
            else if (actitud == "perdona") signo = -Math.Sign(delta);
            if (signo == 0 || Max <= 0) return 0;
            long ahora = clock.NowTicks;
            lock (cerrojo)
            {
                long prev;
                if (ultimaPareja.TryGetValue(parejaClave, out prev) &&
                    ahora - prev < TimeSpan.TicksPerSecond * ParejaSegundos) return 0;
                while (hora.Count > 0 && ahora - hora.Peek() > TimeSpan.TicksPerHour) hora.Dequeue();
                if (hora.Count >= HoraMax) return 0;
                ultimaPareja[parejaClave] = ahora;
                if (ultimaPareja.Count > 2000) ultimaPareja.Clear();
                hora.Enqueue(ahora);
            }
            return signo * Math.Min(Max, Math.Abs(delta) * 0.25);
        }
    }

    // ---- Prompts (texto puro, probado) ----
    public static class Prompts
    {
        public const string SistemaVoz =
            "Eres el narrador de un reino medieval de los siglos XIII al XV. " +
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

        public static string UsuarioVoz(string a, string b, double delta, string porQue, double? valor,
                                        string ficha, string recuerdos, string directriz)
        {
            var sb = new StringBuilder();
            sb.Append("Cambio de opinion: ").Append(a).Append(" empieza a ")
              .Append(delta > 0 ? "RESPETAR Y ADMIRAR" : "DESCONFIAR Y ODIAR")
              .Append(" a ").Append(b).Append(". Motivo: ").Append(porQue).Append(". ");
            if (valor.HasValue)
                sb.Append("Su opinion actual sobre el otro es ").Append(Json.Num(valor.Value))
                  .Append(" (negativo = desagrado, positivo = aprecio; la escala no tiene tope).");
            if (!string.IsNullOrEmpty(ficha)) sb.Append(" Como es ").Append(a).Append(": ").Append(ficha).Append('.');
            if (!string.IsNullOrEmpty(recuerdos)) sb.Append(" Recuerdos de ").Append(a).Append(": ").Append(recuerdos).Append('.');
            if (!string.IsNullOrEmpty(directriz)) sb.Append(" El jugador, que guia el destino del reino, pide: ").Append(directriz).Append('.');
            return sb.ToString();
        }

        public const string SistemaResumen =
            "Resumes recuerdos de un personaje medieval para su memoria. Escribe en espanol, " +
            "en tercera persona, UNA o DOS frases cortas con lo esencial: quien le importa, " +
            "a quien odia o aprecia y por que. Sin markdown. Responde SOLO el resumen.";

        public static string UsuarioResumen(string nombre, string previo, IList<string> episodios)
        {
            var sb = new StringBuilder();
            sb.Append("Personaje: ").Append(nombre).Append(". ");
            if (!string.IsNullOrEmpty(previo)) sb.Append("Resumen anterior: ").Append(previo).Append(" ");
            sb.Append("Recuerdos nuevos: ");
            for (int i = 0; i < episodios.Count; i++) { if (i > 0) sb.Append("; "); sb.Append(episodios[i]); }
            sb.Append(".");
            return sb.ToString();
        }

        public const string SistemaSecreto =
            "Inventas un secreto breve y ocultable sobre un aldeano de una villa medieval. " +
            "UNA frase, en espanol, sin nombres propios. Debe ser algo que de verdad le conviene " +
            "callar (un crimen, una deuda, un amor prohibido, una familia falsa). Nada de magia.";
    }
}
