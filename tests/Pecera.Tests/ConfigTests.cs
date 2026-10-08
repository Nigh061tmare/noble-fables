using Pecera.Core;
using Xunit;

namespace Pecera.Tests
{
    public class ConfigTests
    {
        [Fact]
        public void Defectos_cuando_esta_vacio()
        {
            var c = PeceraConfig.Parse("");
            Assert.Equal(25, c.Int("gap_voz"));
            Assert.False(c.Bool("influencia"));
            Assert.Equal("qwen4b-silly:latest", c.Str("modelo"));
        }

        [Fact]
        public void Toda_escritura_no_verificada_viene_apagada_por_defecto()
        {
            // Regla de seguridad 3: lo que escribe en el juego y no esta VERIFICADO va a 0.
            foreach (var e in PeceraConfig.Schema)
                if (e.EscribeJuego && e.Estado != Estado.Verificado && e.Numeric && e.Key.IndexOf("max") < 0
                    && e.Key.IndexOf("_s") < 0)
                    Assert.True(e.Default == "0", e.Key + " deberia venir apagado");
        }

        [Fact]
        public void Valores_invalidos_o_fuera_de_rango_usan_defecto_y_avisan()
        {
            var c = PeceraConfig.Parse("gap_voz=2\numbral_habla=abc\nclave_rara=1\nsinigual\n");
            Assert.Equal(25, c.Int("gap_voz"));
            Assert.Equal(1.0, c.Num("umbral_habla"));
            Assert.Equal(4, c.Avisos.Count);
        }

        [Fact]
        public void Parsea_comentarios_crlf_y_decimales_invariantes()
        {
            var c = PeceraConfig.Parse("# hola\r\nunbral=1\r\ninfluencia=1\r\ninfluencia_max=0.1\r\n");
            Assert.True(c.Bool("influencia"));
            Assert.Equal(0.1, c.Num("influencia_max"));
        }

        [Fact]
        public void Modo_observador_anula_cualquier_escritura()
        {
            var c = PeceraConfig.Parse("influencia=1\nmodo=observador\n");
            Assert.False(c.PuedeEscribir("influencia"));
            c = PeceraConfig.Parse("influencia=1\n");
            Assert.True(c.PuedeEscribir("influencia"));
            Assert.False(c.PuedeEscribir("esquemas"));
        }

        [Fact]
        public void PuedeEscribir_rechaza_claves_que_no_son_interruptores()
        {
            var c = PeceraConfig.Parse("");
            Assert.Throws<System.ArgumentException>(() => c.PuedeEscribir("gap_voz"));
        }

        [Fact]
        public void RenderDefault_se_vuelve_a_parsear_sin_avisos_ni_cambios()
        {
            var c = PeceraConfig.Parse(PeceraConfig.RenderDefault());
            Assert.Empty(c.Avisos);
            foreach (var e in PeceraConfig.Schema) Assert.Equal(e.Default, c.Str(e.Key));
        }
    }
}

namespace Pecera.Tests
{
    public class ConfigMigracionTests
    {
        [Fact]
        public void Config_v1_conserva_lo_seguro_y_apaga_las_escrituras_sin_verificar()
        {
            // El config.txt real de la version anterior (datos-muestra/config.txt).
            string v1 = "modelo=qwen9b-silly:latest\numbral_habla=1.5\ngap_voz=40\nfps_log=1\ninfluencia=1\ninfluencia_max=0.25\nburbujas=0\n";
            var cambios = new System.Collections.Generic.List<string>();
            var nuevo = PeceraConfig.Parse(PeceraConfig.Migra(v1, cambios));
            Assert.Equal("qwen9b-silly:latest", nuevo.Str("modelo"));
            Assert.Equal(1.5, nuevo.Num("umbral_habla")); Assert.Equal(40, nuevo.Int("gap_voz"));
            Assert.True(nuevo.Bool("fps_log")); Assert.False(nuevo.Bool("burbujas"));
            Assert.False(nuevo.Bool("influencia"));
            Assert.Equal(2, nuevo.Int("config_version"));
            Assert.Single(cambios); Assert.Contains("influencia", cambios[0]);
            Assert.Empty(nuevo.Avisos);
        }

        [Fact]
        public void VersionDe_distingue_un_config_antiguo_de_uno_migrado()
        {
            // BUG real (hallado en el PC del usuario): Parse(texto).Int("config_version") devolvia el DEFECTO (2)
            // cuando la clave faltaba, y un config v1 con influencia=1 nunca se migraba.
            Assert.Equal(1, PeceraConfig.VersionDe("modelo=x\ninfluencia=1\n"));
            Assert.Equal(1, PeceraConfig.VersionDe(null));
            Assert.Equal(1, PeceraConfig.VersionDe("config_version=abc\n"));
            Assert.Equal(2, PeceraConfig.VersionDe("# c\nconfig_version=2\n"));
            Assert.Equal(2, PeceraConfig.Parse("modelo=x").Int("config_version"));   // el trampa: el defecto engana
            Assert.True(PeceraConfig.VersionDe(PeceraConfig.RenderDefault()) >= PeceraConfig.VersionActual);
        }

        [Fact]
        public void Peticiones_solo_observa_hasta_que_se_pide_aplicar_explicitamente()
        {
            var c = PeceraConfig.Parse("peticiones=1\n");
            Assert.False(c.PuedeEscribir("peticiones_aplicar"));
            Assert.True(PeceraConfig.Parse("peticiones_aplicar=1\n").PuedeEscribir("peticiones_aplicar"));
            Assert.False(PeceraConfig.Parse("peticiones_aplicar=1\nmodo=observador\n").PuedeEscribir("peticiones_aplicar"));
        }

        [Fact]
        public void Una_escritura_ya_apagada_o_con_su_valor_por_defecto_no_se_reporta()
        {
            var cambios = new System.Collections.Generic.List<string>();
            PeceraConfig.Migra("influencia=0\n", cambios);
            Assert.Empty(cambios);
        }
    }
}
