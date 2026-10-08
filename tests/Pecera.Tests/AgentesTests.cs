using System;
using System.Collections.Generic;
using System.Linq;
using Pecera.Core;
using Xunit;

namespace Pecera.Tests
{
    static class Mundito
    {
        public static ContextoMundo Ctx(ModeloAfectivo m, int dia, params Persona[] ps)
        {
            var dic = ps.ToDictionary(p => p.Id);
            return new ContextoMundo { Afectos = m, Vivos = ps.Select(p => p.Id).ToList(), Dia = dia, Persona = id => dic[id], Nombre = id => dic[id].Nombre };
        }

        public static Persona P(string id, params Ambicion[] ambs)
        {
            var p = new Persona { Id = id, Nombre = id.ToUpperInvariant() };
            p.Needs.Social = 0.9; p.Needs.Descanso = 0.9; p.Needs.Seguridad = 0.9; p.Needs.Autorrealizacion = 0.9;   // sin urgencias salvo que el test las ponga
            p.Ambiciones.AddRange(ambs);
            return p;
        }

        public static Ambicion A(string cat, double prio = 0.7) { return new Ambicion { Texto = "quiere " + cat, Categoria = cat, Prioridad = prio }; }
    }

    public class PersonaTests
    {
        [Fact]
        public void Generacion_determinista_acotada_y_con_ambiciones()
        {
            for (int i = 0; i < 60; i++)
            {
                var f = FichaGen.Determinista("p" + i, "N" + i);
                var a = PersonaGen.Desde(f); var b = PersonaGen.Desde(f);
                Assert.Equal(a.ToJson(), b.ToJson());
                foreach (var v in new[] { a.Extroversion, a.Amabilidad, a.Escrupulosidad, a.Neuroticismo, a.Apertura }) Assert.InRange(v, 0, 1);
                Assert.NotEmpty(a.Ambiciones);
                Assert.All(a.Ambiciones, x => Assert.Contains(x.Categoria, Ambicion.Categorias));
                Assert.NotEqual("", a.Resumen());
            }
        }

        [Fact]
        public void Ida_y_vuelta_json_y_dato_corrupto_se_descarta()
        {
            var p = PersonaGen.Desde(FichaGen.Determinista("p1", "Ana"));
            p.Needs.Social = 0.123; p.Ambiciones[0].Progreso = 0.4;
            var q = Persona.FromJson(p.ToJson());
            Assert.Equal(p.ToJson(), q.ToJson());
            Assert.Null(Persona.FromJson("{cortada"));
            Assert.Null(Persona.FromJson("{\"v\":1}"));
            var raro = Persona.FromJson("{\"id\":\"x\",\"amb\":[{\"t\":\"a\",\"c\":\"conquistar_la_luna\",\"pr\":5}]}");
            Assert.Empty(raro.Ambiciones);
        }

        [Fact]
        public void Llm_solo_aporta_categorias_validas_y_basura_deja_la_persona_determinista()
        {
            var f = FichaGen.Determinista("p1", "Ana");
            var p = PersonaGen.DesdeLlm(f, "{\"oraculo\":\"la heredera\",\"ambiciones\":[{\"texto\":\"casarse con el herrero\",\"categoria\":\"casarse\",\"plazo\":\"corto\"},{\"texto\":\"volar\",\"categoria\":\"volar\"}]}");
            Assert.Equal("la heredera", p.Oraculo); Assert.Single(p.Ambiciones); Assert.Equal("casarse", p.Ambiciones[0].Categoria); Assert.Equal(Plazo.Corto, p.Ambiciones[0].Plazo);
            Assert.Equal(PersonaGen.Desde(f).ToJson(), PersonaGen.DesdeLlm(f, "no es json").ToJson());
            Assert.Equal(PersonaGen.Desde(f).ToJson(), PersonaGen.DesdeLlm(f, "{\"ambiciones\":[{\"categoria\":\"volar\",\"texto\":\"x\"}]}").ToJson());
        }

        [Fact]
        public void Las_necesidades_nunca_salen_de_rango_y_la_mas_urgente_es_estable()
        {
            var p = Mundito.P("a"); p.Extroversion = 1; p.Neuroticismo = 1;
            for (int i = 0; i < 500; i++) { p.Needs.Avanza(0.5 + (i % 7), p); foreach (var n in Necesidades.Nombres) Assert.InRange(p.Needs.De(n), 0, 1); }
            double v; Assert.Equal("descanso", p.Needs.Mas(out v)); Assert.Equal(0, v);     // empate a 0 => orden fijo
        }

        [Fact]
        public void Almacen_persiste_y_recarga()
        {
            var d = new MemoryStorage(); var a = new AlmacenPersonas(d);
            var f = FichaGen.Determinista("p1", "Ana"); var p = a.GetOCrea(f); p.Reputacion = 0.9; a.Guarda();
            var b = new AlmacenPersonas(d);
            Assert.Equal(0.9, b.Get("p1").Reputacion); Assert.Equal(1, b.Count);
            d.Append("personas.jsonl", "{cortada"); Assert.Equal(1, new AlmacenPersonas(d).LineasCorruptas);
        }
    }

    public class AgendaTests
    {
        static Intencion I(string pawn, TipoIntencion t, string obj, double prio) { return new Intencion { Pawn = pawn, Tipo = t, Objetivo = obj, Prioridad = prio, Texto = "x" }; }

        [Fact]
        public void Duplicado_refuerza_y_no_duplica()
        {
            var ag = new Agenda();
            var a = ag.Anade(I("p", TipoIntencion.Charlar, "q", 0.3), 1); var b = ag.Anade(I("p", TipoIntencion.Charlar, "q", 0.8), 1);
            Assert.Same(a, b); Assert.Equal(0.8, a.Prioridad); Assert.Equal(1, ag.Count);
        }

        [Fact]
        public void Topes_diario_y_por_pawn_y_gana_la_mas_prioritaria()
        {
            var ag = new Agenda { MaxAltasPorDia = 2, MaxPorPawn = 3 };
            Assert.NotNull(ag.Anade(I("p", TipoIntencion.Charlar, "a", 0.5), 1));
            Assert.NotNull(ag.Anade(I("p", TipoIntencion.Visitar, "b", 0.6), 1));
            Assert.Null(ag.Anade(I("p", TipoIntencion.Trabajar, "", 0.9), 1));        // tope diario
            Assert.NotNull(ag.Anade(I("p", TipoIntencion.Trabajar, "", 0.9), 2));
            Assert.Null(ag.Anade(I("p", TipoIntencion.Aprender, "", 0.1), 2));         // 3 activas (tope): no supera a la mas floja (0.5)
            // entra una mejor y expulsa a la mas floja
            ag.MaxAltasPorDia = 10;
            Assert.Null(ag.Anade(I("p", TipoIntencion.Descansar, "", 0.05), 3));      // no supera a nadie
            Assert.NotNull(ag.Anade(I("p", TipoIntencion.Descansar, "", 0.7), 3));
            Assert.DoesNotContain(ag.Top("p", 9), x => x.Tipo == TipoIntencion.Charlar);
            Assert.Equal(3, ag.Top("p", 9).Count);
            Assert.Equal(TipoIntencion.Trabajar, ag.Top("p", 1)[0].Tipo);
        }

        [Fact]
        public void Tras_cerrar_una_peticion_o_venganza_no_se_repite_hasta_pasado_el_enfriamiento()
        {
            var ag = new Agenda { EnfriaPedir = 14, EnfriaHostil = 10 };
            var a = ag.Anade(new Intencion { Pawn = "p", Tipo = TipoIntencion.Pedir, Categoria = "defensa", Prioridad = 0.5, Texto = "x" }, 1);
            ag.Marca(a, EstadoIntencion.Fallida);
            Assert.Null(ag.Anade(new Intencion { Pawn = "p", Tipo = TipoIntencion.Pedir, Categoria = "cultura", Prioridad = 0.9, Texto = "otra" }, 5));   // cualquier categoria
            Assert.NotNull(ag.Anade(new Intencion { Pawn = "q", Tipo = TipoIntencion.Pedir, Categoria = "cultura", Prioridad = 0.9, Texto = "otro pawn" }, 5));
            Assert.NotNull(ag.Anade(new Intencion { Pawn = "p", Tipo = TipoIntencion.Pedir, Categoria = "cultura", Prioridad = 0.9, Texto = "ya" }, 16));
            var h = ag.Anade(new Intencion { Pawn = "p", Tipo = TipoIntencion.Vengarse, Objetivo = "z", Prioridad = 0.5, Texto = "v" }, 20);
            ag.Marca(h, EstadoIntencion.Hecha);
            Assert.Null(ag.Anade(new Intencion { Pawn = "p", Tipo = TipoIntencion.Vengarse, Objetivo = "z", Prioridad = 0.5, Texto = "v" }, 25));
            Assert.NotNull(ag.Anade(new Intencion { Pawn = "p", Tipo = TipoIntencion.Vengarse, Objetivo = "z", Prioridad = 0.5, Texto = "v" }, 31));
            Assert.NotNull(ag.Anade(new Intencion { Pawn = "p", Tipo = TipoIntencion.Charlar, Objetivo = "z", Prioridad = 0.5, Texto = "c" }, 21));    // charlar no se enfria
        }

        [Fact]
        public void Caduca_lo_vencido_y_poda_lo_cerrado()
        {
            var ag = new Agenda();
            var a = ag.Anade(I("p", TipoIntencion.Charlar, "a", 0.5), 1); a.Vence = 3;
            Assert.Equal(1, ag.Caduca(5)); Assert.Equal(EstadoIntencion.Descartada, a.Estado);
            ag.Caduca(40); Assert.Equal(0, ag.Count);
        }

        [Fact]
        public void Marca_cuenta_hechas_y_fallidas_una_sola_vez()
        {
            var ag = new Agenda(); var a = ag.Anade(I("p", TipoIntencion.Charlar, "a", 0.5), 1);
            ag.Marca(a, EstadoIntencion.Hecha); ag.Marca(a, EstadoIntencion.Hecha); ag.Marca(a, EstadoIntencion.Fallida);
            Assert.Equal(1, ag.Hechas); Assert.Equal(0, ag.Fallidas);
        }
    }

    public class PlanificadorTests
    {
        [Fact]
        public void La_soledad_lleva_a_charlar_con_el_mejor_amigo()
        {
            var m = new ModeloAfectivo(null); var a = Mundito.P("a"); var b = Mundito.P("b"); var c = Mundito.P("c");
            a.Needs.Social = 0.1;
            m.Evento("a", "c", TipoEvento.Aprecio, 0.9);
            var plan = Planificador.Planea(a, Mundito.Ctx(m, 5, a, b, c));
            Assert.Equal(TipoIntencion.Charlar, plan[0].Tipo); Assert.Equal("c", plan[0].Objetivo);
        }

        [Fact]
        public void Cortejar_exige_afecto_y_no_rencor_y_la_venganza_exige_rencor()
        {
            var m = new ModeloAfectivo(null);
            var a = Mundito.P("a", Mundito.A("casarse"), Mundito.A("vengar")); var b = Mundito.P("b"); var c = Mundito.P("c");
            var sinNada = Planificador.Planea(a, Mundito.Ctx(m, 1, a, b, c));
            Assert.DoesNotContain(sinNada, x => x.Tipo == TipoIntencion.Vengarse);
            m.Evento("a", "b", TipoEvento.Cortejo, 0.9); m.Evento("a", "b", TipoEvento.Aprecio, 0.9);
            m.Evento("a", "c", TipoEvento.Traicion, 1);
            var plan = Planificador.Planea(a, Mundito.Ctx(m, 1, a, b, c));
            Assert.Contains(plan, x => x.Tipo == TipoIntencion.Cortejar && x.Objetivo == "b");
            Assert.Contains(plan, x => x.Hostil && x.Objetivo == "c");     // el guion de venganza empieza por intrigar y termina en vengarse
            Assert.True(plan.Count <= 3);
        }

        [Fact]
        public void El_muy_amable_o_el_agradecido_no_trama_venganza()
        {
            var m = new ModeloAfectivo(null); var a = Mundito.P("a", Mundito.A("vengar")); var c = Mundito.P("c");
            m.Evento("a", "c", TipoEvento.Traicion, 1);
            a.Amabilidad = 0.95;
            Assert.DoesNotContain(Planificador.Planea(a, Mundito.Ctx(m, 1, a, c)), x => x.Hostil);
            a.Amabilidad = 0.3; m.Evento("a", "c", TipoEvento.Ayuda, 1); m.Evento("a", "c", TipoEvento.Ayuda, 1);
            Assert.True(m.Get("a", "c").Deuda >= 0.4);
            Assert.DoesNotContain(Planificador.Planea(a, Mundito.Ctx(m, 1, a, c)), x => x.Hostil);
        }

        [Fact]
        public void Es_determinista_y_no_se_autotarget()
        {
            var m = new ModeloAfectivo(null);
            var ps = Enumerable.Range(0, 8).Select(i => Mundito.P("p" + i, Mundito.A("casarse"), Mundito.A("paz"))).ToArray();
            ps[0].Needs.Social = 0.1;
            m.Evento("p0", "p3", TipoEvento.Aprecio, 0.7);
            var x = string.Join("|", Planificador.Planea(ps[0], Mundito.Ctx(m, 3, ps)).Select(i => i.Tipo + ">" + i.Objetivo).ToArray());
            var y = string.Join("|", Planificador.Planea(ps[0], Mundito.Ctx(m, 3, ps)).Select(i => i.Tipo + ">" + i.Objetivo).ToArray());
            Assert.Equal(x, y);
            foreach (var p in ps) Assert.All(Planificador.Planea(p, Mundito.Ctx(m, 3, ps)), i => Assert.NotEqual(p.Id, i.Objetivo));
        }

        [Fact]
        public void Con_vivos_ya_ordenados_el_plan_es_identico_y_no_ordena_de_nuevo()
        {
            var m = new ModeloAfectivo(null);
            var ps = Enumerable.Range(0, 12).Select(i => Mundito.P("p" + (11 - i), Mundito.A("casarse"), Mundito.A("vengar"))).ToArray();    // ids desordenados a proposito
            m.Evento("p3", "p7", TipoEvento.Traicion, 1); m.Evento("p3", "p4", TipoEvento.Aprecio, 1);
            var libre = Mundito.Ctx(m, 9, ps);
            var ordenado = Mundito.Ctx(m, 9, ps); ordenado.Vivos = ordenado.Vivos.OrderBy(x => x, StringComparer.Ordinal).ToList(); ordenado.VivosOrdenados = true;
            foreach (var p in ps)
                Assert.Equal(string.Join("|", Planificador.Planea(p, libre).Select(i => i.Tipo + ">" + i.Objetivo + ">" + Json.Num(i.Prioridad)).ToArray()),
                             string.Join("|", Planificador.Planea(p, ordenado).Select(i => i.Tipo + ">" + i.Objetivo + ">" + Json.Num(i.Prioridad)).ToArray()));
        }

        [Fact]
        public void Valida_rechaza_objetivos_inexistentes_y_propios()
        {
            var m = new ModeloAfectivo(null); var a = Mundito.P("a"); var b = Mundito.P("b"); var c = Mundito.Ctx(m, 1, a, b);
            Assert.False(Planificador.Valida(a, new Intencion { Pawn = "a", Tipo = TipoIntencion.Charlar, Objetivo = "fantasma" }, c));
            Assert.False(Planificador.Valida(a, new Intencion { Pawn = "a", Tipo = TipoIntencion.Charlar, Objetivo = "a" }, c));
            Assert.True(Planificador.Valida(a, new Intencion { Pawn = "a", Tipo = TipoIntencion.Charlar, Objetivo = "b" }, c));
            Assert.False(Planificador.Valida(a, new Intencion { Pawn = "a", Tipo = TipoIntencion.Pedir, Categoria = "dominar_el_mundo" }, c));
        }
    }

    public class PlanLlmTests
    {
        [Fact]
        public void El_llm_no_puede_saltarse_los_guardarrailes()
        {
            var m = new ModeloAfectivo(null);
            var a = Mundito.P("a"); var b = Mundito.P("b"); var c = Mundito.P("c");
            m.Evento("a", "c", TipoEvento.Traicion, 1);
            var ctx = Mundito.Ctx(m, 7, a, b, c);
            string raw = "```json\n{\"planes\":[" +
                "{\"id\":\"a\",\"tipo\":\"vengarse\",\"objetivo\":\"c\",\"texto\":\"A planea su venganza\",\"piensa\":\"nunca olvidare\"}," +
                "{\"id\":\"b\",\"tipo\":\"vengarse\",\"objetivo\":\"a\",\"texto\":\"B sin motivo\"}," +      // sin rencor => rechazada
                "{\"id\":\"c\",\"tipo\":\"volar\",\"objetivo\":\"\"}," +                                         // tipo inventado
                "{\"id\":\"zzz\",\"tipo\":\"charlar\",\"objetivo\":\"a\"}," +                                    // pawn inexistente
                "{\"id\":\"a\",\"tipo\":\"charlar\",\"objetivo\":\"b\"}]}\n```";                                 // id repetido
            var r = PlanLlm.Parse(raw, new List<Persona> { a, b, c }, ctx);
            Assert.Single(r.Intenciones); Assert.Equal("a", r.Intenciones[0].Pawn); Assert.Equal("llm", r.Intenciones[0].Origen);
            Assert.Equal(4, r.Rechazadas);
            Assert.Equal("nunca olvidare", r.Pensamientos["a"]);
        }

        [Fact]
        public void Basura_y_json_cortado_no_rompen_nada()
        {
            var a = Mundito.P("a"); var ctx = Mundito.Ctx(new ModeloAfectivo(null), 1, a);
            Assert.Empty(PlanLlm.Parse("", new List<Persona> { a }, ctx).Intenciones);
            Assert.Empty(PlanLlm.Parse("nada", new List<Persona> { a }, ctx).Intenciones);
            var r = PlanLlm.Parse("{\"planes\":[{\"id\":\"a\",\"tipo\":\"descansar\",\"objetivo\":\"\",\"texto\":\"A descansa\",\"piensa\":\"cans", new List<Persona> { a }, ctx);
            Assert.Single(r.Intenciones);
        }

        [Fact]
        public void El_prompt_lista_a_todos_y_sus_opciones()
        {
            var m = new ModeloAfectivo(null); var a = Mundito.P("a", Mundito.A("casarse")); var b = Mundito.P("b");
            var ctx = Mundito.Ctx(m, 2, a, b);
            string u = PlanLlm.Usuario(new List<Persona> { a, b }, new List<List<Intencion>> { new List<Intencion> { new Intencion { Tipo = TipoIntencion.Cortejar, Objetivo = "b" } }, new List<Intencion>() }, ctx);
            Assert.Contains("id=a", u); Assert.Contains("id=b", u); Assert.Contains("opciones: cortejar b", u);
        }
    }

    public class CicloVitalTests
    {
        [Fact]
        public void Sin_plan_las_ambiciones_solo_avanzan_a_goteo_y_nunca_se_cumplen_solas()
        {
            var ag = new Agenda { MaxAltasPorDia = 99, MaxPorPawn = 99 };
            var p = Mundito.P("p", Mundito.A("aprender")); p.Needs.Autorrealizacion = 0.1; p.Escrupulosidad = 1;
            int cumplidas = 0;
            for (int d = 0; d < 40; d++)
            {
                var i = ag.Anade(new Intencion { Pawn = "p", Tipo = TipoIntencion.Aprender, Objetivo = "", Texto = "aprende", Prioridad = 0.5, Vence = d + 3 }, d);
                i.Estado = EstadoIntencion.Hecha;
                cumplidas += Agente.Consolida(p, ag, 1).Count;
                foreach (var n in Necesidades.Nombres) Assert.InRange(p.Needs.De(n), 0, 1);
            }
            // Sin guion (plan de varios pasos) el goteo por categoria se detiene en 0.95: cumplir una ambicion exige completar el plan.
            Assert.Equal(0, cumplidas); Assert.False(p.Ambiciones[0].Cumplida); Assert.Equal(0.95, p.Ambiciones[0].Progreso, 6);
        }

        [Fact]
        public void Una_intencion_no_cuenta_dos_veces()
        {
            var ag = new Agenda(); var p = Mundito.P("p", Mundito.A("paz")); p.Needs.Social = 0.1;
            var i = ag.Anade(new Intencion { Pawn = "p", Tipo = TipoIntencion.Charlar, Objetivo = "q", Prioridad = 0.5, Texto = "x" }, 1);
            ag.Marca(i, EstadoIntencion.Hecha);
            Agente.Consolida(p, ag, 0); double tras = p.Needs.Social;
            Agente.Consolida(p, ag, 0);
            Assert.Equal(tras, p.Needs.Social); Assert.True(tras >= 0.3);
        }

        [Fact]
        public void Reflexion_un_rencor_profundo_engendra_una_ambicion_de_venganza_solo_en_el_inquieto()
        {
            var m = new ModeloAfectivo(null);
            var a = Mundito.P("a"); a.Neuroticismo = 0.9; var b = Mundito.P("b"); var c = Mundito.P("c"); c.Neuroticismo = 0.1;
            m.Evento("a", "b", TipoEvento.Traicion, 1); m.Evento("a", "b", TipoEvento.Duelo, 1); m.Evento("c", "b", TipoEvento.Traicion, 1); m.Evento("c", "b", TipoEvento.Duelo, 1);
            var ctx = Mundito.Ctx(m, 3, a, b, c);
            var ra = Agente.Reflexiona(a, ctx); var rc = Agente.Reflexiona(c, ctx);
            Assert.True(ra.AmbicionNueva); Assert.Contains(a.Ambiciones, x => x.Categoria == "vengar" && x.Objetivo == "b");
            Assert.False(rc.AmbicionNueva); Assert.Contains("rencor", ra.Texto);
            Agente.Reflexiona(a, ctx); Assert.Equal(1, a.Ambiciones.Count(x => x.Categoria == "vengar"));       // no se duplica
        }

        [Fact]
        public void Presupuesto_respeta_techos_total_y_se_renueva_cada_dia()
        {
            var c = new ManualClock { Ticks = 1 };
            var pr = new PresupuestoLlm(c) { LlamadasPorDia = 10, DiaTicks = TimeSpan.TicksPerMinute };
            int plan = 0; while (pr.Pide(PrioridadLlm.Planeacion)) plan++;
            Assert.Equal(5, plan);                                                    // techo 50 %
            int nar = 0; while (pr.Pide(PrioridadLlm.Narrativa)) nar++;
            Assert.Equal(2, nar);
            int esc = 0; while (pr.Pide(PrioridadLlm.Escritura)) esc++;
            Assert.Equal(3, esc);                                                     // escritura usa lo que quede
            Assert.Equal(10, pr.Total); Assert.False(pr.Pide(PrioridadLlm.Conversacion));
            c.AdvanceSeconds(61);
            Assert.True(pr.Pide(PrioridadLlm.Conversacion)); Assert.Equal(1, pr.Total);
        }
    }

    public class ConversaNarrativaTests
    {
        [Fact]
        public void El_resultado_se_aplica_al_modelo_y_es_determinista_con_semilla()
        {
            Func<string> corre = () =>
            {
                var m = new ModeloAfectivo(null); var r = new Rng(11); var a = Mundito.P("a"); var b = Mundito.P("b");
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < 60; i++) { var t = Dialogo.Elige(a, b, m, r); var c = Dialogo.Resuelve(a, b, t, m, r, i); sb.Append(t).Append(c.Exito).Append(','); }
                sb.Append(Json.Num(m.Sentimiento("a", "b")));
                return sb.ToString();
            };
            Assert.Equal(corre(), corre());
        }

        [Fact]
        public void Los_temas_dependen_del_estado_y_las_conversaciones_cambian_afectos_acotados()
        {
            var m = new ModeloAfectivo(null); var r = new Rng(3); var a = Mundito.P("a"); var b = Mundito.P("b");
            m.Evento("a", "b", TipoEvento.Cortejo, 1); m.Evento("a", "b", TipoEvento.Aprecio, 1);
            Assert.Equal(Tema.Cortejo, Dialogo.Elige(a, b, m, r));
            var m2 = new ModeloAfectivo(null); m2.Evento("a", "b", TipoEvento.Traicion, 1);
            int quejas = 0; for (int i = 0; i < 50; i++) if (Dialogo.Elige(a, b, m2, new Rng(i)) == Tema.Queja) quejas++;
            Assert.True(quejas > 15);
            for (int i = 0; i < 300; i++)
            {
                var tema = (Tema)(i % 7); var c = Dialogo.Resuelve(a, b, tema, m, r, i);
                Assert.NotEqual("", c.Efecto);
                Par p = m.Get("a", "b"); Assert.InRange(p.Afecto, -1, 1); Assert.InRange(p.Rencor, 0, 1);
                Assert.InRange(a.Needs.Social, 0, 1); Assert.InRange(b.Needs.Seguridad, 0, 1);
            }
        }

        [Fact]
        public void Una_queja_que_sale_bien_apacigua_y_la_que_sale_mal_enciende()
        {
            int mejora = 0, empeora = 0;
            for (int s = 0; s < 200; s++)
            {
                var m = new ModeloAfectivo(null); m.Evento("b", "a", TipoEvento.Agravio, 0.6); double antes = m.Get("b", "a").Rencor;
                var c = Dialogo.Resuelve(Mundito.P("a"), Mundito.P("b"), Tema.Queja, m, new Rng(s), 1);
                if (m.Get("b", "a").Rencor < antes - 1e-9) { mejora++; Assert.True(c.Exito); }
                else if (m.Get("b", "a").Rencor > antes + 1e-9) { empeora++; Assert.False(c.Exito); }
            }
            Assert.True(mejora > 20 && empeora > 20);
        }

        [Fact]
        public void Lineas_del_llm_se_validan_y_si_no_hay_plantilla()
        {
            var ok = Dialogo.ParseLineas("{\"lineas\":[{\"h\":\"A\",\"t\":\"Hola\"},{\"h\":\"b\",\"t\":\"Buenas\"}]}", "idA", "idB");
            Assert.Equal(new[] { "idA", "idB" }, ok.Select(l => l.Hablante).ToArray());
            Assert.Null(Dialogo.ParseLineas("{\"lineas\":[{\"h\":\"A\",\"t\":\"solo una\"}]}", "a", "b"));
            Assert.Null(Dialogo.ParseLineas("{\"lineas\":[{\"h\":\"C\",\"t\":\"x\"},{\"h\":\"A\",\"t\":\"y\"}]}", "a", "b"));
            Assert.Null(Dialogo.ParseLineas("{\"lineas\":[{\"h\":\"A\",\"t\":\"\"},{\"h\":\"B\",\"t\":\"y\"}]}", "a", "b"));
            Assert.Null(Dialogo.ParseLineas("basura", "a", "b"));
            foreach (Tema t in Enum.GetValues(typeof(Tema)))
                foreach (var exito in new[] { true, false })
                    Assert.Equal(2, Dialogo.Plantilla(new Conversacion { A = "a", B = "b", Tema = t, Exito = exito }, "A", "B").Count);
        }

        [Fact]
        public void Narrativa_plantilla_temporada_y_validacion_del_llm()
        {
            var c = new Cronica { DiasPorTemporada = 30 };
            c.Anota(5, "sucesion", "Aldeano1 muere; le sucede Aldeano18", 9);
            c.Anota(6, "peticion", "Aldeano3 obtiene obra", 1.5);
            c.Anota(40, "juicio", "Aldeano2 es condenado", 5);
            var hs = Narrador.DeTemporada(c, 0, 5);
            Assert.Equal(2, hs.Count); Assert.Equal(5, hs[0].Dia);
            Assert.Contains("contuvo el aliento", hs[0].Texto); Assert.Equal("Primavera del anio 1", hs[0].Titulo);
            Assert.Single(Narrador.DeTemporada(c, 0, 1));
            var origen = c.Hitos[0];
            Assert.Null(Narrador.Valida("# Titulo\n- uno\n- dos", origen));
            Assert.Null(Narrador.Valida("corto", origen));
            Assert.Null(Narrador.Valida("Una historia larga y bonita sobre un reino lejano sin ningun nombre conocido.", origen));    // no conserva ningun hecho
            Assert.NotNull(Narrador.Valida("La corte lloro cuando Aldeano1 murio y Aldeano18 tomo la corona entre susurros.", origen));
            Assert.Contains("Historias de", Narrador.Markdown(hs, "Reinoland")); Assert.Contains("Aun no hay", Narrador.Markdown(new List<Historia>(), "x"));
        }
    }
}
