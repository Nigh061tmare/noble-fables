using System;
using System.Collections.Generic;
using Pecera.Core;
using UnityEngine;

namespace PeceraNF
{
    // =========================================================================
    //  PANTALLA  -  "REGISTRO DE LA CORTE"
    // =========================================================================
    //  Todo lo que se decide aqui viene por lo que el LAG complained de:
    //  OnGUI se llama varias veces por frame y antes se creaba un GUIStyle
    //  nuevo por linea y por repintado. Eso es basura en el recolector a
    //  decenas de veces por segundo, y el juego lo paga en tirones.
    //
    //  Ahora: estilos creados UNA vez, instantanea de lineas reutilizada y
    //  solo se reconstruye cuando entra un evento nuevo.
    //
    //  F8 muestra u oculta. F9 limpia el registro (la cabecera se mantiene).
    // =========================================================================
    // Cola hacia el hilo del juego: el LLM responde en otra hebra y Unity solo
    // permite tocar el juego desde la principal.
    public static class Principal
    {
        static readonly System.Collections.Concurrent.ConcurrentQueue<Action> Q =
            new System.Collections.Concurrent.ConcurrentQueue<Action>();

        public static void Encola(Action a) { Q.Enqueue(a); }

        public static void Drena()
        {
            Action a;
            int n = 0;
            while (n++ < 4 && Q.TryDequeue(out a))
            {
                try { a(); }
                catch (Exception e)
                {
                    // Una accion encolada fallo en el hilo del juego: se registra, no se oculta.
                    Estado.Ev.Fail("cola_principal", e.GetType().Name + ": " + e.Message);
                    Debug.Log("[Pecera] principal: " + e);
                }
            }
        }
    }

    public static class Pantalla
    {
        public static bool BurbujasOn = true;
        public static bool MuestraInicio = false;   // F7 la alterna en caliente; el defecto sale de config.txt (burbujas)

        class Bub
        {
            public Pawn P; public GUIContent C; public float Hasta = -1f; public bool Contada;
        }
        static readonly List<Bub> Burbs = new List<Bub>();
        static GUIStyle _burb;

        // Se llama desde la hebra de voz; el reloj de Unity solo se lee al dibujar.
        public static void Burbuja(Pawn p, string nombre, string texto, bool positivo)
        {
            if (p == null || string.IsNullOrEmpty(texto) || !Estado.Cfg.Bool("burbujas")) return;
            if (texto.Length > 150) texto = texto.Substring(0, 150) + "...";
            var b = new Bub { P = p, C = new GUIContent(nombre + ": " + texto) };
            lock (Burbs)
            {
                Burbs.Add(b);
                if (Burbs.Count > 4) Burbs.RemoveAt(0);
            }
        }

        static void DibujaBurbujas()
        {
            if (!BurbujasOn || Burbs.Count == 0) return;
            var cam = Camera.main;
            if (cam == null) return;
            Estilos();
            if (_burb == null)
            {
                _burb = new GUIStyle(_positivo);
                _burb.normal.textColor = Color.white;
                _burb.fontSize = 13;
                _burb.alignment = TextAnchor.MiddleCenter;
            }
            float ahora = Time.unscaledTime;
            lock (Burbs)
            {
                for (int i = Burbs.Count - 1; i >= 0; i--)
                {
                    var b = Burbs[i];
                    if (b.Hasta < 0f) b.Hasta = ahora + 12f;
                    if (ahora > b.Hasta) { Burbs.RemoveAt(i); continue; }
                    try
                    {
                        Vector3 w = b.P.character.pos + Vector3.up * 2.2f;
                        Vector3 s = cam.WorldToScreenPoint(w);
                        if (s.z <= 0f) continue;
                        const float ancho = 280f;
                        float alto = _burb.CalcHeight(b.C, ancho - 12f) + 8f;
                        var r = new Rect(s.x - ancho / 2f, Screen.height - s.y - alto, ancho, alto);
                        Fondo(r);
                        GUI.Label(r, b.C, _burb);
                        // Evidencia: el bocadillo se dibujo de verdad en pantalla (no solo se encolo).
                        if (!b.Contada) { b.Contada = true; Estado.Ev.Ok("bocadillo_dibujado", "x=" + (int)s.x + " y=" + (int)s.y); }
                    }
                    catch (Exception e) { Estado.Ev.Fail("bocadillo_dibujado", e.GetType().Name + ": " + e.Message); Burbs.RemoveAt(i); }
                }
            }
        }

        const int MAX = 60;
        const int VISIBLES = 6;
        const float ANCHO = 460f;
        const float ALTO_LINEA = 34f;
        const float CABECERA = 22f;

        static readonly string[] Texto = new string[MAX];
        static readonly Color[] Tono = new Color[MAX];
        static int Cuantas = 0;          // entradas validas (tope MAX)
        static int Total = 0;            // total insertado, no se satura
        static int Version = 0;

        // Instantanea cacheada para no asignar en cada repintado.
        static string[] _vTexto = new string[VISIBLES];
        static Color[] _vTono = new Color[VISIBLES];
        static int _vN = 0;
        static int _vVersion = -1;

        // Empieza OCULTO. Ocupaba un cuarto de la pantalla y el jugador no lo
        // necesitaba a la vista: F8 lo saca cuando se quiera consultar.
        static bool Visible = false;
        static bool _limpio = true;
        static GUIStyle _titulo, _positivo, _negativo;
        static Texture2D _fondo;

        public static void Anota(string linea, Color tono)
        {
            lock (Texto)
            {
                Texto[Total % MAX] = linea;
                Tono[Total % MAX] = tono;
                Total++;
                Cuantas = Total < MAX ? Total : MAX;
                Version++;
                _limpio = false;
            }
        }

        public static void Instala()
        {
            try
            {
                var go = new GameObject("PeceraPanel");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<Panel>();
            }
            catch (Exception e)
            {
                Debug.Log("[Pecera] no se pudo crear el panel: " + e.Message);
            }
        }

        class Panel : MonoBehaviour
        {
            void Update()
            {
                Principal.Drena();
                if (Input.GetKeyDown(KeyCode.F7))
                {
                    BurbujasOn = !BurbujasOn;
                    Debug.Log("[Pecera] burbujas " + (BurbujasOn ? "on" : "off"));
                }
                if (Input.GetKeyDown(KeyCode.F10)) Plugin.EscribeInforme("tecla F10");
                if (Input.GetKeyDown(KeyCode.F11)) { Sonda.Tipos(); Sonda.Peticiones(); }
                if (Input.GetKeyDown(KeyCode.F8))
                {
                    Visible = !Visible;
                    Debug.Log("[Pecera] panel " + (Visible ? "visible" : "oculto"));
                }
                if (Input.GetKeyDown(KeyCode.F9))
                {
                    lock (Texto)
                    {
                        Array.Clear(Texto, 0, Texto.Length);
                        Cuantas = 0;
                        Total = 0;
                        Version++;
                        _limpio = true;
                    }
                    Debug.Log("[Pecera] registro limpio");
                }
            }

            void OnGUI() { Dibuja(); }

            void Start()
            {
                Visible = MuestraInicio;
                Debug.Log("[Pecera] panel_inicio=" + (MuestraInicio ? "1" : "0") + " (F8 para alternar)");
            }
        }

        static void Estilos()
        {
            if (_titulo != null) return;

            _fondo = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _fondo.SetPixel(0, 0, new Color(0f, 0f, 0f, 1f));
            _fondo.Apply();
            _fondo.hideFlags = HideFlags.HideAndDontSave;

            var baseEstilo = new GUIStyle(GUI.skin.label);
            baseEstilo.fontSize = 12;
            baseEstilo.wordWrap = true;
            baseEstilo.richText = false;
            baseEstilo.alignment = TextAnchor.UpperLeft;
            baseEstilo.padding = new RectOffset(6, 6, 2, 2);

            _titulo = new GUIStyle(baseEstilo);
            _titulo.fontStyle = FontStyle.Bold;

            _positivo = new GUIStyle(baseEstilo);
            _positivo.normal.textColor = new Color(0.62f, 0.93f, 0.66f);

            _negativo = new GUIStyle(baseEstilo);
            _negativo.normal.textColor = new Color(0.98f, 0.62f, 0.60f);
        }

        // Reconstruye la vista solo si ha entrado algo nuevo desde el ultimo
        // repintado. Sin esto, OnGUI asigna en cada frame.
        static void Instantanea()
        {
            if (_vVersion == Version) return;

            lock (Texto)
            {
                int n = Cuantas < VISIBLES ? Cuantas : VISIBLES;
                int ini = Total - n;
                for (int i = 0; i < n; i++)
                {
                    int k = (ini + i) % MAX;
                    _vTexto[i] = Texto[k];
                    _vTono[i] = Tono[k];
                }
                _vN = n;
            }
            _vVersion = Version;
        }

        static void Dibuja()
        {
            DibujaBurbujas();
            if (!Visible) return;

            Instantanea();
            Estilos();

            if (_limpio && _vN == 0)
            {
                // Tras F9 la cabecera sigue en su sitio: el HUD no desaparece,
                // simplemente esta vacio.
                var r0 = new Rect(14f, 14f, ANCHO, CABECERA + 18f);
                Fondo(r0);
                GUI.Label(new Rect(r0.x + 4, r0.y + 3, r0.width - 8, 20),
                          Cabecera(), _titulo);
                return;
            }

            if (_vN == 0) return;

            float alto = CABECERA + ALTO_LINEA * _vN;
            var rect = new Rect(14f, 14f, ANCHO, alto);
            Fondo(rect);

            GUI.Label(new Rect(rect.x + 4, rect.y + 3, rect.width - 8, 20),
                      Cabecera(), _titulo);

            for (int i = 0; i < _vN; i++)
            {
                var est = _vTono[i].g > _vTono[i].r ? _positivo : _negativo;
                GUI.Label(new Rect(rect.x + 4, rect.y + CABECERA + i * ALTO_LINEA,
                                   rect.width - 10, ALTO_LINEA), _vTexto[i], est);
            }
        }

        // La cabecera se rehace como mucho 2 veces por segundo. OnGUI se llama
        // en cada evento de interfaz, no en cada frame, y concatenar aqui en
        // cada llamada metia basura en el recolector sin parar.
        static string _cabecera = "";
        static float _cabeceraEn = -99f;

        static string Cabecera()
        {
            float ahora = Time.unscaledTime;
            if (ahora - _cabeceraEn < 0.5f && _cabecera.Length > 0) return _cabecera;
            _cabeceraEn = ahora;

            // El FPS va en la cabecera a proposito: sin medirlo se acababa
            // suponiendo que el problema era el LLM cuando puede ser el panel.
            int fps = Time.smoothDeltaTime > 0f
                ? Mathf.Clamp(Mathf.RoundToInt(1f / Time.smoothDeltaTime), 0, 999)
                : 0;
            _cabecera = "REGISTRO DE LA CORTE   " + fps + " fps" +
                        (fps > 0 && fps < 50 ? "  <<< BAJO" : "") +
                        "   (F8 ocultar / F9 limpiar / F10 informe / F11 sonda)";
            return _cabecera;
        }

        static void Fondo(Rect r)
        {
            var anterior = GUI.skin.box.normal.background;
            GUI.skin.box.normal.background = _fondo;
            var color = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.Box(r, GUIContent.none);
            GUI.color = color;
            GUI.skin.box.normal.background = anterior;
        }
    }
}
