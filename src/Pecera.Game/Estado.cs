using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Pecera.Core;

namespace PeceraNF
{
    // Contenedor unico del estado del mod. HILOS:
    //   - Memoria, AlmacenFichas, RegistroIds, Evidence, Compuerta, CupoVoz, SaludLlm,
    //     PoliticaInfluencia: internamente sincronizados (cualquier hebra).
    //   - Afectos, Secretos, Cronica, Costumbres: SOLO el hilo principal (hook y Update).
    public static class Estado
    {
        public const string VERSION = "0.2.0";

        public static string Datos = "";
        public static PeceraConfig Cfg = PeceraConfig.Parse("");
        public static IClock Reloj = new SystemClock();
        public static IStorage Disco;
        public static Evidence Ev;
        public static RegistroIds Ids;
        public static AlmacenFichas Fichas;
        public static Memoria Mem;
        public static ModeloAfectivo Afectos;
        public static RedSecretos Secretos;
        public static Compuerta Compuerta;
        public static SaludLlm Salud;
        public static CupoVoz Voz;
        public static CupoVoz Resumen;
        public static PoliticaInfluencia Influencia;
        public static Cronica Cronica;
        public static Costumbres Costumbres;
        public static CatalogoEsquemas Catalogo = new CatalogoEsquemas();
        public static Directrices Dir = new Directrices();
        public static volatile bool Activo = true;
        public static volatile bool EscrituraSegura = true;   // false si cambio la version del juego
        public static string VersionJuego = "";
        public static readonly List<string> Avisos = new List<string>();
        public static long ArranqueTicks;

        public static void Inicia(string datos)
        {
            Datos = datos;
            Directory.CreateDirectory(datos);
            ArranqueTicks = Reloj.NowTicks;
            Ev = new Evidence(Reloj);
            Criterios();

            string ruta = Path.Combine(datos, "config.txt");
            string texto = File.Exists(ruta) ? File.ReadAllText(ruta, new UTF8Encoding(false)) : null;
            if (texto == null)
            {
                Escribe(ruta, PeceraConfig.RenderDefault());
            }
            else if (PeceraConfig.Parse(texto).Int("config_version") < PeceraConfig.VersionActual)
            {
                var cambios = new List<string>();
                File.Copy(ruta, Path.Combine(datos, "config.v1.bak.txt"), true);
                Escribe(ruta, PeceraConfig.Migra(texto, cambios));
                Avisos.Add("config.txt migrado a la version 2 (copia en config.v1.bak.txt)");
                foreach (var c in cambios) Avisos.Add("config: " + c);
                texto = File.ReadAllText(ruta, new UTF8Encoding(false));
            }
            Cfg = PeceraConfig.Parse(File.ReadAllText(ruta, new UTF8Encoding(false)));
            foreach (var a in Cfg.Avisos) Avisos.Add("config: " + a);
            Escribe(Path.Combine(datos, "config.defecto.txt"), PeceraConfig.RenderDefault());

            string dd = Cfg.Str("datos_dir");
            if (dd.Length > 0) { Datos = dd; Directory.CreateDirectory(dd); }
            Disco = new DiskStorage(Datos, 5 * 1024 * 1024);
            Activo = Cfg.Bool("activo");

            Ids = new RegistroIds(Disco);
            Fichas = new AlmacenFichas(Disco);
            Mem = new Memoria(Disco, Reloj) { MaxRecientes = Cfg.Int("memoria_recientes") };
            if (Mem.LineasCorruptas > 0) Avisos.Add("memoria.jsonl: " + Mem.LineasCorruptas + " lineas ilegibles ignoradas");
            Afectos = new ModeloAfectivo(id => { var f = Fichas.Get(id); return f != null ? f.Rencor : 0.5; });
            Afectos.Carga(Disco.ReadLines("afectos.jsonl"));
            Secretos = new RedSecretos(Afectos, id => Fichas.Get(id), Reloj, new Rng(Environment.TickCount));
            Compuerta = new Compuerta(Reloj)
            {
                VentanaVetoSegundos = Cfg.Int("peticiones_veto_s"),
                DiaTicks = (long)(Cfg.Num("dia_segundos") * TimeSpan.TicksPerSecond)
            };
            Compuerta.FijaTopeDia("esquema", Cfg.Int("esquemas_dia_max"));
            Compuerta.FijaTopeDia("peticion", Cfg.Int("peticiones_dia_max"));
            Salud = new SaludLlm(Reloj);
            Voz = new CupoVoz(Reloj, Cfg.Int("gap_voz"));
            Resumen = new CupoVoz(Reloj, Cfg.Int("resumen_gap_s"));
            Influencia = new PoliticaInfluencia(Reloj)
            {
                Max = Cfg.Num("influencia_max"),
                ParejaSegundos = Cfg.Int("influencia_pareja_s"),
                HoraMax = Cfg.Int("influencia_hora_max")
            };
            Cronica = new Cronica();
            Costumbres = new Costumbres();

            string esq = Path.Combine(Datos, "esquemas.txt");
            if (File.Exists(esq)) Catalogo = CatalogoEsquemas.Parse(File.ReadAllText(esq, new UTF8Encoding(false)));
            foreach (var a in Catalogo.Avisos) Avisos.Add("esquemas.txt: " + a);
            CargaDirectriz();
        }

        static void Criterios()
        {
            // minimo de exitos y maximo de fallos para considerar cada funcion verificada EN PARTIDA.
            Ev.Criterio("hook_opinion", 50, 0);
            Ev.Criterio("voz_frase", 5, 2);
            Ev.Criterio("voz_json_valido", 5, 3);
            Ev.Criterio("bocadillo_dibujado", 5, 0);
            Ev.Criterio("empujon_opinion", 3, 0);
            Ev.Criterio("reentrada_bloqueada", 1, 0);   // informativa: sin_datos = el juego no reentra
            Ev.Criterio("memoria_persistida", 1, 0);
            Ev.Criterio("memoria_resumen_llm", 1, 1);
            Ev.Criterio("id_clave_estable", 1, 0);
            Ev.Criterio("afectos_modelo", 20, 0);
            Ev.Criterio("rumor_fuga", 1, 0);
            Ev.Criterio("sonda_pawn", 1, 0);
            Ev.Criterio("informe_escrito", 1, 0);
        }

        public static void Escribe(string ruta, string texto)
        {
            File.WriteAllText(ruta, texto, new UTF8Encoding(false));
        }

        static string _dirTexto = "";
        static DateTime _dirFecha = DateTime.MinValue;

        // directriz.txt se relee solo cuando cambia (por fecha de modificacion).
        public static void CargaDirectriz()
        {
            try
            {
                string r = Path.Combine(Datos, "directriz.txt");
                if (!File.Exists(r))
                {
                    Escribe(r, "# Escribe aqui lo que quieres que pase en el reino. Se lee solo, sin reiniciar.\r\n" +
                               "# Admite secciones: [faccion:Casa de Ana] y [pawn:Ana]. Las lineas con # se ignoran.\r\n");
                    return;
                }
                DateTime t = File.GetLastWriteTimeUtc(r);
                if (t == _dirFecha) return;
                _dirFecha = t;
                _dirTexto = File.ReadAllText(r, new UTF8Encoding(false));
                Dir = Directrices.Parse(_dirTexto);
            }
            catch (IOException e) { Avisos.Add("directriz.txt ilegible: " + e.Message); }
        }
    }
}
