using System;
using System.Collections.Generic;
using Pecera.Core;
using Xunit;

namespace Pecera.Tests
{
    public class AfectosTests
    {
        static ModeloAfectivo M() { return new ModeloAfectivo(id => 0.5); }

        static void EnRango(Par p)
        {
            Assert.InRange(p.Afecto, -1, 1); Assert.InRange(p.Confianza, 0, 1); Assert.InRange(p.Rencor, 0, 1);
            Assert.InRange(p.Deuda, -1, 1); Assert.InRange(p.Rivalidad, 0, 1); Assert.InRange(p.Romance, 0, 1);
            Assert.InRange(p.Trauma, 0, 1);
        }

        [Fact]
        public void Propiedad_fuzz_nunca_sale_de_rango()
        {
            for (int seed = 1; seed <= 25; seed++)
            {
                var r = new Rng(seed); var m = M();
                var tipos = (TipoEvento[])Enum.GetValues(typeof(TipoEvento));
                for (int i = 0; i < 2000; i++)
                {
                    m.Evento("a", "b", tipos[r.Next(tipos.Length)], r.Next() * 1.5);   // magnitud >1 tambien
                    if (r.Chance(0.3)) m.Avanza(r.Range(0, 20));
                    EnRango(m.Get("a", "b"));
                }
            }
        }

        [Fact]
        public void Sin_eventos_converge_monotonamente_sin_oscilar()
        {
            var m = M();
            foreach (var t in (TipoEvento[])Enum.GetValues(typeof(TipoEvento))) m.Evento("a", "b", t, 0.9);
            Par p = m.Get("a", "b");
            double[] prev = { p.Afecto, p.Confianza - 0.5, p.Rencor, p.Deuda, p.Rivalidad, p.Romance, p.Trauma };
            double[] signo = new double[prev.Length];
            for (int i = 0; i < prev.Length; i++) signo[i] = Math.Sign(prev[i]);
            for (int dia = 0; dia < 2000; dia++)
            {
                m.Avanza(1);
                p = m.Get("a", "b");
                double[] cur = { p.Afecto, p.Confianza - 0.5, p.Rencor, p.Deuda, p.Rivalidad, p.Romance, p.Trauma };
                for (int i = 0; i < cur.Length; i++)
                {
                    Assert.True(Math.Abs(cur[i]) <= Math.Abs(prev[i]) + 1e-12, "no monotono var " + i + " dia " + dia);
                    if (signo[i] != 0) Assert.True(Math.Sign(cur[i]) == signo[i] || Math.Abs(cur[i]) < 1e-9, "cruzo baseline var " + i);
                }
                prev = cur;
            }
            Assert.True(Math.Abs(m.Get("a", "b").Afecto) < 1e-6);
        }

        [Fact]
        public void Avanzar_en_un_paso_o_en_muchos_da_lo_mismo()
        {
            var a = M(); var b = M();
            a.Evento("x", "y", TipoEvento.Traicion, 0.8); b.Evento("x", "y", TipoEvento.Traicion, 0.8);
            a.Avanza(30); for (int i = 0; i < 30; i++) b.Avanza(1);
            Assert.Equal(a.Get("x", "y").Rencor, b.Get("x", "y").Rencor, 9);
        }

        [Fact]
        public void El_afecto_es_dirigido()
        {
            var m = M(); m.Evento("a", "b", TipoEvento.Agravio, 0.8);
            Assert.True(m.Get("a", "b").Rencor > 0.5);
            Assert.Equal(0, m.Get("b", "a").Rencor);
        }

        [Fact]
        public void El_trauma_amplifica_los_agravios_del_mismo_agresor()
        {
            var sin = M(); var con = M();
            con.Evento("a", "b", TipoEvento.Duelo, 1);
            con.Avanza(5); sin.Avanza(5);
            sin.Evento("a", "b", TipoEvento.Agravio, 0.3); con.Evento("a", "b", TipoEvento.Agravio, 0.3);
            Assert.True(con.Get("a", "b").Rencor > sin.Get("a", "b").Rencor);
        }

        [Fact]
        public void Rencoroso_olvida_mas_despacio_que_templado()
        {
            var m = new ModeloAfectivo(id => id == "duro" ? 1.0 : 0.0);
            m.Evento("duro", "x", TipoEvento.Agravio, 0.8); m.Evento("blando", "x", TipoEvento.Agravio, 0.8);
            m.Avanza(30);
            Assert.True(m.Get("duro", "x").Rencor > m.Get("blando", "x").Rencor);
        }

        [Fact]
        public void Reconciliacion_perdon_enfria_el_rencor_y_la_gratitud_crea_deuda()
        {
            var m = M(); m.Evento("a", "b", TipoEvento.Agravio, 0.9);
            double antes = m.Get("a", "b").Rencor;
            m.Evento("a", "b", TipoEvento.Perdon, 1);
            Assert.True(m.Get("a", "b").Rencor < antes * 0.5);
            m.Evento("c", "d", TipoEvento.Ayuda, 0.8);
            Assert.True(m.Get("c", "d").Deuda > 0.3);
        }

        [Fact]
        public void Rencor_sostenido_se_cuenta_en_dias_y_se_reinicia_al_enfriarse()
        {
            var m = M(); m.Evento("a", "b", TipoEvento.Traicion, 1);
            for (int i = 0; i < 5; i++) m.Avanza(1);
            Assert.True(m.Get("a", "b").DiasRencorAlto >= 5);
            Assert.Single(m.RencoresSostenidos(3, 0.5));
            m.Evento("a", "b", TipoEvento.Perdon, 1); m.Avanza(1);
            Assert.Equal(0, m.Get("a", "b").DiasRencorAlto);
            Assert.Empty(m.RencoresSostenidos(3, 0.5));
        }

        [Fact]
        public void DesdeOpinion_rasgo_pesa_menos_que_hecho()
        {
            var m = M();
            m.DesdeOpinion("a", "b", -2, true); m.DesdeOpinion("a", "c", -2, false);
            Assert.True(m.Get("a", "c").Rencor > 3 * m.Get("a", "b").Rencor);
        }

        [Fact]
        public void Un_par_que_vuelve_a_su_baseline_se_olvida()
        {
            var m = M();
            m.Evento("a", "b", TipoEvento.Agravio, 0.5);
            Assert.Equal(1, m.Pares);
            m.Avanza(2000);
            Assert.Equal(0, m.Pares);
            Assert.Equal(0, m.Get("a", "b").Rencor);
        }

        [Fact]
        public void Serializa_y_carga_sin_perdida()
        {
            var m = M(); var r = new Rng(3);
            for (int i = 0; i < 50; i++) { m.Evento("a" + r.Next(5), "b" + r.Next(5), (TipoEvento)r.Next(9), r.Next()); m.Avanza(1.5); }
            var m2 = M(); m2.Carga(m.Serializa());
            Assert.Equal(m.Pares, m2.Pares);
            foreach (var kv in m.Todos())
            {
                string a, b; ModeloAfectivo.Separa(kv.Key, out a, out b);
                Assert.True(Math.Abs(kv.Value.Rencor - m2.Get(a, b).Rencor) < 0.001);
                Assert.True(Math.Abs(kv.Value.Afecto - m2.Get(a, b).Afecto) < 0.001);
            }
        }
    }
}
