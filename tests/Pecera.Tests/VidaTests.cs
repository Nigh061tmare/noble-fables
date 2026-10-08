using System;
using System.Collections.Generic;
using System.Linq;
using Pecera.Core;
using Pecera.Sim;
using Xunit;

namespace Pecera.Tests
{
    // v0.5 «Vida»: bus de eventos del mundo, psique (CK3 / Dwarf Fortress / The Sims / Voyager), ordenes del soberano,
    // prejuicio de grupo calibrado con datos reales, maestro de juego y persistencia del estado social.
    public class BusYVidaTests
    {
        static Persona P(string id) { return new Persona { Id = id, Nombre = id.ToUpperInvariant() }; }

        [Fact]
        public void El_bus_reparte_por_tipo_y_comodin_y_un_suscriptor_roto_no_rompe_a_los_demas()
        {
            var bus = new BusEventos();
            int muertes = 0, todos = 0;
            bus.Suscribe("muerte", e => muertes++);
            bus.Suscribe("*", e => todos++);
            bus.Suscribe("muerte", e => { throw new InvalidOperationException("roto"); });
            bus.Publica(new EventoMundo { Tipo = "muerte", A = "a" });
            bus.Publica(new EventoMundo { Tipo = "boda", A = "a", B = "b" });
            Assert.Equal(1, muertes); Assert.Equal(2, todos);
            Assert.Equal(1, bus.Errores); Assert.Contains("roto", bus.UltimoError);
            Assert.Equal(1, bus.Cuenta("muerte")); Assert.Equal(0, bus.Cuenta("nada"));
            Assert.Equal(2, bus.Ultimos.Count());
        }

        static ContextoVida Ctx(ModeloAfectivo af, Dictionary<string, Persona> ps, RecuerdosFuertes rf, Cronica cr, Linaje li)
        {
            return new ContextoVida { Afectos = af, Cronica = cr, Linaje = li, Fuertes = rf, Persona = id => ps.ContainsKey(id) ? ps[id] : null,
                                      Vivos = () => ps.Keys.ToList() };
        }

        [Fact]
        public void La_muerte_duele_en_proporcion_al_vinculo_y_crea_rencor_contra_el_causante()
        {
            var af = new ModeloAfectivo(null);
            var ps = new Dictionary<string, Persona> { { "amigo", P("amigo") }, { "extrano", P("extrano") }, { "asesino", P("asesino") } };
            for (int k = 0; k < 6; k++) af.Evento("amigo", "muerto", TipoEvento.Ayuda, 1);
            var rf = new RecuerdosFuertes(); var cr = new Cronica(); var bus = new BusEventos();
            Vida.Conecta(bus, Ctx(af, ps, rf, cr, new Linaje()));
            bus.Publica(new EventoMundo { Tipo = "muerte", A = "muerto", B = "asesino", Texto = "en una pelea", Dia = 10 });
            Assert.Contains(rf.De("amigo"), r => r.Tipo == "duelo" && r.Valencia < 0);
            Assert.Empty(rf.De("extrano"));
            Assert.True(ps["amigo"].Estres > 0);                       // el duelo estresa (CK3)
            Assert.Equal(0, ps["extrano"].Estres);
            Assert.True(af.Get("amigo", "asesino").Rencor > 0);
            Assert.Contains(cr.Hitos, h => h.Tipo == "muerte" && h.Texto.Contains("a manos de"));
            Assert.Equal(0, bus.Errores);
        }

        [Fact]
        public void Boda_y_nacimiento_mueven_linaje_ambiciones_y_recuerdos()
        {
            var af = new ModeloAfectivo(null);
            var ps = new Dictionary<string, Persona> { { "a", P("a") }, { "b", P("b") } };
            ps["a"].Ambiciones.Add(new Ambicion { Categoria = "casarse", Texto = "casarse" });
            var li = new Linaje(); li.Nace("a", 0, null, null); li.Nace("b", 0, null, null);
            var rf = new RecuerdosFuertes(); var bus = new BusEventos();
            Vida.Conecta(bus, Ctx(af, ps, rf, new Cronica(), li));
            bus.Publica(new EventoMundo { Tipo = "boda", A = "a", B = "b", Dia = 5 });
            Assert.Equal("b", li.Conyuge("a"));
            Assert.True(ps["a"].Ambiciones[0].Cumplida);
            Assert.True(af.Get("a", "b").Romance > 0);
            bus.Publica(new EventoMundo { Tipo = "nacimiento", A = "c", B = "a", Texto = "madre=b", Dia = 6 });
            Assert.True(li.Vivo("c"));
            Assert.Contains("c", li.HijosVivos("a"));
            Assert.Contains(rf.De("b"), r => r.Tipo == "nacimiento" && r.Valencia > 0);
        }

        [Fact]
        public void Una_herida_estresa_y_deja_agravio_con_el_agresor()
        {
            var af = new ModeloAfectivo(null);
            var ps = new Dictionary<string, Persona> { { "a", P("a") }, { "b", P("b") } };
            var bus = new BusEventos(); Vida.Conecta(bus, Ctx(af, ps, new RecuerdosFuertes(), new Cronica(), new Linaje()));
            bus.Publica(new EventoMundo { Tipo = "herida", A = "a", B = "b", Valor = 0.9, Dia = 1 });
            Assert.True(ps["a"].Estres > 20);
            Assert.True(af.Get("a", "b").Rencor > 0);
        }
    }

    public class PsiqueTests
    {
        static Persona P(string id, double n = 0.5, double a = 0.5, double e = 0.5)
        {
            var p = new Persona { Id = id, Nombre = id, Neuroticismo = n, Amabilidad = a, Extroversion = e };
            p.BaseNeuroticismo = n; p.BaseAmabilidad = a; return p;
        }

        [Fact]
        public void Recuerdos_fuertes_ignoran_lo_trivial_guardan_uno_por_tipo_y_promueven_a_largo_plazo()
        {
            var rf = new RecuerdosFuertes { DiasPromocion = 10 };
            rf.Anota("p", "insulto", "me insultaron", -0.5, 2, 0);          // trivial
            Assert.Empty(rf.De("p"));
            rf.Anota("p", "duelo", "perdi a mi madre", -1, 6, 0);
            rf.Anota("p", "duelo", "perdi un perro", -0.5, 4, 1);         // mismo tipo, mas debil: no sustituye
            Assert.Single(rf.De("p")); Assert.Equal("perdi a mi madre", rf.De("p")[0].Texto);
            for (int k = 0; k < RecuerdosFuertes.Huecos + 3; k++) rf.Anota("p", "t" + k, "cosa " + k, 0.5, 3 + k * 0.1, 2);
            Assert.Equal(RecuerdosFuertes.Huecos, rf.De("p").Count(x => !x.Largo));
            rf.Promueve("p", 20);
            Assert.Contains(rf.De("p"), x => x.Largo && x.Tipo == "duelo");
        }

        [Fact]
        public void Revivir_un_trauma_estresa_lo_cura_poco_a_poco_y_deriva_la_personalidad_acotada()
        {
            var rf = new RecuerdosFuertes { DiasPromocion = 0 };
            var p = P("p", n: 0.5);
            rf.Anota("p", "duelo", "perdi a mi hijo", -1, 10, 0); rf.Promueve("p", 0);
            string t; double antes = rf.De("p")[0].Intensidad;
            double ef = rf.Revive(p, 0, out t);
            Assert.True(ef > 0); Assert.Contains("perdi a mi hijo", t);
            Assert.True(rf.De("p")[0].Intensidad < antes);
            Assert.True(p.Neuroticismo > 0.5);
            for (int k = 0; k < 200; k++) Psique.DerivaNeuroticismo(p, 0.01);
            Assert.True(p.Neuroticismo <= 0.5 + Psique.DerivaMax + 1e-9);
        }

        [Fact]
        public void Actuar_contra_el_caracter_estresa_y_el_estresado_lo_evita_mas()
        {
            var bueno = P("b", a: 0.9); var malo = P("m", a: 0.1);
            Assert.True(Estres.Coste(bueno, TipoIntencion.Vengarse) > 40);
            Assert.True(Estres.Coste(malo, TipoIntencion.Vengarse) < 2);
            Assert.True(Estres.Coste(bueno, TipoIntencion.Descansar) < 0);
            double f0 = Estres.Factor(bueno, TipoIntencion.Vengarse);
            bueno.Estres = 250;
            Assert.True(Estres.Factor(bueno, TipoIntencion.Vengarse) < f0);
            Assert.True(Estres.Factor(bueno, TipoIntencion.Descansar) > 1);
        }

        [Fact]
        public void Cruzar_un_nivel_provoca_crisis_segun_el_caracter_y_libera_estres()
        {
            var irascible = P("i", n: 0.8, a: 0.2); irascible.Estres = 95;
            Assert.Equal(Ruptura.Arrebato, Estres.Suma(irascible, 10));
            Assert.True(irascible.Estres < 100);
            var sereno = P("s", n: 0.2, a: 0.8); sereno.Estres = 95;
            Assert.Equal(Ruptura.Retiro, Estres.Suma(sereno, 10));
            var roto = P("r"); roto.Estres = 290;
            Assert.Equal(Ruptura.Hundimiento, Estres.Suma(roto, 20));
            var tranquilo = P("t"); tranquilo.Estres = 10;
            Assert.Equal(Ruptura.Ninguna, Estres.Suma(tranquilo, 20));
        }

        [Fact]
        public void Lo_sufrido_fuera_del_dia_llega_como_crisis_al_dia_siguiente_y_el_estres_se_disipa()
        {
            var p = P("p", n: 0.5); p.Estres = 90;
            Estres.Sufre(p, 30);
            Assert.True(p.Estres >= 100);
            string rev;
            Assert.NotEqual(Ruptura.Ninguna, Psique.Dia(p, null, 1, out rev));
            double e = p.Estres;
            Assert.Equal(Ruptura.Ninguna, Psique.Dia(p, null, 2, out rev));
            Assert.True(p.Estres < e);
            var nervioso = P("n", n: 1); var calmado = P("c", n: 0);
            Assert.True(Estres.Disipacion(calmado) > Estres.Disipacion(nervioso));
        }

        [Fact]
        public void El_nivel_de_estres_vivido_sobrevive_al_guardado()
        {
            var p = P("p"); p.Estres = 150; p.NivelEstres = 1;
            var q = Persona.FromJson(p.ToJson());
            Assert.Equal(1, q.NivelEstres); Assert.Equal(150, q.Estres, 3);
        }

        [Fact]
        public void La_experiencia_premia_lo_que_sale_bien_y_guia_el_curriculo()
        {
            var ex = new Experiencia();
            Assert.Equal(1.0, ex.Factor("p", TipoIntencion.Aprender), 6);
            for (int k = 0; k < 8; k++) ex.Anota("p", TipoIntencion.Aprender, true);
            for (int k = 0; k < 8; k++) ex.Anota("p", TipoIntencion.Pedir, false);
            Assert.True(ex.Factor("p", TipoIntencion.Aprender) > 1.15);
            Assert.True(ex.Factor("p", TipoIntencion.Pedir) < 0.85);
            Assert.Equal(16, ex.Intentos("p", TipoIntencion.Aprender) + ex.Intentos("p", TipoIntencion.Pedir));
            var per = P("p"); per.Apertura = 0.5; per.Escrupulosidad = 0.5;
            var hecha = new Ambicion { Categoria = "aprender", Cumplida = true };
            var n = Agente.Sucesora(per, hecha, 10, ex);
            Assert.Contains(n.Categoria, new[] { "descubrir", "enriquecerse" });
            Assert.Equal(10, n.Creada);
        }

        [Fact]
        public void La_eleccion_no_es_robotica_pero_favorece_lo_mejor()
        {
            var ops = new List<Intencion> { new Intencion { Prioridad = 0.8, Texto = "a" }, new Intencion { Prioridad = 0.7, Texto = "b" }, new Intencion { Prioridad = 0.1, Texto = "c" } };
            Assert.Equal("a", Eleccion.Elige(ops, new Rng(1), 0).Texto);
            var cuenta = new Dictionary<string, int> { { "a", 0 }, { "b", 0 }, { "c", 0 } };
            var rng = new Rng(7);
            for (int k = 0; k < 2000; k++) cuenta[Eleccion.Elige(ops, rng, 0.12).Texto]++;
            Assert.True(cuenta["a"] > cuenta["b"]); Assert.True(cuenta["b"] > 200); Assert.True(cuenta["c"] < 100);
            Assert.Null(Eleccion.Elige(new List<Intencion>(), rng, 0.1));
        }

        [Fact]
        public void Las_preferencias_aprenden_de_los_vetos_y_silencian_lo_que_siempre_vetas()
        {
            var pr = new Preferencias();
            Assert.Equal(1, pr.Factor("esquema", "Difamar"), 6);
            pr.Registra("esquema", "Difamar", true); pr.Registra("esquema", "Difamar", true);
            Assert.False(pr.Silenciada("esquema", "Difamar"));
            pr.Registra("esquema", "Difamar", true);
            Assert.True(pr.Silenciada("esquema", "Difamar"));
            for (int k = 0; k < 10; k++) pr.Registra("esquema", "Festejar", false);
            Assert.True(pr.Factor("esquema", "Festejar") > 1);
            Assert.False(pr.Silenciada("esquema", "Festejar"));
            Assert.Contains("esquema:Difamar (0 si / 3 veto)", pr.Resumen());
        }

        [Fact]
        public void La_compuerta_avisa_de_cada_veto_y_de_cada_decision_que_pasa()
        {
            var c = new Compuerta(new ManualClock { Ticks = 1 }) { VentanaVetoSegundos = 0 };
            var vistas = new List<string>();
            c.AlDecidir = (d, vetada) => vistas.Add(d.Etiqueta + (vetada ? ":veto" : ":ok"));
            var d1 = c.Propone("esquema", "k1", "difamar", "r", null, false); d1.Etiqueta = "Difamar";
            var d2 = c.Propone("esquema", "k2", "festejar", "r", null, false); d2.Etiqueta = "Festejar";
            Assert.True(c.Veta(d1.Id));
            c.Listas();
            Assert.Equal(new[] { "Difamar:veto", "Festejar:ok" }, vistas.ToArray());
        }
    }

    public class OrdenesYPrejuiciosTests
    {
        [Fact]
        public void Entiende_las_ordenes_del_soberano_en_lenguaje_natural()
        {
            var o = Ordenes.Parse("Quiero paz en el reino, favorece a Ámbar Rojo y a la casa de Pino, y haced una fiesta.");
            Assert.Equal("paz_publica", o.NormaForzada);
            Assert.True(o.PideFiesta);
            Assert.Contains("ambar rojo", o.Favorecidos);
            Assert.True(o.Favorece("Ámbar Rojo", ""));
            Assert.False(o.Favorece("Otro", "Robles"));
            var v = Ordenes.Parse("Ojo por ojo. Quiero drama.");
            Assert.Equal("ojo_por_ojo", v.NormaForzada); Assert.Equal(EstiloDirector.Caotico, v.Estilo);
            var nada = Ordenes.Parse("hola");
            Assert.Null(nada.NormaForzada); Assert.Empty(nada.Entendidas);
        }

        [Fact]
        public void La_magnitud_sigue_los_percentiles_reales_de_opinion()
        {
            Assert.Equal(0.5, Calibracion.Magnitud(0.72), 2);    // mediana real
            Assert.Equal(0.9, Calibracion.Magnitud(1.96), 2);    // p90
            Assert.Equal(0.03, Calibracion.Magnitud(0.1), 6);     // suelo: hasta el cambio minimo cuenta algo
            Assert.Equal(1.0, Calibracion.Magnitud(10), 6);
            var rng = new Rng(3); int rasgos = 0, negativos = 0, n = 5000;
            for (int k = 0; k < n; k++) { bool r, ng; double d = Calibracion.Muestra(rng, out r, out ng); Assert.True(d >= 0.5); if (r) rasgos++; if (ng) negativos++; }
            Assert.InRange(rasgos / (double)n, 0.90, 0.96);
            Assert.InRange(negativos / (double)n, 0.79, 0.85);
        }

        [Fact]
        public void Extrae_el_grupo_de_los_motivos_reales_del_juego()
        {
            bool al;
            Assert.Equal("Elfo oscuro", Prejuicios.GrupoDeMotivo("Shah Bir es Elfo oscuro | PersistentLocalizedStringContext", out al)); Assert.False(al);
            Assert.Equal("Enano", Prejuicios.GrupoDeMotivo("Brigitte es Enano", out al));
            Assert.Equal("Bueno", Prejuicios.GrupoDeMotivo("Chiyo está Bueno", out al)); Assert.True(al);
            Assert.Null(Prejuicios.GrupoDeMotivo("Beto ha insultado a Ana", out al));
            Assert.Null(Prejuicios.GrupoDeMotivo("", out al));
        }

        [Fact]
        public void El_prejuicio_es_de_grupo_no_dentro_del_propio_y_se_atenua_y_olvida()
        {
            var pj = new Prejuicios();
            pj.Anota("x", "a", "Humano", 1, false);              // x sabe que a es humano (y le cae bien)
            pj.Anota("a", "x", "Orco", -0.5, false);
            for (int k = 0; k < 10; k++) pj.Anota("a", "o" + k, "Orco", -2, false);
            Assert.True(pj.Actitud("a", "Orco") < -0.5);
            Assert.True(pj.Sesgo("a", "o1") < -0.5);
            pj.Anota("z", "o2", "Orco", 1, false); pj.Anota("w", "z", "Orco", 1, false);
            Assert.Equal(0, pj.Sesgo("z", "o2"));               // z tambien es orco: dentro del grupo no cuenta
            double s = pj.Sesgo("a", "o1");
            pj.Atenuacion = 0.5;
            Assert.Equal(s * 0.5, pj.Sesgo("a", "o1"), 6);
            double h = pj.Hostilidad(); pj.Avanza(120);
            Assert.True(pj.Hostilidad() < h * 0.5);
            Assert.Equal("Orco", pj.MediaPorGrupo()[0].Key);
            pj.Anota("a", "b", "Malo", -2, true);
            Assert.Null(pj.Grupo("b"));                         // el alineamiento no define el grupo
        }
    }

    public class MaestroYPersistenciaTests
    {
        [Fact]
        public void El_maestro_resuelve_lo_social_por_dentro_y_deja_al_juego_lo_que_no_es_suyo()
        {
            var af = new ModeloAfectivo(null);
            var ps = new Dictionary<string, Persona>();
            foreach (var id in new[] { "a", "b" }) ps[id] = new Persona { Id = id, Nombre = id.ToUpperInvariant() };
            var m = new MaestroDeJuego { Afectos = af, Persona = id => ps.ContainsKey(id) ? ps[id] : null };
            var vivos = new List<string> { "a", "b" };
            var r = m.Resuelve(new Intencion { Pawn = "a", Tipo = TipoIntencion.Charlar, Objetivo = "b" }, 1, new Rng(1), vivos);
            Assert.True(r.Resuelta); Assert.False(r.RequiereJuego); Assert.NotNull(r.Conversacion);
            Assert.Equal(1, m.Conversaciones); Assert.Equal(1, m.Exp.Intentos("a", TipoIntencion.Charlar));
            var d = m.Resuelve(new Intencion { Pawn = "a", Tipo = TipoIntencion.Descansar }, 1, new Rng(1), vivos);
            Assert.True(d.Resuelta && d.Exito);
            var p = m.Resuelve(new Intencion { Pawn = "a", Tipo = TipoIntencion.Pedir }, 1, new Rng(1), vivos);
            Assert.True(p.RequiereJuego); Assert.False(p.Resuelta);
            Assert.Equal(0, m.Exp.Intentos("a", TipoIntencion.Pedir));    // no se hizo nada todavia
            m.Cierra(new Intencion { Pawn = "a", Tipo = TipoIntencion.Pedir }, false);
            Assert.Equal(1, m.Exp.Intentos("a", TipoIntencion.Pedir));
            Assert.True(ps["a"].Estres > 0);                               // fracasar tambien pesa
            var nadie = m.Resuelve(new Intencion { Pawn = "fantasma", Tipo = TipoIntencion.Charlar }, 1, new Rng(1), vivos);
            Assert.False(nadie.Resuelta || nadie.RequiereJuego);
        }

        static EstadoSocial Nuevo()
        {
            return new EstadoSocial { Agenda = new Agenda(), Normas = new Normas(), Director = new Director(1), Cultura = new Cultura(), Cronica = new Cronica(),
                                      Costumbres = new Costumbres(), Fuertes = new RecuerdosFuertes(), Exp = new Experiencia(), Prefs = new Preferencias(), Prejuicios = new Prejuicios() };
        }

        [Fact]
        public void El_estado_social_hace_ida_y_vuelta_identica_y_respeta_versiones_futuras()
        {
            var e = Nuevo();
            e.Cronica.Anota(3, "boda", "A y B se casan", 7);
            e.Fuertes.Anota("a", "duelo", "perdi a \"C\"", -1, 8, 2);
            e.Exp.Anota("a", TipoIntencion.Aprender, true);
            e.Prefs.Registra("esquema", "Difamar", true);
            e.Prejuicios.Anota("a", "b", "Orco", -1.5, false);
            e.Cultura.Suceso("comunidad", 0.4);
            var lineas = e.Serializa();
            Assert.StartsWith("{\"k\":\"v\"", lineas[0]);
            var f = Nuevo(); bool fut;
            Assert.Equal(0, f.Carga(lineas.ToArray(), out fut)); Assert.False(fut);
            Assert.Equal(lineas, f.Serializa());
            Assert.Equal("Orco", f.Prejuicios.Grupo("b"));
            // version futura: no carga nada
            var g = Nuevo();
            g.Carga(new[] { "{\"k\":\"v\",\"v\":99}", lineas[1] }, out fut);
            Assert.True(fut); Assert.Equal(0, g.Cronica.Count);
            // basura: se cuenta, no se lanza
            Assert.Equal(2, Nuevo().Carga(new[] { "no es json", "{\"k\":\"desconocido\"}" }, out fut));
        }

        [Fact]
        public void Componentes_ausentes_se_omiten_al_guardar_e_ignoran_al_cargar()
        {
            var e = new EstadoSocial { Cronica = new Cronica() };
            e.Cronica.Anota(1, "x", "algo", 1);
            var full = Nuevo(); full.Prefs.Registra("a", "b", false);
            bool fut;
            Assert.Equal(0, e.Carga(full.Serializa().ToArray(), out fut));
            Assert.Equal(2, e.Serializa().Count);
        }
    }

    public class SimVidaTests
    {
        static Resultado Corre(int seed)
        {
            return new Mundo(new SimConfig { Dias = 360, Seed = seed }).Run();
        }

        [Fact]
        public void Un_anio_de_vida_es_estable_persiste_y_el_reino_aprende_del_jugador()
        {
            var r = Corre(1);
            Assert.True(r.PersistenciaIdempotente);
            Assert.Equal(0, r.ErroresBus);
            Assert.True(r.Nacimientos + r.Llegadas > 0 && r.Muertes > 0);
            Assert.InRange(r.CrisisEstres, 3, 60);                         // CK3: crisis de vez en cuando, no a diario
            Assert.True(r.SilenciadasPorPreferencia > 0);
            Assert.True(r.OrdenAplicada && r.NormaForzadaVigente);
            Assert.True(r.AprobadasFavorecidoDespues > r.AprobadasFavorecidoAntes);
            Assert.True(r.HostilidadGrupalFinal > 0 && r.HostilidadGrupalFinal < 0.6);
        }
    }
}
