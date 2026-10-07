using System;
using System.Collections.Generic;
using System.Linq;
using Pecera.Core;
using Xunit;

namespace Pecera.Tests
{
    public class CompuertaTests
    {
        [Fact]
        public void Veto_dentro_de_la_ventana_cancela_y_pasada_la_ventana_se_entrega_una_vez()
        {
            var c = new ManualClock { Ticks = 1 };
            var g = new Compuerta(c) { VentanaVetoSegundos = 30 };
            var d1 = g.Propone("peticion", "p1", "x", "r", null, false);
            var d2 = g.Propone("peticion", "p2", "y", "r", null, false);
            Assert.Empty(g.Listas());
            Assert.True(g.Veta(d1.Id));
            c.AdvanceSeconds(31);
            var listas = g.Listas();
            Assert.Single(listas); Assert.Equal(d2.Id, listas[0].Id);
            Assert.Empty(g.Listas());                       // una sola vez
            Assert.False(g.Veta(d2.Id));                    // ya ejecutada
        }

        [Fact]
        public void Modo_dios_no_tiene_ventana()
        {
            var g = new Compuerta(new ManualClock { Ticks = 1 });
            g.Propone("esquema", "k", "x", "r", null, true);
            Assert.Single(g.Listas());
        }

        [Fact]
        public void Tope_diario_y_enfriamiento_por_clave()
        {
            var c = new ManualClock { Ticks = 1 };
            var g = new Compuerta(c) { EnfriarClaveSegundos = 100 };
            g.FijaTopeDia("esquema", 2);
            Assert.NotNull(g.Propone("esquema", "a", "", "", null, true));
            Assert.Null(g.Propone("esquema", "a", "", "", null, true));        // enfriamiento
            Assert.NotNull(g.Propone("esquema", "b", "", "", null, true));
            Assert.Null(g.Propone("esquema", "c", "", "", null, true));        // tope
            c.Ticks += g.DiaTicks + 1;
            Assert.NotNull(g.Propone("esquema", "c", "", "", null, true));     // nuevo dia
        }

        [Fact]
        public void Una_propuesta_rechazada_por_tope_no_consume_cupo_de_otra_clase()
        {
            var g = new Compuerta(new ManualClock { Ticks = 1 });
            g.FijaTopeDia("esquema", 0);
            Assert.Null(g.Propone("esquema", "a", "", "", null, true));
            Assert.NotNull(g.Propone("peticion", "a", "", "", null, true));
        }
    }

    public class RobustezTests
    {
        [Fact]
        public void Disyuntor_degrada_tras_fallos_y_se_recupera()
        {
            var c = new ManualClock { Ticks = 1 };
            var s = new SaludLlm(c) { Umbral = 3 };
            s.Fallo(); s.Fallo();
            Assert.True(s.Disponible);
            s.Fallo();
            Assert.False(s.Disponible); Assert.True(s.Degradado);
            c.AdvanceSeconds(31);
            Assert.True(s.Disponible);                    // un reintento
            s.Fallo();                                    // falla otra vez: espera el doble
            Assert.False(s.Disponible);
            c.AdvanceSeconds(31); Assert.False(s.Disponible);
            c.AdvanceSeconds(31); Assert.True(s.Disponible);
            s.Exito();
            Assert.False(s.Degradado);
        }

        [Fact]
        public void La_espera_nunca_pasa_de_10_minutos()
        {
            var c = new ManualClock { Ticks = 1 };
            var s = new SaludLlm(c) { Umbral = 1 };
            for (int i = 0; i < 20; i++) s.Fallo();
            c.AdvanceSeconds(601);
            Assert.True(s.Disponible);
        }

        [Theory]
        [InlineData(null, "1.0", null, true)]
        [InlineData("1.0", "1.0", null, true)]
        [InlineData("1.0", "1.1", null, false)]
        [InlineData("1.0", "1.1", "1.0", false)]
        [InlineData("1.0", "1.1", "1.1", true)]
        public void Guarda_de_version(string guardada, string actual, string confirmada, bool seguro)
        {
            Assert.Equal(seguro, GuardaVersion.Seguro(guardada, actual, confirmada));
        }

        [Fact]
        public void Formato_mas_nuevo_no_se_sobrescribe()
        {
            Assert.Equal(2, FormatoDatos.LeeVersion(new[] { "{\"op\":\"v\",\"v\":2}" }));
            Assert.False(FormatoDatos.PuedeEscribir(2));
            Assert.True(FormatoDatos.PuedeEscribir(FormatoDatos.LeeVersion(new string[0])));
        }

        [Fact]
        public void Evidencia_veredictos()
        {
            var e = new Evidence(new ManualClock { Ticks = TimeSpan.TicksPerDay * 400 });
            e.Criterio("burbuja", 5, 0); e.Criterio("empujon", 3, 1);
            Assert.Equal("sin_datos", e.Veredicto("burbuja"));
            for (int i = 0; i < 4; i++) e.Ok("burbuja", "Ana dice");
            Assert.Equal("insuficiente", e.Veredicto("burbuja"));
            e.Ok("burbuja", "x"); Assert.Equal("pasa", e.Veredicto("burbuja"));
            e.Fail("burbuja", "excepcion"); Assert.Equal("falla", e.Veredicto("burbuja"));
            string j = e.ToJson("0.2", "\"juego\":\"1.0\"");
            object o; Assert.True(Json.TryParse(j, out o));
            Assert.Equal(2, Json.Lista(o, "funciones").Count);
        }
    }

    public class SecretosTests
    {
        static RedSecretos Red(ModeloAfectivo m, int seed, ManualClock c)
        {
            return new RedSecretos(m, id => new Ficha { Id = id, Locuacidad = 0.9 }, c, new Rng(seed));
        }

        [Fact]
        public void Nadie_cuenta_su_propio_secreto_ni_lo_repite_a_quien_ya_lo_sabe()
        {
            var m = new ModeloAfectivo(null); var c = new ManualClock { Ticks = 1 };
            var r = Red(m, 1, c); r.ProbBase = 5;
            r.Asigna("a", "robo grano", 0.9);
            for (int i = 0; i < 200; i++) Assert.Empty(r.Conversan("a", "b").Where(f => f.Sujeto == "a" && f.Emisor == "a"));
            Assert.Equal(1, r.Count);
        }

        [Fact]
        public void El_chisme_se_propaga_pierde_fidelidad_y_cambia_el_texto()
        {
            var m = new ModeloAfectivo(null); var c = new ManualClock { Ticks = 1 };
            var r = Red(m, 7, c); r.ProbBase = 5;
            var s = r.Asigna("a", "robo en secreto grano y huyo de un crimen", 0.8);
            string[] ids = { "a", "b", "c", "d", "e", "f" };
            m.Evento("b", "a", TipoEvento.Agravio, 0.9);          // b odia a a -> cuenta con malicia
            m.Evento("b", "c", TipoEvento.Ayuda, 0.5);
            // b sabe el secreto de a (se lo dijo a): simulamos la primera fuga
            s.Saben["b"] = new Conocimiento { Version = s.Texto, Fidelidad = 0.95, Fuente = "a", Saltos = 1 };
            for (int i = 0; i < 400; i++)
            {
                var x = ids[i % ids.Length]; var y = ids[(i * 3 + 1) % ids.Length];
                if (x != y) r.Conversan(x, y);
            }
            Assert.True(r.Alcance(s.Id) >= 4, "alcance " + r.Alcance(s.Id));
            Assert.True(r.FidelidadMedia(s.Id) < 0.95);
            Assert.True(r.SaltosMaximos() >= 2);
            Assert.Contains(r.Fugas, f => f.Texto != s.Texto);
            Assert.True(m.Get("c", "a").Rencor > 0 || m.Get("d", "a").Rencor > 0);   // consecuencia afectiva
        }

        [Fact]
        public void Con_semilla_fija_es_determinista()
        {
            Func<string> corre = () =>
            {
                var m = new ModeloAfectivo(null); var c = new ManualClock { Ticks = 1 };
                var r = Red(m, 42, c); r.ProbBase = 3;
                foreach (var id in new[] { "a", "b", "c", "d" }) r.Asigna(id, RedSecretos.SecretoDeReserva(id), 0.7);
                for (int i = 0; i < 100; i++) r.Conversan("abcd"[i % 4].ToString(), "abcd"[(i + 1 + i / 4) % 4].ToString());
                return string.Join("\n", r.Fugas.Select(f => f.ToJson()).ToArray());
            };
            Assert.Equal(corre(), corre());
        }

        [Fact]
        public void La_confianza_y_el_rencor_suben_la_probabilidad()
        {
            var m = new ModeloAfectivo(null); var c = new ManualClock { Ticks = 1 };
            var r = Red(m, 1, c);
            double basal = r.ProbContar("a", "b", "x");
            m.Evento("a", "x", TipoEvento.Agravio, 0.9);
            Assert.True(r.ProbContar("a", "b", "x") > basal);
            m.Evento("a", "b", TipoEvento.Ayuda, 0.9);
            Assert.True(r.ProbContar("a", "b", "x") > basal);
        }

        [Fact]
        public void Distorsion_baja_con_la_fidelidad()
        {
            var rng = new Rng(1);
            Assert.Equal("robo grano", RedSecretos.Distorsiona("robo grano", 0.99, rng));
            Assert.StartsWith("dicen que", RedSecretos.Distorsiona("robo grano", 0.8, rng));
            Assert.Contains("se rumorea", RedSecretos.Distorsiona("robo grano", 0.5, rng));
        }

        [Fact]
        public void Fuga_json_es_valido()
        {
            var f = new Fuga { SecretoId = 1, Emisor = "a\"", Receptor = "b", Sujeto = "c", Texto = "x\ny", Fidelidad = 0.5 };
            object o; Assert.True(Json.TryParse(f.ToJson(), out o));
        }
    }

    public class EsquemasTests
    {
        const string CAT = "# catalogo\nInsultar|seguro|hostil\nEnvenenar|prohibido|hostil\nCortejar|seguro|amistoso\nRaro|inventado\n";

        [Fact]
        public void Catalogo_vacio_no_propone_nada()
        {
            var m = new ModeloAfectivo(null); m.Evento("a", "b", TipoEvento.Traicion, 1); m.Avanza(10);
            Assert.Empty(new MotorEsquemas().Propone(m, CatalogoEsquemas.Parse("")));
        }

        [Fact]
        public void Solo_se_proponen_esquemas_seguros_y_clase_desconocida_queda_prohibida()
        {
            var cat = CatalogoEsquemas.Parse(CAT);
            Assert.Single(cat.Avisos);
            Assert.Equal(ClaseSeguridad.Prohibido, cat.Tipos.First(t => t.Nombre == "Raro").Clase);
            var m = new ModeloAfectivo(id => 1.0); m.Evento("a", "b", TipoEvento.Traicion, 1);
            for (int i = 0; i < 8; i++) m.Avanza(1);
            var p = new MotorEsquemas().Propone(m, cat);
            Assert.Single(p); Assert.Equal("Insultar", p[0].Tipo); Assert.Equal("a", p[0].Ejecutor); Assert.Equal("b", p[0].Objetivo);
        }

        [Fact]
        public void Rencor_reciente_no_dispara_hasta_ser_sostenido()
        {
            var m = new ModeloAfectivo(id => 1.0); m.Evento("a", "b", TipoEvento.Traicion, 1);
            m.Avanza(1);
            Assert.Empty(new MotorEsquemas().Propone(m, CatalogoEsquemas.Parse(CAT)));
        }

        [Fact]
        public void Romance_mutuo_propone_esquema_amistoso()
        {
            var m = new ModeloAfectivo(null);
            for (int i = 0; i < 4; i++) { m.Evento("a", "b", TipoEvento.Cortejo, 1); m.Evento("a", "b", TipoEvento.Aprecio, 1); }
            var p = new MotorEsquemas().Propone(m, CatalogoEsquemas.Parse(CAT));
            Assert.Contains(p, x => x.Tipo == "Cortejar");
        }

        [Theory]
        [InlineData("AssassinationScheme", "prohibido")]
        [InlineData("CourtshipScheme", "seguro")]
        [InlineData("SpreadRumorScheme", "riesgo")]
        [InlineData("Whatever", "prohibido")]
        public void Sugerencia_conservadora(string n, string clase)
        {
            Assert.Contains("|" + clase + "|", CatalogoEsquemas.Sugiere(n));
        }
    }

    public class SociedadTests
    {
        [Fact]
        public void Detecta_dos_facciones_y_un_lider_por_carisma()
        {
            var m = new ModeloAfectivo(null);
            string[] g1 = { "a1", "a2", "a3", "a4" }, g2 = { "b1", "b2", "b3", "b4" };
            foreach (var g in new[] { g1, g2 }) foreach (var x in g) foreach (var y in g) if (x != y) { m.Evento(x, y, TipoEvento.Aprecio, 0.9); m.Evento(x, y, TipoEvento.Ayuda, 0.6); }
            foreach (var x in g1) foreach (var y in g2) m.Evento(x, y, TipoEvento.Agravio, 0.7);
            foreach (var y in g1) foreach (var x in g2) m.Evento(x, y, TipoEvento.Agravio, 0.7);
            var ids = g1.Concat(g2).ToList();
            var fs = Sociedad.Facciones(m, ids, 0.15, 2);
            Assert.Equal(2, fs.Count);
            Assert.All(fs, f => Assert.True(f.Cohesion > 0.2));
            Func<string, Ficha> fichas = id => new Ficha { Id = id, Carisma = id == "b3" ? 0.95 : 0.3 };
            Sociedad.AsignaLideres(m, fs, fichas, id => id);
            Assert.Equal("b3", fs.First(f => f.Miembros.Contains("b3")).Lider);
            Assert.StartsWith("Casa de", fs[0].Nombre);
        }

        [Fact]
        public void Facciones_son_deterministas()
        {
            Func<string> corre = () =>
            {
                var m = new ModeloAfectivo(null); var r = new Rng(5); var ids = Enumerable.Range(0, 12).Select(i => "p" + i).ToList();
                for (int i = 0; i < 200; i++) m.Evento(ids[r.Next(12)], ids[r.Next(12)], r.Chance(0.6) ? TipoEvento.Aprecio : TipoEvento.Agravio, r.Next());
                return string.Join("|", Sociedad.Facciones(m, ids, 0.1, 2).Select(f => string.Join(",", f.Miembros.ToArray())).ToArray());
            };
            Assert.Equal(corre(), corre());
        }

        [Fact]
        public void Favores_y_parejas_y_soledad()
        {
            var m = new ModeloAfectivo(null);
            m.Evento("a", "b", TipoEvento.Ayuda, 1);      // a le debe a b
            var saldo = Sociedad.SaldoDeFavores(m, new[] { "a", "b" });
            Assert.Equal("b", saldo[0].Key); Assert.True(saldo[0].Value > 0);
            for (int i = 0; i < 4; i++) { m.Evento("c", "d", TipoEvento.Cortejo, 1); m.Evento("d", "c", TipoEvento.Cortejo, 1); m.Evento("c", "d", TipoEvento.Aprecio, 1); m.Evento("d", "c", TipoEvento.Aprecio, 1); }
            Assert.Single(Sociedad.ParejasCandidatas(m, 0.5, 0.4));
            Assert.True(Sociedad.Soledad(m, "z", new[] { "z", "c", "d" }) > Sociedad.Soledad(m, "c", new[] { "z", "c", "d" }));
        }

        [Fact]
        public void Tradicion_emerge_tras_tres_anios_en_la_misma_temporada()
        {
            var c = new Costumbres();
            Assert.False(c.Observa("fiesta de la cosecha", 1, 2));
            Assert.False(c.Observa("fiesta de la cosecha", 1, 2));     // mismo anio no suma
            Assert.False(c.Observa("fiesta de la cosecha", 2, 2));
            Assert.True(c.Observa("fiesta de la cosecha", 3, 2));
            Assert.False(c.Observa("fiesta de la cosecha", 4, 2));     // ya es tradicion
            Assert.False(c.Observa("fiesta de la cosecha", 5, 0));     // otra temporada: no
            Assert.Single(c.Tradiciones);
        }

        [Fact]
        public void Deriva_esta_acotada()
        {
            double t = 0.5;
            for (int i = 0; i < 10000; i++) t = Deriva.Aplica(t, 0.5, 1, 0);
            Assert.Equal(0.7, t, 6);
            for (int i = 0; i < 10000; i++) t = Deriva.Aplica(t, 0.5, 0, 1);
            Assert.Equal(0.3, t, 6);
        }
    }

    public class GobiernoTests
    {
        [Fact]
        public void Metas_priorizan_el_mayor_deficit_y_dejan_suelo()
        {
            var m = new MetricasReino { Poblacion = 0.9, Seguridad = 0.9, Conocimiento = 0.1, Riqueza = 0.9, Cohesion = 0.9, Estabilidad = 0.9 };
            var metas = new MetasReino(); metas.Ajusta(m);
            Assert.True(metas.Peso("cultura") > metas.Peso("economia"));
            Assert.All(MetasReino.Categorias, c => Assert.True(metas.Peso(c) >= 0.15));
        }

        [Fact]
        public void Peticion_de_un_amigo_importante_se_aprueba_y_la_de_un_enemigo_no()
        {
            var m = new ModeloAfectivo(null);
            m.Evento("rey", "amigo", TipoEvento.Ayuda, 1); m.Evento("rey", "amigo", TipoEvento.Aprecio, 1);
            m.Evento("rey", "enemigo", TipoEvento.Traicion, 1);
            var c = new Consejo(m); var metas = new MetasReino();
            var pa = new Peticion { Id = "1", Tipo = "obra", Solicitante = "amigo", Importancia = 0.7, Categoria = "economia" };
            var pe = new Peticion { Id = "2", Tipo = "obra", Solicitante = "enemigo", Importancia = 0.7, Categoria = "economia" };
            Assert.True(c.Evalua(pa, "rey", metas).Aprueba);
            var v = c.Evalua(pe, "rey", metas);
            Assert.False(v.Aprueba); Assert.Contains("rencor", v.Razon);
        }

        [Fact]
        public void Eleccion_de_investigacion_es_determinista_y_respeta_disponibilidad()
        {
            var metas = new MetasReino(); metas.Pesos["defensa"] = 0.9; metas.Pesos["cultura"] = 0.2;
            var ops = new List<OpcionElegible> {
                new OpcionElegible { Id = "b", Nombre = "Murallas", Categoria = "defensa", Coste = 3 },
                new OpcionElegible { Id = "a", Nombre = "Escritura", Categoria = "cultura", Coste = 1 },
                new OpcionElegible { Id = "c", Nombre = "Torres", Categoria = "defensa", Coste = 1, Disponible = false } };
            var c = new Consejo(new ModeloAfectivo(null));
            Assert.Equal("b", c.Elige(ops, metas).Id);       // 0.9/3=0.3 > 0.2/1
            Assert.Null(c.Elige(new List<OpcionElegible>(), metas));
        }
    }

    public class DireccionTests
    {
        sealed class Host : IConsolaHost
        {
            public Dictionary<string, string> D = new Dictionary<string, string>();
            public string Modo { get; set; }
            public int Vetada = -1;
            public string Estado() { return "ok"; }
            public bool Veta(int id) { Vetada = id; return id == 3; }
            public string Pendientes() { return "ninguna"; }
            public void PonDirectriz(string a, string k, string t) { D[a + ":" + k] = t; }
            public string QuitaDirectriz(string a, string k) { string p; if (D.TryGetValue(a + ":" + k, out p)) { D.Remove(a + ":" + k); return p; } return null; }
        }

        [Fact]
        public void Directriz_antigua_es_global_y_las_secciones_se_separan()
        {
            var d = Directrices.Parse("# c\nQue haya paz.\n[faccion:Casa de Ana]\nQue conquiste.\n[pawn:Beto]\nQue sea humilde.\nY callado.\n");
            Assert.Equal("Que haya paz.", d.Global);
            Assert.Equal("Que haya paz. Que conquiste. Que sea humilde. Y callado.", d.Para("Beto", "Casa de Ana"));
            Assert.Equal("Que haya paz.", d.Para("Otro", null));
            Assert.Equal("Que haya paz. Que sea humilde. Y callado.", d.Para("beto", null));
        }

        [Fact]
        public void Directriz_se_acota_a_400()
        {
            Assert.True(Directrices.Parse(new string('x', 1000)).Global.Length <= 400);
        }

        [Fact]
        public void Consola_comandos_y_deshacer()
        {
            var h = new Host(); var c = new Consola(h);
            Assert.Equal("ok", c.Ejecuta("estado"));
            Assert.Contains("fijada", c.Ejecuta("dir pawn Ana: sé amable"));
            Assert.Equal("sé amable", h.D["pawn:Ana"]);
            Assert.Contains("fijada", c.Ejecuta("dir pawn Ana: sé firme"));
            Assert.Equal("deshecho", c.Ejecuta("deshacer"));
            Assert.Equal("sé amable", h.D["pawn:Ana"]);
            Assert.Equal("deshecho", c.Ejecuta("deshacer"));
            Assert.False(h.D.ContainsKey("pawn:Ana"));
            c.Ejecuta("dir global paz"); c.Ejecuta("quita global");
            Assert.False(h.D.ContainsKey("global:"));
            c.Ejecuta("deshacer"); Assert.Equal("paz", h.D["global:"]);
            Assert.Contains("vetada", c.Ejecuta("veta 3"));
            Assert.Contains("no hay", c.Ejecuta("veta 9"));
            Assert.Contains("uso", c.Ejecuta("veta x"));
            Assert.Contains("observador", c.Ejecuta("modo observador")); Assert.Equal("observador", h.Modo);
            Assert.Contains("uso", c.Ejecuta("modo caos"));
            Assert.Contains("desconocido", c.Ejecuta("bailar"));
        }
    }

    public class CronicaInformeTests
    {
        [Fact]
        public void Cronica_resumen_leyendas_y_poda_por_importancia()
        {
            var c = new Cronica { MaxHitos = 20 };
            for (int i = 0; i < 100; i++) c.Anota(i, "x", "suceso " + i, i == 50 ? 99 : 1);
            Assert.True(c.Count <= 20);
            Assert.Equal("suceso 50", c.Leyendas(1)[0].Texto);
            Assert.Contains("Hubo", c.ResumenTemporada(0));
            Assert.Contains("Cronica de Reinoland", c.ToMarkdown("Reinoland"));
            Assert.Equal("Una temporada sin sucesos que contar.", new Cronica().ResumenTemporada(3));
            Assert.Equal("primavera del anio 1", c.NombreTemporada(0));
            Assert.Equal("primavera del anio 2", c.NombreTemporada(120));
        }

        [Fact]
        public void Informe_resume_opiniones_y_tolera_lineas_malas()
        {
            var l = new[] {
                "{\"t\":\"2026-10-07T11:48:07\",\"a\":\"Ana\",\"b\":\"Beto\",\"delta\":-1.5,\"valor\":-2,\"motivo_juego\":\"x\",\"dice\":\"Cruel\",\"piensa\":\"\",\"actitud\":\"empeora\",\"ajuste\":-0.25}",
                "{\"t\":\"2026-10-07T11:49:00\",\"a\":\"Ana\",\"b\":\"Beto\",\"delta\":-1,\"dice\":\"(sin frase)\"}",
                "{\"t\":\"2026-10-07T11:50:00\",\"a\":\"Cora\",\"b\":\"Beto\",\"delta\":0.6,\"dice\":\"\"}",
                "{cortada" };
            var r = Informe.Resume(l);
            Assert.Equal(3, r.Eventos); Assert.Equal(1, r.ConVoz); Assert.Equal(1, r.SinFrase); Assert.Equal(1, r.Empujones);
            Assert.Equal(1, r.LineasInvalidas); Assert.Equal(0.25, r.MaxEmpujon, 6);
            Assert.Equal("2026-10-07T11:48:07", r.Desde);
            string md = Informe.Markdown("Sesion", r, null, null, null, new[] { "aviso1" });
            Assert.Contains("Ana -> Beto: 2", md); Assert.Contains("aviso1", md);
        }

        [Fact]
        public void Datos_de_muestra_reales_antiguos_se_leen()
        {
            // Formato antiguo (antes/despues, sin ajuste): debe resumirse igual.
            var r = Informe.Resume(new[] { "{\"t\":\"2026-10-07T11:48:07\",\"a\":\"Jinne\",\"b\":\"Despota\",\"delta\":-1.615,\"antes\":-0.9,\"despues\":-2.5,\"motivo_juego\":\"x\",\"dice\":\"Cruel traidor\",\"piensa\":\"\"}" });
            Assert.Equal(1, r.Eventos); Assert.Equal(1, r.ConVoz);
        }

        [Fact]
        public void AB_detecta_una_caida_real_y_no_inventa_una_inexistente()
        {
            var r = new Rng(9);
            Func<double, double[]> serie = m => Enumerable.Range(0, 200).Select(i => m + r.Range(-4, 4)).ToArray();
            var igual = Informe.CompararAB(serie(60), serie(60));
            Assert.Equal("sin caida medible", igual.Veredicto);
            var caida = Informe.CompararAB(serie(50), serie(60));
            Assert.Equal("caida medible", caida.Veredicto); Assert.True(caida.DeltaPct < -10);
            Assert.StartsWith("insuficiente", Informe.CompararAB(serie(60).Take(10).ToArray(), serie(60)).Veredicto);
        }

        [Fact]
        public void Fps_percentiles_y_lectura_de_csv()
        {
            var v = Informe.LeeFps(new[] { "hora,fps_medio,fps_instantanea,llamadas_opinion", "10:00:00,60.5,61,5", "10:00:05,50,40,9", "x" });
            Assert.Equal(new double[] { 61, 40 }, v);
            var e = Informe.Fps(Enumerable.Range(1, 100).Select(i => (double)i).ToArray());
            Assert.Equal(50.5, e.Media, 6); Assert.Equal(5, e.P5); Assert.Equal(1, e.P1);
        }

        [Fact]
        public void Grafo_dot_y_json()
        {
            var m = new ModeloAfectivo(null);
            m.Evento("a", "b", TipoEvento.Aprecio, 1); m.Evento("b", "a", TipoEvento.Agravio, 1);
            var ids = new[] { "a", "b" };
            string dot = Informe.GrafoDot(m, ids, id => id.ToUpper(), 0.1);
            Assert.Contains("darkgreen", dot); Assert.Contains("red", dot);
            object o; Assert.True(Json.TryParse(Informe.GrafoJson(m, ids, id => id, 0.1), out o));
            Assert.Equal(2, Json.Lista(o, "aristas").Count);
        }
    }
}
