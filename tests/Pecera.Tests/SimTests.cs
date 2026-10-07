using System;
using System.Linq;
using Pecera.Core;
using Pecera.Sim;
using Xunit;

namespace Pecera.Tests
{
    // Criterios de aceptacion del simulador (H): el reino crece N dias sin atascarse,
    // sin oscilar y sin desbocarse, con semillas fijas.
    public class SimTests
    {
        static Resultado Corre(int seed, int dias, Action<SimConfig> ajusta = null)
        {
            var c = new SimConfig { Seed = seed, Dias = dias, Pawns = 20 };
            if (ajusta != null) ajusta(c);
            return new Mundo(c).Run();
        }

        [Fact]
        public void Es_determinista_con_la_misma_semilla()
        {
            Assert.Equal(Corre(3, 120).Informe, Corre(3, 120).Informe);
            Assert.NotEqual(Corre(3, 120).Informe, Corre(4, 120).Informe);
        }

        [Fact]
        public void Un_anio_sin_atascos_ni_oscilacion_con_varias_semillas()
        {
            for (int seed = 1; seed <= 6; seed++)
            {
                var r = Corre(seed, 360);
                Assert.True(r.MaxDiasSinProgreso <= 10, "semilla " + seed + ": atasco de " + r.MaxDiasSinProgreso + " dias");
                Assert.True(r.TasaCambioSigno < 0.10, "semilla " + seed + ": oscilacion " + r.TasaCambioSigno);
                Assert.InRange(r.RencorMax, 0, 1);
                Assert.True(r.Investigadas >= 10, "semilla " + seed + ": investigo " + r.Investigadas);
                Assert.True(r.Final.Estabilidad > 0.5, "semilla " + seed + ": estabilidad " + r.Final.Estabilidad);
                // el saber nunca retrocede
                for (int i = 1; i < r.Semanas.Count; i++) Assert.True(r.Semanas[i].M.Conocimiento >= r.Semanas[i - 1].M.Conocimiento - 1e-9);
            }
        }

        [Fact]
        public void El_gobierno_autonomo_hace_crecer_el_reino_frente_a_no_hacer_nada()
        {
            var on = Corre(2, 240); var off = Corre(2, 240, c => c.Gobierno = false);
            Assert.True(on.Final.Conocimiento > off.Final.Conocimiento + 0.2);
            Assert.Equal(0, off.Investigadas);
            Assert.True(on.Aprobadas > 0 && on.Denegadas > 0);        // ni todo si ni todo no
        }

        [Fact]
        public void Sin_llm_nada_se_rompe_y_la_memoria_sigue_acotada()
        {
            var r = Corre(5, 240, c => c.ProbLlmCae = 1.0);
            Assert.True(r.Investigadas >= 8);
            Assert.True(r.ResumenesFallback > 0 && r.ResumenesLlm == 0);
            Assert.True(r.MaxDiasSinProgreso <= 10);
        }

        [Fact]
        public void Apagar_capas_las_apaga_de_verdad()
        {
            var r = Corre(1, 200, c => { c.Rumores = false; c.Esquemas = false; });
            Assert.Equal(0, r.Fugas); Assert.Equal(0, r.EsquemasEjecutados); Assert.Equal(0, r.EsquemasPropuestos);
        }

        [Fact]
        public void Los_topes_diarios_se_respetan()
        {
            var r = Corre(1, 360, c => c.EsquemasDiaMax = 1);
            Assert.True(r.EsquemasEjecutados <= 360);
            var cero = Corre(1, 100, c => c.EsquemasDiaMax = 0);
            Assert.Equal(0, cero.EsquemasEjecutados);
        }

        [Fact]
        public void Los_rumores_se_propagan_sin_inundar()
        {
            int expuestos = 0;
            for (int seed = 1; seed <= 4; seed++) { var r = Corre(seed, 360); expuestos += r.SecretosExpuestos; Assert.True(r.Fugas < 20 * 19 * 2); }
            Assert.True(expuestos >= 2, "ningun secreto se filtro en 4 anios simulados");
        }

        [Fact]
        public void Salidas_se_generan()
        {
            var r = Corre(1, 120);
            Assert.Contains("# Cronica", r.CronicaMd);
            Assert.StartsWith("digraph", r.GrafoDot);
            Assert.Contains("dia,poblacion", Mundo.CsvSemanas(r));
        }
    }
}

namespace Pecera.Tests
{
    public class DocumentacionTests
    {
        [Fact]
        public void Config_md_esta_al_dia_con_el_esquema()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "Pecera.sln"))) dir = System.IO.Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            string esperado = Program.DocConfig().Replace("\r\n", "\n");
            string real = System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "docs", "CONFIG.md")).Replace("\r\n", "\n");
            Assert.Equal(esperado, real);
        }
    }
}
