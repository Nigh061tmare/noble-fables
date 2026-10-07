using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace Pecera
{
    // Cliente minimo contra Ollama por el endpoint OpenAI.
    //
    // TRAMPA IMPORTANTE (la descubri en Going Medieval):
    // en /v1/chat/completions hay que mandar  "reasoning_effort": "none".
    // Con "think": false NO funciona y el modelo devuelve el content VACIO
    // porque se gasta los tokens en razonar.
    // El proxy de 127.0.0.1:11436 lo inyecta por si acaso.
    public static class Llm
    {
        public static string Ask(string sistema, string usuario, int maxTokens = 160)
        {
            try
            {
                var body = "{"
                    + "\"model\":\"" + Esc(Plugin.MODELO) + "\","
                    + "\"reasoning_effort\":\"none\","
                    + "\"think\":false,"
                    + "\"stream\":false,"
                    + "\"keep_alive\":\"30m\","
                    + "\"temperature\":0.85,"
                    + "\"max_tokens\":" + maxTokens + ","
                    + "\"messages\":["
                    + "{\"role\":\"system\",\"content\":\"" + Esc(sistema) + "\"},"
                    + "{\"role\":\"user\",\"content\":\"" + Esc(usuario) + "\"}"
                    + "]}";

                var req = (HttpWebRequest)WebRequest.Create(Plugin.OLLAMA);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Timeout = 120000;
                req.ReadWriteTimeout = 120000;
                var bytes = Encoding.UTF8.GetBytes(body);
                req.ContentLength = bytes.Length;
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);

                string raw;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var rd = new StreamReader(resp.GetResponseStream()))
                    raw = rd.ReadToEnd();

                var c = Extrae(raw, "content");
                if (c.StartsWith("\"")) c = c.Trim('"');
                return c.Replace("\\n", " ").Replace("\\\"", "\"").Trim();
            }
            catch (Exception e)
            {
                return null;
            }
        }

        // Saca el valor de un campo del JSON que devuelve Ollama.
        // A proposito NO usamos una libreria de JSON: el proxy ya devuelve
        // JSON limpio y esto es mas robusto que Referenciar otra DLL.
        public static string Extrae(string json, string campo)
        {
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
                    if (json[i] == '\\' && i + 1 < json.Length) { sb.Append(json[++i]); }
                    else sb.Append(json[i]);
                }
                return sb.ToString();
            }
            int f = i;
            while (f < json.Length && json[f] != ',' && json[f] != '}' && json[f] != '\n') f++;
            return json.Substring(i, f - i).Trim();
        }

        static string Esc(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}