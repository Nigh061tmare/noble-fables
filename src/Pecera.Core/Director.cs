using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    public enum EstiloDirector { Calmo, Clasico, Caotico }
    public enum TipoSugerencia { Escandalo, Rivalidad, Fiesta, Reconciliacion }

    public sealed class Sugerencia
    {
        public TipoSugerencia Tipo;
        public List<string> Pawns = new List<string>();
        public string Razon = "";
        public double TensionAntes, Objetivo;
    }

    // Director de drama (el «narrador» de RimWorld, que a su vez imita al AI Director de Left 4 Dead): mide la TENSION del
    // reino, la compara con una curva objetivo y, si el reino esta demasiado quieto, propone un conflicto; si esta demasiado
    // tenso, un alivio. No es azar: son sugerencias deterministas con semilla, con enfriamiento para no saturar. Las ejecuta
    // quien tenga el mundo (el simulador hoy; en el juego, pasaran por la Compuerta como decisiones con veto).
    public sealed class Director
    {
        public EstiloDirector Estilo = EstiloDirector.Clasico;
        public double Tension;              // media movil 0..1
        public int DiasCiclo = 24;          // clasico: sube de 0.2 a 0.7 y se relaja
        public int Enfriamiento = 3;        // dias minimos entre sugerencias
        public int Ultimo = -999;
        public double Banda = 0.12;
        readonly Rng rng;

        public Director(int semilla) { rng = new Rng(semilla); }

        public bool FiestaPedida;          // orden del jugador: la proxima sugerencia sera una fiesta (si el enfriamiento lo permite)
        public Func<string, bool> Protegido;   // el director no elige como blanco de drama a quien el jugador favorece

        public string Serializa() { return "{\"k\":\"di\",\"t\":" + Json.Num(Tension) + ",\"u\":" + Ultimo + "}"; }
        public void Carga(object d) { Tension = Json.Num(d, "t", 0); Ultimo = (int)Json.Num(d, "u", -999); }

        public double Objetivo(int dia)
        {
            switch (Estilo)
            {
                case EstiloDirector.Calmo: return 0.25;
                case EstiloDirector.Caotico: return 0.15 + 0.6 * new Rng(dia * 7919 + 13).Next();
                default: { double fase = (dia % Math.Max(2, DiasCiclo)) / (double)Math.Max(2, DiasCiclo); return 0.2 + 0.5 * fase; }
            }
        }

        // Tension instantanea: parejas con rencor alto (25 %), intensidad de los 5 peores rencores (20 %), intenciones hostiles vivas (20 %), peso de lo que acaba de pasar (20 %), descohesion (15 %).
        public static double Mide(ModeloAfectivo m, IList<string> vivos, Agenda ag, Cronica cr, int dia)
        {
            int pares = 0, altos = 0; double sentimiento = 0;
            var top = new List<double>();             // los rencores mas fuertes: un conflicto grave entre DOS personas tambien es tension
            foreach (var a in vivos) foreach (var b in vivos)
            {
                if (a == b) continue;
                pares++; Par p = m.Get(a, b);
                if (p.Rencor > 0.4) altos++;
                if (p.Rencor > 0.15) top.Add(p.Rencor);
                sentimiento += m.Sentimiento(a, b);
            }
            top.Sort((x, y) => y.CompareTo(x));
            double intensidad = 0; for (int i = 0; i < 5 && i < top.Count; i++) intensidad += top[i];
            intensidad = Math.Min(1, intensidad / 3.0);
            if (pares == 0) return 0;
            int hostiles = 0, vivas = 0;
            foreach (var a in vivos) foreach (var i in ag.De(a)) if (i.Estado <= EstadoIntencion.EnCurso) { vivas++; if (i.Hostil) hostiles++; }
            double reciente = 0;
            foreach (var h in cr.Hitos) if (h.Dia > dia - 10 && h.Dia <= dia) reciente += h.Peso;
            double t = 0.25 * Math.Min(1, 3.0 * altos / pares) + 0.2 * intensidad + 0.2 * (vivas > 0 ? Math.Min(1, 2.0 * hostiles / vivas) : 0)
                     + 0.2 * Math.Min(1, reciente / 120.0) + 0.15 * Math.Max(0, Math.Min(1, 0.5 - 0.5 * (sentimiento / pares) * 2));
            return Math.Max(0, Math.Min(1, t));
        }

        public Sugerencia Decide(int dia, double tensionInstantanea, ModeloAfectivo m, IList<string> vivos, Func<string, Persona> personas)
        {
            Tension = Tension * 0.8 + tensionInstantanea * 0.2;
            if (dia - Ultimo < Enfriamiento || vivos.Count < 3) return null;
            double obj = Objetivo(dia);
            var orden = new List<string>(vivos); orden.Sort(StringComparer.Ordinal);
            Sugerencia s = null;
            if (FiestaPedida)
            {
                FiestaPedida = false;
                s = new Sugerencia { Tipo = TipoSugerencia.Fiesta, Razon = "el soberano lo ha pedido" };
                int k0 = Math.Min(8, orden.Count); int i0 = rng.Next(orden.Count);
                for (int i = 0; i < k0; i++) s.Pawns.Add(orden[(i0 + i) % orden.Count]);
                s.TensionAntes = Tension; s.Objetivo = obj; Ultimo = dia;
                return s;
            }
            if (Tension < obj - Banda)
            {
                // Demasiado quieto: conflicto. Escandalo si hay secretos graves; si no, rivalidad entre dos ambiciosos.
                if (rng.Chance(0.5)) s = new Sugerencia { Tipo = TipoSugerencia.Escandalo, Razon = "el reino esta demasiado tranquilo" };
                else
                {
                    string a = null, b = null; double ma = -1, mb = -1;
                    foreach (var id in orden)
                    {
                        Persona p = personas(id); if (p == null) continue;
                        if (Protegido != null && Protegido(id)) continue;
                        double amb = p.Ambiciones.Count > 0 ? p.Ambiciones[0].Prioridad + (1 - p.Amabilidad) : 0;
                        if (amb > ma) { mb = ma; b = a; ma = amb; a = id; } else if (amb > mb) { mb = amb; b = id; }
                    }
                    if (a != null && b != null) { s = new Sugerencia { Tipo = TipoSugerencia.Rivalidad, Razon = "dos ambiciosos chocan" }; s.Pawns.Add(a); s.Pawns.Add(b); }
                }
            }
            else if (Tension > obj + Banda)
            {
                if (rng.Chance(0.5))
                {
                    s = new Sugerencia { Tipo = TipoSugerencia.Fiesta, Razon = "el reino necesita respirar" };
                    int k = Math.Min(8, orden.Count); int ini = rng.Next(orden.Count);
                    for (int i = 0; i < k; i++) s.Pawns.Add(orden[(ini + i) % orden.Count]);
                }
                else
                {
                    string a = null, b = null; double mr = 0.3;
                    foreach (var x in orden) foreach (var y in orden) { if (x == y) continue; double r = m.Get(x, y).Rencor; if (r > mr + 1e-12) { mr = r; a = x; b = y; } }
                    if (a != null) { s = new Sugerencia { Tipo = TipoSugerencia.Reconciliacion, Razon = "el peor rencor del reino pide una salida" }; s.Pawns.Add(a); s.Pawns.Add(b); }
                }
            }
            if (s == null) return null;
            s.TensionAntes = Tension; s.Objetivo = obj; Ultimo = dia;
            return s;
        }
    }
}
