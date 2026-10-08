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
        public int PlanesReglas, PlanesLlm, LlmLlamadasPlan, LlmRechazadas, LlmFallos;
        public int Conversaciones, ConversacionesLlm, ConversacionesExito;
        public int IntencionesHechas, IntencionesFallidas, IntencionesInvalidasEjecutadas, AmbicionesCumplidas, AmbicionesNuevas;
        public int MaxLlamadasLlmEnUnDia;
        public double ProgresoMedioAmbiciones, NecesidadSocialMedia, NecesidadMinimaMedia;
        public int HistoriasGeneradas;
        public string HistoriasMd = "";
        public string TiposIntencion = "";
    }

    // LLM de mentira para el simulador: elige entre las "opciones" que lista el prompt. Con probabilidad
    // PInvalido devuelve un plan que los guardarrailes DEBEN rechazar (tipo inventado, objetivo fantasma o
    // venganza sin rencor), para demostrar que el mundo no depende de que el modelo acierte.
    public sealed class MenteSimulada : IMente
    {
        readonly Rng rng;
        public double PFalla = 0.10, PInvalido = 0.20;
        public int Llamadas, Invalidos;

        public MenteSimulada(int seed) { rng = new Rng(seed); }

        public string Pregunta(string sistema, string usuario, int maxTokens)
        {
            Llamadas++;
            if (rng.Chance(PFalla)) return null;
            var sb = new StringBuilder("{\"planes\":[");
            bool primero = true;
            foreach (string linea in usuario.Split('\n'))
            {
                if (!linea.StartsWith("- id=", StringComparison.Ordinal)) continue;
                string id = linea.Substring(5, linea.IndexOf(' ', 5) - 5);
                string tipo = "descansar", obj = "";
                int k = linea.IndexOf("opciones: ", StringComparison.Ordinal);
                if (k >= 0)
                {
                    var ops = linea.Substring(k + 10).Split(';');
                    string op = ops[rng.Next(ops.Length)].Trim();
                    var partes = op.Split(' ');
                    tipo = partes[0]; obj = partes.Length > 1 ? partes[1] : "";
                }
                if (rng.Chance(PInvalido))
                {
                    Invalidos++;
                    double r = rng.Next();
                    if (r < 0.34) tipo = "teletransportar";
                    else if (r < 0.67) { tipo = "charlar"; obj = "fantasma"; }
                    else { tipo = "vengarse"; obj = id == "p00" ? "p01" : "p00"; }
                }
                if (!primero) sb.Append(',');
                primero = false;
                sb.Append("{\"id\":\"").Append(id).Append("\",\"tipo\":\"").Append(tipo).Append("\",\"objetivo\":\"").Append(obj).Append("\",\"texto\":\"\",\"piensa\":\"Veremos que trae el dia\"}");
            }
            return sb.Append("]}").ToString();
        }
    }

    public sealed partial class Mundo
    {
        AlmacenPersonas personas;
        Agenda agenda;
        PresupuestoLlm presupuesto;
        MenteSimulada mente;
        Rng rngAg;
        readonly Dictionary<TipoIntencion, int> tiposVistos = new Dictionary<TipoIntencion, int>();
        readonly Dictionary<int, Intencion> porEsquema = new Dictionary<int, Intencion>();
        int llamadasHoy, diaLlamadas = -1;

        void IniciaAgentes()
        {
            personas = new AlmacenPersonas(disco);
            foreach (var id in ids) personas.GetOCrea(fichas.Get(id));
            agenda = new Agenda();
            presupuesto = new PresupuestoLlm(reloj) { LlamadasPorDia = cfg.LlamadasLlmDia, DiaTicks = compuerta.DiaTicks };
            mente = new MenteSimulada(cfg.Seed * 17 + 3) { PFalla = cfg.ProbLlmCae };
            rngAg = new Rng(cfg.Seed * 29 + 5);
        }

        ContextoMundo Ctx()
        {
            return new ContextoMundo { Afectos = afectos, Vivos = ids, Dia = dia, Metas = metas, Persona = id => personas.Get(id), Nombre = id => nombres.ContainsKey(id) ? nombres[id] : id };
        }

        void AgentesDia()
        {
            var ctx = Ctx();
            // 1) reflexion + plan base por reglas (gratis)
            var vivos = ids.ToList();
            var reglas = new Dictionary<string, List<Intencion>>();
            foreach (var id in vivos)
            {
                var p = personas.Get(id);
                var r = Agente.Reflexiona(p, ctx);
                if (r.AmbicionNueva) res.AmbicionesNuevas++;
                var plan = Planificador.Planea(p, ctx);
                reglas[id] = plan;
                foreach (var i in plan) { if (agenda.Anade(i, dia) != null) res.PlanesReglas++; }
            }

            // 2) plan con LLM en lote: solo para quien no tiene nada vivo, con presupuesto
            var sinPlan = vivos.Where(id => agenda.Top(id, 1).Count == 0).ToList();
            var candidatos = vivos.Where(id => reglas[id].Count > 0 && agenda.Top(id, 1).Count > 0).OrderBy(id => id, StringComparer.Ordinal).ToList();
            var pendientes = sinPlan.Concat(candidatos.Where((id, k) => (k + dia) % 5 == 0)).Distinct().Take(cfg.LoteAgentes).ToList();
            if (pendientes.Count > 0 && presupuesto.Pide(PrioridadLlm.Planeacion))
            {
                Cuenta();
                res.LlmLlamadasPlan++;
                var ps = pendientes.Select(id => personas.Get(id)).ToList();
                var sug = pendientes.Select(id => reglas[id]).ToList();
                string raw = salud.Disponible ? mente.Pregunta(Pecera.Core.PlanLlm.Sistema, Pecera.Core.PlanLlm.Usuario(ps, sug, ctx), 400) : null;
                if (raw == null) { salud.Fallo(); res.LlmFallos++; }
                else
                {
                    salud.Exito();
                    var r = Pecera.Core.PlanLlm.Parse(raw, ps, ctx);
                    res.LlmRechazadas += r.Rechazadas;
                    foreach (var i in r.Intenciones) if (agenda.Anade(i, dia) != null) res.PlanesLlm++;
                }
            }

            // 3) ejecucion: la intencion mas prioritaria de cada uno, con una probabilidad
            foreach (var id in vivos)
            {
                var top = agenda.Top(id, 1);
                if (top.Count == 0 || !rngAg.Chance(0.7)) continue;
                Ejecuta(personas.Get(id), top[0], ctx);
            }

            // 4) cierre del dia
            agenda.Caduca(dia);
            foreach (var id in vivos)
            {
                var p = personas.Get(id);
                foreach (var a in Agente.Consolida(p, agenda, 1))
                {
                    res.AmbicionesCumplidas++;
                    cronica.Anota(dia, "sueno", nombres[id] + " cumple su ambicion: " + a.Texto, 5);
                }
            }
            if (dia % 30 == 29) personas.Guarda();
        }

        void Cuenta()
        {
            if (diaLlamadas != dia) { diaLlamadas = dia; llamadasHoy = 0; }
            llamadasHoy++;
            if (llamadasHoy > res.MaxLlamadasLlmEnUnDia) res.MaxLlamadasLlmEnUnDia = llamadasHoy;
        }

        string Otro(string id, string preferido)
        {
            if (preferido.Length > 0 && ids.Contains(preferido)) return preferido;
            var otros = ids.Where(x => x != id).ToList();
            return otros.Count > 0 ? otros[rngAg.Next(otros.Count)] : "";
        }

        void Habla(Persona a, Persona b, Tema t)
        {
            var c = Dialogo.Resuelve(a, b, t, afectos, rngAg, dia);
            res.Conversaciones++; if (c.Exito) res.ConversacionesExito++;
            // El texto es opcional y barato: con presupuesto, el LLM lo escribe; si no, plantilla. El resultado ya esta decidido.
            if (presupuesto.Pide(PrioridadLlm.Conversacion)) { Cuenta(); res.ConversacionesLlm++; }
            if (cfg.Rumores) res.Fugas += secretos.Conversan(a.Id, b.Id).Count;
        }

        void Ejecuta(Persona p, Intencion i, ContextoMundo ctx)
        {
            // Defensa en profundidad: aunque se hubiera colado algo invalido, no se ejecuta.
            if (!Planificador.Valida(p, i, ctx)) { res.IntencionesInvalidasEjecutadas++; agenda.Marca(i, EstadoIntencion.Descartada); return; }
            int antes = tiposVistos.ContainsKey(i.Tipo) ? tiposVistos[i.Tipo] : 0; tiposVistos[i.Tipo] = antes + 1;
            string obj = i.Objetivo;
            bool hecho = true;
            switch (i.Tipo)
            {
                case TipoIntencion.Charlar: { obj = Otro(p.Id, obj); if (obj.Length == 0) { hecho = false; break; } var b = personas.Get(obj); Habla(p, b, Dialogo.Elige(p, b, afectos, rngAg)); break; }
                case TipoIntencion.Visitar: { obj = Otro(p.Id, obj); if (obj.Length == 0) { hecho = false; break; } Habla(p, personas.Get(obj), Tema.Saludo); break; }
                case TipoIntencion.Cortejar: { var b = personas.Get(obj); var c = Dialogo.Resuelve(p, b, Tema.Cortejo, afectos, rngAg, dia); res.Conversaciones++; if (c.Exito) res.ConversacionesExito++; hecho = c.Exito; break; }
                case TipoIntencion.Consolar: { Habla(p, personas.Get(obj), Tema.Consuelo); break; }
                case TipoIntencion.Celebrar: { Habla(p, personas.Get(obj), Tema.Celebracion); cultura.Suceso("comunidad", 0.3); break; }
                case TipoIntencion.Descansar: p.Needs.Pon("descanso", p.Needs.Descanso + 0.2); break;
                case TipoIntencion.Trabajar: riqueza = Math.Min(1, riqueza + 0.0008); break;
                case TipoIntencion.Aprender: habilidad[p.Id] = Math.Min(0.99, habilidad[p.Id] + 0.01 * (1 - habilidad[p.Id])); break;
                case TipoIntencion.Pedir:
                {
                    var pet = new Peticion { Id = "ag" + dia + "_" + p.Id, Tipo = i.Categoria == "defensa" ? "defensa" : i.Categoria == "cultura" ? "biblioteca" : i.Categoria == "economia" ? "obra" : "tierras",
                                              Categoria = i.Categoria.Length > 0 ? i.Categoria : "cohesion", Solicitante = p.Id, Importancia = 0.3 + 0.5 * p.Ambiciones.Select(a => a.Prioridad).DefaultIfEmpty(0.3).Max(), Texto = i.Texto };
                    var v = consejo.Evalua(pet, soberano, metas);
                    if (v.Aprueba && compuerta.Propone("peticion", pet.Id, pet.Texto, v.Razon, pet, false) != null) res.Aprobadas++;
                    else { hecho = false; if (!v.Aprueba) res.Denegadas++; }
                    break;
                }
                case TipoIntencion.Intrigar:
                case TipoIntencion.Vengarse:
                {
                    if (!cfg.Esquemas) { hecho = false; break; }          // esquemas apagados: la intencion falla, no se cuela por otra puerta
                    var pr = new PropuestaEsquema { Ejecutor = p.Id, Objetivo = obj, Tipo = i.Tipo == TipoIntencion.Vengarse ? "Difamar" : "Desairar", Talante = Talante.Hostil, Razon = i.Razon, Intencion = i.Id };
                    var d = compuerta.Propone("esquema", p.Id + ">" + obj, pr.Tipo, pr.Razon, pr, false);
                    if (d == null) { hecho = false; break; }
                    res.EsquemasPropuestos++;
                    agenda.Marca(i, EstadoIntencion.EnCurso); porEsquema[i.Id] = i;
                    return;      // se cierra cuando la compuerta lo entregue
                }
            }
            agenda.Marca(i, hecho ? EstadoIntencion.Hecha : EstadoIntencion.Fallida);
            if (hecho) res.IntencionesHechas++; else res.IntencionesFallidas++;
        }

        void CierraIntencionDeEsquema(PropuestaEsquema pr)
        {
            Intencion i;
            if (pr.Intencion == 0 || !porEsquema.TryGetValue(pr.Intencion, out i)) return;
            porEsquema.Remove(pr.Intencion);
            agenda.Marca(i, EstadoIntencion.Hecha); res.IntencionesHechas++;
        }

        void CierraAgentes()
        {
            personas.Guarda();
            var todas = personas.Todas().Where(p => ids.Contains(p.Id)).ToList();
            double prog = 0; int n = 0; foreach (var p in todas) foreach (var a in p.Ambiciones) { prog += a.Progreso; n++; }
            res.ProgresoMedioAmbiciones = n > 0 ? prog / n : 0;
            res.NecesidadSocialMedia = todas.Count > 0 ? todas.Average(p => p.Needs.Social) : 0;
            double v; res.NecesidadMinimaMedia = todas.Count > 0 ? todas.Average(p => { p.Needs.Mas(out v); return v; }) : 0;
            res.TiposIntencion = string.Join(", ", tiposVistos.OrderBy(kv => kv.Key.ToString()).Select(kv => kv.Key.ToString().ToLowerInvariant() + "=" + kv.Value).ToArray());
            // Narrativa: las 3 historias mas pesadas de cada temporada con hitos (plantilla; el LLM es opcional).
            var hs = new List<Historia>();
            int maxT = 0; foreach (var h in cronica.Hitos) if (cronica.Temporada(h.Dia) > maxT) maxT = cronica.Temporada(h.Dia);
            for (int t = maxT; t >= 0 && t > maxT - 4; t--) hs.AddRange(Narrador.DeTemporada(cronica, t, 3));
            res.HistoriasGeneradas = hs.Count;
            res.HistoriasMd = Narrador.Markdown(hs, "el reino simulado");
        }

        void FilasAgentes(StringBuilder sb)
        {
            Fila(sb, "Agentes: planes por reglas / por LLM / rechazados por guardarrailes", res.PlanesReglas + " / " + res.PlanesLlm + " / " + res.LlmRechazadas);
            Fila(sb, "Agentes: llamadas LLM de planificacion (max en un dia) / fallos", res.LlmLlamadasPlan + " (" + res.MaxLlamadasLlmEnUnDia + ") / " + res.LlmFallos);
            Fila(sb, "Agentes: intenciones hechas / fallidas / reevaluadas y descartadas al ejecutar", res.IntencionesHechas + " / " + res.IntencionesFallidas + " / " + res.IntencionesInvalidasEjecutadas);
            Fila(sb, "Agentes: conversaciones (con exito / con texto LLM)", res.Conversaciones + " (" + res.ConversacionesExito + " / " + res.ConversacionesLlm + ")");
            Fila(sb, "Agentes: ambiciones cumplidas / nuevas por reflexion / progreso medio", res.AmbicionesCumplidas + " / " + res.AmbicionesNuevas + " / " + Json.Num(res.ProgresoMedioAmbiciones));
            Fila(sb, "Agentes: necesidad social media / necesidad mas urgente media", Json.Num(res.NecesidadSocialMedia) + " / " + Json.Num(res.NecesidadMinimaMedia));
            Fila(sb, "Agentes: tipos de intencion ejecutados", res.TiposIntencion);
            Fila(sb, "Narrativa: historias generadas", res.HistoriasGeneradas.ToString(CultureInfo.InvariantCulture));
        }
    }
}
