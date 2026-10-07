using System;
using System.IO;
using System.Net;
using System.Text;
using Pecera.Core;

namespace PeceraNF
{
    // Cliente de Ollama. Las trampas (informe §6.4): /v1 ignora keep_alive y abre contexto
    // 65536, asi que se usa la API NATIVA /api/chat; /v1 por el proxy solo es red de seguridad.
    // La construccion de cuerpos y la lectura de respuestas viven en Core (OllamaWire) y estan probadas.
    public static class Llm
    {
        public const string NATIVA = "http://127.0.0.1:11434/api/chat";
        public const string PROXY = "http://127.0.0.1:11436/v1/chat/completions";
        const int NUM_CTX = 2048;

        public static string UltimoError = "";

        public static void Calienta()
        {
            Ask("Responde con la palabra: listo.", "Responde con la palabra: listo.", 4);
        }

        // Bloqueante: SOLO desde la hebra de voz, nunca desde el hilo del juego.
        public static string Ask(string sistema, string usuario, int maxTokens)
        {
            UltimoError = "";
            if (!Estado.Salud.Disponible) { UltimoError = "modo degradado (LLM caido, reintento programado)"; return null; }
            string modelo = Estado.Cfg.Str("modelo"), keep = Estado.Cfg.Str("keep_alive");
            string r = Pide(NATIVA, OllamaWire.BodyNativo(modelo, keep, NUM_CTX, maxTokens, sistema, usuario), "nativa");
            if (string.IsNullOrEmpty(r))
                r = Pide(PROXY, OllamaWire.BodyProxy(modelo, keep, maxTokens, sistema, usuario), "proxy");
            if (string.IsNullOrEmpty(r)) Estado.Salud.Fallo(); else Estado.Salud.Exito();
            return r;
        }

        static string Pide(string url, string body, string modo)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json; charset=utf-8";
                req.Timeout = 120000;
                req.ReadWriteTimeout = 120000;
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                req.ContentLength = bytes.Length;
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);

                string raw;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    raw = rd.ReadToEnd();

                Traza(modo, raw);
                string c = OllamaWire.Contenido(raw);
                if (c == null) UltimoError = modo + ": respuesta vacia o ilegible";
                return c;
            }
            catch (WebException e) { UltimoError = modo + ": " + e.Message; return null; }
            catch (IOException e) { UltimoError = modo + ": " + e.Message; return null; }
        }

        // Respuesta cruda del modelo, acotada (sin esto hay que adivinar por que un campo sale vacio).
        static void Traza(string modo, string raw)
        {
            try { Estado.Disco.Append("raw_llm.log", DateTime.Now.ToString("HH:mm:ss") + " [" + modo + "] " + Json.UnaLinea(raw)); }
            catch (IOException e) { UnityEngine.Debug.Log("[Pecera] raw_llm.log: " + e.Message); }
        }
    }
}
