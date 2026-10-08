using System;
using System.Collections.Generic;
using System.Linq;
using Pecera.Core;
using Pecera.Sim;
using Xunit;

namespace Pecera.Tests
{
    public class RecuperacionTests
    {
        static Memoria M(ManualClock c) { return new Memoria(new MemoryStorage(), c) { MaxRecientes = 50 }; }

        [Fact]
        public void La_relevancia_pesa_mas_que_la_recencia_y_la_importancia_desempata()
        {
            var c = new ManualClock { Ticks = 10 }; var m = M(c);
            m.Registra("p", "x", "q", "discutio con Beto por el herrero", 1);
            m.Registra("p", "x", "q", "compro pan en el mercado", 1);
            m.Registra("p", "x", "q", "paseo por el bosque", 1);
            m.Registra("p", "x", "q", "vio llover toda la tarde", 1);
            var r = m.Recupera("p", "Beto el herrero", 2);
            Assert.Equal("discutio con Beto por el herrero", r[0]);                  // la mas antigua, pero la unica relevante
            m.Registra("p", "x", "q", "Beto lo traiciono", 9);
            Assert.Equal("Beto lo traiciono", m.Recupera("p", "algo de Beto", 1)[0]); // relevante + muy importante
        }

        [Fact]
        public void Sin_consulta_manda_importancia_y_recencia_y_es_determinista_incluye_resumenes()
        {
            var c = new ManualClock { Ticks = 10 }; var m = new Memoria(new MemoryStorage(), c) { MaxRecientes = 4, MantenerCrudos = 2 };
            for (int i = 0; i < 5; i++) { c.Ticks += 3; m.Registra("p", "x", "", "evento " + i, i == 1 ? 9 : 1); }
            m.AplicaResumen(m.PreparaResumen("p"), "resumen de lo vivido");
            var a = m.Recupera("p", "", 3); var b = m.Recupera("p", "", 3);
            Assert.Equal(a, b); Assert.Equal(3, a.Count);
            Assert.Contains("resumen de lo vivido", m.Recupera("p", "resumen", 5));
            Assert.Empty(m.Recupera("nadie", "x", 3));
        }

        [Fact]
        public void La_reflexion_se_dispara_por_importancia_acumulada_y_se_reinicia()
        {
            var m = M(new ManualClock { Ticks = 1 }); m.UmbralReflexion = 10;
            for (int i = 0; i < 9; i++) m.Registra("p", "x", "q", "poca cosa " + i, 1);
            Assert.False(m.ToqueReflexion("p"));
            m.Registra("p", "x", "q", "algo gordo", 5);
            Assert.True(m.ToqueReflexion("p")); Assert.False(m.ToqueReflexion("p"));
            m.Registra("p", "reflexion", "q", "una conclusion", 8);        // las reflexiones no cuentan para la siguiente
            Assert.False(m.ToqueReflexion("p"));
        }

        [Fact]
        public void Las_conclusiones_se_guardan_como_recuerdos_de_peso_alto_y_vuelven_al_recuperar()
        {
            var m = M(new ManualClock { Ticks = 1 }); var a = Mundito.P("a"); var b = Mundito.P("b");
            var af = new ModeloAfectivo(null); af.Evento("a", "b", TipoEvento.Traicion, 1);
            for (int i = 0; i < 4; i++) m.Registra("a", "opinion", "b", "Beto la engano " + i, 2);
            var ins = Agente.Insights(a, m, Mundito.Ctx(af, 5, a, b));
            Assert.Single(ins); Assert.Contains("no es de fiar", ins[0]);
            Assert.Contains(ins[0], m.Recupera("a", "B", 2));
        }
    }

    public class AnimoTests
    {
        [Fact]
        public void Umbral_y_niveles_siguen_el_modelo_de_rimworld_y_el_neuroticismo_lo_desplaza_acotado()
        {
            var tranquilo = Mundito.P("t"); tranquilo.Neuroticismo = 0; var nervioso = Mundito.P("n"); nervioso.Neuroticismo = 1; var medio = Mundito.P("m"); medio.Neuroticismo = 0.5;
            Assert.Equal(0.35, Rupturas.Umbral(medio), 9);
            Assert.True(Rupturas.Umbral(tranquilo) < 0.35 && Rupturas.Umbral(nervioso) > 0.35);
            Assert.InRange(Rupturas.Umbral(tranquilo), 0.05, 0.5); Assert.InRange(Rupturas.Umbral(nervioso), 0.05, 0.5);
            double u = 0.35;
            Assert.Equal("sereno", AnimoCalc.Nivel(0.6, u)); Assert.Equal("inquieto", AnimoCalc.Nivel(0.3, u));
            Assert.Equal("al limite", AnimoCalc.Nivel(0.15, u)); Assert.Equal("al borde del colapso", AnimoCalc.Nivel(0.04, u)); Assert.Equal("radiante", AnimoCalc.Nivel(0.9, u));
        }

        [Fact]
        public void Sobre_el_umbral_nunca_se_rompe_y_debajo_se_rompe_a_la_frecuencia_publicada()
        {
            var p = Mundito.P("a"); p.Neuroticismo = 0.5;
            var rng = new Rng(1);
            for (int i = 0; i < 2000; i++) Assert.Equal(Ruptura.Ninguna, Rupturas.Evalua(p, 0.6, rng, 1));
            int menor = 0, mayor = 0, extrema = 0, n = 20000;
            for (int i = 0; i < n; i++) { if (Rupturas.Evalua(p, 0.30, rng, 1) == Ruptura.Retiro) menor++; if (Rupturas.Evalua(p, 0.15, rng, 1) == Ruptura.Arrebato) mayor++; if (Rupturas.Evalua(p, 0.02, rng, 1) == Ruptura.Hundimiento) extrema++; }
            Assert.InRange((double)menor / n, 0.08, 0.11);       // 1 - e^(-1/10) = 0.095
            Assert.InRange((double)mayor / n, 0.26, 0.31);       // 1 - e^(-1/3)  = 0.283
            Assert.InRange((double)extrema / n, 0.72, 0.77);     // 1 - e^(-1/0.7)= 0.760
        }

        [Fact]
        public void Un_arrebato_agravia_a_quien_peor_lleva_y_desahoga_un_poco()
        {
            var m = new ModeloAfectivo(null); var a = Mundito.P("a"); var b = Mundito.P("b"); var c = Mundito.P("c");
            m.Evento("a", "c", TipoEvento.Agravio, 0.9);
            string txt = Rupturas.Aplica(Ruptura.Arrebato, a, Mundito.Ctx(m, 1, a, b, c), new Rng(1));
            Assert.Contains("estalla contra C", txt); Assert.True(m.Get("c", "a").Rencor > 0.2);
            Assert.Equal("", Rupturas.Aplica(Ruptura.Ninguna, a, Mundito.Ctx(m, 1, a, b, c), new Rng(1)));
        }

        [Fact]
        public void El_animo_baja_con_necesidades_y_trauma_y_esta_acotado()
        {
            var m = new ModeloAfectivo(null); var a = Mundito.P("a"); var b = Mundito.P("b");
            double bien = AnimoCalc.Calcula(a, m, new[] { "a", "b" });
            foreach (var n in Necesidades.Nombres) a.Needs.Pon(n, 0);
            double mal = AnimoCalc.Calcula(a, m, new[] { "a", "b" });
            m.Evento("a", "b", TipoEvento.Duelo, 1);
            double peor = AnimoCalc.Calcula(a, m, new[] { "a", "b" });
            Assert.True(bien > mal && mal > peor); Assert.InRange(peor, 0, 1); Assert.InRange(bien, 0, 1);
        }

        [Fact]
        public void Inspiracion_solo_con_animo_muy_alto()
        {
            var p = Mundito.P("a", Mundito.A("aprender")); var rng = new Rng(2); int ok = 0;
            for (int i = 0; i < 2000; i++) if (Rupturas.Inspira(p, 0.5, rng, 1)) ok++;
            Assert.Equal(0, ok);
            for (int i = 0; i < 2000; i++) if (Rupturas.Inspira(Mundito.P("b", Mundito.A("aprender")), 0.95, rng, 1)) ok++;
            Assert.InRange(ok, 100, 200);                           // 1 - e^(-1/15) = 6.4 %
        }
    }

    public class GuionTests
    {
        [Fact]
        public void Casarse_son_cuatro_pasos_que_empiezan_donde_esta_la_relacion()
        {
            var m = new ModeloAfectivo(null); var a = Mundito.P("a", Mundito.A("casarse")); var b = Mundito.P("b");
            m.Evento("a", "b", TipoEvento.Aprecio, 0.2);                        // casi desconocidos
            var g = Guiones.De(a, a.Ambiciones[0], Mundito.Ctx(m, 1, a, b));
            Assert.Equal(4, g.Pasos.Count); Assert.Equal(0, g.Actual); Assert.Equal(TipoIntencion.Charlar, g.PasoActual.Tipo);
            a.Guiones.Clear();
            for (int i = 0; i < 3; i++) { m.Evento("a", "b", TipoEvento.Cortejo, 1); m.Evento("a", "b", TipoEvento.Aprecio, 1); }
            var g2 = Guiones.De(a, a.Ambiciones[0], Mundito.Ctx(m, 1, a, b));
            Assert.Equal(TipoIntencion.Celebrar, g2.PasoActual.Tipo);          // ya hay romance y afecto mutuo: no vuelve a «charlar» para conocerse
        }

        [Fact]
        public void El_guion_avanza_solo_con_el_paso_exacto_y_con_dias_entre_pasos()
        {
            var m = new ModeloAfectivo(null); var a = Mundito.P("a", Mundito.A("aprender")); var b = Mundito.P("b");
            m.Evento("a", "b", TipoEvento.Aprecio, 0.9);
            var ctx = Mundito.Ctx(m, 1, a, b);
            var g = Guiones.De(a, a.Ambiciones[0], ctx);
            Assert.Equal(TipoIntencion.Visitar, g.PasoActual.Tipo);
            Assert.Null(Guiones.Avanza(a, new Intencion { Tipo = TipoIntencion.Charlar, Objetivo = "b" }, 1));       // no es el paso
            Assert.Null(Guiones.Avanza(a, new Intencion { Tipo = TipoIntencion.Visitar, Objetivo = "zzz" }, 1));       // otro objetivo
            Assert.Same(g, Guiones.Avanza(a, new Intencion { Tipo = TipoIntencion.Visitar, Objetivo = "b" }, 1));
            Assert.Null(Guiones.Avanza(a, new Intencion { Tipo = TipoIntencion.Aprender }, 1));                          // demasiado pronto
            Assert.Same(g, Guiones.Avanza(a, new Intencion { Tipo = TipoIntencion.Aprender }, 3));
            Assert.Same(g, Guiones.Avanza(a, new Intencion { Tipo = TipoIntencion.Aprender }, 6));
            Assert.True(g.Terminado);
        }

        [Fact]
        public void Se_rehace_si_el_objetivo_desaparece_o_si_se_estanca()
        {
            var m = new ModeloAfectivo(null); var a = Mundito.P("a", Mundito.A("vengar")); var b = Mundito.P("b"); var c = Mundito.P("c");
            m.Evento("a", "b", TipoEvento.Traicion, 1);
            var g = Guiones.De(a, a.Ambiciones[0], Mundito.Ctx(m, 1, a, b, c));
            Assert.Equal("b", g.Pasos[0].Objetivo);
            m.Evento("a", "c", TipoEvento.Traicion, 1); m.Evento("a", "c", TipoEvento.Duelo, 1);
            var sinB = Guiones.De(a, a.Ambiciones[0], Mundito.Ctx(m, 2, a, c));            // b ya no esta
            Assert.NotSame(g, sinB); Assert.Equal("c", sinB.Pasos[0].Objetivo);
            Assert.Same(sinB, Guiones.De(a, a.Ambiciones[0], Mundito.Ctx(m, 3, a, c)));
            Assert.NotSame(sinB, Guiones.De(a, a.Ambiciones[0], Mundito.Ctx(m, 3 + Guion.Paciencia + 1, a, c)));     // estancado
        }

        [Fact]
        public void Sin_rencor_no_hay_guion_de_venganza_y_una_ambicion_no_madura_no_se_cumple_antes_de_tiempo()
        {
            var m = new ModeloAfectivo(null); var a = Mundito.P("a", Mundito.A("vengar")); var b = Mundito.P("b");
            Assert.Null(Guiones.De(a, a.Ambiciones[0], Mundito.Ctx(m, 1, a, b)));
            // plan de 2 pasos hecho en 4 dias, pero la ambicion (plazo medio = 60 d) aun no ha madurado
            var ag = new Agenda { MaxAltasPorDia = 9 }; var p = Mundito.P("p", Mundito.A("aprender")); p.Ambiciones[0].Plazo = Plazo.Medio;
            var mm = new ModeloAfectivo(null); var q = Mundito.P("q"); mm.Evento("p", "q", TipoEvento.Aprecio, 0.9);
            var ctx = Mundito.Ctx(mm, 1, p, q);
            var g = Guiones.De(p, p.Ambiciones[0], ctx);
            int dia = 1; var cumplidas = new List<Ambicion>();
            while (!g.Terminado && dia < 30)
            {
                var s = g.PasoActual; var i = ag.Anade(new Intencion { Pawn = "p", Tipo = s.Tipo, Objetivo = s.Objetivo, Categoria = s.Categoria, Prioridad = 0.5, Texto = "x" }, dia);
                if (i != null) ag.Marca(i, EstadoIntencion.Hecha);
                cumplidas.AddRange(Agente.Consolida(p, ag, 1)); dia++; ag.Caduca(dia);
            }
            Assert.True(g.Terminado); Assert.Empty(cumplidas); Assert.False(p.Ambiciones[0].Cumplida); Assert.True(p.Ambiciones[0].Progreso <= 0.95 + 1e-9);
        }

        [Fact]
        public void Sucesora_encadena_ambiciones_y_acota_el_historial()
        {
            var p = Mundito.P("p", Mundito.A("casarse"));
            var n = Agente.Sucesora(p, p.Ambiciones[0], 40);
            Assert.Equal("proteger", n.Categoria); Assert.Equal(40, n.Creada); Assert.Equal(1, p.Logros); Assert.Equal(Plazo.Largo, n.Plazo);
            for (int i = 0; i < 30; i++) { var u = p.Ambiciones[p.Ambiciones.Count - 1]; u.Cumplida = true; Agente.Sucesora(p, u, 100 + i); }
            Assert.True(p.Ambiciones.Count <= 5, "historial " + p.Ambiciones.Count);
            Assert.NotNull(p.Principal());
            var q = Persona.FromJson(p.ToJson()); Assert.Equal(p.Logros, q.Logros); Assert.Equal(p.Ambiciones.Last().Creada, q.Ambiciones.Last().Creada);
        }
    }

    public class DirectorNormasTests
    {
        [Fact]
        public void Curvas_objetivo_por_estilo()
        {
            var cl = new Director(1) { Estilo = EstiloDirector.Clasico, DiasCiclo = 20 };
            Assert.Equal(0.2, cl.Objetivo(0), 9); Assert.True(cl.Objetivo(19) > 0.65); Assert.Equal(0.2, cl.Objetivo(20), 9);
            Assert.Equal(0.25, new Director(1) { Estilo = EstiloDirector.Calmo }.Objetivo(77), 9);
            var ca = new Director(1) { Estilo = EstiloDirector.Caotico }; Assert.NotEqual(ca.Objetivo(3), ca.Objetivo(4)); Assert.Equal(ca.Objetivo(3), ca.Objetivo(3));
            for (int d = 0; d < 200; d++) Assert.InRange(ca.Objetivo(d), 0.15, 0.75);
        }

        [Fact]
        public void Si_el_reino_esta_quieto_propone_conflicto_y_si_esta_tenso_alivio_y_respeta_el_enfriamiento()
        {
            var m = new ModeloAfectivo(null);
            var ps = Enumerable.Range(0, 8).Select(i => Mundito.P("p" + i, Mundito.A("mandar"))).ToDictionary(p => p.Id);
            var ids = ps.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
            m.Evento("p1", "p2", TipoEvento.Traicion, 1); m.Evento("p1", "p2", TipoEvento.Duelo, 1);
            var d = new Director(3) { Estilo = EstiloDirector.Calmo, Enfriamiento = 5 };
            Sugerencia quieto = null; for (int dia = 0; dia < 30 && quieto == null; dia++) { d.Tension = 0; quieto = d.Decide(dia * 10, 0, m, ids, id => ps[id]); }
            Assert.NotNull(quieto); Assert.True(quieto.Tipo == TipoSugerencia.Escandalo || quieto.Tipo == TipoSugerencia.Rivalidad);
            Assert.Null(d.Decide(d.Ultimo + 1, 0, m, ids, id => ps[id]));                      // enfriamiento
            var d2 = new Director(3) { Estilo = EstiloDirector.Calmo };
            Sugerencia tenso = null; for (int dia = 0; dia < 30 && tenso == null; dia++) { d2.Tension = 1; tenso = d2.Decide(dia * 10, 1, m, ids, id => ps[id]); }
            Assert.NotNull(tenso); Assert.True(tenso.Tipo == TipoSugerencia.Fiesta || tenso.Tipo == TipoSugerencia.Reconciliacion);
            if (tenso.Tipo == TipoSugerencia.Reconciliacion) { Assert.Equal("p1", tenso.Pawns[0]); Assert.Equal("p2", tenso.Pawns[1]); }
            Assert.Null(new Director(1).Decide(100, 0, m, new[] { "a", "b" }, id => null));    // con menos de 3 pawns no hay drama
        }

        [Fact]
        public void La_tension_sube_con_rencor_y_hostilidad_y_esta_acotada()
        {
            var m = new ModeloAfectivo(null); var ag = new Agenda(); var cr = new Cronica();
            var ids = Enumerable.Range(0, 10).Select(i => "p" + i).ToList();
            double calma = Director.Mide(m, ids, ag, cr, 10);
            for (int i = 0; i < 4; i++) { m.Evento("p" + i, "p" + (i + 1), TipoEvento.Traicion, 1); ag.Anade(new Intencion { Pawn = "p" + i, Tipo = TipoIntencion.Vengarse, Objetivo = "p" + (i + 1), Prioridad = 0.5, Texto = "x" }, 10); }
            cr.Anota(9, "x", "algo grave", 40);
            double caos = Director.Mide(m, ids, ag, cr, 10);
            Assert.True(caos > calma + 0.15); Assert.InRange(caos, 0, 1); Assert.InRange(calma, 0, 1);
            Assert.Equal(0, Director.Mide(m, new List<string>(), ag, cr, 1));
        }

        static List<Persona> Gente(double amabilidad, int n)
        {
            return Enumerable.Range(0, n).Select(i => { var p = Mundito.P("p" + i); p.Amabilidad = amabilidad; p.Extroversion = 0.5; return p; }).ToList();
        }

        [Fact]
        public void Una_cultura_amable_aprueba_la_paz_con_histeresis_y_no_se_deroga_enseguida()
        {
            var n = new Normas(); var cult = new Cultura(); cult.Suceso("clemencia", 10);
            var cambios = n.Evalua(0, cult, Gente(0.8, 10), id => 1, 25);
            Assert.True(n.Tiene("paz_publica")); Assert.False(n.Tiene("ojo_por_ojo")); Assert.Single(cambios.Where(c => c.Contains("Paz Publica")));
            Assert.Equal(0.20, n.UmbralHostilExtra, 9); Assert.Equal(2.0, n.FactorEnfriaHostil, 9);
            // la gente se vuelve menos amable, pero la norma recien aprobada aguanta 90 dias
            var cult2 = new Cultura(); var hostil = Gente(0.35, 10);
            Assert.Empty(n.Evalua(30, cult2, hostil, id => 1, 0).Where(c => c.StartsWith("Se deroga")));
            Assert.True(n.Tiene("paz_publica"));
            Assert.Contains(n.Evalua(120, cult2, hostil, id => 1, 0), c => c.StartsWith("Se deroga la norma paz_publica"));
            Assert.False(n.Tiene("paz_publica"));
            Assert.True(n.Tiene("ojo_por_ojo")); Assert.Equal(-0.10, n.UmbralHostilExtra, 9);       // una sociedad que ya no es amable pasa de la paz publica al ojo por ojo
        }

        [Fact]
        public void Paz_y_ojo_por_ojo_son_incompatibles_y_la_hospitalidad_da_bono()
        {
            var n = new Normas(); var cult = new Cultura(); cult.Suceso("venganza", 10); cult.Suceso("comunidad", 10);
            var gente = Gente(0.2, 12); foreach (var p in gente) p.Extroversion = 0.9;
            n.Evalua(0, cult, gente, id => 1, 0);
            Assert.True(n.Tiene("ojo_por_ojo")); Assert.False(n.Tiene("paz_publica"));
            Assert.Equal(-0.10, n.UmbralHostilExtra, 9); Assert.Equal(0.5, n.FactorEnfriaHostil, 9);
            Assert.True(n.Tiene("hospitalidad")); Assert.Equal(0.10, n.BonoHospitalidad, 9);
        }

        [Fact]
        public void Los_lideres_pesan_mas_en_la_votacion()
        {
            var gente = Gente(0.3, 9); gente[0].Amabilidad = 1.0;       // un solo amable...
            var n1 = new Normas(); n1.Evalua(0, new Cultura(), gente, id => 1, 0);
            var n2 = new Normas(); n2.Evalua(0, new Cultura(), gente, id => id == "p0" ? 50 : 1, 0);   // ...pero es el lider
            Assert.False(n1.Tiene("paz_publica")); Assert.True(n2.Tiene("paz_publica"));
        }
    }

    public class SocialExtraTests
    {
        [Fact]
        public void Etapas_de_relacion_por_umbrales()
        {
            var m = new ModeloAfectivo(null);
            Assert.Equal("desconocido", Relaciones.Etapa(m, "a", "b"));
            for (int i = 0; i < 3; i++) m.Evento("a", "b", TipoEvento.Aprecio, 0.1);
            Assert.Equal("conocido", Relaciones.Etapa(m, "a", "b"));
            for (int i = 0; i < 4; i++) m.Evento("a", "b", TipoEvento.Aprecio, 0.9);
            Assert.Equal("mejor amigo", Relaciones.Etapa(m, "a", "b"));
            m.Evento("a", "c", TipoEvento.Aprecio, 0.8); m.Evento("a", "c", TipoEvento.Aprecio, 0.5);
            Assert.Equal("amigo", Relaciones.Etapa(m, "a", "c"));
            m.Evento("a", "d", TipoEvento.Traicion, 1); Assert.Equal("enemigo", Relaciones.Etapa(m, "a", "d"));
            for (int i = 0; i < 3; i++) m.Evento("a", "e", TipoEvento.Competencia, 1); Assert.Equal("rival", Relaciones.Etapa(m, "a", "e"));
            for (int i = 0; i < 4; i++) { m.Evento("a", "f", TipoEvento.Cortejo, 1); m.Evento("f", "a", TipoEvento.Cortejo, 1); m.Evento("a", "f", TipoEvento.Aprecio, 1); }
            Assert.Equal("pareja", Relaciones.Etapa(m, "a", "f"));
            string r = Relaciones.Resumen(m, "a", new[] { "a", "b", "c", "d", "e", "f" }, id => id.ToUpper());
            Assert.Contains("pareja: F", r); Assert.Contains("enemigo: D", r); Assert.Contains("rival: E", r);
            Assert.Equal("", Relaciones.Resumen(new ModeloAfectivo(null), "a", new[] { "a", "b" }, id => id));
        }

        [Fact]
        public void Freno_de_conversacion_por_pareja_es_simetrico_y_caduca()
        {
            var f = new FrenoConversacion { Dias = 2 };
            Assert.True(f.Puede("a", "b", 1)); f.Anota("a", "b", 1);
            Assert.False(f.Puede("b", "a", 2)); Assert.True(f.Puede("a", "c", 2)); Assert.True(f.Puede("b", "a", 3));
        }

        [Fact]
        public void Roles_emergen_de_lo_hecho_y_no_de_la_ficha()
        {
            Assert.Equal("", Roles.De(new Dictionary<TipoIntencion, int> { { TipoIntencion.Trabajar, 5 } }));            // pocos datos
            Assert.Equal("artesano", Roles.De(new Dictionary<TipoIntencion, int> { { TipoIntencion.Trabajar, 12 }, { TipoIntencion.Charlar, 3 }, { TipoIntencion.Descansar, 50 } }));
            Assert.Equal("intrigante", Roles.De(new Dictionary<TipoIntencion, int> { { TipoIntencion.Intrigar, 6 }, { TipoIntencion.Vengarse, 6 }, { TipoIntencion.Charlar, 4 } }));
            Assert.Equal("", Roles.De(new Dictionary<TipoIntencion, int> { { TipoIntencion.Trabajar, 3 }, { TipoIntencion.Aprender, 3 }, { TipoIntencion.Charlar, 3 }, { TipoIntencion.Consolar, 3 }, { TipoIntencion.Pedir, 3 } }));   // ninguno domina
        }

        [Fact]
        public void Curvas_normalizadas()
        {
            Assert.Equal(0, Curvas.Logistica(0, 0.55, 9), 9); Assert.Equal(1, Curvas.Logistica(1, 0.55, 9), 9);
            Assert.True(Curvas.Logistica(0.2, 0.55, 9) < 0.1 && Curvas.Logistica(0.8, 0.55, 9) > 0.8);
            double prev = -1; for (double x = 0; x <= 1; x += 0.05) { double v = Curvas.Logistica(x, 0.55, 9); Assert.True(v >= prev - 1e-12); prev = v; }
            Assert.Equal(0.25, Curvas.Cuadratica(0.5), 9); Assert.Equal(1, Curvas.Lineal(7), 9);
        }

        [Fact]
        public void Escandalo_filtra_el_secreto_mas_grave_con_consecuencias()
        {
            var m = new ModeloAfectivo(null); var c = new ManualClock { Ticks = 1 };
            var r = new RedSecretos(m, id => null, c, new Rng(4));
            r.Asigna("a", "robo", 0.9); r.Asigna("b", "deuda", 0.3);
            var f = r.Escandalo("x");
            Assert.Equal("a", f.Sujeto); Assert.Equal("escandalo", f.Motivo); Assert.True(m.Get("x", "a").Rencor > 0.2);
            Assert.Equal("b", r.Escandalo("x").Sujeto);                 // ya conoce el grave: sigue el siguiente
            Assert.Null(r.Escandalo("x"));
            Assert.Equal("b", r.Escandalo("a").Sujeto);                 // nadie se entera de su propio secreto por escandalo
        }
    }

    public class SimFase3Tests
    {
        static Resultado Corre(int seed, int dias, Action<SimConfig> aj = null)
        {
            var c = new SimConfig { Seed = seed, Dias = dias, Pawns = 24 };
            if (aj != null) aj(c);
            return new Mundo(c).Run();
        }

        [Fact]
        public void El_director_mantiene_la_tension_cerca_de_su_curva_y_mezcla_drama_y_alivio()
        {
            for (int seed = 1; seed <= 4; seed++)
            {
                var r = Corre(seed, 360);
                Assert.InRange(r.TensionFueraDeBandaPct, 0, 35);
                Assert.InRange(r.TensionMedia, 0.3, 0.6);
                Assert.True(r.DirectorEscandalos + r.DirectorRivalidades > 10 && r.DirectorFiestas + r.DirectorReconciliaciones > 10, "drama " + (r.DirectorEscandalos + r.DirectorRivalidades) + " alivio " + (r.DirectorFiestas + r.DirectorReconciliaciones));
                Assert.True(r.DirectorEventos < 360 / 3 + 1, "el enfriamiento limita los eventos");
            }
        }

        [Fact]
        public void El_director_calmo_provoca_menos_drama_que_el_caotico_y_los_tres_estilos_son_estables()
        {
            var calmo = Corre(2, 300, c => c.EstiloDirector = EstiloDirector.Calmo); var caos = Corre(2, 300, c => c.EstiloDirector = EstiloDirector.Caotico);
            Assert.True(calmo.TensionMedia < caos.TensionMedia + 0.05, calmo.TensionMedia + " vs " + caos.TensionMedia);
            foreach (var r in new[] { calmo, caos, Corre(2, 300, c => c.EstiloDirector = EstiloDirector.Clasico) })
            { Assert.True(r.MaxDiasSinProgreso <= 10); Assert.True(r.Final.Estabilidad > 0.4); Assert.True(r.TasaCambioSigno < 0.10); }
        }

        [Fact]
        public void Los_animos_son_sanos_las_rupturas_son_raras_y_las_conclusiones_se_generan()
        {
            for (int seed = 1; seed <= 4; seed++)
            {
                var r = Corre(seed, 360);
                Assert.InRange(r.PorcentajeBajoUmbral, 0, 15);
                Assert.InRange(r.Rupturas, 1, 24 * 360 / 10);
                Assert.True(r.Reflexiones > 24 && r.InsightsGenerados > 24, "reflexiones " + r.Reflexiones);
            }
        }

        [Fact]
        public void Las_ambiciones_llevan_tiempo_los_planes_se_completan_y_hay_etapas_de_relacion_variadas()
        {
            for (int seed = 1; seed <= 4; seed++)
            {
                var r = Corre(seed, 360);
                Assert.InRange(r.AmbicionesCumplidas, 4, 24 * 3);        // ni nada ni seis por persona al anio
                Assert.True(r.Etapas.Contains("amigo") && r.Etapas.Contains("enemigo"), r.Etapas);
                Assert.True(r.Roles.Length > 0, "ningun rol emergente");
            }
        }

        [Fact]
        public void Las_normas_no_parpadean_y_la_fase3_es_determinista()
        {
            for (int seed = 1; seed <= 4; seed++) { var r = Corre(seed, 360); Assert.True(r.NormasDerogadas <= r.NormasAprobadas && r.NormasAprobadas <= 6, r.NormasAprobadas + "/" + r.NormasDerogadas); }
            Assert.Equal(Corre(9, 200).Informe, Corre(9, 200).Informe);
        }
    }
}
