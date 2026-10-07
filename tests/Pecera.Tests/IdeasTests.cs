using System;
using System.Collections.Generic;
using System.Linq;
using Pecera.Core;
using Xunit;

namespace Pecera.Tests
{
    public class JusticiaTests
    {
        static Acusacion Causa(double pruebas, double gravedad) { return new Acusacion { Acusador = "ac", Acusado = "reo", Delito = "robo", Pruebas = pruebas, Gravedad = gravedad }; }

        [Fact]
        public void Sin_pruebas_se_absuelve_y_con_pruebas_fuertes_se_condena()
        {
            var m = new ModeloAfectivo(null); var t = new Tribunal(m);
            Assert.Equal(Pena.Absolver, t.Juzga(Causa(0.05, 0.3), "juez", false).Pena);
            Assert.NotEqual(Pena.Absolver, t.Juzga(Causa(0.95, 0.9), "juez", false).Pena);
        }

        [Fact]
        public void El_sesgo_del_juez_cambia_el_fallo_y_queda_explicado()
        {
            var amigo = new ModeloAfectivo(null); var enemigo = new ModeloAfectivo(null);
            amigo.Evento("juez", "reo", TipoEvento.Ayuda, 1); amigo.Evento("juez", "reo", TipoEvento.Aprecio, 1);
            enemigo.Evento("juez", "reo", TipoEvento.Traicion, 1);
            var c = Causa(0.4, 0.5);
            var a = new Tribunal(amigo).Juzga(c, "juez", false); var e = new Tribunal(enemigo).Juzga(c, "juez", false);
            Assert.True(e.Culpa > a.Culpa);
            Assert.Contains("rencor", e.Razon);
        }

        [Fact]
        public void Sin_modo_dios_nunca_hay_penas_irreversibles()
        {
            var m = new ModeloAfectivo(null); m.Evento("juez", "reo", TipoEvento.Traicion, 1);
            var s = new Tribunal(m).Juzga(Causa(1, 1), "juez", false);
            Assert.True(s.Segura);
            var dios = new Tribunal(m).Juzga(Causa(1, 1), "juez", true);
            Assert.True(dios.Pena == Pena.Encarcelar || dios.Pena == Pena.Desterrar);
            Assert.False(dios.Segura);
        }

        [Fact]
        public void Las_consecuencias_afectivas_siguen_al_fallo()
        {
            var m = new ModeloAfectivo(null); var t = new Tribunal(m);
            t.Aplica(new Sentencia { Causa = Causa(0.1, 0.5), Juez = "juez", Pena = Pena.Absolver });
            Assert.True(m.Get("reo", "juez").Afecto > 0); Assert.True(m.Get("ac", "juez").Rencor > 0);
            var m2 = new ModeloAfectivo(null);
            new Tribunal(m2).Aplica(new Sentencia { Causa = Causa(0.9, 0.9), Juez = "juez", Pena = Pena.Multar });
            Assert.True(m2.Get("reo", "ac").Rencor > 0.3);
        }

        [Fact]
        public void Solo_las_fugas_graves_y_no_confesiones_llegan_a_juicio()
        {
            var s = new Secreto { Id = 1, Sujeto = "reo", Texto = "robo", Gravedad = 0.8 };
            Assert.NotNull(Tribunal.DesdeFuga(new Fuga { Emisor = "ac", Sujeto = "reo", Fidelidad = 0.9, Motivo = "malicia" }, s, 3));
            Assert.Null(Tribunal.DesdeFuga(new Fuga { Emisor = "reo", Sujeto = "reo", Fidelidad = 1, Motivo = "confesion" }, s, 3));
            s.Gravedad = 0.3;
            Assert.Null(Tribunal.DesdeFuga(new Fuga { Emisor = "ac", Sujeto = "reo", Fidelidad = 0.9, Motivo = "malicia" }, s, 3));
        }
    }

    public class LinajeTests
    {
        static Linaje Familia()
        {
            var l = new Linaje();
            l.Nace("rey", 0, null, null); l.Nace("reina", 0, null, null); l.Casa("rey", "reina");
            l.Nace("a", 20, "rey", "reina"); l.Nace("b", 22, "rey", "reina"); l.Nace("c", 25, "rey", "reina");
            return l;
        }

        [Fact]
        public void Herederos_por_edad_y_conyuge_si_no_hay_hijos()
        {
            var l = Familia(); l.Muere("rey");
            Assert.Equal(new[] { "a", "b", "c" }, l.Herederos("rey").ToArray());
            l.Muere("a"); l.Muere("b"); l.Muere("c");
            Assert.Equal(new[] { "reina" }, l.Herederos("rey").ToArray());
            l.Muere("reina");
            Assert.Empty(l.Herederos("rey"));
        }

        [Theory]
        [InlineData(100, 3)]
        [InlineData(101, 3)]
        [InlineData(7, 4)]
        [InlineData(0, 2)]
        public void El_reparto_conserva_los_bienes_exactamente(int bienes, int hijos)
        {
            var hs = Enumerable.Range(0, hijos).Select(i => "h" + i).ToList();
            var con = Linaje.Reparte(bienes, hs, "viuda");
            Assert.Equal(bienes, con.Values.Sum());
            var sin = Linaje.Reparte(bienes, hs, null);
            Assert.Equal(bienes, sin.Values.Sum());
            Assert.Equal(bienes, Linaje.Reparte(bienes, new List<string>(), "viuda").Values.Sum());
            Assert.True(sin.Values.Max() - sin.Values.Min() <= 1);
        }

        [Fact]
        public void Sucesion_prefiere_el_derecho_y_la_estima_y_detecta_disputas()
        {
            var l = Familia(); var m = new ModeloAfectivo(null);
            var reino = new List<string> { "reina", "a", "b", "c", "x" };
            foreach (var o in reino) foreach (var h in new[] { "a", "b", "c" }) if (o != h) m.Evento(o, "b", TipoEvento.Aprecio, 1);
            Func<string, Ficha> f = id => new Ficha { Id = id, Carisma = 0.5, Ambicion = 0.5 };
            l.Muere("rey");
            var r = Sucesion.Elige("rey", l, reino, m, f, 0.02);
            Assert.Equal("b", r.Sucesor);                     // le quieren
            // Sin diferencias, empate -> disputa y rivalidad en el modelo
            var m2 = new ModeloAfectivo(null);
            var r2 = Sucesion.Elige("rey", l, reino, m2, f, 0.5);
            Assert.True(r2.Disputada);
            Assert.True(m2.Get(r2.Sucesor, r2.Rival).Rivalidad > 0);
        }

        [Fact]
        public void Sin_herederos_legales_aspira_cualquiera_vivo_y_sin_nadie_no_hay_sucesor()
        {
            var l = new Linaje(); l.Nace("rey", 0, null, null); l.Nace("v1", 5, null, null); l.Nace("v2", 6, null, null); l.Muere("rey");
            var m = new ModeloAfectivo(null);
            var r = Sucesion.Elige("rey", l, new List<string> { "rey", "v1", "v2" }, m, id => new Ficha { Id = id, Carisma = id == "v2" ? 0.9 : 0.1 }, 0.01);
            Assert.Equal("v2", r.Sucesor);
            l.Muere("v1"); l.Muere("v2");
            Assert.Equal("sin candidatos", Sucesion.Elige("rey", l, new List<string> { "rey" }, m, id => null, 0.01).Razon);
        }
    }

    public class MentoriaTests
    {
        [Fact]
        public void Empareja_por_brecha_y_simpatia_con_tope_de_aprendices_y_sin_autoemparejarse()
        {
            var m = new ModeloAfectivo(null);
            var ids = new List<string> { "maestro", "a1", "a2", "a3", "odiado" };
            Func<string, string, double> nivel = (id, h) => id == "maestro" ? 0.9 : id == "odiado" ? 0.95 : 0.1;
            m.Evento("a1", "odiado", TipoEvento.Traicion, 1); m.Evento("a2", "odiado", TipoEvento.Traicion, 1); m.Evento("a3", "odiado", TipoEvento.Traicion, 1);
            var me = new Mentoria { MaxAprendicesPorMentor = 2 };
            me.Empareja(ids, nivel, new[] { "cocina" }, m, 0.2);
            Assert.Equal(2, me.Lazos.Count(l => l.Mentor == "maestro"));
            Assert.DoesNotContain(me.Lazos, l => l.Mentor == "odiado");
            Assert.All(me.Lazos, l => Assert.NotEqual(l.Mentor, l.Aprendiz));
            Assert.Equal(me.Lazos.Count, me.Lazos.Select(l => l.Aprendiz).Distinct().Count());
        }

        [Fact]
        public void El_aprendiz_converge_sin_alcanzar_nunca_al_maestro_y_nace_gratitud()
        {
            var m = new ModeloAfectivo(null); var me = new Mentoria();
            var l = new Mentoria.Lazo { Mentor = "m", Aprendiz = "a", Habilidad = "x" };
            double n = 0.1;
            for (int d = 0; d < 500; d++) { double nuevo = me.Avanza(l, 0.9, n, 1, m); Assert.True(nuevo >= n - 1e-12 && nuevo < 0.9); n = nuevo; }
            Assert.True(n > 0.85);
            Assert.True(m.Get("a", "m").Deuda > 0.2);
        }
    }

    public class CulturaTests
    {
        [Fact]
        public void El_credo_nace_del_valor_dominante_y_del_mito_mas_pesado()
        {
            var c = new Cultura();
            Assert.Equal(1.0, c.Reparto().Values.Sum(), 9);
            c.Suceso("clemencia", 10); c.Suceso("honor", 3);
            Assert.Equal("clemencia", c.Dominante());
            Assert.True(c.Observa(new Hito { Dia = 5, Texto = "el gran perdon", Peso = 9 }));
            Assert.False(c.Observa(new Hito { Dia = 6, Texto = "el gran perdon", Peso = 9 }));    // ya contado
            Assert.False(c.Observa(new Hito { Dia = 7, Texto = "menudencia", Peso = 2 }));
            Assert.Contains("Fe del Perdon", c.Credo()); Assert.Contains("el gran perdon", c.Credo());
            Assert.Throws<ArgumentException>(() => c.Suceso("inventado", 1));
        }

        [Fact]
        public void La_fe_esta_acotada_y_sube_con_fiestas_y_baja_con_soledad()
        {
            var c = new Cultura();
            for (int i = 0; i < 100; i++) { c.ActualizaFe("feliz", 5, 5, 0); c.ActualizaFe("solo", 0, 0, 1); }
            Assert.InRange(c.Fe("feliz"), 0, 1); Assert.InRange(c.Fe("solo"), 0, 1);
            Assert.True(c.Fe("feliz") > 0.8 && c.Fe("solo") < 0.05);
        }

        [Fact]
        public void Dialecto_cambia_solo_palabras_enteras_acotado_y_determinista()
        {
            Func<Dialecto> nuevo = () => { var d = new Dialecto { Max = 3 }; var r = new Rng(4); for (int i = 0; i < 10; i++) d.Deriva(r); return d; };
            var a = nuevo(); var b = nuevo();
            Assert.Equal(3, a.Count); Assert.Equal(a.Pista(), b.Pista());
            var d1 = new Dialecto(); d1.Deriva(new Rng(1)); d1.Deriva(new Rng(2));
            string t = "Hola amigo, ¿tienes pan? Casa grande; amigos tuyos";
            string r1 = d1.Aplica(t);
            Assert.Contains("amigos", r1);       // 'amigos' no es la palabra 'amigo'
            Assert.Equal(t, new Dialecto().Aplica(t));
            Assert.Equal("", new Dialecto().Aplica(null));
        }

        [Fact]
        public void Estaciones_son_modificadores_suaves_y_ciclicos()
        {
            for (int d = 0; d < 400; d += 7)
            {
                var e = Estaciones.De(d, 30);
                Assert.InRange(e.Sociabilidad, 0.7, 1.3); Assert.InRange(e.Irritabilidad, 0.7, 1.3); Assert.InRange(e.Fiesta, 0.6, 1.4);
            }
            Assert.Equal("primavera", Estaciones.De(0, 30).Nombre);
            Assert.Equal("invierno", Estaciones.De(95, 30).Nombre);
            Assert.Equal("primavera", Estaciones.De(120, 30).Nombre);
            Assert.True(Estaciones.De(100, 30).Irritabilidad > Estaciones.De(0, 30).Irritabilidad);
        }
    }

    public class SuenosEspiaTests
    {
        static Ficha F(string id, double amb) { var f = new Ficha { Id = id, Nombre = id, Ambicion = amb }; f.Metas.Add("descubrir el saber antiguo"); return f; }

        [Fact]
        public void El_sueno_avanza_solo_con_sucesos_de_su_categoria_da_inspiraciones_y_se_cumple_una_vez()
        {
            var s = new Suenos(); var m = new ModeloAfectivo(null);
            var f = F("ana", 0.5);
            Assert.Equal("cultura", s.De(f).Categoria);
            Assert.Empty(s.Avanza(f, "defensa", 1, m));
            int insp = 0, cumplidos = 0;
            for (int i = 0; i < 40; i++) foreach (var t in s.Avanza(f, "cultura", 0.1, m)) { insp++; if (t.Contains("cumple")) cumplidos++; }
            Assert.Equal(4, insp); Assert.Equal(1, cumplidos);
            Assert.Equal(1, s.Cumplidos);
            Assert.InRange(s.De(f).Progreso, 0, 1);
            Assert.Empty(s.Avanza(f, "cultura", 1, m));
        }

        [Fact]
        public void El_ambicioso_avanza_mas_deprisa()
        {
            var s = new Suenos(); var m = new ModeloAfectivo(null);
            s.Avanza(F("amb", 0.95), "cultura", 0.1, m); s.Avanza(F("tib", 0.1), "cultura", 0.1, m);
            Assert.True(s.De(F("amb", 0.95)).Progreso > s.De(F("tib", 0.1)).Progreso);
        }

        [Theory]
        [InlineData("enriquecerse", "economia")]
        [InlineData("proteger a los suyos", "defensa")]
        [InlineData("encontrar el amor", "cohesion")]
        [InlineData("ser alguien importante", "crecimiento")]
        public void Categoria_de_meta(string meta, string cat) { Assert.Equal(cat, Suenos.CategoriaDeMeta(meta)); }

        [Fact]
        public void Espionaje_obtiene_secretos_con_riesgo_y_consecuencias()
        {
            int exitos = 0, descubiertos = 0;
            for (int seed = 1; seed <= 200; seed++)
            {
                var m = new ModeloAfectivo(null); var c = new ManualClock { Ticks = 1 };
                var r = new RedSecretos(m, id => new Ficha { Id = id, Carisma = 0.9, Locuacidad = 0.8 }, c, new Rng(seed));
                r.Asigna("obj", "robo grano", 0.8);
                bool desc; var f = r.Espia("esp", "obj", out desc);
                if (f != null) { exitos++; Assert.Equal("espionaje", f.Motivo); Assert.True(r.Get(1).Saben.ContainsKey("esp")); Assert.True(f.Fidelidad < 1); }
                if (desc) { descubiertos++; Assert.True(m.Get("obj", "esp").Rencor > 0.3); }
            }
            Assert.InRange(exitos, 100, 200); Assert.True(descubiertos > 10 && descubiertos < 150);
            var m2 = new ModeloAfectivo(null); var r2 = new RedSecretos(m2, id => null, new ManualClock { Ticks = 1 }, new Rng(1));
            bool d2; Assert.Null(r2.Espia("a", "a", out d2)); Assert.Null(r2.Espia("a", "sinsecreto", out d2));
        }

        [Fact]
        public void Ficha_antigua_sin_ambicion_se_lee_con_defecto()
        {
            var f = Ficha.FromJson("{\"v\":1,\"id\":\"p1\",\"nombre\":\"Ana\",\"rasgos\":[],\"metas\":[],\"miedos\":[],\"voz\":\"\",\"carisma\":0.7,\"rencor\":0.4,\"locuacidad\":0.2}");
            Assert.Equal(0.5, f.Ambicion);
            Assert.Equal(f.ToJson(), Ficha.FromJson(f.ToJson()).ToJson());
        }
    }
}
