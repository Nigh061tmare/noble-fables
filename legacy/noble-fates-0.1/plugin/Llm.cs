using System;
using System.IO;
using System.Net;
using System.Text;

namespace PeceraNF
{
    // =========================================================================
    //  Cliente de Ollama
    // =========================================================================
    //  TRAMPA 1 (descubierta en Going Medieval): en /v1/chat/completions hay que
    //  mandar "reasoning_effort":"none". Con solo "think":false el modelo se
    //  gasta los tokens razonando y devuelve el content VACIO.
    //
    //  TRAMPA 2 (descubierta aqui, con Noble Fates): el endpoint /v1 de Ollama
    //  IGNORA el keep_alive. Da igual lo que pidamos: el modelo se queda
    //  cargado para siempre. Con un 9B son 6,6 GB de VRAM retenidos y la RTX
    //  3060 se queda sin memoria para el juego: eso era el LAG.
    //  Ademas /v1 abria el contexto en 65536, y un KV cache de 65k para
    //  prompts de 300 tokens es tirar VRAM a lo tonto.
    //
    //  SOLUCION: se usa la API NATIVA /api/chat, que si respeta keep_alive y
    //  deja fijar num_ctx. Medido: 3,8 GB -> 2,9 GB de VRAM, y el modelo se
    //  descarga solo tras el intervalo de inactividad.
    //  Si la nativa falla, se cae al proxy por /v1 como red de seguridad.
    // =========================================================================
    public static class Llm
    {
        public const string NATIVA = "http://127.0.0.1:11434/api/chat";
        public const string PROXY = "http://127.0.0.1:11436/v1/chat/completions";

        // 5 minutos medido: aguanta las rafagas y suelta la VRAM en cuanto la
        // corte se calma. Con 15m el juego va algo mas justo de VRAM pero el
        // primer cambio de opinion ya no paga los ~20 s de carga en frio.
        // Configurable en config.txt con la clave keep_alive.
        public static string KeepAlive = "15m";

        // Los prompts reales rondan los 300 tokens. 2048 es de sobra y es lo
        // que mantiene el KV cache en un tamano sensato (65536 Costaba 1 GB).
        const int NUM_CTX = 2048;

        public static string UltimoError = "";

        // Se dispara al cargar la partida: deja el modelo resident para que el
        // primer cambio de opinion no espere la carga en frio.
        public static void Calienta()
        {
            try { Pide(NATIVA, "Responde con la palabra: listo.", "Responde con la palabra: listo.",
                       4, "nativa"); }
            catch { }
        }

        public static string Ask(string sistema, string usuario, int maxTokens = 200)
        {
            UltimoError = "";

            string r = Pide(NATIVA, sistema, usuario, maxTokens, "nativa");
            if (!string.IsNullOrEmpty(r)) return r;

            // Red de seguridad: el proxy, que ademas injecta reasoning_effort.
            return Pide(PROXY, sistema, usuario, maxTokens, "proxy");
        }

        static string Pide(string url, string sistema, string usuario,
                           int maxTokens, string modo)
        {
            try
            {
                string body;
                if (modo == "nativa")
                {
                    body = "{"
                        + "\"model\":\"" + Esc(Plugin.MODELO) + "\","
                        + "\"stream\":false,"
                        + "\"think\":false,"
                        + "\"keep_alive\":\"" + KeepAlive + "\","
                        + "\"options\":{\"num_ctx\":" + NUM_CTX + ",\"num_predict\":" + maxTokens + "},"
                        + "\"messages\":["
                        + "{\"role\":\"system\",\"content\":\"" + Esc(sistema) + "\"},"
                        + "{\"role\":\"user\",\"content\":\"" + Esc(usuario) + "\"}"
                        + "]}";
                }
                else
                {
                    body = "{"
                        + "\"model\":\"" + Esc(Plugin.MODELO) + "\","
                        + "\"reasoning_effort\":\"none\","
                        + "\"think\":false,"
                        + "\"stream\":false,"
                        + "\"keep_alive\":\"" + KeepAlive + "\","
                        + "\"temperature\":0.85,"
                        + "\"max_tokens\":" + maxTokens + ","
                        + "\"messages\":["
                        + "{\"role\":\"system\",\"content\":\"" + Esc(sistema) + "\"},"
                        + "{\"role\":\"user\",\"content\":\"" + Esc(usuario) + "\"}"
                        + "]}";
                }

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json; charset=utf-8";
                req.Timeout = 120000;
                req.ReadWriteTimeout = 120000;

                var bytes = Encoding.UTF8.GetBytes(body);
                req.ContentLength = bytes.Length;
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);

                string raw;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    raw = rd.ReadToEnd();

                // Traza de lo que devuelve el modelo de verdad. Sin esto hay que
                // adivinar por que un campo sale vacio; con esto se lee.
                Traza(modo, raw);

                // La nativa devuelve {"message":{"content":"..."}}; la de /v1
                // devuelve {"choices":[{"message":{"content":"..."}}]}. En ambos
                // casos el primer "content" es el que interesa.
                var c = Extrae(raw, "content");
                if (c.StartsWith("\"")) c = c.Trim('"');
                c = c.Trim();
                if (c.Length > 0) return c;

                UltimoError = "vacio";
                return null;
            }
            catch (Exception e)
            {
                UltimoError = modo + ": " + e.Message;
                return null;
            }
        }

        // Registro acotado de la respuesta cruda del modelo.
        static void Traza(string modo, string raw)
        {
            try
            {
                var dir = Path.Combine(Plugin.Datos, "raw_llm.log");
                var info = new FileInfo(dir);
                if (info.Exists && info.Length > 512 * 1024)
                    File.Delete(dir);

                var marca = DateTime.Now.ToString("HH:mm:ss") + " [" + modo + "] ";
                File.AppendAllText(dir, marca + raw + "\n", new UTF8Encoding(false));
            }
            catch { }
        }

        // Aislar: si el modelo envuelve el JSON en prosa o en vallas de markdown,
        // se recorta solo el objeto { ... } bien balanceado.
        public static string Aislar(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int ini = s.IndexOf('{');
            if (ini < 0) return s;

            int nivel = 0;
            bool enStr = false;
            bool esc = false;
            for (int i = ini; i < s.Length; i++)
            {
                char c = s[i];
                if (enStr)
                {
                    if (esc) { esc = false; continue; }
                    if (c == '\\') { esc = true; continue; }
                    if (c == '"') enStr = false;
                    continue;
                }
                if (c == '"') { enStr = true; continue; }
                if (c == '{') nivel++;
                else if (c == '}')
                {
                    nivel--;
                    if (nivel == 0) return s.Substring(ini, i - ini + 1);
                }
            }
            return s.Substring(ini);
        }

        // Campo: extraccion ESTRICTA de un valor de cadena. Si tras los dos
        // puntos no hay una comilla de apertura devuelve "" en vez de un
        // fragmento basura: antes devolvia cosas como ':  "Dios te maldiga"'.
        public static string Campo(string json, string nombre)
        {
            if (string.IsNullOrEmpty(json)) return "";
            var clave = "\"" + nombre + "\"";
            int i = json.IndexOf(clave, StringComparison.Ordinal);
            if (i < 0) return "";
            int j = json.IndexOf(':', i + clave.Length);
            if (j < 0) return "";
            j++;
            while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
            if (j >= json.Length || json[j] != '"') return "";

            var sb = new StringBuilder();
            for (j++; j < json.Length && json[j] != '"'; j++)
            {
                if (json[j] == '\\' && j + 1 < json.Length) Desescapa(json, ref j, sb);
                else sb.Append(json[j]);
            }
            return sb.ToString().Trim();
        }

        // Saca el valor de un campo del JSON que devuelve Ollama.
        // A proposito NO usamos una libreria de JSON: es mas robusto que
        // referenciar otra DLL dentro de un juego ya instalado.
        public static string Extrae(string json, string campo)
        {
            if (string.IsNullOrEmpty(json)) return "";
            var clave = "\"" + campo + "\"";
            int i = json.IndexOf(clave, StringComparison.Ordinal);
            if (i < 0) return "";
            i = json.IndexOf(':', i + clave.Length);
            if (i < 0) return "";
            while (i < json.Length && (json[i] == ' ' || json[i] == '\n' || json[i] == '\r')) i++;
            if (i >= json.Length) return "";
            if (json[i] == '"')
            {
                var sb = new StringBuilder();
                for (i++; i < json.Length && json[i] != '"'; i++)
                {
                    if (json[i] == '\\' && i + 1 < json.Length) Desescapa(json, ref i, sb);
                    else sb.Append(json[i]);
                }
                return sb.ToString();
            }
            int f = i;
            while (f < json.Length && json[f] != ',' && json[f] != '}' && json[f] != '\n') f++;
            return json.Substring(i, f - i).Trim();
        }

        // Interpreta UN escape JSON. i apunta a la barra; al salir, al ultimo
        // caracter consumido. Antes \n salia como 'n' y \u00e9 como 'u00e9'.
        static void Desescapa(string json, ref int i, StringBuilder sb)
        {
            char n = json[++i];
            if (n == 'n' || n == 't' || n == 'r') { sb.Append(' '); return; }
            if (n == 'u' && i + 4 < json.Length)
            {
                int cp;
                if (int.TryParse(json.Substring(i + 1, 4),
                        System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out cp))
                {
                    sb.Append((char)cp);
                    i += 4;
                    return;
                }
            }
            sb.Append(n);   // \" \\ \/ y cualquier otro
        }

        static string Esc(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
