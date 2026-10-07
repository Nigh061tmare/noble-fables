using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    // Una decision que el mod quiere aplicar al juego. NADA que escriba en el estado del
    // juego se ejecuta sin pasar por la compuerta: tope diario, enfriamiento por clave
    // y ventana de veto del jugador.
    public sealed class Decision
    {
        public int Id;
        public string Clase = "";      // esquema | peticion | investigacion | plano | mision | influencia
        public string Clave = "";      // para enfriamiento (p. ej. par de pawns o id de peticion)
        public string Descripcion = "";
        public string Razon = "";
        public object Carga;           // lo que necesita el adaptador para ejecutarla
        public long ListaEn;           // ticks a partir de los cuales puede ejecutarse
        public bool Vetada;
        public bool Ejecutada;
    }

    public sealed class Compuerta
    {
        readonly IClock reloj;
        readonly object cerrojo = new object();
        readonly List<Decision> cola = new List<Decision>();
        readonly Dictionary<string, long> ultimaClave = new Dictionary<string, long>();
        readonly Dictionary<string, int> topeDia = new Dictionary<string, int>();
        readonly Dictionary<string, Queue<long>> usos = new Dictionary<string, Queue<long>>();
        int sig = 1;

        public int VentanaVetoSegundos = 30;
        public int EnfriarClaveSegundos = 600;
        public long DiaTicks = 10 * TimeSpan.TicksPerMinute;    // duracion de un "dia" de juego en reloj; la ajusta el adaptador

        public Compuerta(IClock reloj) { this.reloj = reloj; }

        public void FijaTopeDia(string clase, int tope) { lock (cerrojo) { topeDia[clase] = tope; } }

        // null = rechazada (tope o enfriamiento). Si modo es "dios" no hay ventana de veto.
        public Decision Propone(string clase, string clave, string descripcion, string razon, object carga, bool sinVeto)
        {
            lock (cerrojo)
            {
                long ahora = reloj.NowTicks;
                string k = clase + "|" + clave;
                long prev;
                if (ultimaClave.TryGetValue(k, out prev) && ahora - prev < TimeSpan.TicksPerSecond * EnfriarClaveSegundos) return null;
                int tope;
                if (topeDia.TryGetValue(clase, out tope))
                {
                    Queue<long> q;
                    if (!usos.TryGetValue(clase, out q)) { q = new Queue<long>(); usos[clase] = q; }
                    while (q.Count > 0 && ahora - q.Peek() > DiaTicks) q.Dequeue();
                    if (q.Count >= tope) return null;
                    q.Enqueue(ahora);
                }
                ultimaClave[k] = ahora;
                var d = new Decision
                {
                    Id = sig++, Clase = clase, Clave = clave, Descripcion = descripcion, Razon = razon, Carga = carga,
                    ListaEn = ahora + (sinVeto ? 0 : TimeSpan.TicksPerSecond * VentanaVetoSegundos)
                };
                cola.Add(d);
                if (ultimaClave.Count > 4000) ultimaClave.Clear();
                return d;
            }
        }

        public bool Veta(int id)
        {
            lock (cerrojo)
            {
                foreach (var d in cola) if (d.Id == id && !d.Ejecutada) { d.Vetada = true; return true; }
                return false;
            }
        }

        // Decisiones cuya ventana de veto ha pasado: las entrega UNA vez.
        public List<Decision> Listas()
        {
            var r = new List<Decision>();
            lock (cerrojo)
            {
                long ahora = reloj.NowTicks;
                foreach (var d in cola)
                    if (!d.Vetada && !d.Ejecutada && d.ListaEn <= ahora) { d.Ejecutada = true; r.Add(d); }
                cola.RemoveAll(d => d.Vetada || d.Ejecutada);
            }
            return r;
        }

        public List<Decision> Pendientes()
        {
            lock (cerrojo) { return cola.FindAll(d => !d.Vetada && !d.Ejecutada); }
        }
    }

    // Disyuntor del LLM (modo degradado): tras N fallos seguidos deja de intentarlo con
    // espera exponencial hasta 10 minutos. Asi un Ollama caido no congela nada ni inunda el log.
    public sealed class SaludLlm
    {
        readonly IClock reloj;
        readonly object cerrojo = new object();
        int fallos;
        long hasta;
        public int Umbral = 3;
        public int Reintentos { get; private set; }

        public SaludLlm(IClock reloj) { this.reloj = reloj; }

        public bool Disponible
        {
            get { lock (cerrojo) { return fallos < Umbral || reloj.NowTicks >= hasta; } }
        }

        public bool Degradado { get { lock (cerrojo) { return fallos >= Umbral; } } }

        public void Exito() { lock (cerrojo) { fallos = 0; hasta = 0; } }

        public void Fallo()
        {
            lock (cerrojo)
            {
                fallos++;
                if (fallos >= Umbral)
                {
                    int exp = Math.Min(fallos - Umbral, 5);                 // 30 s, 60, 120, 240, 480, 600
                    double seg = Math.Min(600, 30 * Math.Pow(2, exp));
                    hasta = reloj.NowTicks + (long)(seg * TimeSpan.TicksPerSecond);
                    Reintentos++;
                }
            }
        }
    }

    // Guarda de version del juego: si cambia, las escrituras quedan en modo seguro hasta
    // que el usuario confirme (el hook podria haber cambiado de firma).
    public static class GuardaVersion
    {
        // 'guardada' es lo que dejo meta.json. Devuelve true si es seguro escribir.
        public static bool Seguro(string guardada, string actual, string confirmada)
        {
            if (string.IsNullOrEmpty(guardada)) return true;                 // primera vez
            if (guardada == actual) return true;
            return confirmada == actual;                                       // el usuario acepto esta version
        }
    }

    // Formato de datos versionado: nunca se escribe sobre datos de un formato MAS NUEVO.
    public static class FormatoDatos
    {
        public const int Actual = 1;

        public static int LeeVersion(string[] lineas)
        {
            foreach (var l in lineas)
            {
                object d;
                if (Json.TryParse(l, out d) && Json.Str(d, "op") == "v") return (int)Json.Num(d, "v", 1);
                break;
            }
            return 1;
        }

        public static bool PuedeEscribir(int version) { return version <= Actual; }
    }
}
