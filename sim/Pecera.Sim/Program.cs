using System;
using System.Globalization;
using System.IO;
using Pecera.Core;

namespace Pecera.Sim
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            var c = new SimConfig();
            string salida = "sim_out";
            bool barrido = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--dias": c.Dias = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--pawns": c.Pawns = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--seed": c.Seed = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--sin-gobierno": c.Gobierno = false; break;
                    case "--sin-esquemas": c.Esquemas = false; break;
                    case "--sin-rumores": c.Rumores = false; break;
                    case "--umbral": c.UmbralAprobacion = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--out": salida = args[++i]; break;
                    case "--barrido": barrido = true; break;
                    default: Console.Error.WriteLine("argumento desconocido: " + args[i]); return 2;
                }
            }
            if (barrido) return Barrido(c);
            var r = new Mundo(c).Run();
            Directory.CreateDirectory(salida);
            File.WriteAllText(Path.Combine(salida, "informe.md"), r.Informe);
            File.WriteAllText(Path.Combine(salida, "cronica.md"), r.CronicaMd);
            File.WriteAllText(Path.Combine(salida, "grafo.dot"), r.GrafoDot);
            File.WriteAllText(Path.Combine(salida, "metricas.csv"), Mundo.CsvSemanas(r));
            Console.WriteLine(r.Informe);
            return 0;
        }

        // Barrido de umbrales: evidencia para elegir los valores por defecto.
        static int Barrido(SimConfig c)
        {
            Console.WriteLine("umbral,seed,aprobadas,denegadas,investigadas,rencor_max,cambios_signo_pct,max_dias_sin_progreso,conocimiento,riqueza,estabilidad");
            foreach (double u in new[] { 0.35, 0.40, 0.45, 0.50, 0.55 })
                for (int seed = 1; seed <= 5; seed++)
                {
                    var cc = new SimConfig { Dias = c.Dias, Pawns = c.Pawns, Seed = seed, UmbralAprobacion = u };
                    var r = new Mundo(cc).Run();
                    var f = r.Final;
                    Console.WriteLine(string.Join(",", new[] {
                        Json.Num(u), seed.ToString(CultureInfo.InvariantCulture), r.Aprobadas.ToString(CultureInfo.InvariantCulture), r.Denegadas.ToString(CultureInfo.InvariantCulture),
                        r.Investigadas.ToString(CultureInfo.InvariantCulture), Json.Num(r.RencorMax), Json.Num(100 * r.TasaCambioSigno),
                        r.MaxDiasSinProgreso.ToString(CultureInfo.InvariantCulture), Json.Num(f.Conocimiento), Json.Num(f.Riqueza), Json.Num(f.Estabilidad) }));
                }
            return 0;
        }
    }
}
