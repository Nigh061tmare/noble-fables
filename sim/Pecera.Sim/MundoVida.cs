using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Pecera.Core;

namespace Pecera.Sim
{
    public sealed partial class Resultado
    {
        public int Nacimientos, Muertes, Bodas, Llegadas, Heridas, MayoriasDeEdad, PoblacionFinal;
        public int CrisisEstres, CrisisArrebato, CrisisHundimiento, RecuerdosRevividos, SilenciadasPorPreferencia, VetosPrimeraMitad, VetosSegundaMitad, PropuestasPrimeraMitad, PropuestasSegundaMitad;
        public bool OrdenAplicada, NormaForzadaVigente; public int AprobadasFavorecidoAntes, AprobadasFavorecidoDespues; public string Favorecido = "";
        public double HostilidadGrupalInicial = -1, HostilidadGrupalFinal, EstresMedio;
        public string PrejuiciosFinales = "", PreferenciasAprendidas = "";
        public bool PersistenciaIdempotente; public int LineasPersistidas;
        public int EventosBus, ErroresBus;
    }

    // v0.5 «Vida»: poblacion que nace, se casa, envejece hasta la mayoria de edad, emigra y muere; prejuicios de grupo calibrados con la
    // partida real; un JUGADOR SIMULADO con gustos ocultos (veta lo que no le gusta) para comprobar que el reino aprende de los vetos;
    // ordenes escritas a mitad de partida; y una comprobacion de persistencia completa al final.
    public sealed partial class Mundo
    {
        BusEventos bus;
        Preferencias prefs = new Preferencias();
        readonly Prejuicios prejuicios = new Prejuicios();
        readonly Dictionary<string, string> raza = new Dictionary<string, string>();
        readonly Dictionary<string, int> nacidoEn = new Dictionary<string, int>();
        readonly HashSet<string> disgustos = new HashSet<string> { "esquema:Difamar" };   // gustos OCULTOS del jugador simulado
        Ordenes ordenes;
        int hijos;
        static readonly string[] Razas = { "Humano", "Enano", "Elfo oscuro", "Orco", "Alto elfo" };
        const int DiasMayoria = 90;

        void IniciaVida()
        {
            bus = new BusEventos();
            Vida.Conecta(bus, new ContextoVida
            {
                Afectos = afectos, Mem = memoria, Cronica = cronica, Linaje = linaje, Persona = id => personas.Get(id),
                Nombre = id => nombres.ContainsKey(id) ? nombres[id] : id, Vivos = () => ids, Fuertes = fuertes
            });
            foreach (var id in ids) raza[id] = Razas[FichaGen.Hash(id + "raza") % Razas.Length];
            compuerta.AlDecidir = (d, vetada) =>
            {
                prefs.Registra(d.Clase, d.Etiqueta, vetada);
                bool primera = dia < cfg.Dias / 2;
                if (vetada) { if (primera) res.VetosPrimeraMitad++; else res.VetosSegundaMitad++; }
                if (primera) res.PropuestasPrimeraMitad++; else res.PropuestasSegundaMitad++;
            };
        }

        bool EsAdulto(string id) { int n; return !nacidoEn.TryGetValue(id, out n) || dia - n >= DiasMayoria; }

        // El jugador simulado veta casi siempre lo que le disgusta y casi nunca lo demas.
        double ProbVetoJugador(Decision d) { return disgustos.Contains(Preferencias.Clave(d.Clase, d.Etiqueta)) ? 0.85 : 0.03; }

        void VidaDia()
        {
            if (res.HostilidadGrupalInicial < 0 && dia == 30) res.HostilidadGrupalInicial = prejuicios.Hostilidad();
            prejuicios.Avanza(1);
            prejuicios.Atenuacion = normas.AtenuacionPrejuicio;
            ObservacionesDeGrupo();
            // mayoria de edad
            foreach (var kv in nacidoEn.ToList())
                if (dia - kv.Value == DiasMayoria && ids.Contains(kv.Key)) { res.MayoriasDeEdad++; cronica.Anota(dia, "mayoria", nombres[kv.Key] + " alcanza la mayoria de edad", 3); }
            // bodas y nacimientos
            var adultos = ids.Where(EsAdulto).OrderBy(x => x, StringComparer.Ordinal).ToList();
            foreach (var a in adultos)
                foreach (var b in adultos)
                {
                    if (string.CompareOrdinal(a, b) >= 0 || Relaciones.Etapa(afectos, a, b) != "pareja" && Relaciones.Etapa(afectos, b, a) != "pareja") continue;
                    if (linaje.Conyuge(a) == null && linaje.Conyuge(b) == null && rngVida.Chance(1.0 / 20)) { Publica("boda", a, b, ""); res.Bodas++; }
                }
            foreach (var a in adultos)
            {
                string c = linaje.Conyuge(a);
                if (c == null || string.CompareOrdinal(a, c) > 0 || !ids.Contains(c)) continue;
                if (ids.Count < cfg.Pawns * 1.5 && rngVida.Chance(1.0 / 150)) NacePawn(a, c);
            }
            // muertes (no el soberano: su muerte la guion la sucesion) y llegadas
            foreach (var a in adultos)
                if (a != soberano && rngVida.Chance(1.0 / (360 * 8))) MuerePawn(a, "", rngVida.Chance(0.5) ? "de enfermedad" : "en un accidente");
            if (dia % 120 == 60 && ids.Count < cfg.Pawns * 1.5) LlegaPawn();
            if (dia == (int)(cfg.Dias * 0.4)) AplicaOrdenes();
        }

        Rng _rngVida;
        Rng rngVida { get { if (_rngVida == null) _rngVida = new Rng(cfg.Seed * 53 + 11); return _rngVida; } }

        void Publica(string tipo, string a, string b, string texto)
        {
            bus.Publica(new EventoMundo { Tipo = tipo, A = a, B = b, Texto = texto, Dia = dia });
        }

        // Observaciones de rasgo con la distribucion REAL (93 % rasgo, 82 % negativas). El signo lo modulan el caracter y el grupo.
        void ObservacionesDeGrupo()
        {
            var adultos = ids.Where(EsAdulto).ToList();
            int n = Math.Max(1, (int)(adultos.Count * 0.3));
            for (int k = 0; k < n; k++)
            {
                string a = adultos[rngVida.Next(adultos.Count)], b = ids[rngVida.Next(ids.Count)];
                if (a == b) continue;
                bool rasgo, neg;
                double d = Calibracion.Muestra(rngVida, out rasgo, out neg);
                if (!rasgo) continue;                                        // los hechos ya los produce la vida social
                Persona pa = personas.Get(a);
                bool mismo = raza[a] == raza[b];
                double pNeg = mismo ? 0.3 : Calibracion.FraccionNegativa - 0.4 * (pa.Amabilidad - 0.5) - 0.2 * prejuicios.Actitud(a, raza[b]);
                pNeg = Math.Max(0.05, Math.Min(0.95, pNeg * (normas.AtenuacionPrejuicio < 1 ? 0.8 : 1)));
                double delta = rngVida.Chance(pNeg) ? -d : d;
                afectos.DesdeOpinion(a, b, delta, true);
                prejuicios.Anota(a, b, raza[b], delta, false);
            }
        }

        void NacePawn(string padre, string madre)
        {
            string id = "n" + (++hijos).ToString("00");
            nombres[id] = "Hijo" + hijos + "_de_" + nombres[padre];
            ids.Add(id); nacidoEn[id] = dia;
            var f = fichas.GetOCrea(id, nombres[id]); personas.GetOCrea(f);
            habilidad[id] = 0.05; rencorBase[id] = f.Rencor; raza[id] = raza[padre];
            if (cfg.Rumores) secretos.Asigna(id, RedSecretos.SecretoDeReserva(id), 0.3);
            Publica("nacimiento", id, padre, "madre=" + madre);
            afectos.Evento(padre, id, TipoEvento.Ayuda, 0.8); afectos.Evento(madre, id, TipoEvento.Ayuda, 0.8);
            res.Nacimientos++;
        }

        void LlegaPawn()
        {
            string id = "l" + (++hijos).ToString("00");
            nombres[id] = "Forastero" + hijos;
            ids.Add(id);
            var f = fichas.GetOCrea(id, nombres[id]); personas.GetOCrea(f);
            habilidad[id] = 0.4; rencorBase[id] = f.Rencor; raza[id] = Razas[FichaGen.Hash(id + "raza") % Razas.Length];
            if (cfg.Rumores) secretos.Asigna(id, RedSecretos.SecretoDeReserva(id), 0.5);
            Publica("llegada", id, "", "");
            res.Llegadas++;
        }

        void MuerePawn(string id, string causante, string como)
        {
            if (!ids.Contains(id)) return;
            Publica("muerte", id, causante, como);          // el duelo se calcula con los vivos, ANTES de quitarlo
            ids.Remove(id); muertos.Add(id);
            res.Muertes++;
        }

        void AplicaOrdenes()
        {
            var candidatos = ids.Where(x => x != soberano && EsAdulto(x)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (candidatos.Count == 0) return;
            string fav = candidatos[candidatos.Count / 2];
            res.Favorecido = nombres[fav];
            ordenes = Ordenes.Parse("Quiero paz en el reino, favorece a " + nombres[fav] + " y haced una fiesta.");
            var cambios = new List<string>();
            normas.Fuerza(ordenes.NormaForzada, dia, cambios);
            foreach (var c in cambios) cronica.Anota(dia, "orden", c, 6);
            consejo.Favorecido = sol => nombres.ContainsKey(sol) && ordenes.Favorece(nombres[sol], null);
            director.Protegido = x => nombres.ContainsKey(x) && ordenes.Favorece(nombres[x], null);
            director.FiestaPedida = ordenes.PideFiesta;
            res.OrdenAplicada = ordenes.Entendidas.Count >= 3;
            cronica.Anota(dia, "orden", "El soberano ordena: " + string.Join(", ", ordenes.Entendidas.ToArray()), 6);
        }

        // El que sera favorecido (mismo criterio que AplicaOrdenes), para medir sus aprobaciones ANTES de la orden.
        string FavorecidoPrevisto()
        {
            var c = ids.Where(x => x != soberano && EsAdulto(x)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            return c.Count == 0 ? "" : c[c.Count / 2];
        }

        void CierraVida()
        {
            res.PoblacionFinal = ids.Count;
            res.HostilidadGrupalFinal = prejuicios.Hostilidad();
            res.NormaForzadaVigente = ordenes != null && ordenes.NormaForzada != null && normas.Tiene(ordenes.NormaForzada);
            res.PrejuiciosFinales = string.Join(", ", prejuicios.MediaPorGrupo().Select(kv => kv.Key + "=" + Json.Num(kv.Value)).ToArray());
            res.PreferenciasAprendidas = prefs.Resumen();
            res.EventosBus = bus.Cuentas.Sum(kv => kv.Value); res.ErroresBus = bus.Errores;
            res.EstresMedio = ids.Select(id => personas.Get(id)).Where(p => p != null).Select(p => p.Estres).DefaultIfEmpty(0).Average();
            // Persistencia: guardar todo el estado social, cargarlo en componentes NUEVOS y volver a guardar debe dar lo mismo.
            var es = new EstadoSocial { Agenda = agenda, Normas = normas, Director = director, Cultura = cultura, Cronica = cronica, Costumbres = costumbres, Secretos = secretos, Fuertes = fuertes, Exp = maestro.Exp, Prefs = prefs, Prejuicios = prejuicios };
            var lineas = es.Serializa();
            var es2 = new EstadoSocial
            {
                Agenda = new Agenda(), Normas = new Normas(), Director = new Director(1), Cultura = new Cultura(), Cronica = new Cronica { MaxHitos = int.MaxValue }, Costumbres = new Costumbres(),
                Secretos = new RedSecretos(new ModeloAfectivo(null), x => null, reloj, new Rng(1)), Fuertes = new RecuerdosFuertes(), Exp = new Experiencia(), Prefs = new Preferencias(), Prejuicios = new Prejuicios()
            };
            bool futura; int malas = es2.Carga(lineas.ToArray(), out futura);
            res.LineasPersistidas = lineas.Count;
            res.PersistenciaIdempotente = malas == 0 && !futura && string.Join("\n", es2.Serializa().ToArray()) == string.Join("\n", lineas.ToArray());
        }

        void FilasVida(StringBuilder sb)
        {
            Fila(sb, "Vida: nacimientos / mayorias de edad / bodas / llegadas / muertes / poblacion final", res.Nacimientos + " / " + res.MayoriasDeEdad + " / " + res.Bodas + " / " + res.Llegadas + " / " + res.Muertes + " / " + res.PoblacionFinal);
            Fila(sb, "Psique: crisis por estres (arrebatos / hundimientos) / recuerdos fuertes revividos / estres medio final", res.CrisisEstres + " (" + res.CrisisArrebato + " / " + res.CrisisHundimiento + ") / " + res.RecuerdosRevividos + " / " + Json.Num(res.EstresMedio));
            Fila(sb, "Prejuicio de grupo: hostilidad media dia 30 -> final", Json.Num(res.HostilidadGrupalInicial) + " -> " + Json.Num(res.HostilidadGrupalFinal));
            Fila(sb, "Prejuicio medio hacia cada grupo", res.PrejuiciosFinales);
            Fila(sb, "Jugador simulado: vetos/propuestas 1a mitad -> 2a mitad / silenciadas por preferencia", res.VetosPrimeraMitad + "/" + res.PropuestasPrimeraMitad + " -> " + res.VetosSegundaMitad + "/" + res.PropuestasSegundaMitad + " / " + res.SilenciadasPorPreferencia);
            Fila(sb, "Preferencias aprendidas", res.PreferenciasAprendidas);
            Fila(sb, "Ordenes: entendidas / norma forzada vigente / favorecido (aprobadas antes -> despues)", res.OrdenAplicada + " / " + res.NormaForzadaVigente + " / " + res.Favorecido + " (" + res.AprobadasFavorecidoAntes + " -> " + res.AprobadasFavorecidoDespues + ")");
            Fila(sb, "Bus de eventos: eventos / errores de suscriptores", res.EventosBus + " / " + res.ErroresBus);
            Fila(sb, "Persistencia: lineas / ida y vuelta identica", res.LineasPersistidas + " / " + res.PersistenciaIdempotente);
        }
    }
}
