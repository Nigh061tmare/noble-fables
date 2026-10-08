using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    // Un suceso del mundo, venga de donde venga (hook de opinion, ganchos observadores configurables, simulador...).
    // Tipos conocidos: opinion, muerte, nacimiento, boda, herida, combate, trabajo, llegada, partida, conversacion, juicio.
    public sealed class EventoMundo
    {
        public string Tipo = "", A = "", B = "", Texto = "";
        public double Valor;
        public int Dia;
        public bool Rasgo;
    }

    // Bus de eventos tipado (alife-sdk): los sistemas se suscriben a TIPOS de suceso en vez de llamarse entre si, asi un gancho nuevo
    // del juego alimenta memoria, afectos, cronica y linaje sin tocar mas codigo. Sincrono, SOLO hilo principal.
    // Una excepcion en un suscriptor no impide que los demas reciban el evento: se cuenta y se informa.
    public sealed class BusEventos
    {
        readonly Dictionary<string, List<Action<EventoMundo>>> subs = new Dictionary<string, List<Action<EventoMundo>>>();
        readonly Dictionary<string, int> cuenta = new Dictionary<string, int>();
        readonly Queue<EventoMundo> ultimos = new Queue<EventoMundo>();
        public int Errores { get; private set; }
        public string UltimoError = "";
        public int MaxUltimos = 200;

        public void Suscribe(string tipo, Action<EventoMundo> f)
        {
            List<Action<EventoMundo>> l;
            if (!subs.TryGetValue(tipo, out l)) { l = new List<Action<EventoMundo>>(); subs[tipo] = l; }
            l.Add(f);
        }

        public void Publica(EventoMundo e)
        {
            int n; cuenta.TryGetValue(e.Tipo, out n); cuenta[e.Tipo] = n + 1;
            ultimos.Enqueue(e); while (ultimos.Count > MaxUltimos) ultimos.Dequeue();
            Entrega(e.Tipo, e); Entrega("*", e);
        }

        void Entrega(string clave, EventoMundo e)
        {
            List<Action<EventoMundo>> l;
            if (!subs.TryGetValue(clave, out l)) return;
            foreach (var f in l.ToArray())
            {
                try { f(e); }
                catch (Exception ex)
                {
                    // un suscriptor roto no tumba a los demas ni al hook del juego; queda contado y visible
                    Errores++; UltimoError = e.Tipo + ": " + ex.GetType().Name + ": " + ex.Message;
                }
            }
        }

        public int Cuenta(string tipo) { int n; return cuenta.TryGetValue(tipo, out n) ? n : 0; }
        public IEnumerable<KeyValuePair<string, int>> Cuentas { get { return cuenta; } }
        public IEnumerable<EventoMundo> Ultimos { get { return ultimos; } }
    }

    // Lo que necesita la Vida para dar consecuencias a un suceso.
    public sealed class ContextoVida
    {
        public ModeloAfectivo Afectos;
        public Memoria Mem;
        public Cronica Cronica;
        public Linaje Linaje;
        public Func<string, Persona> Persona;          // puede devolver null
        public Func<string, string> Nombre = id => id;
        public Func<IList<string>> Vivos;
        public RecuerdosFuertes Fuertes;                 // opcional
    }

    // Sucesos de vida → consecuencias (Dwarf Fortress: cada suceso deja una emocion y un recuerdo cuya fuerza depende del vinculo).
    // Nada escribe en el juego: modelo afectivo, memoria, recuerdos fuertes, linaje y cronica.
    public static class Vida
    {
        public static void Conecta(BusEventos bus, ContextoVida c)
        {
            bus.Suscribe("muerte", e => Muerte(e, c));
            bus.Suscribe("nacimiento", e => Nacimiento(e, c));
            bus.Suscribe("boda", e => Boda(e, c));
            bus.Suscribe("herida", e => Herida(e, c));
            bus.Suscribe("combate", e => Herida(e, c));
            bus.Suscribe("llegada", e => Llegada(e, c));
            bus.Suscribe("partida", e => Partida(e, c));
            bus.Suscribe("trabajo", e => Trabajo(e, c));
        }

        static void Recuerda(ContextoVida c, string quien, string tipo, string con, string texto, double peso, double valencia)
        {
            if (c.Mem != null) c.Mem.Registra(quien, tipo, con, texto, peso);
            if (c.Fuertes != null) c.Fuertes.Anota(quien, tipo, texto, valencia, peso, 0);
        }

        // Muerte: el duelo de cada vivo es proporcional a su vinculo; si hubo causante, quien amaba al muerto le guarda rencor y trauma.
        static void Muerte(EventoMundo e, ContextoVida c)
        {
            string muerto = e.A, causa = e.B;
            if (c.Linaje != null) c.Linaje.Muere(muerto);
            c.Cronica.Anota(e.Dia, "muerte", c.Nombre(muerto) + " muere" + (e.Texto.Length > 0 ? " (" + e.Texto + ")" : "") + (causa.Length > 0 ? " a manos de " + c.Nombre(causa) : ""), 8);
            foreach (var o in c.Vivos())
            {
                if (o == muerto) continue;
                Par p = c.Afectos.Get(o, muerto);
                double vinculo = Math.Max(0, p.Afecto) + p.Romance;
                if (vinculo < 0.15) continue;
                double fuerza = Math.Min(1, vinculo);
                Recuerda(c, o, "duelo", muerto, "perdi a " + c.Nombre(muerto), 4 + 6 * fuerza, -fuerza);
                var per = c.Persona(o);
                Estres.Sufre(per, 90 * fuerza * fuerza);   // perder a quien amas es lo que mas pesa (CK3: duelo)
                if (per != null) { per.Needs.Pon("seguridad", per.Needs.Seguridad - 0.2 * fuerza); per.Needs.Pon("social", per.Needs.Social - 0.15 * fuerza); }
                if (causa.Length > 0 && causa != o) c.Afectos.Evento(o, causa, TipoEvento.Duelo, fuerza);
            }
        }

        static void Nacimiento(EventoMundo e, ContextoVida c)
        {
            string padre = e.B, madre = e.Texto.StartsWith("madre=", StringComparison.Ordinal) ? e.Texto.Substring(6) : "";
            if (c.Linaje != null) c.Linaje.Nace(e.A, e.Dia, padre.Length > 0 ? padre : null, madre.Length > 0 ? madre : null);
            c.Cronica.Anota(e.Dia, "nacimiento", "Nace " + c.Nombre(e.A) + (padre.Length > 0 ? ", hijo de " + c.Nombre(padre) : ""), 6);
            foreach (var pa in new[] { padre, madre })
            {
                if (pa.Length == 0) continue;
                Recuerda(c, pa, "nacimiento", e.A, "nació mi hijo " + c.Nombre(e.A), 8, 0.9);
                var per = c.Persona(pa);
                if (per != null) per.Needs.Pon("autorrealizacion", per.Needs.Autorrealizacion + 0.3);
            }
        }

        static void Boda(EventoMundo e, ContextoVida c)
        {
            if (c.Linaje != null) c.Linaje.Casa(e.A, e.B);
            c.Afectos.Evento(e.A, e.B, TipoEvento.Cortejo, 1); c.Afectos.Evento(e.B, e.A, TipoEvento.Cortejo, 1);
            c.Afectos.Evento(e.A, e.B, TipoEvento.Fiesta, 1); c.Afectos.Evento(e.B, e.A, TipoEvento.Fiesta, 1);
            c.Cronica.Anota(e.Dia, "boda", c.Nombre(e.A) + " y " + c.Nombre(e.B) + " se casan", 7);
            Recuerda(c, e.A, "boda", e.B, "me case con " + c.Nombre(e.B), 9, 1);
            Recuerda(c, e.B, "boda", e.A, "me case con " + c.Nombre(e.A), 9, 1);
            foreach (var quien in new[] { e.A, e.B })
            {
                var per = c.Persona(quien);
                if (per == null) continue;
                var amb = per.Ambiciones.Find(x => x.Categoria == "casarse" && !x.Cumplida);
                if (amb != null) { amb.Progreso = 1; amb.Cumplida = true; per.Logros++; }
            }
        }

        static void Herida(EventoMundo e, ContextoVida c)
        {
            double g = Math.Max(0.2, Math.Min(1, e.Valor <= 0 ? 0.5 : e.Valor));
            Recuerda(c, e.A, "herida", e.B, "me hirieron" + (e.B.Length > 0 ? " (" + c.Nombre(e.B) + ")" : ""), 3 + 5 * g, -g);
            var per = c.Persona(e.A);
            Estres.Sufre(per, 50 * g);
            if (per != null) { per.Needs.Pon("seguridad", per.Needs.Seguridad - 0.25 * g); per.Needs.Pon("descanso", per.Needs.Descanso - 0.2 * g); }
            if (e.B.Length > 0) c.Afectos.Evento(e.A, e.B, TipoEvento.Agravio, g);
            if (g >= 0.7) c.Cronica.Anota(e.Dia, "herida", c.Nombre(e.A) + " cae gravemente herido", 4);
        }

        static void Llegada(EventoMundo e, ContextoVida c)
        {
            if (c.Linaje != null && !c.Linaje.Vivo(e.A)) c.Linaje.Nace(e.A, e.Dia - 7000, null, null);
            c.Cronica.Anota(e.Dia, "llegada", c.Nombre(e.A) + " llega al reino", 4);
        }

        static void Partida(EventoMundo e, ContextoVida c)
        {
            c.Cronica.Anota(e.Dia, "partida", c.Nombre(e.A) + " deja el reino" + (e.Texto.Length > 0 ? " (" + e.Texto + ")" : ""), 5);
            foreach (var o in c.Vivos())
            {
                if (o == e.A) continue;
                double v = Math.Max(0, c.Afectos.Get(o, e.A).Afecto);
                if (v >= 0.3) Recuerda(c, o, "partida", e.A, c.Nombre(e.A) + " se fue del reino", 2 + 3 * v, -0.5 * v);
            }
        }

        static void Trabajo(EventoMundo e, ContextoVida c)
        {
            var per = c.Persona(e.A);
            if (per != null) per.Needs.Pon("autorrealizacion", per.Needs.Autorrealizacion + 0.05);
            if (c.Mem != null) c.Mem.Registra(e.A, "trabajo", "", "termine " + (e.Texto.Length > 0 ? e.Texto : "un trabajo"), 0.5);
        }
    }
}
