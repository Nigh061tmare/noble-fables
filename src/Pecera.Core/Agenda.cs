using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    public enum TipoIntencion { Charlar, Visitar, Cortejar, Intrigar, Pedir, Aprender, Trabajar, Descansar, Consolar, Vengarse, Celebrar }
    public enum EstadoIntencion { Pendiente, EnCurso, Hecha, Fallida, Descartada }

    public sealed class Intencion
    {
        public int Id;
        public string Pawn = "";
        public TipoIntencion Tipo;
        public string Objetivo = "";        // id del otro pawn, o "" si no aplica
        public string Categoria = "";       // meta del reino a la que sirve (crecimiento, defensa, cultura, economia, cohesion)
        public string Texto = "";
        public double Prioridad = 0.5;
        public int Creada, Vence;
        public EstadoIntencion Estado = EstadoIntencion.Pendiente;
        public string Razon = "";
        public string Origen = "reglas";    // reglas | llm
        public bool Contada;                // ya conto para necesidades/ambiciones en un cierre de dia

        public bool Hostil { get { return Tipo == TipoIntencion.Intrigar || Tipo == TipoIntencion.Vengarse; } }
    }

    // Cola de intenciones por pawn (hoy / esta semana / largo plazo). Acotada por pawn y por dia para que
    // ni un LLM desbocado ni un bucle de reglas puedan llenarla. Solo hilo principal.
    public sealed class Agenda
    {
        readonly Dictionary<string, List<Intencion>> por = new Dictionary<string, List<Intencion>>();
        readonly Dictionary<string, int> altasHoy = new Dictionary<string, int>();
        int diaAltas = -1, sig = 1;
        public int MaxPorPawn = 6;
        public int MaxAltasPorDia = 3;
        public int Hoy { get; private set; }
        // Tras cerrar una intencion no se vuelve a proponer la misma durante estos dias (evita el spam de peticiones/venganzas).
        public int EnfriaPedir = 14, EnfriaHostil = 10, EnfriaCortejo = 3, EnfriaConsolar = 4;
        readonly Dictionary<string, int> cerradas = new Dictionary<string, int>();
        public int Hechas { get; private set; }
        public int Fallidas { get; private set; }

        public IList<Intencion> De(string pawn)
        {
            List<Intencion> l;
            return por.TryGetValue(pawn, out l) ? l : new List<Intencion>();
        }

        public int Count { get { int n = 0; foreach (var l in por.Values) n += l.Count; return n; } }

        // Devuelve la intencion guardada (nueva o la existente actualizada) o null si se rechazo por topes.
        public Intencion Anade(Intencion i, int dia)
        {
            if (dia != diaAltas) { altasHoy.Clear(); diaAltas = dia; }
            Hoy = dia;
            int hasta;
            if (cerradas.TryGetValue(Clave(i), out hasta) && dia < hasta) return null;
            List<Intencion> l;
            if (!por.TryGetValue(i.Pawn, out l)) { l = new List<Intencion>(); por[i.Pawn] = l; }
            foreach (var x in l)
                if (x.Tipo == i.Tipo && x.Objetivo == i.Objetivo && x.Estado <= EstadoIntencion.EnCurso)
                {
                    x.Prioridad = Math.Max(x.Prioridad, i.Prioridad);   // duplicado: solo se refuerza
                    x.Vence = Math.Max(x.Vence, i.Vence);
                    return x;
                }
            int hoy; altasHoy.TryGetValue(i.Pawn, out hoy);
            if (hoy >= MaxAltasPorDia) return null;
            int activas = 0; foreach (var x in l) if (x.Estado <= EstadoIntencion.EnCurso) activas++;
            if (activas >= MaxPorPawn)
            {
                // Entra solo si supera a la mas floja; esa se descarta.
                Intencion floja = null;
                foreach (var x in l) if (x.Estado <= EstadoIntencion.EnCurso && (floja == null || x.Prioridad < floja.Prioridad)) floja = x;
                if (floja == null || floja.Prioridad >= i.Prioridad) return null;
                floja.Estado = EstadoIntencion.Descartada;
            }
            i.Id = sig++; i.Creada = dia;
            if (i.Vence <= dia) i.Vence = dia + 3;
            l.Add(i);
            altasHoy[i.Pawn] = hoy + 1;
            return i;
        }

        // Las n intenciones vivas mas prioritarias (desempate por id => determinista).
        public List<Intencion> Top(string pawn, int n)
        {
            var r = new List<Intencion>();
            foreach (var x in De(pawn)) if (x.Estado <= EstadoIntencion.EnCurso) r.Add(x);
            r.Sort((a, b) => { int c = b.Prioridad.CompareTo(a.Prioridad); return c != 0 ? c : a.Id.CompareTo(b.Id); });
            if (r.Count > n) r.RemoveRange(n, r.Count - n);
            return r;
        }

        // Pedir se enfria por pawn (no por categoria): un pawn no pide una obra distinta cada dia.
        static string Clave(Intencion i) { return i.Tipo == TipoIntencion.Pedir ? i.Pawn + "|Pedir" : i.Pawn + "|" + i.Tipo + "|" + i.Objetivo + "|" + i.Categoria; }

        int Enfria(Intencion i)
        {
            if (i.Tipo == TipoIntencion.Pedir) return EnfriaPedir;
            if (i.Hostil) return EnfriaHostil;
            if (i.Tipo == TipoIntencion.Cortejar) return EnfriaCortejo;
            if (i.Tipo == TipoIntencion.Consolar) return EnfriaConsolar;
            return 0;
        }

        public void Marca(Intencion i, EstadoIntencion e)
        {
            bool antesVivo = i.Estado <= EstadoIntencion.EnCurso;
            i.Estado = e;
            if (antesVivo && e >= EstadoIntencion.Hecha && Enfria(i) > 0) cerradas[Clave(i)] = Hoy + Enfria(i);
            if (antesVivo && e == EstadoIntencion.Hecha) Hechas++;
            if (antesVivo && e == EstadoIntencion.Fallida) Fallidas++;
        }

        // Caduca lo vencido y poda lo cerrado hace tiempo (memoria acotada).
        public int Caduca(int dia)
        {
            Hoy = dia;
            if (cerradas.Count > 2000) { var viejas = new List<string>(); foreach (var kv in cerradas) if (kv.Value <= dia) viejas.Add(kv.Key); foreach (var k in viejas) cerradas.Remove(k); }
            int n = 0;
            foreach (var l in por.Values)
            {
                foreach (var x in l) if (x.Estado <= EstadoIntencion.EnCurso && x.Vence < dia) { x.Estado = EstadoIntencion.Descartada; n++; }
                l.RemoveAll(x => x.Estado >= EstadoIntencion.Hecha && x.Vence < dia - 10);
            }
            return n;
        }

        public Dictionary<TipoIntencion, int> PorTipo()
        {
            var d = new Dictionary<TipoIntencion, int>();
            foreach (var l in por.Values) foreach (var x in l) { int n; d.TryGetValue(x.Tipo, out n); d[x.Tipo] = n + 1; }
            return d;
        }
    }
}
