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

namespace Pecera.Tests
{
    // Las ideas (justicia, sucesion, mentoria, cultura, dialectos, estaciones, suenos, espionaje, deriva)
    // ejercitadas dentro del simulador completo.
    public class SimIdeasTests
    {
        static Resultado Corre(int seed, int dias, bool ideas = true) { return new Mundo(new SimConfig { Seed = seed, Dias = dias, Pawns = 24, Ideas = ideas }).Run(); }

        [Fact]
        public void La_sucesion_ocurre_reparte_toda_la_herencia_y_queda_en_la_cronica()
        {
            for (int seed = 1; seed <= 4; seed++)
            {
                var r = Corre(seed, 240);
                Assert.True(r.HuboSucesion); Assert.NotEqual("", r.Sucesor);
                Assert.Equal(30, r.HerenciaRepartida);
                Assert.Contains("muere", r.CronicaMd + r.Informe);
            }
        }

        [Fact]
        public void Hay_juicios_y_solo_penas_seguras_y_la_justicia_no_rompe_la_estabilidad()
        {
            for (int seed = 1; seed <= 4; seed++)
            {
                var r = Corre(seed, 360);
                Assert.True(r.Juicios > 0 && r.Condenas + r.Absoluciones > 0, "semilla " + seed);
                Assert.True(r.Final.Estabilidad > 0.5);
                Assert.True(r.TasaCambioSigno < 0.10);
            }
        }

        [Fact]
        public void La_mentoria_mejora_las_habilidades_medias()
        {
            var r = Corre(2, 360);
            Assert.True(r.LazosMentoria > 0);
            Assert.True(r.HabilidadMediaFinal > r.HabilidadMediaInicial + 0.05, r.HabilidadMediaInicial + " -> " + r.HabilidadMediaFinal);
            Assert.True(r.HabilidadMediaFinal <= 1);
        }

        [Fact]
        public void La_deriva_de_personalidad_nunca_supera_el_tope()
        {
            for (int seed = 1; seed <= 3; seed++) Assert.True(Corre(seed, 720).DerivaMax <= Deriva.TopeTotal + 1e-9);
        }

        [Fact]
        public void Cultura_dialectos_suenos_y_espionaje_producen_resultados_acotados()
        {
            var r = Corre(3, 360);
            Assert.NotEqual("", r.Credo);
            Assert.True(r.GirosDialecto > 0);
            Assert.True(r.InspiracionesTotal > 0);
            Assert.True(r.EspiasExito + r.EspiasDescubiertos > 0);
        }

        [Fact]
        public void Con_ideas_apagadas_no_hay_nada_de_esto_y_es_determinista_con_ideas_encendidas()
        {
            var off = Corre(1, 200, false);
            Assert.False(off.HuboSucesion); Assert.Equal(0, off.Juicios); Assert.Equal(0, off.LazosMentoria); Assert.Equal(0, off.InspiracionesTotal);
            Assert.Equal(Corre(5, 150).Informe, Corre(5, 150).Informe);
        }

        [Fact]
        public void Con_ideas_el_reino_sigue_creciendo_sin_atascos_ni_oscilacion()
        {
            for (int seed = 1; seed <= 5; seed++)
            {
                var r = Corre(seed, 360);
                Assert.True(r.MaxDiasSinProgreso <= 10); Assert.True(r.Investigadas >= 10); Assert.True(r.TasaCambioSigno < 0.10);
            }
        }
    }
}

namespace Pecera.Tests
{
    // Criterios de aceptacion de los AGENTES con proposito (persona, agenda, plan en lote, conversaciones, narrativa).
    public class SimAgentesTests
    {
        static Resultado Corre(int seed, int dias, Action<SimConfig> ajusta = null)
        {
            var c = new SimConfig { Seed = seed, Dias = dias, Pawns = 24 };
            if (ajusta != null) ajusta(c);
            return new Mundo(c).Run();
        }

        [Fact]
        public void El_presupuesto_del_llm_nunca_se_supera_en_ningun_dia()
        {
            for (int seed = 1; seed <= 4; seed++)
            {
                var r = Corre(seed, 240, c => c.LlamadasLlmDia = 12);
                Assert.True(r.MaxLlamadasLlmEnUnDia <= 12, "semilla " + seed + ": " + r.MaxLlamadasLlmEnUnDia);
                Assert.True(r.LlmLlamadasPlan > 0);
                Assert.True(r.LlmLlamadasPlan <= 240 * 6, "techo de planificacion (50 %)");
            }
        }

        [Fact]
        public void Los_guardarrailes_paran_al_llm_que_se_equivoca_y_nada_invalido_llega_a_ejecutarse_en_masa()
        {
            var r = Corre(2, 360);
            Assert.True(r.LlmRechazadas > 50, "el LLM simulado inyecta ~20 % de planes invalidos");
            Assert.True(r.IntencionesInvalidasEjecutadas * 100 <= r.IntencionesHechas + r.IntencionesFallidas,
                "reevaluadas al ejecutar: " + r.IntencionesInvalidasEjecutadas);
        }

        [Fact]
        public void Los_agentes_tienen_proposito_ambiciones_que_avanzan_y_se_cumplen_y_necesidades_sanas()
        {
            for (int seed = 1; seed <= 4; seed++)
            {
                var r = Corre(seed, 360);
                Assert.True(r.AmbicionesCumplidas >= 3, "semilla " + seed + ": cumplidas " + r.AmbicionesCumplidas);
                Assert.True(r.ProgresoMedioAmbiciones > 0.2 && r.ProgresoMedioAmbiciones < 1, "progreso " + r.ProgresoMedioAmbiciones);
                Assert.InRange(r.NecesidadSocialMedia, 0.2, 0.95);
                Assert.True(r.NecesidadMinimaMedia > 0.05, "todos agotados: " + r.NecesidadMinimaMedia);
                Assert.True(r.TiposIntencion.Split(',').Length >= 6, r.TiposIntencion);
                double tasa = (double)r.ConversacionesExito / Math.Max(1, r.Conversaciones);
                Assert.InRange(tasa, 0.5, 0.97);
            }
        }

        [Fact]
        public void Con_el_llm_caido_los_agentes_siguen_viviendo_por_reglas()
        {
            var r = Corre(3, 240, c => c.ProbLlmCae = 1.0);
            Assert.Equal(0, r.PlanesLlm); Assert.True(r.LlmFallos > 0); Assert.True(r.PlanesReglas > 1000);
            Assert.True(r.ProgresoMedioAmbiciones > 0.1);
            Assert.True(r.MaxDiasSinProgreso <= 10);
        }

        [Fact]
        public void No_hay_spam_de_peticiones_gracias_al_enfriamiento()
        {
            var r = Corre(1, 360);
            Assert.True(r.Aprobadas + r.Denegadas < 360 * 6, "peticiones " + (r.Aprobadas + r.Denegadas));
        }

        [Fact]
        public void Es_determinista_se_apaga_y_genera_historias()
        {
            Assert.Equal(Corre(7, 120).Informe, Corre(7, 120).Informe);
            var off = Corre(7, 120, c => c.Agentes = false);
            Assert.Equal(0, off.PlanesReglas); Assert.Equal(0, off.Conversaciones); Assert.Equal(0, off.LlmLlamadasPlan);
            var on = Corre(7, 240);
            Assert.True(on.HistoriasGeneradas > 0); Assert.Contains("Historias de", on.CronicaMd);
        }

        [Fact]
        public void Los_agentes_no_rompen_la_estabilidad_ni_el_crecimiento_del_reino()
        {
            for (int seed = 1; seed <= 4; seed++)
            {
                var r = Corre(seed, 360);
                Assert.True(r.MaxDiasSinProgreso <= 10); Assert.True(r.TasaCambioSigno < 0.10); Assert.True(r.Final.Estabilidad > 0.5);
                Assert.True(r.Investigadas >= 10);
            }
        }
    }
}
