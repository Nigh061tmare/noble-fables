using System.Collections.Generic;
using Pecera.Core;
using Xunit;

namespace Pecera.Tests
{
    public class MemoriaTests
    {
        static Memoria Nueva(MemoryStorage d, ManualClock c) { return new Memoria(d, c) { MaxRecientes = 4, MantenerCrudos = 2, MaxMedio = 2 }; }

        [Fact]
        public void Reiniciar_conserva_los_recuerdos()
        {
            var d = new MemoryStorage(); var c = new ManualClock { Ticks = 100 };
            var m = Nueva(d, c);
            m.Registra("p1", "opinion", "p2", "empezo a odiar a Beto", 2);
            m.Registra("p1", "opinion", "p3", "empezo a apreciar a Cora", 1);
            var m2 = Nueva(d, c);          // "reinicio del juego"
            Assert.Equal(2, m2.Recientes("p1"));
            Assert.Contains("odiar a Beto", m2.ParaPrompt("p1", 400));
        }

        [Fact]
        public void Memoria_por_id_no_se_mezcla_entre_homonimos()
        {
            var d = new MemoryStorage(); var m = Nueva(d, new ManualClock { Ticks = 1 });
            m.Registra("pA", "x", "", "algo de A", 1);
            m.Registra("pB", "x", "", "algo de B", 1);
            Assert.DoesNotContain("de B", m.ParaPrompt("pA", 400));
        }

        [Fact]
        public void Resumen_reemplaza_los_viejos_y_mantiene_los_crudos()
        {
            var d = new MemoryStorage(); var c = new ManualClock { Ticks = 10 };
            var m = Nueva(d, c);
            for (int i = 0; i < 5; i++) { c.Ticks += 5; m.Registra("p1", "op", "", "hecho " + i, 1); }
            Assert.True(m.NecesitaResumen("p1"));
            var q = m.PreparaResumen("p1");
            Assert.Equal(3, q.Textos.Count);
            m.AplicaResumen(q, "Resumen: sufrio cinco agravios");
            Assert.Equal(2, m.Recientes("p1"));
            Assert.Equal(1, m.NivelesMedio("p1"));
            Assert.Contains("hecho 4", m.ParaPrompt("p1", 600));
            Assert.Contains("Resumen", m.ParaPrompt("p1", 600));
            // y sobrevive al reinicio, sin duplicar
            var m2 = Nueva(d, c);
            Assert.Equal(2, m2.Recientes("p1")); Assert.Equal(1, m2.NivelesMedio("p1"));
        }

        [Fact]
        public void Sin_llm_el_resumen_usa_fallback_y_sigue_acotado()
        {
            var d = new MemoryStorage(); var c = new ManualClock { Ticks = 1 };
            var m = Nueva(d, c);
            for (int i = 0; i < 400; i++) { c.Ticks += 1; m.Registra("p1", "op", "", "evento " + i, i % 7); }
            Assert.True(m.Recientes("p1") <= 8);          // poda dura: 2 x MaxRecientes
            var q = m.PreparaResumen("p1");
            m.AplicaResumen(q, null);                       // LLM caido
            Assert.True(m.NivelesMedio("p1") >= 1);
        }

        [Fact]
        public void Fusion_a_largo_plazo_y_persistencia()
        {
            var d = new MemoryStorage(); var c = new ManualClock { Ticks = 1 };
            var m = Nueva(d, c);
            for (int k = 0; k < 3; k++)
            {
                for (int i = 0; i < 5; i++) { c.Ticks += 3; m.Registra("p1", "op", "", "k" + k + "i" + i, 1); }
                m.AplicaResumen(m.PreparaResumen("p1"), "res" + k);
            }
            Assert.True(m.NecesitaFusion("p1"));
            Assert.Equal("p1", m.SiguienteParaFusionar());
            m.AplicaFusion("p1", "De larga data: rencores y deudas");
            Assert.Equal(0, m.NivelesMedio("p1"));
            Assert.Equal("De larga data: rencores y deudas", Nueva(d, c).Largo("p1"));
        }

        [Fact]
        public void Compactar_conserva_el_estado_exacto()
        {
            var d = new MemoryStorage(); var c = new ManualClock { Ticks = 1 };
            var m = Nueva(d, c);
            for (int i = 0; i < 10; i++) { c.Ticks += 2; m.Registra("p" + (i % 2), "op", "", "e" + i, 1); }
            m.AplicaResumen(m.PreparaResumen("p0"), "r0");
            string antes0 = m.ParaPrompt("p0", 900), antes1 = m.ParaPrompt("p1", 900);
            int lineas = d.ReadLines("memoria.jsonl").Length;
            m.Compacta();
            Assert.True(d.ReadLines("memoria.jsonl").Length <= lineas + 1);
            var m2 = Nueva(d, c);
            Assert.Equal(antes0, m2.ParaPrompt("p0", 900)); Assert.Equal(antes1, m2.ParaPrompt("p1", 900));
        }

        [Fact]
        public void Linea_corrupta_por_cierre_brusco_se_ignora_y_se_cuenta()
        {
            var d = new MemoryStorage(); var c = new ManualClock { Ticks = 1 };
            var m = Nueva(d, c);
            m.Registra("p1", "op", "", "bueno", 1);
            d.Append("memoria.jsonl", "{\"op\":\"ep\",\"id\":\"p1\",\"t\":5,\"tx\":\"cort");
            var m2 = Nueva(d, c);
            Assert.Equal(1, m2.Recientes("p1")); Assert.Equal(1, m2.LineasCorruptas);
        }

        [Fact]
        public void T_estrictamente_creciente_aunque_el_reloj_no_avance()
        {
            var d = new MemoryStorage(); var m = Nueva(d, new ManualClock { Ticks = 50 });
            for (int i = 0; i < 5; i++) m.Registra("p1", "op", "", "e" + i, 1);
            var q = m.PreparaResumen("p1");
            m.AplicaResumen(q, "r");
            Assert.Equal(2, m.Recientes("p1"));    // los dos crudos NO se pierden por T repetido
        }

        [Fact]
        public void Prompt_de_resumen_incluye_previo_y_recuerdos()
        {
            string u = Prompts.UsuarioResumen("Ana", "odia a Beto", new List<string> { "robo", "insulto" });
            Assert.Contains("odia a Beto", u); Assert.Contains("robo; insulto", u);
        }
    }

    public class IdentidadTests
    {
        [Fact]
        public void Misma_clave_estable_mismo_id_entre_sesiones()
        {
            var d = new MemoryStorage();
            string a = new RegistroIds(d).Resuelve("guid-123", "Ana", 11);
            string b = new RegistroIds(d).Resuelve("guid-123", "Ana", 99);
            Assert.Equal(a, b);
        }

        [Fact]
        public void Si_el_nombre_cambia_pero_la_clave_estable_no_el_id_se_mantiene()
        {
            var r = new RegistroIds(new MemoryStorage());
            Assert.Equal(r.Resuelve("g1", "Ana", 1), r.Resuelve("g1", "Ana la Roja", 1));
        }

        [Fact]
        public void Homonimos_sin_clave_estable_se_separan_y_se_marcan_ambiguos()
        {
            var r = new RegistroIds(new MemoryStorage());
            string a = r.Resuelve(null, "Oddny", 10);
            string b = r.Resuelve(null, "Oddny", 20);
            Assert.NotEqual(a, b);
            Assert.Equal(a, r.Resuelve(null, "Oddny", 10));
            Assert.Equal(b, r.Resuelve(null, "Oddny", 20));
            Assert.Equal(1, r.Ambiguos);
        }

        [Fact]
        public void Ficha_determinista_es_reproducible_y_se_persiste()
        {
            var d = new MemoryStorage();
            var f1 = new AlmacenFichas(d).GetOCrea("p1", "Ana");
            var f2 = new AlmacenFichas(d).GetOCrea("p1", "Ana");
            Assert.Equal(f1.ToJson(), f2.ToJson());
            Assert.Equal(f1.ToJson(), FichaGen.Determinista("p1", "Ana").ToJson());
            Assert.NotEmpty(f1.Resumen());
            Assert.Single(d.ReadLines("fichas.jsonl"));
        }

        [Fact]
        public void Ficha_desde_llm_mezcla_y_tolera_basura()
        {
            var f = FichaGen.DesdeLlm("p1", "Ana", "{\"rasgos\":[\"fiera\",\"leal\"],\"meta\":\"ser reina\",\"miedo\":\"el fuego\",\"voz\":\"ronca\"}");
            Assert.Equal("fiera", f.Rasgos[0]); Assert.Equal("ser reina", f.Metas[0]); Assert.Equal("ronca", f.Voz);
            var g = FichaGen.DesdeLlm("p1", "Ana", "basura total");
            Assert.Equal(FichaGen.Determinista("p1", "Ana").ToJson(), g.ToJson());
        }

        [Fact]
        public void Ficha_ida_y_vuelta_json()
        {
            var f = FichaGen.Determinista("p9", "Zed");
            Assert.Equal(f.ToJson(), Ficha.FromJson(f.ToJson()).ToJson());
        }
    }
}

namespace Pecera.Tests
{
    public class IdentidadEvidenciaTests
    {
        [Fact]
        public void Detecta_si_la_clave_del_juego_es_estable_entre_sesiones()
        {
            var d = new MemoryStorage();
            var s1 = new RegistroIds(d);
            s1.Resuelve("77", "Ana", 1); s1.Resuelve("88", "Beto", 2);
            var s2 = new RegistroIds(d);                       // reinicio, el juego conserva los ids
            s2.Resuelve("77", "Ana", 5); s2.Resuelve("88", "Beto", 6);
            Assert.Equal(2, s2.ClavesCoinciden); Assert.Equal(0, s2.ClavesDiscrepan);
            var s3 = new RegistroIds(d);                       // otro reinicio: el juego renumera
            s3.Resuelve("1", "Ana", 5);
            Assert.Equal(1, s3.ClavesDiscrepan);
        }
    }
}
