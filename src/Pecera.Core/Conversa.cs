using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    public enum Tema { Saludo, Chisme, Queja, Cortejo, Negocio, Consuelo, Celebracion }

    public sealed class Linea
    {
        public string Hablante = "", Texto = "";
    }

    public sealed class Conversacion
    {
        public string A = "", B = "";
        public Tema Tema;
        public bool Exito;
        public string Efecto = "";                  // resumen legible del resultado
        public List<Linea> Lineas = new List<Linea>();
        public int Dia;
    }

    // Conversaciones entre dos pawns. PRINCIPIO: el RESULTADO (que cambia en el modelo afectivo) se decide con
    // reglas deterministas ANTES; el LLM, si hay presupuesto, solo escribe las lineas. Asi el mundo no depende de
    // que el modelo acierte y una conversacion cuesta como mucho UNA llamada.
    public static class Dialogo
    {
        public static Tema Elige(Persona a, Persona b, ModeloAfectivo m, Rng rng)
        {
            Par ab = m.Get(a.Id, b.Id);
            if (ab.Romance > 0.25 && ab.Afecto > 0.1) return Tema.Cortejo;
            if (ab.Rencor > 0.4 && rng.Chance(0.6)) return Tema.Queja;
            if (ab.Deuda > 0.3) return Tema.Celebracion;
            if (b.Needs.Social < 0.3 || b.Needs.Seguridad < 0.3) return Tema.Consuelo;
            if (m.Sentimiento(a.Id, b.Id) > 0.2 && a.Extroversion > 0.5 && rng.Chance(0.4)) return Tema.Chisme;
            if (a.Ambiciones.Exists(x => x.Categoria == "enriquecerse" && !x.Cumplida) && rng.Chance(0.3)) return Tema.Negocio;
            return Tema.Saludo;
        }

        // Probabilidad de que la conversacion salga bien: sentimiento previo + amabilidad de ambos - rencor.
        public static double ProbExito(Persona a, Persona b, ModeloAfectivo m, Tema t)
        {
            double s = (m.Sentimiento(a.Id, b.Id) + m.Sentimiento(b.Id, a.Id)) / 2;
            double p = 0.55 + 0.3 * s + 0.15 * (a.Amabilidad + b.Amabilidad - 1) - 0.1 * (a.Neuroticismo + b.Neuroticismo - 1);
            if (t == Tema.Queja) p -= 0.15;
            if (t == Tema.Consuelo || t == Tema.Celebracion) p += 0.1;
            return Math.Max(0.05, Math.Min(0.95, p));
        }

        // Aplica el resultado al modelo y devuelve la conversacion SIN lineas (las rellena plantilla o LLM).
        public static Conversacion Resuelve(Persona a, Persona b, Tema t, ModeloAfectivo m, Rng rng, int dia)
        {
            var c = new Conversacion { A = a.Id, B = b.Id, Tema = t, Dia = dia };
            c.Exito = rng.Chance(ProbExito(a, b, m, t));
            double mag = 0.3 + 0.4 * rng.Next();       // calibrado con el simulador: con 0.15-0.4 casi nadie llegaba a tener amigos en un anio
            switch (t)
            {
                case Tema.Saludo:
                    if (c.Exito) { m.Evento(a.Id, b.Id, TipoEvento.Aprecio, mag * 0.6); m.Evento(b.Id, a.Id, TipoEvento.Aprecio, mag * 0.6); c.Efecto = "se caen un poco mejor"; }
                    else { m.Evento(b.Id, a.Id, TipoEvento.Agravio, mag * 0.4); c.Efecto = "un saludo frio"; }
                    break;
                case Tema.Chisme:
                    if (c.Exito) { m.Evento(a.Id, b.Id, TipoEvento.Aprecio, mag * 0.5); m.Evento(b.Id, a.Id, TipoEvento.Aprecio, mag * 0.5); c.Efecto = "se unen cotilleando"; }
                    else { m.Evento(b.Id, a.Id, TipoEvento.Agravio, mag * 0.5); c.Efecto = "el cotilleo sienta mal"; }
                    break;
                case Tema.Queja:
                    if (c.Exito) { m.Evento(b.Id, a.Id, TipoEvento.Perdon, mag * 1.5); m.Evento(a.Id, b.Id, TipoEvento.Perdon, mag * 1.5); c.Efecto = "aclaran el malentendido"; }
                    else { m.Evento(b.Id, a.Id, TipoEvento.Agravio, mag); m.Evento(a.Id, b.Id, TipoEvento.Agravio, mag * 0.5); c.Efecto = "la discusion empeora las cosas"; }
                    break;
                case Tema.Cortejo:
                    if (c.Exito) { m.Evento(a.Id, b.Id, TipoEvento.Cortejo, mag * 1.2); m.Evento(b.Id, a.Id, TipoEvento.Cortejo, mag); c.Efecto = "el romance avanza"; }
                    else { m.Evento(a.Id, b.Id, TipoEvento.Agravio, mag * 0.3); c.Efecto = "un desaire amable"; }
                    break;
                case Tema.Negocio:
                    if (c.Exito) { m.Evento(a.Id, b.Id, TipoEvento.Ayuda, mag); m.Evento(b.Id, a.Id, TipoEvento.Aprecio, mag * 0.4); c.Efecto = "cierran un trato"; }
                    else { m.Evento(b.Id, a.Id, TipoEvento.Competencia, mag); c.Efecto = "no se ponen de acuerdo"; }
                    break;
                case Tema.Consuelo:
                    if (c.Exito) { m.Evento(b.Id, a.Id, TipoEvento.Ayuda, mag * 1.2); b.Needs.Pon("social", b.Needs.Social + 0.2); b.Needs.Pon("seguridad", b.Needs.Seguridad + 0.1); c.Efecto = "le consuela"; }
                    else { c.Efecto = "no sabe que decir"; }
                    break;
                default: // Celebracion
                    m.Evento(a.Id, b.Id, TipoEvento.Fiesta, mag); m.Evento(b.Id, a.Id, TipoEvento.Fiesta, mag);
                    a.Needs.Pon("social", a.Needs.Social + 0.1); b.Needs.Pon("social", b.Needs.Social + 0.1);
                    c.Efecto = "brindan juntos"; c.Exito = true;
                    break;
            }
            return c;
        }

        // ---- texto ----
        public static List<Linea> Plantilla(Conversacion c, string nomA, string nomB)
        {
            string[] t; 
            switch (c.Tema)
            {
                case Tema.Cortejo: t = c.Exito ? new[] { "Vuestra compania me alegra el dia.", "Y a mi la vuestra, de veras." } : new[] { "Quisiera pasear con vos.", "Hoy no puedo, perdonadme." }; break;
                case Tema.Queja: t = c.Exito ? new[] { "Aun me escuece lo de antes.", "Tenéis razon, me excedi. Lo siento." } : new[] { "No olvido lo que hicisteis.", "Y yo no pienso disculparme." }; break;
                case Tema.Chisme: t = c.Exito ? new[] { "¿Habeis oido lo ultimo en la plaza?", "Contadme, contadme." } : new[] { "Dicen cosas de todos.", "No me gusta hablar a espaldas." }; break;
                case Tema.Negocio: t = c.Exito ? new[] { "Os propongo un trato justo.", "Hecho, cerremoslo." } : new[] { "Mi precio es este.", "Es demasiado, buscare otra via." }; break;
                case Tema.Consuelo: t = c.Exito ? new[] { "Os veo abatido. ¿Puedo ayudar?", "Gracias, solo necesitaba compania." } : new[] { "Animo...", "No sabeis lo que dices." }; break;
                case Tema.Celebracion: t = new[] { "¡Brindemos por lo que hemos logrado!", "¡Por el reino!" }; break;
                default: t = c.Exito ? new[] { "Buenos dias, vecino.", "Buenos dias, que os sea leve." } : new[] { "Buenos dias.", "Mm." }; break;
            }
            return new List<Linea> { new Linea { Hablante = c.A, Texto = t[0] }, new Linea { Hablante = c.B, Texto = t[1] } };
        }

        public const string Sistema =
            "Escribes un dialogo muy breve entre dos personajes de un reino medieval. Espanol de epoca, sin markdown. " +
            "El resultado YA esta decidido y debes reflejarlo. Responde SOLO un JSON: {\"lineas\":[{\"h\":\"A\",\"t\":\"...\"},{\"h\":\"B\",\"t\":\"...\"}]} " +
            "con 2 a 4 lineas, 'h' es A o B.";

        public static string Usuario(Conversacion c, Persona a, Persona b)
        {
            return "A = " + a.Nombre + " (" + a.Resumen() + "). B = " + b.Nombre + " (" + b.Resumen() + "). Tema: " + c.Tema.ToString().ToLowerInvariant() +
                   ". Resultado: " + (c.Exito ? "sale bien" : "sale mal") + " (" + c.Efecto + ").";
        }

        // Valida el dialogo del LLM: 2..4 lineas, hablante A o B, texto no vacio y corto. Si no cumple => null (se usa plantilla).
        public static List<Linea> ParseLineas(string raw, string idA, string idB)
        {
            var d = Json.ParseObjeto(raw);
            var l = d != null ? Json.Lista(d, "lineas") : null;
            if (l == null || l.Count < 2 || l.Count > 4) return null;
            var r = new List<Linea>();
            foreach (var o in l)
            {
                string h = Json.Str(o, "h").Trim().ToUpperInvariant(), t = Json.UnaLinea(Json.Str(o, "t"));
                if ((h != "A" && h != "B") || t.Length == 0 || t.Length > 200) return null;
                r.Add(new Linea { Hablante = h == "A" ? idA : idB, Texto = t });
            }
            return r;
        }
    }
}
