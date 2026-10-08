using System.Collections.Generic;
using Pecera.Core;
using Xunit;

namespace Pecera.Tests
{
    public class JsonTests
    {
        [Fact]
        public void Escapes_n_y_unicode_se_decodifican()
        {
            var v = Json.Parse("{\"a\":\"l1\\nl2\\u00e9\\t\\\"q\\\"\"}");
            Assert.Equal("l1\nl2é\t\"q\"", Json.Str(v, "a"));
        }

        [Fact]
        public void UnaLinea_reduce_saltos_y_tabs()
        {
            Assert.Equal("a b c", Json.UnaLinea(" a\n\tb  c \r\n"));
        }

        [Fact]
        public void Escape_ida_y_vuelta()
        {
            string raro = "comilla\" barra\\ nl\n tab\t ctl\u0001 ñ 😀";
            var v = Json.Parse("{\"x\":\"" + Json.Escape(raro) + "\"}");
            Assert.Equal(raro, Json.Str(v, "x"));
        }

        [Theory]
        [InlineData("{\"a\":1,}")]
        [InlineData("{\"a\":}")]
        [InlineData("{\"a\":\"x")]
        [InlineData("[1,2")]
        [InlineData("{a:1}")]
        [InlineData("")]
        public void Estricto_rechaza_invalidos(string s)
        {
            object v;
            Assert.False(Json.TryParse(s, out v));
        }

        [Fact]
        public void ParseObjeto_quita_valla_markdown_y_prosa()
        {
            var d = Json.ParseObjeto("Claro:\n```json\n{\"texto\":\"hola\",\"actitud\":\"perdona\"}\n```\nespero que sirva");
            Assert.NotNull(d);
            Assert.Equal("hola", Json.Str(d, "texto"));
        }

        [Theory]
        [InlineData("{\"texto\":\"ho")]                                  // cortado dentro de la cadena
        [InlineData("{\"texto\":\"hola\",\"piensa\":\"mmm\",\"act")]       // clave a medias
        [InlineData("{\"texto\":\"hola\",\"piensa\":")]                   // valor ausente
        [InlineData("{\"texto\":\"hola\",")]                              // coma colgando
        [InlineData("{\"texto\":\"hola\\")]                                // escape a medias
        [InlineData("{\"texto\":\"hola \\u00")]                           // \\u a medias
        public void JsonTruncado_se_repara(string s)
        {
            var d = Json.ParseObjeto(s);
            Assert.NotNull(d);
            Assert.StartsWith("ho", Json.Str(d, "texto"));
        }

        [Fact]
        public void Sin_json_devuelve_null()
        {
            Assert.Null(Json.ParseObjeto("no hay nada aqui"));
            Assert.Null(Json.ParseObjeto(null));
        }

        [Fact]
        public void Llaves_dentro_de_cadenas_no_rompen_el_aislado()
        {
            var d = Json.ParseObjeto("x {\"texto\":\"} { trampa\"} y {\"otro\":1}");
            Assert.Equal("} { trampa", Json.Str(d, "texto"));
        }

        [Fact]
        public void Numeros_y_listas()
        {
            var v = Json.Parse("{\"n\":-1.5e1,\"l\":[1,\"a\",null,true]}");
            Assert.Equal(-15.0, Json.Num(v, "n", 0));
            Assert.Equal(4, Json.Lista(v, "l").Count);
        }

        [Fact]
        public void Num_escribe_invariante_y_null_para_NaN()
        {
            Assert.Equal("0.125", Json.Num(0.12499));
            Assert.Equal("null", Json.Num(double.NaN));
        }
    }

    public class VozTests
    {
        [Fact]
        public void Contenido_nativo_y_v1()
        {
            Assert.Equal("hola", OllamaWire.Contenido("{\"message\":{\"role\":\"assistant\",\"content\":\"hola\"}}"));
            Assert.Equal("hola", OllamaWire.Contenido("{\"choices\":[{\"message\":{\"content\":\"hola\"}}]}"));
            Assert.Null(OllamaWire.Contenido("{\"message\":{\"content\":\"\"}}"));
            Assert.Null(OllamaWire.Contenido("basura"));
        }

        [Fact]
        public void Body_nativo_es_json_valido_y_lleva_keep_alive_y_ctx()
        {
            string b = OllamaWire.BodyNativo("m:1", "15m", 2048, 220, "sis \"x\"\n", "usu\\");
            var d = Json.Parse(b);
            Assert.Equal("15m", Json.Str(d, "keep_alive"));
            Assert.Equal(2048, Json.Num(((Dictionary<string, object>)d)["options"], "num_ctx", 0));
            Assert.DoesNotContain("reasoning_effort", b);
        }

        [Fact]
        public void Body_proxy_lleva_reasoning_effort_none()
        {
            var d = Json.Parse(OllamaWire.BodyProxy("m", "5m", 100, "s", "u"));
            Assert.Equal("none", Json.Str(d, "reasoning_effort"));
        }

        [Fact]
        public void Replica_json_limpio()
        {
            var r = ReplicaParser.Parse("{\"texto\":\"Vos me ofendeis\",\"piensa\":\"cuidado\",\"actitud\":\"Empeora\"}");
            Assert.True(r.Ok); Assert.Equal("json", r.Metodo); Assert.Equal("empeora", r.Actitud);
        }

        [Fact]
        public void Replica_truncada_se_repara()
        {
            var r = ReplicaParser.Parse("{\"texto\":\"Vos me ofendeis\",\"piensa\":\"cuid");
            Assert.True(r.Ok); Assert.Equal("reparado", r.Metodo); Assert.Equal("mantiene", r.Actitud);
        }

        [Fact]
        public void Replica_con_comillas_internas_sin_escapar_cae_a_suelto()
        {
            var r = ReplicaParser.Parse("{\"texto\":\"Dijo \"basta\" y se fue\",\"piensa\":\"hm\",\"actitud\":\"perdona\"}");
            Assert.True(r.Ok); Assert.Equal("suelto", r.Metodo);
            Assert.Equal("Dijo \"basta\" y se fue", r.Texto);
            Assert.Equal("perdona", r.Actitud);
        }

        [Fact]
        public void Replica_vacia_o_prosa()
        {
            Assert.False(ReplicaParser.Parse("").Ok);
            Assert.False(ReplicaParser.Parse("Lo siento, no puedo.").Ok);
        }

        [Theory]
        [InlineData("Ha matado a su hermano", false)]
        [InlineData("Chico es Orco", true)]
        [InlineData("Chiyo está Malo", true)]
        [InlineData("Brigitte ha sido traicionada", false)]
        [InlineData("", true)]
        public void EsRasgo(string s, bool esperado) { Assert.Equal(esperado, Habla.EsRasgo(s)); }

        [Fact]
        public void Cupo_voz_no_permite_dos_en_vuelo_ni_antes_del_gap()
        {
            var c = new ManualClock { Ticks = 1000 };
            var cupo = new CupoVoz(c, 25);
            Assert.True(cupo.TryAcquire());
            Assert.False(cupo.TryAcquire());      // en vuelo
            cupo.Release();
            Assert.False(cupo.TryAcquire());      // gap
            c.AdvanceSeconds(24.9);
            Assert.False(cupo.TryAcquire());
            c.AdvanceSeconds(0.2);
            Assert.True(cupo.TryAcquire());
        }

        [Fact]
        public void Influencia_respeta_tope_pareja_y_hora()
        {
            var c = new ManualClock { Ticks = 1 };
            var p = new PoliticaInfluencia(c) { Max = 0.25, ParejaSegundos = 300, HoraMax = 3 };
            Assert.Equal(0, p.Decide("a|b", -2, "mantiene"));
            Assert.Equal(-0.25, p.Decide("a|b", -5, "empeora"), 6);        // tope
            Assert.Equal(0, p.Decide("a|b", -5, "empeora"));               // enfriando
            c.AdvanceSeconds(301);
            Assert.Equal(0.1, p.Decide("a|b", -0.4, "perdona"), 6);        // 0.4*0.25, signo invertido
            Assert.Equal(0, p.Decide("g|h", 0.8, "perdona"));              // nada que perdonar en un delta positivo
            Assert.NotEqual(0, p.Decide("c|d", 1, "empeora"));
            Assert.Equal(0, p.Decide("e|f", 1, "empeora"));                // 3 por hora
            c.AdvanceSeconds(3700);
            Assert.NotEqual(0, p.Decide("e|f", 1, "empeora"));
        }

        [Fact]
        public void Prompt_voz_incluye_ficha_memoria_y_directriz()
        {
            string u = Prompts.UsuarioVoz("Ana", "Beto", -1.5, "Beto ha robado", -2.2, "orgullosa", "le robaron", "paz");
            Assert.Contains("DESCONFIAR", u); Assert.Contains("orgullosa", u); Assert.Contains("le robaron", u); Assert.Contains("paz", u);
            Assert.Contains("-2.2", u);
        }
    }
}
