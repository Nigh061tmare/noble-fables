using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    public enum TipoEvento
    {
        Agravio,        // me han hecho dano (insulto, robo, agresion)
        Aprecio,        // me han tratado bien (elogio, compania)
        Ayuda,          // me han ayudado de forma concreta (crea deuda de gratitud)
        Traicion,       // violacion de confianza: golpea confianza y rencor a la vez
        Cortejo,        // gesto romantico
        Competencia,    // competimos por algo
        Perdon,         // el agraviado perdona
        Fiesta,         // convivencia agradable compartida
        Duelo           // el otro causo una perdida grave (trauma)
    }

    // Estado afectivo de A hacia B (dirigido: A->B no es B->A).
    //
    // MODELO (documentado y verificado en simulacion, ver tests/AfectosTests):
    //   afecto    [-1,1]  agrado general; baseline 0
    //   confianza [ 0,1]  baseline 0.5
    //   rencor    [ 0,1]  baseline 0; decae segun el temperamento de A (Ficha.Rencor)
    //   deuda     [-1,1]  >0: A debe gratitud a B; <0: B debe algo a A; baseline 0
    //   rivalidad [ 0,1]  baseline 0
    //   romance   [ 0,1]  baseline 0
    //   trauma    [ 0,1]  baseline 0; amplifica agravios futuros del mismo B
    //
    // Reglas que garantizan estabilidad por construccion:
    //  1. Cada evento mueve una variable hacia un OBJETIVO dentro de su rango con ganancia
    //     g in [0,1]:  x <- x + g (objetivo - x). Es una media ponderada: no sale del rango.
    //  2. Cada dia, cada variable decae exponencialmente hacia su baseline:
    //     x <- base + (x - base) e^(-dt/tau). Monotono: sin eventos nunca cruza su baseline
    //     ni oscila.
    //  3. No hay realimentacion entre variables entre eventos (solo dentro de un evento),
    //     asi que el sistema entre eventos es un conjunto de decaimientos independientes.
    public sealed class Par
    {
        public double Afecto, Confianza = 0.5, Rencor, Deuda, Rivalidad, Romance, Trauma;
        public double DiasRencorAlto;      // dias seguidos con rencor > UmbralRencor
        public int Eventos;

        public const double UmbralRencor = 0.5;

        public Par Copia() { return (Par)MemberwiseClone(); }
    }

    public sealed class ModeloAfectivo
    {
        // Constantes de tiempo (dias de juego).
        public double TauAfecto = 40, TauConfianza = 60, TauDeuda = 30, TauRivalidad = 25, TauRomance = 50, TauTrauma = 120;
        public double TauRencorBase = 10, TauRencorExtra = 70;   // tau_rencor = base + extra * temperamento
        public double BonoReconciliacion = 1.0;                  // +100 % de velocidad de olvido si hay afecto positivo

        readonly Dictionary<string, Par> pares = new Dictionary<string, Par>();
        readonly Func<string, double> temperamento;   // id -> Ficha.Rencor (0..1)

        public ModeloAfectivo(Func<string, double> temperamentoRencor)
        {
            temperamento = temperamentoRencor ?? (id => 0.5);
        }

        static string K(string a, string b) { return a + "\u001f" + b; }

        public int Pares { get { return pares.Count; } }

        public Par Get(string a, string b)
        {
            Par p;
            return pares.TryGetValue(K(a, b), out p) ? p : new Par();
        }

        Par Crea(string a, string b)
        {
            Par p;
            string k = K(a, b);
            if (!pares.TryGetValue(k, out p)) { p = new Par(); pares[k] = p; }
            return p;
        }

        public IEnumerable<KeyValuePair<string, Par>> Todos() { return pares; }

        public static void Separa(string clave, out string a, out string b)
        {
            int i = clave.IndexOf('\u001f');
            a = clave.Substring(0, i); b = clave.Substring(i + 1);
        }

        // magnitud en [0,1]. Devuelve el par actualizado.
        public Par Evento(string a, string b, TipoEvento tipo, double magnitud)
        {
            double m = Clamp(magnitud, 0, 1);
            Par p = Crea(a, b);
            p.Eventos++;
            switch (tipo)
            {
                case TipoEvento.Agravio:
                {
                    double m2 = Math.Min(1, m * (1 + p.Trauma));          // el trauma amplifica
                    Hacia(ref p.Afecto, -1, 0.5 * m2);
                    Hacia(ref p.Rencor, 1, m2);
                    Hacia(ref p.Confianza, 0, 0.3 * m2);
                    break;
                }
                case TipoEvento.Aprecio:
                    Hacia(ref p.Afecto, 1, 0.4 * m);
                    Hacia(ref p.Confianza, 1, 0.2 * m);
                    break;
                case TipoEvento.Ayuda:
                    Hacia(ref p.Afecto, 1, 0.5 * m);
                    Hacia(ref p.Confianza, 1, 0.4 * m);
                    Hacia(ref p.Deuda, 1, 0.6 * m);                       // gratitud
                    Hacia(ref p.Rencor, 0, 0.3 * m);
                    break;
                case TipoEvento.Traicion:
                {
                    double m2 = Math.Min(1, m * (1 + p.Trauma));
                    Hacia(ref p.Confianza, 0, 0.9 * m2);
                    Hacia(ref p.Afecto, -1, 0.6 * m2);
                    Hacia(ref p.Rencor, 1, 0.9 * m2);
                    Hacia(ref p.Trauma, 1, 0.25 * m2);
                    break;
                }
                case TipoEvento.Cortejo:
                    Hacia(ref p.Romance, 1, 0.3 * m);
                    Hacia(ref p.Afecto, 1, 0.2 * m);
                    break;
                case TipoEvento.Competencia:
                    Hacia(ref p.Rivalidad, 1, 0.4 * m);
                    break;
                case TipoEvento.Perdon:
                    Hacia(ref p.Rencor, 0, 0.7 * m);
                    Hacia(ref p.Afecto, 1, 0.15 * m);
                    Hacia(ref p.Trauma, 0, 0.1 * m);
                    break;
                case TipoEvento.Fiesta:
                    Hacia(ref p.Afecto, 1, 0.15 * m);
                    Hacia(ref p.Rencor, 0, 0.15 * m);
                    Hacia(ref p.Rivalidad, 0, 0.15 * m);
                    break;
                case TipoEvento.Duelo:
                    Hacia(ref p.Trauma, 1, 0.8 * m);
                    Hacia(ref p.Rencor, 1, 0.9 * m);
                    Hacia(ref p.Afecto, -1, 0.7 * m);
                    break;
            }
            return p;
        }

        // Traduccion de un cambio de opinion del juego a un evento afectivo. Los rasgos
        // ("es Orco") pesan poco: son prejuicio, no historia compartida.
        public Par DesdeOpinion(string a, string b, double delta, bool esRasgo)
        {
            // v0.5: magnitud = percentil del cambio en la partida real (Calibracion). Antes: tanh(|delta|/2).
            double m = Calibracion.Magnitud(Math.Abs(delta));
            if (esRasgo) m *= 0.25;
            return Evento(a, b, delta < 0 ? TipoEvento.Agravio : TipoEvento.Aprecio, m);
        }

        // Avanza 'dias' de juego. Para pasos grandes se subdivide: el resultado es identico
        // (exp compone) pero se mantiene el contador de dias de rencor sostenido.
        public void Avanza(double dias)
        {
            if (dias <= 0) return;
            List<string> muertos = null;
            foreach (var kv in pares)
            {
                string a, b; Separa(kv.Key, out a, out b);
                Par p = kv.Value;
                double t = Clamp(temperamento(a), 0, 1);
                double tauR = TauRencorBase + TauRencorExtra * t;
                // Reconciliacion: si aun hay cariño, el rencor se enfria mas deprisa.
                if (p.Afecto > 0.2) tauR /= (1 + BonoReconciliacion * p.Afecto);
                Decae(ref p.Afecto, 0, TauAfecto, dias);
                Decae(ref p.Confianza, 0.5, TauConfianza, dias);
                Decae(ref p.Rencor, 0, tauR, dias);
                Decae(ref p.Deuda, 0, TauDeuda, dias);
                Decae(ref p.Rivalidad, 0, TauRivalidad, dias);
                Decae(ref p.Romance, 0, TauRomance, dias);
                Decae(ref p.Trauma, 0, TauTrauma, dias);
                if (p.Rencor > Par.UmbralRencor) p.DiasRencorAlto += dias; else p.DiasRencorAlto = 0;
                if (EnBaseline(p)) { if (muertos == null) muertos = new List<string>(); muertos.Add(kv.Key); }
            }
            // Un par que ha vuelto a su baseline no aporta nada: se olvida para que el modelo
            // solo pague por las relaciones activas (con n pawns podria haber n^2 pares).
            if (muertos != null) foreach (var k in muertos) pares.Remove(k);
        }

        const double Eps = 1e-3;

        static bool EnBaseline(Par p)
        {
            return Math.Abs(p.Afecto) < Eps && Math.Abs(p.Confianza - 0.5) < Eps && p.Rencor < Eps && Math.Abs(p.Deuda) < Eps
                && p.Rivalidad < Eps && p.Romance < Eps && p.Trauma < Eps;
        }

        // Sentimiento neto de A hacia B, en [-1,1]: lo que A "siente" por B ahora.
        public double Sentimiento(string a, string b)
        {
            Par p = Get(a, b);
            double v = p.Afecto - 0.7 * p.Rencor + 0.3 * (p.Confianza - 0.5) + 0.2 * p.Romance + 0.15 * p.Deuda - 0.2 * p.Rivalidad;
            return Clamp(v, -1, 1);
        }

        // Pares con rencor sostenido: candidatos a "esquema" (E). Ordenados por intensidad.
        public List<KeyValuePair<string, Par>> RencoresSostenidos(double minDias, double minRencor)
        {
            var r = new List<KeyValuePair<string, Par>>();
            foreach (var kv in pares)
                if (kv.Value.DiasRencorAlto >= minDias && kv.Value.Rencor >= minRencor && kv.Value.Afecto < 0)
                    r.Add(kv);
            r.Sort((x, y) => y.Value.Rencor.CompareTo(x.Value.Rencor));
            return r;
        }

        static void Hacia(ref double x, double objetivo, double g)
        {
            g = Clamp(g, 0, 1);
            x = x + g * (objetivo - x);
        }

        static void Decae(ref double x, double baseline, double tau, double dias)
        {
            x = baseline + (x - baseline) * Math.Exp(-dias / tau);
        }

        static double Clamp(double v, double a, double b) { return v < a ? a : v > b ? b : v; }

        // ---- persistencia (afectos.json de una linea por par; se reescribe entero) ----
        public IEnumerable<string> Serializa()
        {
            foreach (var kv in pares)
            {
                string a, b; Separa(kv.Key, out a, out b);
                Par p = kv.Value;
                yield return "{\"a\":\"" + Json.Escape(a) + "\",\"b\":\"" + Json.Escape(b) + "\",\"af\":" + Json.Num(p.Afecto)
                    + ",\"co\":" + Json.Num(p.Confianza) + ",\"re\":" + Json.Num(p.Rencor) + ",\"de\":" + Json.Num(p.Deuda)
                    + ",\"ri\":" + Json.Num(p.Rivalidad) + ",\"ro\":" + Json.Num(p.Romance) + ",\"tr\":" + Json.Num(p.Trauma)
                    + ",\"dr\":" + Json.Num(p.DiasRencorAlto) + ",\"n\":" + p.Eventos + "}";
            }
        }

        public void Carga(IEnumerable<string> lineas)
        {
            foreach (var l in lineas)
            {
                object d;
                if (!Json.TryParse(l, out d)) continue;
                string a = Json.Str(d, "a"), b = Json.Str(d, "b");
                if (a.Length == 0 || b.Length == 0) continue;
                Par p = Crea(a, b);
                p.Afecto = Clamp(Json.Num(d, "af", 0), -1, 1);
                p.Confianza = Clamp(Json.Num(d, "co", 0.5), 0, 1);
                p.Rencor = Clamp(Json.Num(d, "re", 0), 0, 1);
                p.Deuda = Clamp(Json.Num(d, "de", 0), -1, 1);
                p.Rivalidad = Clamp(Json.Num(d, "ri", 0), 0, 1);
                p.Romance = Clamp(Json.Num(d, "ro", 0), 0, 1);
                p.Trauma = Clamp(Json.Num(d, "tr", 0), 0, 1);
                p.DiasRencorAlto = Math.Max(0, Json.Num(d, "dr", 0));
                p.Eventos = (int)Json.Num(d, "n", 0);
            }
        }
    }
}
