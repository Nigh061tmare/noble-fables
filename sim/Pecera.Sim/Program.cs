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
                    case "--doc-config": Console.Write(DocConfig()); return 0;
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

        // docs/CONFIG.md se genera de PeceraConfig.Schema: la documentacion no puede desfasarse
        // (un test compara este texto con el fichero).
        public static string DocConfig()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("# config.txt: referencia\n\n");
            sb.Append("Generado desde el codigo (`dotnet run --project sim/Pecera.Sim -- --doc-config > docs/CONFIG.md`). No lo edites a mano.\n\n");
            sb.Append("Las claves marcadas **escribe en el juego** modifican el estado de la partida. Mientras no esten VERIFICADAS en partida vienen apagadas (`0`) ");
            sb.Append("y `modo=observador` las anula todas.\n\n");
            sb.Append("| Clave | Defecto | Rango | Estado | Que hace |\n|---|---|---|---|---|\n");
            foreach (var e in PeceraConfig.Schema)
            {
                string rango = e.Numeric ? Json.Num(e.Min) + " .. " + Json.Num(e.Max) : "texto";
                string st = e.Estado == Estado.Verificado ? "VERIFICADO" : e.Estado == Estado.Leido ? "LEIDO" : "NO VERIFICADO";
                if (e.EscribeJuego) st += " - **escribe en el juego**";
                sb.Append("| `").Append(e.Key).Append("` | `").Append(e.Default.Length == 0 ? "(vacio)" : e.Default).Append("` | ").Append(rango).Append(" | ").Append(st).Append(" | ").Append(e.Doc.Replace("|", "\\|")).Append(" |\n");
            }
            return sb.ToString();
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
