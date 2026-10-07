using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using HarmonyLib;
using Pecera.Core;
using UnityEngine;

namespace PeceraNF
{
    // =========================================================================
    //  PECERA  -  Noble Fates   (arranque, tareas periodicas, informe, consola)
    // =========================================================================
    [BepInPlugin("pecera.nf", "Pecera NobleFates", Estado.VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        Harmony _h;
        bool _panel;
        float _tFps, _fps1, _t1, _t5, _t30;
        static long _ultimoInforme;
        bool _fpsHeader;
        float _diaPendiente;

        void Awake()
        {
            // Info.Location es la ruta del .dll, no la carpeta (informe §6.2).
            string dir = Path.Combine(Path.GetDirectoryName(Info.Location), "pecera_datos");
            try { Estado.Inicia(dir); }
            catch (IOException e) { Logger.LogError("[Pecera] no se pudo preparar " + dir + ": " + e.Message); return; }
            foreach (var a in Estado.Avisos) Logger.LogWarning("[Pecera] " + a);

            VigilaVersion();
            if (!Estado.Activo)
            {
                Logger.LogInfo("[Pecera] activo=0: mod inerte (solo fps_log si esta encendido)");
                return;
            }
            foreach (string k in new[] { "esquemas", "peticiones", "investigacion", "planos", "misiones" })
                if (Estado.Cfg.Bool(k))
                {
                    string msg = k + "=1 pero su adaptador al juego esta PENDIENTE de confirmar firmas (ver sonda.json): no hace nada";
                    Estado.Avisos.Add(msg);
                    Logger.LogWarning("[Pecera] " + msg);
                }
            _h = new Harmony("pecera.nf");
            try { _h.PatchAll(); }
            catch (Exception e) { Logger.LogError("[Pecera] PatchAll fallo: " + e); Estado.Ev.Fail("hook_opinion", "PatchAll: " + e.Message); }
            foreach (var m in _h.GetPatchedMethods()) Logger.LogInfo("[Pecera] parcheado: " + m.DeclaringType.Name + "." + m.Name);
            Logger.LogInfo("[Pecera] v" + Estado.VERSION + " modelo=" + Estado.Cfg.Str("modelo") + " modo=" + Estado.Cfg.Modo + " datos=" + Estado.Datos);
        }

        // Si el juego se actualiza, las firmas del hook pueden haber cambiado: las escrituras
        // quedan bloqueadas hasta que el usuario ponga confirmar_version=<version> en config.txt.
        void VigilaVersion()
        {
            Estado.VersionJuego = Application.version;
            string guardada = "";
            foreach (var l in Estado.Disco.ReadLines("meta.json")) { object d; if (Json.TryParse(l, out d)) guardada = Json.Str(d, "juego"); }
            Estado.EscrituraSegura = GuardaVersion.Seguro(guardada, Estado.VersionJuego, Estado.Cfg.Str("confirmar_version"));
            if (!Estado.EscrituraSegura)
                Logger.LogWarning("[Pecera] el juego cambio de version (" + guardada + " -> " + Estado.VersionJuego + "): escrituras BLOQUEADAS. " +
                                  "Si todo va bien, pon confirmar_version=" + Estado.VersionJuego + " en config.txt.");
            else
                Estado.Disco.Rewrite("meta.json", new[] { "{\"juego\":\"" + Json.Escape(Estado.VersionJuego) + "\",\"pecera\":\"" + Estado.VERSION + "\"}" });
        }

        void Update()
        {
            // El panel se crea aqui y no en Awake: al arrancar no hay escena lista (informe §5).
            if (!_panel)
            {
                _panel = true;
                Pantalla.Instala();
                Pantalla.BurbujasOn = Estado.Cfg.Bool("burbujas");
                Logger.LogInfo("[Pecera] panel listo (F7 bocadillos, F8 panel, F9 limpiar, F10 informe, F11 sonda)");
                if (Estado.Activo)
                {
                    var t = new System.Threading.Thread(delegate () { Llm.Calienta(); });
                    t.IsBackground = true;
                    t.Start();
                }
            }

            MuestraFps();
            if (!Estado.Activo) return;

            float dt = Time.unscaledDeltaTime;
            _t1 += dt; _t5 += dt; _t30 += dt;
            if (_t1 >= 1f)
            {
                _diaPendiente += _t1 / (float)Math.Max(10, Estado.Cfg.Num("dia_segundos"));
                // El decaimiento compone exactamente (test), asi que se avanza a trozos de >= 1/4 de dia:
                // el coste O(pares) no se paga cada segundo.
                if (Estado.Cfg.Bool("afectos") && _diaPendiente >= 0.25f) { Estado.Afectos.Avanza(_diaPendiente); _diaPendiente = 0f; }
                _t1 = 0f;
                SondeaIds();
            }
            if (_t5 >= 5f) { _t5 = 0f; Estado.CargaDirectriz(); Consola.Atiende(); }
            if (_t30 >= 30f)
            {
                _t30 = 0f;
                Persiste();
                long ahora = Estado.Reloj.NowTicks;
                if (ahora - _ultimoInforme > Estado.Cfg.Num("informe_min") * TimeSpan.TicksPerMinute) EscribeInforme("periodico");
            }
        }

        void SondeaIds()
        {
            if (Estado.Ids.ClavesCoinciden > 0 && Estado.Ev.OkCount("id_clave_estable") == 0 && Estado.Ids.ClavesDiscrepan == 0)
                Estado.Ev.Ok("id_clave_estable", Identidad.MiembroUsado + " coincide en " + Estado.Ids.ClavesCoinciden + " pawns");
            if (Estado.Ids.ClavesDiscrepan > 0 && Estado.Ev.FailCount("id_clave_estable") == 0)
                Estado.Ev.Fail("id_clave_estable", Identidad.MiembroUsado + ": la clave cambia entre sesiones en " + Estado.Ids.ClavesDiscrepan + " pawns");
        }

        static void Persiste()
        {
            try
            {
                if (Estado.Cfg.Bool("afectos")) Estado.Disco.Rewrite("afectos.jsonl", Estado.Afectos.Serializa());
                if (Estado.Cfg.Bool("memoria") && Estado.Mem.Pawns > 0 && Estado.Disco.Exists("memoria.jsonl"))
                    Estado.Ev.Ok("memoria_persistida", Estado.Mem.Pawns + " pawns");
            }
            catch (IOException e) { Estado.Ev.Fail("memoria_persistida", e.Message); }
        }

        // Informe, cronica, grafo y verificacion.json. SOLO desde el hilo principal (Afectos).
        public static void EscribeInforme(string motivo)
        {
            try
            {
                Estado.Disco.Rewrite("afectos.jsonl", Estado.Afectos.Serializa());
                var fichas = Estado.Fichas.Todas();
                var ids = new List<string>();
                foreach (var f in fichas) ids.Add(f.Id);
                var m = new MetricasReino();
                MetricasReino.Calcula(Estado.Afectos, ids, m);
                var fs = Sociedad.Facciones(Estado.Afectos, ids, 0.15, 2);
                Sociedad.AsignaLideres(Estado.Afectos, fs, id => Estado.Fichas.Get(id), id => Estado.Ids.Nombre(id));
                m.Facciones = fs.Count;
                int dia = Gancho.DiaActual();
                foreach (var f in fs)
                    if (f.Miembros.Count >= 3) Estado.Cronica.Anota(dia, "faccion", f.Nombre + " reune a " + f.Miembros.Count + " personas", 4);

                var resumen = Informe.Resume(Estado.Disco.ReadLines("opiniones.jsonl"));
                var fps = Informe.Fps(Informe.LeeFps(Estado.Disco.ReadLines("fps.csv")));
                var avisos = new List<string>(Estado.Avisos);
                if (Estado.Salud.Degradado) avisos.Add("LLM en modo degradado (Ollama no responde)");
                if (!Estado.EscrituraSegura) avisos.Add("escrituras bloqueadas: version del juego sin confirmar");
                foreach (var f in fs) avisos.Add(f.Nombre + ": " + f.Miembros.Count + " miembros, cohesion " + Json.Num(f.Cohesion));
                string ev = Estado.Ev.ToJson(Estado.VERSION, Extra());
                Estado.Disco.Rewrite("verificacion.json", new[] { ev });
                Estado.Disco.Rewrite("informe.md", new[] { Informe.Markdown("Informe de Pecera (" + motivo + ")", resumen, fps, ev, m, avisos) });
                Estado.Disco.Rewrite("grafo.dot", new[] { Informe.GrafoDot(Estado.Afectos, ids, id => Estado.Ids.Nombre(id), 0.3) });
                Estado.Disco.Rewrite("grafo.json", new[] { Informe.GrafoJson(Estado.Afectos, ids, id => Estado.Ids.Nombre(id), 0.3) });
                if (Estado.Cfg.Bool("cronica")) Estado.Disco.Rewrite("cronica.md", new[] { Estado.Cronica.ToMarkdown("el reino") });
                _ultimoInforme = Estado.Reloj.NowTicks;
                Estado.Ev.Ok("informe_escrito", motivo);
            }
            catch (IOException e) { Estado.Ev.Fail("informe_escrito", e.Message); }
        }

        static string Extra()
        {
            var sb = new StringBuilder();
            sb.Append("\"juego\":\"").Append(Json.Escape(Estado.VersionJuego)).Append("\",\"modo\":\"").Append(Estado.Cfg.Modo).Append("\",");
            sb.Append("\"miembro_id\":\"").Append(Json.Escape(Identidad.MiembroUsado)).Append("\",\"ids_coinciden\":").Append(Estado.Ids.ClavesCoinciden)
              .Append(",\"ids_discrepan\":").Append(Estado.Ids.ClavesDiscrepan).Append(",\"ids_ambiguos\":").Append(Estado.Ids.Ambiguos).Append(',');
            sb.Append("\"llm_degradado\":").Append(Estado.Salud.Degradado ? "true" : "false").Append(",\"escritura_segura\":").Append(Estado.EscrituraSegura ? "true" : "false").Append(',');
            sb.Append("\"hook_llamadas\":").Append(Gancho.LlamadasTotal).Append(",\"pawns_con_ficha\":").Append(Estado.Fichas.Count).Append(",\"pares_afectivos\":").Append(Estado.Afectos.Pares).Append(',');
            sb.Append("\"config\":{");
            bool p = true;
            foreach (var e in PeceraConfig.Schema)
            {
                if (!p) sb.Append(','); p = false;
                sb.Append('"').Append(e.Key).Append("\":\"").Append(Json.Escape(Estado.Cfg.Str(e.Key))).Append('"');
            }
            sb.Append('}');
            return sb.ToString();
        }

        // fps.csv: sin medirlo no hay forma de saber si el mod pesa (informe §6.7).
        void MuestraFps()
        {
            if (!Estado.Cfg.Bool("fps_log")) return;
            _tFps += Time.unscaledDeltaTime;
            if (_tFps < 5f) return;
            _tFps = 0f;
            float dt = Time.smoothDeltaTime;
            int fps = dt > 0f ? Mathf.Clamp(Mathf.RoundToInt(1f / dt), 1, 999) : 0;
            if (fps <= 0) return;
            _fps1 = _fps1 == 0f ? fps : _fps1 * 0.9f + fps * 0.1f;
            try
            {
                if (!_fpsHeader && !Estado.Disco.Exists("fps.csv")) Estado.Disco.Append("fps.csv", "hora,fps_medio,fps_instantanea,llamadas_opinion,mod_activo");
                _fpsHeader = true;
                Estado.Disco.Append("fps.csv", DateTime.Now.ToString("HH:mm:ss") + "," + Math.Round(_fps1, 1).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
                    + "," + fps + "," + Gancho.LlamadasTotal + "," + (Estado.Activo ? 1 : 0));
            }
            catch (IOException e) { Debug.Log("[Pecera] fps.csv: " + e.Message); }
        }
    }
}
