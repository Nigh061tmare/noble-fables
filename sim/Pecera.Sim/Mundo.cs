using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Pecera.Core;

namespace Pecera.Sim
{
    public sealed class SimConfig
    {
        public int Dias = 360, Pawns = 24, Seed = 1;
        public bool Gobierno = true, Esquemas = true, Rumores = true, Afectos = true;
        public EstiloDirector EstiloDirector = EstiloDirector.Clasico;
        public bool Agentes = true;             // agentes con proposito (requiere Ideas): persona, agenda, plan en lote, conversaciones, narrativa
        public int LlamadasLlmDia = 12, LoteAgentes = 6;    // 12/dia: una llamada cada ~10 s de reloj con dia_segundos=120 (la voz ya usa 1 cada 25 s)
        public bool Ideas = true;               // justicia, sucesion, mentoria, cultura, dialectos, estaciones, suenos, espionaje, deriva
        public double UmbralAprobacion = 0.45;
        public double ProbVeto = 0.10;          // el jugador veta el 10 % de lo que ve
        public double ProbLlmCae = 0.10;        // 10 % de las llamadas al LLM simulado fallan
        public int EsquemasDiaMax = 2;
        public int PeticionesDiaMax = 3;
    }

    public sealed class Semana
    {
        public int Dia;
        public MetricasReino M = new MetricasReino();
        public double RencorMedio;
        public int Rumores, Aprobadas, Denegadas, Esquemas;
    }

    public sealed partial class Resultado
    {
        public readonly List<Semana> Semanas = new List<Semana>();
        public int Aprobadas, Denegadas, Vetadas, EsquemasEjecutados, EsquemasPropuestos, Fugas, Investigadas, Obras;
        public int CambiosDeSigno, ParesObservados;
        public int MaxDiasSinProgreso;
        public int FallosLlm, ResumenesFallback, ResumenesLlm;
        public double RencorMax;
        public string CronicaMd = "", GrafoDot = "", Informe = "";
        public double DiasSinProgresoFinal;
        public int TradicionesEmergidas;
        public double AlcanceMedio, FidelidadMedia; public int SaltosMax, SecretosExpuestos;
        public string TopChismosos = "";
        public int Juicios, Condenas, Absoluciones, EspiasExito, EspiasDescubiertos, InspiracionesTotal, SuenosCumplidos, LazosMentoria, GirosDialecto;
        public bool HuboSucesion, SucesionDisputada; public string Sucesor = "", Credo = "";
        public double HabilidadMediaInicial, HabilidadMediaFinal, DerivaMax;
        public int HerenciaRepartida;
        public int FaccionesFinales;
        public double TasaCambioSigno { get { return ParesObservados == 0 ? 0 : (double)CambiosDeSigno / ParesObservados; } }
        public MetricasReino Final { get { return Semanas.Count > 0 ? Semanas[Semanas.Count - 1].M : new MetricasReino(); } }
    }

    // Mundo falso y determinista para ejercitar el Core sin el juego: dias enteros de
    // vida social, gobierno y crecimiento. Las constantes del mundo (produccion, efectos de
    // obras...) son SUPUESTOS del simulador, no medidas del juego real: sirven para
    // comprobar que la logica no se atasca, no oscila y no se desboca, y para afinar
    // umbrales relativos. No predicen valores absolutos de Noble Fates.
    public sealed partial class Mundo
    {
        readonly SimConfig cfg;
        readonly ManualClock reloj = new ManualClock { Ticks = TimeSpan.TicksPerDay * 365 * 10 };
        readonly Rng rng;
        readonly Rng rngLlm;
        readonly List<string> ids = new List<string>();
        readonly Dictionary<string, string> nombres = new Dictionary<string, string>();
        readonly MemoryStorage disco = new MemoryStorage();
        readonly AlmacenFichas fichas;
        readonly ModeloAfectivo afectos;
        readonly Memoria memoria;
        readonly RedSecretos secretos;
        readonly Compuerta compuerta;
        readonly Consejo consejo;
        readonly MotorEsquemas motor = new MotorEsquemas();
        readonly CatalogoEsquemas catalogo = CatalogoEsquemas.Parse(
    "Insultar|seguro|hostil\n" +
    "Difamar|seguro|hostil\n" +
    "SabotearFestin|seguro|hostil\n" +
    "Desairar|seguro|hostil\n" +
    "Cortejar|seguro|amistoso\n" +
    "Festejar|seguro|amistoso\n" +
    "Aliarse|seguro|amistoso\n" +
    "Felicitacion|seguro|amistoso\n" +
    "Tregua|seguro|amistoso\n" +
    "Asesinar|prohibido|hostil\n" +
    "Exiliar|prohibido|hostil\n" +
    "Guerra|prohibido|hostil\n");
        readonly MetasReino metas = new MetasReino();
        readonly Cronica cronica = new Cronica();
        readonly Costumbres costumbres = new Costumbres();
        readonly SaludLlm salud;
        readonly Resultado res = new Resultado();
        readonly Dictionary<string, int> signos = new Dictionary<string, int>();

        // Estado del reino (supuestos del simulador)
        double riqueza = 0.3, seguridad = 0.3, saber = 0.0, saberExtra, poblacion;
        double puntosInvestigacion;
        OpcionElegible enCurso;
        readonly List<OpcionElegible> arbol = new List<OpcionElegible>();
        readonly HashSet<string> hechas = new HashSet<string>();
        string soberano;
        readonly Tribunal tribunal;
        readonly Linaje linaje = new Linaje();
        readonly Mentoria mentoria = new Mentoria();
        readonly Cultura cultura = new Cultura();
        readonly Suenos suenos = new Suenos();
        readonly Dictionary<string, Dialecto> dialectos = new Dictionary<string, Dialecto>();
        readonly Dictionary<string, double> habilidad = new Dictionary<string, double>();
        readonly Dictionary<string, double> rencorBase = new Dictionary<string, double>();
        readonly HashSet<string> muertos = new HashSet<string>();
        int fugasJuzgadas;
        bool sucesionHecha;
        int diasSinProgreso;
        int dia;

        public Mundo(SimConfig cfg)
        {
            this.cfg = cfg;
            rng = new Rng(cfg.Seed); rngLlm = new Rng(cfg.Seed * 7 + 1);
            fichas = new AlmacenFichas(disco);
            for (int i = 0; i < cfg.Pawns; i++)
            {
                string id = "p" + i.ToString("00");
                ids.Add(id); nombres[id] = "Aldeano" + i;
                fichas.GetOCrea(id, nombres[id]);
            }
            afectos = new ModeloAfectivo(id => { var f = fichas.Get(id); return f != null ? f.Rencor : 0.5; });
            memoria = new Memoria(disco, reloj) { MaxRecientes = 6, MantenerCrudos = 2, MaxMedio = 3 };
            secretos = new RedSecretos(afectos, id => fichas.Get(id), reloj, new Rng(cfg.Seed + 99));
            compuerta = new Compuerta(reloj) { VentanaVetoSegundos = 30, EnfriarClaveSegundos = 3600 };
            compuerta.FijaEnfriamiento("esquema", 30 * 600);        // 30 dias de juego por pareja
            compuerta.FijaEnfriamiento("peticion", 600);
            compuerta.FijaTopeDia("esquema", cfg.EsquemasDiaMax);
            compuerta.FijaTopeDia("peticion", cfg.PeticionesDiaMax);
            consejo = new Consejo(afectos) { UmbralAprobacion = cfg.UmbralAprobacion };
            salud = new SaludLlm(reloj);
            poblacion = 0.4;
            tribunal = new Tribunal(afectos);
            compuerta.FijaTopeDia("juicio", 1);
            compuerta.FijaEnfriamiento("juicio", 3600);
            if (cfg.Ideas) IniciaIdeas();
            if (cfg.Ideas && cfg.Agentes) IniciaAgentes();
            // El soberano es quien tiene mas carisma.
            soberano = ids.Take(cfg.Ideas ? Math.Min(6, ids.Count) : ids.Count).OrderByDescending(i => fichas.Get(i).Carisma).ThenBy(i => i, StringComparer.Ordinal).First();   // con Ideas, de entre los mayores (tienen familia)
            string[] cat = { "crecimiento", "defensa", "cultura", "economia", "cohesion" };
            for (int i = 0; i < 40; i++)
                arbol.Add(new OpcionElegible { Id = "inv" + i.ToString("00"), Nombre = "Saber " + i, Categoria = cat[i % 5], Coste = 12 + (i / 5) * 6 });
            cronica.DiasPorTemporada = 90;
            if (cfg.Rumores) foreach (var id in ids) secretos.Asigna(id, RedSecretos.SecretoDeReserva(id), 0.3 + 0.6 * ((FichaGen.Hash(id + "g") % 100) / 100.0));
        }

        void IniciaIdeas()
        {
            // 6 mayores forman 3 matrimonios; el resto son sus hijos. Nadie tiene mas de un hogar.
            for (int i = 0; i < ids.Count; i++) linaje.Nace(ids[i], i < 6 ? -8000 - i : -1000 - i * 10, i < 6 ? null : ids[(i % 6) & ~1], i < 6 ? null : ids[((i % 6) & ~1) + 1]);
            for (int i = 0; i + 1 < 6 && i + 1 < ids.Count; i += 2) linaje.Casa(ids[i], ids[i + 1]);
            var rh = new Rng(cfg.Seed * 31 + 7);
            foreach (var id in ids) { habilidad[id] = rh.Range(0.1, 0.9); rencorBase[id] = fichas.Get(id).Rencor; }
            double t = 0; foreach (var v in habilidad.Values) t += v; res.HabilidadMediaInicial = t / habilidad.Count;
        }

        string LlmSimulado(string tipo, string entrada)
        {
            if (rngLlm.Chance(cfg.ProbLlmCae)) { salud.Fallo(); res.FallosLlm++; return null; }
            salud.Exito();
            if (tipo == "resumen") return "Resumen: " + (entrada.Length > 60 ? entrada.Substring(0, 60) : entrada);
            return "{\"texto\":\"Por mi honor\",\"piensa\":\"hmm\",\"actitud\":\"mantiene\"}";
        }

        public Resultado Run()
        {
            for (dia = 0; dia < cfg.Dias; dia++)
            {
                reloj.Ticks += compuerta.DiaTicks;
                if (cfg.Ideas) IdeasDiarias();
                if (cfg.Ideas && cfg.Agentes) AgentesDia();
                VidaSocial();
                if (cfg.Afectos) afectos.Avanza(1);
                if (cfg.Gobierno) GobiernoDia();
                Economia();
                if (cfg.Esquemas && dia % 3 == 0) Intenciones();
                EjecutaDecisiones();
                Mantenimiento();
                if (cfg.Ideas && dia % 30 == 29) IdeasMensuales();
                if (dia % 7 == 6) Semanal();
            }
            Cierra();
            return res;
        }

        // --- vida social ---
        void VidaSocial()
        {
            ModEstacion est = cfg.Ideas ? Estaciones.De(dia, cronica.DiasPorTemporada) : new ModEstacion { Nombre = "neutra" };
            foreach (var a in ids)
            {
                if (!rng.Chance(0.5 * Math.Min(1, est.Sociabilidad))) continue;
                string b = ids[rng.Next(ids.Count)];
                if (a == b) continue;
                double s = afectos.Sentimiento(a, b);
                double pBien = Math.Max(0.1, Math.Min(0.9, (0.62 + 0.4 * s) * est.Sociabilidad / est.Irritabilidad));
                if (cfg.Afectos)
                {
                    if (rng.Chance(pBien))
                    {
                        double r = rng.Next();
                        if (r > 0.9 && cfg.Ideas) cultura.Suceso("comunidad", 0.1);
                        if (r < 0.55) afectos.Evento(a, b, TipoEvento.Aprecio, rng.Range(0.1, 0.5));
                        else if (r < 0.75) afectos.Evento(a, b, TipoEvento.Ayuda, rng.Range(0.1, 0.6));
                        else if (r < 0.85 && s > 0.15) afectos.Evento(a, b, TipoEvento.Cortejo, rng.Range(0.1, 0.5));
                        else afectos.Evento(a, b, TipoEvento.Fiesta, rng.Range(0.1, 0.4));
                    }
                    else
                    {
                        double r = rng.Next();
                        double mag = rng.Range(0.1, 0.7);
                        if (r < 0.85) afectos.Evento(a, b, TipoEvento.Agravio, mag);
                        else if (r < 0.95) afectos.Evento(a, b, TipoEvento.Competencia, mag);
                        else afectos.Evento(a, b, TipoEvento.Traicion, mag);
                        Registra(a, b, "agravio de " + nombres[b], mag * 2);
                    }
                    if (rng.Chance(0.0003)) afectos.Evento(a, b, TipoEvento.Duelo, 1);
                    // El perdon espontaneo existe, y mas en quien tiene afecto previo.
                    if (afectos.Get(a, b).Rencor > 0.3 && rng.Chance(0.02 + 0.05 * Math.Max(0, afectos.Get(a, b).Afecto)))
                    {
                        afectos.Evento(a, b, TipoEvento.Perdon, rng.Range(0.3, 0.9));
                        if (cfg.Ideas) cultura.Suceso("clemencia", 1);
                    }
                }
                if (cfg.Rumores) res.Fugas += secretos.Conversan(a, b).Count;
            }
        }

        void Registra(string a, string b, string texto, double peso)
        {
            memoria.Registra(a, "opinion", b, texto, peso);
            if (memoria.NecesitaResumen(a))
            {
                var q = memoria.PreparaResumen(a);
                if (q != null)
                {
                    string s = salud.Disponible ? LlmSimulado("resumen", string.Join("; ", q.Textos.ToArray())) : null;
                    if (s == null) res.ResumenesFallback++; else res.ResumenesLlm++;
                    memoria.AplicaResumen(q, s);
                }
            }
            if (memoria.NecesitaFusion(a)) memoria.AplicaFusion(a, salud.Disponible ? LlmSimulado("resumen", memoria.TextoParaFusion(a)) : null);
        }

        // --- gobierno ---
        void GobiernoDia()
        {
            // peticiones nuevas
            if (rng.Chance(0.35))
            {
                string quien = ids[rng.Next(ids.Count)];
                if (quien != soberano)
                {
                    string[] tipos = { "obra", "defensa", "fiesta", "biblioteca", "tierras" };
                    string[] cats = { "economia", "defensa", "cohesion", "cultura", "crecimiento" };
                    int k = rng.Next(5);
                    var p = new Peticion { Id = "pet" + dia + "_" + quien, Tipo = tipos[k], Categoria = cats[k], Solicitante = quien, Importancia = rng.Range(0.3, 0.9), Texto = "pide " + tipos[k] };
                    var v = consejo.Evalua(p, soberano, metas);
                    if (v.Aprueba)
                    {
                        var d = compuerta.Propone("peticion", p.Id, p.Texto, v.Razon, p, false);
                        if (d != null) { res.Aprobadas++; }
                    }
                    else
                    {
                        res.Denegadas++;
                        // el solicitante se resiente (poco) por el rechazo
                        if (cfg.Afectos) afectos.Evento(quien, soberano, TipoEvento.Agravio, 0.12);
                    }
                }
            }
            // investigacion: si no hay nada en curso, el consejo elige
            if (enCurso == null)
            {
                metas.Ajusta(MetricasActuales());
                var disp = arbol.Where(o => !hechas.Contains(o.Id)).ToList();
                if (disp.Count > 0)
                {
                    foreach (var o in disp) o.Disponible = true;
                    var d = compuerta.Propone("investigacion", "inv", "investigar", "metas", consejo.Elige(disp, metas), false);

                }
            }
        }



        void EjecutaDecisiones()
        {
            foreach (var d in compuerta.Pendientes()) if (rng.Chance(cfg.ProbVeto / 10.0 * 3)) { if (compuerta.Veta(d.Id)) res.Vetadas++; }
            reloj.AdvanceSeconds(31);
            foreach (var d in compuerta.Listas())
            {
                if (d.Clase == "peticion") Aplica((Peticion)d.Carga);
                else if (d.Clase == "investigacion") { if (enCurso == null) enCurso = (OpcionElegible)d.Carga; }
                else if (d.Clase == "esquema") { EjecutaEsquema((PropuestaEsquema)d.Carga); CierraIntencionDeEsquema((PropuestaEsquema)d.Carga); }
                else if (d.Clase == "juicio") EjecutaJuicio((Sentencia)d.Carga);
            }
            reloj.Ticks -= 31 * TimeSpan.TicksPerSecond;
        }

        void Aplica(Peticion p)
        {
            double e = 0.04;
            if (p.Tipo == "obra") { riqueza = Math.Min(1, riqueza + e); res.Obras++; }
            else if (p.Tipo == "defensa") { seguridad = Math.Min(1, seguridad + e); res.Obras++; }
            else if (p.Tipo == "biblioteca") { saberExtra = Math.Min(0.2, saberExtra + 0.01); saber = Math.Min(1, (double)hechas.Count / arbol.Count + saberExtra); res.Obras++; }
            else if (p.Tipo == "tierras") { poblacion = Math.Min(1, poblacion + 0.03); res.Obras++; }
            else if (p.Tipo == "fiesta")
            {
                int n = Math.Min(8, ids.Count);
                for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) if (i != j && cfg.Afectos) afectos.Evento(ids[(dia + i) % ids.Count], ids[(dia + j) % ids.Count], TipoEvento.Fiesta, 0.5);
                if (costumbres.Observa("fiesta", dia / 360, (dia / 90) % 4)) { res.TradicionesEmergidas++; cronica.Anota(dia, "tradicion", "Nace la tradicion de la fiesta de " + (new[] { "primavera", "verano", "otono", "invierno" })[(dia / 90) % 4], 8); }
            }
            if (cfg.Ideas) Inspira(p.Solicitante, p.Categoria, 0.25 * p.Importancia + 0.05);
            if (cfg.Afectos) afectos.Evento(p.Solicitante, soberano, TipoEvento.Aprecio, 0.3);
            cronica.Anota(dia, "peticion", nombres[p.Solicitante] + " obtiene " + p.Tipo, 1 + p.Importancia);
        }

        // --- ideas: justicia, sucesion, mentoria, cultura, dialectos, suenos, espionaje, deriva ---
        void Inspira(string id, string categoria, double cantidad)
        {
            Ficha f = fichas.Get(id);
            if (f == null || muertos.Contains(id)) return;
            foreach (var t in suenos.Avanza(f, categoria, cantidad, afectos))
            {
                res.InspiracionesTotal++;
                if (t.Contains("cumple")) cronica.Anota(dia, "sueno", t, 3);
            }
        }

        void IdeasDiarias()
        {
            // Sucesion: el soberano muere a mitad del periodo simulado (un solo caso, para ejercitarla).
            if (!sucesionHecha && dia >= cfg.Dias / 2)
            {
                sucesionHecha = true;
                string viejo = soberano;
                linaje.Muere(viejo); muertos.Add(viejo); ids.Remove(viejo);
                var r = Sucesion.Elige(viejo, linaje, ids, afectos, id => fichas.Get(id), 0.03);
                if (r.Sucesor.Length > 0) soberano = r.Sucesor;
                res.HuboSucesion = true; res.SucesionDisputada = r.Disputada; res.Sucesor = r.Sucesor;
                var reparto = Linaje.Reparte(30, linaje.HijosVivos(viejo), linaje.Conyuge(viejo));
                int tot = 0; foreach (var v in reparto.Values) tot += v; res.HerenciaRepartida = tot;
                cronica.Anota(dia, "sucesion", nombres[viejo] + " muere; le sucede " + (r.Sucesor.Length > 0 ? nombres[r.Sucesor] : "nadie") + (r.Disputada ? " tras una disputa" : ""), 9);
            }
            // Juicios: las fugas graves nuevas pueden acabar ante el soberano.
            while (fugasJuzgadas < secretos.Fugas.Count)
            {
                Fuga f = secretos.Fugas[fugasJuzgadas++];
                Secreto sec = secretos.Get(f.SecretoId);
                Acusacion ac = Tribunal.DesdeFuga(f, sec, dia);
                if (ac == null || muertos.Contains(ac.Acusado) || muertos.Contains(ac.Acusador) || !rng.Chance(0.4)) continue;
                var sen = tribunal.Juzga(ac, soberano, false);
                res.Juicios++;
                compuerta.Propone("juicio", ac.Acusador + ">" + ac.Acusado, "juicio por " + ac.Delito, sen.Razon, sen, false);
            }
            // Mentoria: avanzan los lazos activos.
            foreach (var l in mentoria.Lazos)
            {
                double nuevo = mentoria.Avanza(l, habilidad[l.Mentor], habilidad[l.Aprendiz], 1, afectos);
                habilidad[l.Aprendiz] = nuevo;
            }
        }

        void EjecutaJuicio(Sentencia sen)
        {
            tribunal.Aplica(sen);
            if (sen.Pena == Pena.Absolver) res.Absoluciones++; else { res.Condenas++; cultura.Suceso("honor", 1); }
            cronica.Anota(dia, "juicio", nombres[sen.Causa.Acusado] + " " + (sen.Pena == Pena.Absolver ? "es absuelto" : "es condenado (" + sen.Pena + ")"), sen.Pena == Pena.Absolver ? 3 : 5);
        }

        void IdeasMensuales()
        {
            var vivos = ids;
            mentoria.Empareja(vivos, (id, h) => habilidad[id], new[] { "oficio" }, afectos, 0.25);
            mentoria.Termina(vivos, (id, h) => habilidad[id]);
            res.LazosMentoria = Math.Max(res.LazosMentoria, mentoria.Lazos.Count);
            double t = 0; foreach (var id in vivos) t += habilidad[id]; res.HabilidadMediaFinal = t / Math.Max(1, vivos.Count);

            var fs = Sociedad.Facciones(afectos, vivos, 0.15, 3);
            Sociedad.AsignaLideres(afectos, fs, id => fichas.Get(id), id => nombres[id]);
            var rd = new Rng(cfg.Seed * 13 + dia);
            foreach (var f in fs)
            {
                Dialecto d; if (!dialectos.TryGetValue(f.Lider, out d)) { d = new Dialecto(); dialectos[f.Lider] = d; }
                d.Deriva(rd);
                // Espionaje: el lider espia a la persona que mas le cae mal fuera de su faccion.
                string objetivo = null; double peor = 1;
                foreach (var o in vivos) { if (f.Miembros.Contains(o)) continue; double sen = afectos.Sentimiento(f.Lider, o); if (sen < peor) { peor = sen; objetivo = o; } }
                if (objetivo != null && peor < -0.05)
                {
                    bool desc; var fu = secretos.Espia(f.Lider, objetivo, out desc);
                    if (fu != null) res.EspiasExito++;
                    if (desc) res.EspiasDescubiertos++;
                }
            }
            int giros = 0; foreach (var d in dialectos.Values) giros += d.Count; res.GirosDialecto = giros;

            // Fe y deriva de personalidad.
            double maxDer = res.DerivaMax;
            foreach (var id in vivos)
            {
                double trauma = 0, grat = 0; int n = 0;
                foreach (var o in vivos) { if (o == id) continue; Par p = afectos.Get(id, o); trauma = Math.Max(trauma, p.Trauma); grat += Math.Max(0, p.Deuda); n++; }
                Ficha f = fichas.Get(id);
                f.Rencor = Deriva.Aplica(f.Rencor, rencorBase[id], trauma, n > 0 ? Math.Min(1, grat / n * 5) : 0);
                maxDer = Math.Max(maxDer, Math.Abs(f.Rencor - rencorBase[id]));
                cultura.ActualizaFe(id, Math.Min(1, afectos.Get(id, soberano).Afecto + 0.5), trauma < 0.1 ? 0.5 : 0, Sociedad.Soledad(afectos, id, vivos));
            }
            res.DerivaMax = maxDer;
            foreach (var h in cronica.Hitos) cultura.Observa(h);
            res.Credo = cultura.Credo();
        }

        // --- esquemas ---
        void Intenciones()
        {
            foreach (var pr in motor.Propone(afectos, catalogo))
            {
                res.EsquemasPropuestos++;
                compuerta.Propone("esquema", pr.Ejecutor + ">" + pr.Objetivo, pr.Tipo, pr.Razon, pr, false);
            }
        }

        void EjecutaEsquema(PropuestaEsquema p)
        {
            res.EsquemasEjecutados++;
            if (p.Talante == Talante.Hostil)
            {
                if (cfg.Ideas) cultura.Suceso("venganza", 0.5);
                afectos.Evento(p.Objetivo, p.Ejecutor, TipoEvento.Agravio, 0.35);       // el objetivo se ofende
                afectos.Evento(p.Ejecutor, p.Objetivo, TipoEvento.Perdon, 0.35);       // el ejecutor se desahoga (catarsis parcial)
                cronica.Anota(dia, "esquema", nombres[p.Ejecutor] + " intriga contra " + nombres[p.Objetivo], 3);
            }
            else
            {
                afectos.Evento(p.Ejecutor, p.Objetivo, TipoEvento.Cortejo, 0.6);
                afectos.Evento(p.Objetivo, p.Ejecutor, TipoEvento.Cortejo, 0.4);
                cronica.Anota(dia, "esquema", nombres[p.Ejecutor] + " corteja a " + nombres[p.Objetivo], 3);
            }
        }

        // --- economia y crecimiento (supuestos del simulador) ---
        MetricasReino MetricasActuales()
        {
            var m = new MetricasReino { Poblacion = poblacion, Seguridad = seguridad, Conocimiento = saber, Riqueza = riqueza };
            MetricasReino.Calcula(afectos, ids, m);
            return m;
        }

        void Economia()
        {
            var m = MetricasActuales();
            double produccion = 0.002 * (0.3 + m.Estabilidad) * (0.5 + poblacion);
            riqueza = Math.Min(1, riqueza + produccion - 0.0008 * riqueza);
            seguridad = Math.Max(0, seguridad - 0.0004);
            double antes = saber + hechas.Count + (enCurso != null ? 0.001 * puntosInvestigacion : 0);
            if (enCurso != null)
            {
                puntosInvestigacion += 0.6 + 2.0 * m.Estabilidad * poblacion;
                if (puntosInvestigacion >= enCurso.Coste)
                {
                    hechas.Add(enCurso.Id);
                    saber = Math.Min(1, (double)hechas.Count / arbol.Count + saberExtra);
                    res.Investigadas++;
                    cronica.Anota(dia, "saber", "Se descubre " + enCurso.Nombre, 4);
                    if (cfg.Ideas) { cultura.Suceso("saber", 1); foreach (var id in ids) Inspira(id, enCurso.Categoria, 0.04); }
                    if (enCurso.Categoria == "economia") riqueza = Math.Min(1, riqueza + 0.05);
                    if (enCurso.Categoria == "defensa") seguridad = Math.Min(1, seguridad + 0.08);
                    if (enCurso.Categoria == "crecimiento") poblacion = Math.Min(1, poblacion + 0.05);
                    enCurso = null; puntosInvestigacion = 0;
                }
            }
            double despues = saber + hechas.Count + (enCurso != null ? 0.001 * puntosInvestigacion : 0);
            // "Progreso": avanza el saber, la riqueza o las obras. Si nada avanza varios dias, se cuenta atasco.
            bool progreso = despues > antes + 1e-9 || res.Obras > obrasPrev;
            obrasPrev = res.Obras;
            if (progreso) diasSinProgreso = 0; else diasSinProgreso++;
            if (diasSinProgreso > res.MaxDiasSinProgreso) res.MaxDiasSinProgreso = diasSinProgreso;
            // atasco "duro" solo si no queda nada por investigar: entonces es fin de arbol, no atasco
            if (hechas.Count == arbol.Count) diasSinProgreso = 0;
        }
        int obrasPrev;

        void Mantenimiento()
        {
            reloj.Ticks += 0;
            if (dia % 30 == 29) memoria.Compacta();
        }

        // --- observacion semanal ---
        void Semanal()
        {
            var s = new Semana { Dia = dia, M = MetricasActuales() };
            double r = 0; int n = 0;
            foreach (var a in ids) foreach (var b in ids)
            {
                if (a == b) continue;
                Par p = afectos.Get(a, b);
                r += p.Rencor; n++;
                if (p.Rencor > res.RencorMax) res.RencorMax = p.Rencor;
                double sen = afectos.Sentimiento(a, b);
                int sg = sen > 0.1 ? 1 : sen < -0.1 ? -1 : 0;
                string k = a + ">" + b; int prev;
                if (signos.TryGetValue(k, out prev) && sg != 0 && prev != 0) { res.ParesObservados++; if (sg != prev) res.CambiosDeSigno++; }
                if (sg != 0) signos[k] = sg;
            }
            s.RencorMedio = n > 0 ? r / n : 0;
            var fs = Sociedad.Facciones(afectos, ids, 0.15, 2);
            s.M.Facciones = fs.Count;
            res.FaccionesFinales = fs.Count;
            s.Rumores = secretos.Fugas.Count;
            res.Semanas.Add(s);
            if (dia % 28 == 27 && cfg.Gobierno) metas.Ajusta(s.M);
        }

        void Cierra()
        {
            var fs = Sociedad.Facciones(afectos, ids, 0.15, 2);
            Sociedad.AsignaLideres(afectos, fs, id => fichas.Get(id), id => nombres[id]);
            res.FaccionesFinales = fs.Count;
            foreach (var f in fs) cronica.Anota(cfg.Dias - 1, "faccion", f.Nombre + " reune a " + f.Miembros.Count + " personas", 5);
            if (cfg.Ideas && cfg.Agentes) CierraAgentes();
            res.CronicaMd = cronica.ToMarkdown("Reino simulado") + (res.HistoriasMd.Length > 0 ? "\n" + res.HistoriasMd : "");
            res.GrafoDot = Informe.GrafoDot(afectos, ids, id => nombres[id], 0.35);
            var f0 = res.Final;
            double al = 0, fi = 0; int nsec = 0, expuestos = 0;
            for (int i = 1; i <= secretos.Count; i++) { al += secretos.Alcance(i); fi += secretos.FidelidadMedia(i); nsec++; if (secretos.Alcance(i) > 1) expuestos++; }
            res.AlcanceMedio = nsec > 0 ? al / nsec : 0; res.FidelidadMedia = nsec > 0 ? fi / nsec : 1; res.SaltosMax = secretos.SaltosMaximos(); res.SecretosExpuestos = expuestos;
            var top = new StringBuilder(); foreach (var kv in secretos.TopChismosos(3)) top.Append(nombres[kv.Key]).Append('=').Append(kv.Value).Append(' ');
            res.TopChismosos = top.ToString().Trim();
            var sb = new StringBuilder();
            sb.Append("# Informe del simulador\n\n");
            sb.Append("Semilla ").Append(cfg.Seed).Append(", ").Append(cfg.Dias).Append(" dias, ").Append(cfg.Pawns).Append(" personajes. Gobierno=").Append(cfg.Gobierno)
              .Append(", esquemas=").Append(cfg.Esquemas).Append(", rumores=").Append(cfg.Rumores).Append(".\n\n");
            sb.Append("Los valores del mundo son supuestos del simulador, no medidas del juego.\n\n");
            sb.Append("## Resultado\n\n");
            Fila(sb, "Peticiones aprobadas / denegadas / vetadas", res.Aprobadas + " / " + res.Denegadas + " / " + res.Vetadas);
            Fila(sb, "Investigaciones completadas", res.Investigadas.ToString(CultureInfo.InvariantCulture));
            Fila(sb, "Obras", res.Obras.ToString(CultureInfo.InvariantCulture));
            Fila(sb, "Esquemas propuestos / ejecutados", res.EsquemasPropuestos + " / " + res.EsquemasEjecutados);
            Fila(sb, "Fugas de secretos", res.Fugas.ToString(CultureInfo.InvariantCulture));
            Fila(sb, "Secretos expuestos / alcance medio / fidelidad media / saltos max", res.SecretosExpuestos + " / " + Json.Num(res.AlcanceMedio) + " / " + Json.Num(res.FidelidadMedia) + " / " + res.SaltosMax);
            Fila(sb, "Mayores chismosos", res.TopChismosos);
            Fila(sb, "Max dias seguidos sin progreso", res.MaxDiasSinProgreso.ToString(CultureInfo.InvariantCulture));
            Fila(sb, "Rencor maximo de un par", Json.Num(res.RencorMax));
            Fila(sb, "Cambios de signo semanales (oscilacion)", Json.Num(100 * res.TasaCambioSigno) + " %");
            Fila(sb, "Fallos del LLM simulado / resumenes con fallback", res.FallosLlm + " / " + res.ResumenesFallback);
            Fila(sb, "Facciones finales", res.FaccionesFinales.ToString(CultureInfo.InvariantCulture));
            Fila(sb, "Tradiciones emergidas", res.TradicionesEmergidas.ToString(CultureInfo.InvariantCulture));
            if (cfg.Ideas)
            {
                Fila(sb, "Juicios propuestos / condenas / absoluciones ejecutadas", res.Juicios + " / " + res.Condenas + " / " + res.Absoluciones);
                Fila(sb, "Sucesion", res.HuboSucesion ? "sucede " + (res.Sucesor.Length > 0 ? nombres[res.Sucesor] : "nadie") + (res.SucesionDisputada ? " (disputada)" : "") + "; herencia repartida " + res.HerenciaRepartida + "/30" : "no hubo");
                Fila(sb, "Mentoria: lazos maximos / habilidad media inicial -> final", res.LazosMentoria + " / " + Json.Num(res.HabilidadMediaInicial) + " -> " + Json.Num(res.HabilidadMediaFinal));
                Fila(sb, "Espionaje: exitos / descubiertos", res.EspiasExito + " / " + res.EspiasDescubiertos);
                Fila(sb, "Suenos: inspiraciones / cumplidos", res.InspiracionesTotal + " / " + suenos.Cumplidos + " de " + suenos.Total);
                res.SuenosCumplidos = suenos.Cumplidos;
                Fila(sb, "Credo emergente", res.Credo.Length > 0 ? res.Credo : "(aun sin credo)");
                Fila(sb, "Giros de dialecto (total) / deriva maxima de temperamento", res.GirosDialecto + " / " + Json.Num(res.DerivaMax));
            }
            if (cfg.Ideas && cfg.Agentes) FilasAgentes(sb);
            sb.Append("\n## Metricas finales\n\n```\n").Append(f0.ToJson()).Append("\n```\n");
            res.Informe = sb.ToString();
        }

        static void Fila(StringBuilder sb, string k, string v) { sb.Append("- ").Append(k).Append(": ").Append(v).Append('\n'); }

        public static string CsvSemanas(Resultado r)
        {
            var sb = new StringBuilder("dia,poblacion,seguridad,conocimiento,riqueza,cohesion,estabilidad,facciones,rencor_medio,fugas\n");
            foreach (var s in r.Semanas)
                sb.Append(s.Dia).Append(',').Append(Json.Num(s.M.Poblacion)).Append(',').Append(Json.Num(s.M.Seguridad)).Append(',').Append(Json.Num(s.M.Conocimiento))
                  .Append(',').Append(Json.Num(s.M.Riqueza)).Append(',').Append(Json.Num(s.M.Cohesion)).Append(',').Append(Json.Num(s.M.Estabilidad))
                  .Append(',').Append(s.M.Facciones).Append(',').Append(Json.Num(s.RencorMedio)).Append(',').Append(s.Rumores).Append('\n');
            return sb.ToString();
        }
    }
}
