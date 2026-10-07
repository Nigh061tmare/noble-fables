using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace PeceraNF
{
    // =========================================================================
    //  PECERA  -  Noble Fates
    // =========================================================================
    //  El juego ya tiene el sistema social entero: opiniones cuantitativas
    //  con su PORQUE, memoria (Moments), intencion (Schemes) ysudeseos
    //  (Wants). Aqui solo le ponemos la voz y guardamos lo que pasa para
    //  poder medirlo despues.
    // =========================================================================
    [BepInPlugin("pecera.nf", "Pecera NobleFates", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string OLLAMA = "http://127.0.0.1:11436/v1/chat/completions";

        // Medido en esta maquina (RTX 3060 12 GB) con el prompt real:
        //   qwen4b-silly 3,4 GB  1,4 s  41 tok/s  JSON completo   <- elegido
        //   qwen9b-silly 6,6 GB  2,1 s  29 tok/s  se corta a 150 tokens
        //   mimo-9b:q4   5,8 GB  2,9 s  28 tok/s  pronombre equivocado
        //   mistral-nemo 7,1 GB  1,9 s  26 tok/s  correcto pero pesado
        // Se puede cambiar en pecera_datos\config.txt sin recompilar.
        public static string MODELO = "qwen4b-silly:latest";

        public static string Datos = "";
        public static volatile bool Encendido = true;
        public static bool FpsLog = false;
        private Harmony _h;
        private bool _panel;

        private void Awake()
        {
            Datos = Path.Combine(Path.GetDirectoryName(Info.Location), "pecera_datos");
            Directory.CreateDirectory(Datos);
            LeeConfig();
            CreaDirectriz();

            _h = new Harmony("pecera.nf");
            try { _h.PatchAll(); }
            catch (Exception e) { Logger.LogError("[Pecera] PatchAll fallo: " + e); }
            foreach (var m in _h.GetPatchedMethods())
                Logger.LogInfo("[Pecera] parcheado: " + m.DeclaringType.Name + "." + m.Name);
            Logger.LogInfo("[Pecera] enganchado. modelo=" + MODELO);
            Logger.LogInfo("[Pecera] datos en " + Datos);
        }

        // directriz.txt: tu mano sobre el reino. Se crea con instrucciones.
        private void CreaDirectriz()
        {
            try
            {
                var r = Path.Combine(Datos, "directriz.txt");
                if (!File.Exists(r))
                    File.WriteAllText(r,
                        "# Escribe aqui lo que quieres que pase en el reino. Se lee solo, sin reiniciar.\r\n" +
                        "# Las lineas con # se ignoran. Ejemplos:\r\n" +
                        "#   Que la corte se divida en dos bandos por la herencia.\r\n" +
                        "#   Que los rencores se enfrien despues de una gran fiesta.\r\n",
                        new UTF8Encoding(false));
            }
            catch { }
        }

        // config.txt con pares clave=valor. Si no existe se crea con los
        // valores por defecto, para que sea facil de editar a mano.
        private void LeeConfig()
        {
            var ruta = Path.Combine(Datos, "config.txt");
            try
            {
                if (!File.Exists(ruta))
                {
                    File.WriteAllText(ruta,
                        "# Ajustes de Pecera NobleFates. Edita y reinicia el juego.\r\n" +
                        "# modelos medidos: qwen4b-silly / qwen9b-silly / mimo-9b:q4 / mistral-nemo\r\n" +
                        "# keep_alive: cuanto tiempo sigue el modelo en la VRAM tras la ultima\r\n" +
                        "#   peticion. Mas tiempo = menos esperas, mas VRAM ocupada.\r\n" +
                        "# umbral_habla: |cambio| minimo para que alguien hable por un HECHO\r\n" +
                        "#   (ha matado, ha desertado...). umbral_rasgo: lo mismo para rasgos\r\n" +
                        "#   (es Orco, esta Malo), que son ruido: por defecto solo si es fuerte.\r\n" +
                        "# gap_voz: segundos minimos entre dos frases. Mas bajo = mas voces y mas GPU.\r\n" +
                        "# fps_log: 1 escribe fps.csv cada 5 s (solo para diagnosticar lag).\r\n" +
                        "modelo=" + MODELO + "\r\n" +
                        "keep_alive=" + Llm.KeepAlive + "\r\n" +
                        "umbral_habla=" + Gancho.UmbralHabla.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n" +
                        "umbral_rasgo=" + Gancho.UmbralRasgo.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n" +
                        "gap_voz=" + Gancho.GapHablaSegundos + "\r\n" +
                        "fps_log=0\r\n");
                }

                foreach (var linea in File.ReadAllLines(ruta))
                {
                    var l = linea.Trim();
                    if (l.Length == 0 || l.StartsWith("#")) continue;
                    int eq = l.IndexOf('=');
                    if (eq <= 0) continue;
                    var clave = l.Substring(0, eq).Trim().ToLowerInvariant();
                    var valor = l.Substring(eq + 1).Trim();
                    if (clave == "modelo" && valor.Length > 0) MODELO = valor;
                    if (clave == "keep_alive" && valor.Length > 0) Llm.KeepAlive = valor;
                    float f; int k;
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    if (clave == "umbral_habla" && float.TryParse(valor, System.Globalization.NumberStyles.Float, inv, out f) && f > 0f) Gancho.UmbralHabla = f;
                    if (clave == "umbral_rasgo" && float.TryParse(valor, System.Globalization.NumberStyles.Float, inv, out f) && f > 0f) Gancho.UmbralRasgo = f;
                    if (clave == "gap_voz" && int.TryParse(valor, out k) && k >= 5) Gancho.GapHablaSegundos = k;
                    if (clave == "fps_log") FpsLog = valor == "1";
                    if (clave == "influencia") Gancho.Influencia = valor == "1";
                    if (clave == "influencia_max" && float.TryParse(valor, System.Globalization.NumberStyles.Float, inv, out f) && f >= 0f && f <= 1f) Gancho.InfluenciaMax = f;
                    if (clave == "burbujas") Pantalla.BurbujasOn = valor == "1";
                }
            }
            catch (Exception e)
            {
                Logger.LogWarning("[Pecera] config.txt ilegible, uso defecto: " + e.Message);
            }
        }

        // El panel se crea aqui y no en Awake: al arrancar el juego todavia no
        // hay escena de Unity lista y un GameObject se perderia.
        private float _t;
        private float _f1;

        private void Update()
        {
            if (!_panel)
            {
                _panel = true;
                Pantalla.Instala();
                Logger.LogInfo("[Pecera] panel listo, oculto (F8 mostrar/ocultar, F9 limpiar)");

                // Se deja el modelo resident en segundo plano. Si no, el primer
                // cambio de opinion tras cargar la partida paga ~20 s de carga.
                var t = new System.Threading.Thread(() =>
                {
                    try { Llm.Calienta(); } catch { }
                });
                t.IsBackground = true;
                t.Start();
            }

            // Muestreo de FPS una vez por segundo. Sin esto no hay forma de
            // saber si el mod pesa o no: habria que estar suponiendo.
            if (!FpsLog) return;
            _t += UnityEngine.Time.unscaledDeltaTime;
            if (_t >= 5f)
            {
                _t = 0f;
                float dt = UnityEngine.Time.smoothDeltaTime;
                int fps = dt > 0f ? Mathf.Clamp(Mathf.RoundToInt(1f / dt), 1, 999) : 0;
                if (fps > 0)
                {
                    _f1 = _f1 == 0f ? fps : _f1 * 0.9f + fps * 0.1f;
                    Muestra(_f1, fps);
                }
            }
        }

        static void Muestra(float media, int instantanea)
        {
            try
            {
                var ruta = Path.Combine(Datos, "fps.csv");
                var info = new FileInfo(ruta);
                if (info.Exists && info.Length > 256 * 1024) File.Delete(ruta);

                var nuevo = !info.Exists;
                using (var w = new StreamWriter(ruta, true, new UTF8Encoding(false)))
                {
                    if (nuevo)
                        w.WriteLine("hora,fps_medio,fps_instantanea,llamadas_opinion");
                    w.WriteLine(DateTime.Now.ToString("HH:mm:ss") + "," +
                                Math.Round(media, 1) + "," +
                                instantanea + "," +
                                Gancho.LlamadasTotal);
                }
            }
            catch { }
        }
    }
}