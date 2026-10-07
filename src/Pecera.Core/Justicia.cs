using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    public enum Pena { Absolver, Amonestar, Multar, Encarcelar, Desterrar }

    public sealed class Acusacion
    {
        public string Acusador = "", Acusado = "", Delito = "";
        public double Gravedad = 0.5;     // 0..1
        public double Pruebas = 0.5;      // 0..1 (fidelidad de lo que se sabe)
        public int Dia;
    }

    public sealed class Sentencia
    {
        public Acusacion Causa;
        public string Juez = "";
        public Pena Pena;
        public double Culpa;              // 0..1, lo convencido que queda el juez
        public string Razon = "";
        public bool Segura { get { return Pena == Pena.Absolver || Pena == Pena.Amonestar || Pena == Pena.Multar; } }
    }

    // Justicia y juicios (F). El juez es un pawn (el soberano o un lider): su sentencia depende
    // de las pruebas, de lo creible que sea el acusador y de SUS PROPIOS afectos (sesgo medido,
    // no oculto). Las penas Encarcelar/Desterrar solo se proponen en modo dios: son las
    // irreversibles. Todo pasa luego por la Compuerta (clase "juicio") con ventana de veto.
    public sealed class Tribunal
    {
        readonly ModeloAfectivo afectos;
        public double UmbralCulpa = 0.45;

        public Tribunal(ModeloAfectivo afectos) { this.afectos = afectos; }

        // Convierte una fuga grave en acusacion (o null si no da para juicio).
        public static Acusacion DesdeFuga(Fuga f, Secreto s, int dia)
        {
            if (f == null || s == null || f.Motivo == "confesion" || s.Gravedad < 0.6) return null;
            return new Acusacion { Acusador = f.Emisor, Acusado = f.Sujeto, Delito = s.Texto, Gravedad = s.Gravedad, Pruebas = f.Fidelidad * 0.8, Dia = dia };
        }

        public Sentencia Juzga(Acusacion a, string juez, bool permitirIrreversibles)
        {
            double credibilidad = afectos.Get(juez, a.Acusador).Confianza;                 // se fia del acusador
            double estimaAcusado = (afectos.Sentimiento(juez, a.Acusado) + 1) / 2;         // 0..1
            double rencorAcusado = afectos.Get(juez, a.Acusado).Rencor;
            double culpa = 0.55 * a.Pruebas + 0.25 * credibilidad - 0.25 * estimaAcusado + 0.25 * rencorAcusado + 0.1 * a.Gravedad;
            culpa = Math.Max(0, Math.Min(1, culpa + 0.1));
            var s = new Sentencia { Causa = a, Juez = juez, Culpa = culpa };
            if (culpa < UmbralCulpa) s.Pena = Pena.Absolver;
            else
            {
                double dureza = culpa * (0.5 + a.Gravedad);
                if (dureza < 0.55) s.Pena = Pena.Amonestar;
                else if (dureza < 0.8 || !permitirIrreversibles) s.Pena = Pena.Multar;
                else s.Pena = dureza < 0.95 ? Pena.Encarcelar : Pena.Desterrar;
            }
            s.Razon = "culpa " + Json.Num(culpa) + " (pruebas " + Json.Num(a.Pruebas) + ", credibilidad " + Json.Num(credibilidad) + ", estima " + Json.Num(estimaAcusado)
                    + ", rencor " + Json.Num(rencorAcusado) + ", gravedad " + Json.Num(a.Gravedad) + ") -> " + s.Pena;
            return s;
        }

        // Consecuencias en el modelo afectivo. Un fallo duro agravia al condenado contra el
        // acusador y, si se le tenia estima, contra el juez; una absolucion enfria al acusador.
        public void Aplica(Sentencia s)
        {
            var a = s.Causa;
            switch (s.Pena)
            {
                case Pena.Absolver:
                    afectos.Evento(a.Acusado, a.Acusador, TipoEvento.Agravio, 0.25 * a.Gravedad);      // la acusacion ofende
                    afectos.Evento(a.Acusado, s.Juez, TipoEvento.Aprecio, 0.25);
                    afectos.Evento(a.Acusador, s.Juez, TipoEvento.Agravio, 0.15);
                    break;
                case Pena.Amonestar:
                    afectos.Evento(a.Acusado, a.Acusador, TipoEvento.Agravio, 0.3);
                    break;
                default:
                    double dur = s.Pena == Pena.Multar ? 0.4 : s.Pena == Pena.Encarcelar ? 0.7 : 0.9;
                    afectos.Evento(a.Acusado, a.Acusador, TipoEvento.Traicion, dur);
                    afectos.Evento(a.Acusado, s.Juez, TipoEvento.Agravio, dur * 0.6);
                    afectos.Evento(a.Acusador, s.Juez, TipoEvento.Aprecio, 0.2);
                    break;
            }
        }
    }
}
