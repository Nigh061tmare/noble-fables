using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    public sealed class Hito
    {
        public int Dia;
        public string Tipo = "", Texto = "";
        public double Peso = 1;
    }

    // Cronica del reino (J): hitos con peso, resumen por temporada y leyendas (lo mas
    // pesado de todos los tiempos). Plantillas deterministas; el LLM puede pulir pero no
    // hace falta para tener cronica.
    public sealed class Cronica
    {
        readonly List<Hito> hitos = new List<Hito>();
        public int DiasPorTemporada = 30, TemporadasPorAnio = 4;
        public int MaxHitos = 5000;

        public int Count { get { return hitos.Count; } }
        public IList<Hito> Hitos { get { return hitos; } }

        public void Anota(int dia, string tipo, string texto, double peso)
        {
            hitos.Add(new Hito { Dia = dia, Tipo = tipo, Texto = Json.UnaLinea(texto), Peso = peso });
            if (hitos.Count > MaxHitos)
            {
                // Se conserva lo mas importante, no lo mas reciente.
                hitos.Sort((a, b) => b.Peso.CompareTo(a.Peso));
                hitos.RemoveRange(MaxHitos / 2, hitos.Count - MaxHitos / 2);
                hitos.Sort((a, b) => a.Dia.CompareTo(b.Dia));
            }
        }

        public int Temporada(int dia) { return dia / DiasPorTemporada; }
        public int Anio(int dia) { return dia / (DiasPorTemporada * TemporadasPorAnio); }
        static readonly string[] Nombres = { "primavera", "verano", "otono", "invierno" };
        public string NombreTemporada(int dia) { return Nombres[Temporada(dia) % Nombres.Length] + " del anio " + (Anio(dia) + 1); }

        public List<Hito> Leyendas(int n)
        {
            var l = new List<Hito>(hitos);
            l.Sort((a, b) => { int c = b.Peso.CompareTo(a.Peso); return c != 0 ? c : a.Dia.CompareTo(b.Dia); });
            if (l.Count > n) l.RemoveRange(n, l.Count - n);
            return l;
        }

        public string ResumenTemporada(int temporada)
        {
            var l = hitos.FindAll(h => Temporada(h.Dia) == temporada);
            if (l.Count == 0) return "Una temporada sin sucesos que contar.";
            l.Sort((a, b) => b.Peso.CompareTo(a.Peso));
            var sb = new StringBuilder();
            sb.Append("Hubo ").Append(l.Count).Append(l.Count == 1 ? " suceso. " : " sucesos. ");
            int top = Math.Min(3, l.Count);
            for (int i = 0; i < top; i++) sb.Append("Dia ").Append(l[i].Dia).Append(": ").Append(l[i].Texto).Append(i < top - 1 ? ". " : ".");
            return sb.ToString();
        }

        public string ToMarkdown(string nombreReino)
        {
            var sb = new StringBuilder();
            sb.Append("# Cronica de ").Append(nombreReino).Append("\n\n");
            var ley = Leyendas(5);
            if (ley.Count > 0)
            {
                sb.Append("## Leyendas\n\n");
                foreach (var h in ley) sb.Append("- (dia ").Append(h.Dia).Append(") ").Append(h.Texto).Append('\n');
                sb.Append('\n');
            }
            int max = 0; foreach (var h in hitos) if (Temporada(h.Dia) > max) max = Temporada(h.Dia);
            for (int t = hitos.Count == 0 ? 0 : max; t >= 0 && max - t < 12; t--)
            {
                sb.Append("## ").Append(char.ToUpperInvariant(NombreTemporada(t * DiasPorTemporada)[0])).Append(NombreTemporada(t * DiasPorTemporada).Substring(1)).Append("\n\n");
                sb.Append(ResumenTemporada(t)).Append("\n\n");
            }
            return sb.ToString();
        }
    }
}
