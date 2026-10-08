using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    public enum Plazo { Corto, Medio, Largo }

    // Necesidades en [0,1]: 1 = satisfecha. Salud y hambre las lleva el juego; el mod solo las ESTIMA
    // cuando no hay lectura directa (firma pendiente: Wants/Needs de Noble Fates, ver sonda).
    public sealed class Necesidades
    {
        public double Descanso = 0.8, Social = 0.6, Seguridad = 0.8, Autorrealizacion = 0.4;

        public static readonly string[] Nombres = { "descanso", "social", "seguridad", "autorrealizacion" };

        public double De(string n)
        {
            switch (n) { case "descanso": return Descanso; case "social": return Social; case "seguridad": return Seguridad; default: return Autorrealizacion; }
        }

        public void Pon(string n, double v)
        {
            v = Math.Max(0, Math.Min(1, v));
            switch (n) { case "descanso": Descanso = v; break; case "social": Social = v; break; case "seguridad": Seguridad = v; break; default: Autorrealizacion = v; break; }
        }

        // La necesidad mas urgente (la mas baja). Desempate por orden fijo => determinista.
        public string Mas(out double valor)
        {
            string peor = Nombres[0]; valor = De(peor);
            foreach (var n in Nombres) if (De(n) < valor - 1e-12) { valor = De(n); peor = n; }
            return peor;
        }

        // Cada dia las necesidades se degradan hacia un suelo propio de cada pawn: los sociables
        // pierden "social" antes; los neuroticos pierden "seguridad" antes. Nunca salen de [0,1].
        public void Avanza(double dias, Persona p)
        {
            Pon("descanso", Descanso - 0.18 * dias);
            Pon("social", Social - (0.05 + 0.08 * p.Extroversion) * dias);
            Pon("seguridad", Seguridad - (0.01 + 0.03 * p.Neuroticismo) * dias);
            Pon("autorrealizacion", Autorrealizacion - 0.015 * dias);
        }
    }

    public sealed class Ambicion
    {
        public string Texto = "", Categoria = "paz", Objetivo = "";
        public Plazo Plazo = Plazo.Medio;
        public double Progreso;      // 0..1
        public double Prioridad = 0.5;
        public bool Cumplida;

        public static readonly string[] Categorias = { "casarse", "aprender", "vengar", "enriquecerse", "mandar", "proteger", "descubrir", "paz" };
    }

    // Personalidad (cinco grandes) + oraculo narrativo + necesidades + ambiciones. Es el estado interno
    // del agente; la Ficha sigue siendo lo que NO cambia y la Persona lo que vive dia a dia.
    public sealed class Persona
    {
        public string Id = "", Nombre = "", Oraculo = "";
        public double Extroversion = 0.5, Amabilidad = 0.5, Escrupulosidad = 0.5, Neuroticismo = 0.5, Apertura = 0.5;
        public double Reputacion = 0.5;     // 0..1, lo bien visto que esta (la recalcula quien tenga el modelo afectivo)
        public Necesidades Needs = new Necesidades();
        public List<Ambicion> Ambiciones = new List<Ambicion>();

        public Ambicion Principal()
        {
            Ambicion m = null;
            foreach (var a in Ambiciones) if (!a.Cumplida && (m == null || a.Prioridad > m.Prioridad + 1e-12)) m = a;
            return m;
        }

        public string Resumen()
        {
            var sb = new StringBuilder();
            sb.Append(Oraculo.Length > 0 ? Oraculo : "sin rasgo definido");
            var p = Principal();
            if (p != null) sb.Append("; ambiciona ").Append(p.Texto).Append(" (").Append((int)Math.Round(p.Progreso * 100)).Append(" %)");
            double v; string n = Needs.Mas(out v);
            if (v < 0.35) sb.Append("; le falta ").Append(n);
            return sb.ToString();
        }

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"v\":1,\"id\":\"").Append(Json.Escape(Id)).Append("\",\"nombre\":\"").Append(Json.Escape(Nombre)).Append("\",\"oraculo\":\"").Append(Json.Escape(Oraculo))
              .Append("\",\"e\":").Append(Json.Num(Extroversion)).Append(",\"a\":").Append(Json.Num(Amabilidad)).Append(",\"c\":").Append(Json.Num(Escrupulosidad))
              .Append(",\"n\":").Append(Json.Num(Neuroticismo)).Append(",\"o\":").Append(Json.Num(Apertura)).Append(",\"rep\":").Append(Json.Num(Reputacion))
              .Append(",\"nd\":").Append(Json.Num(Needs.Descanso)).Append(",\"ns\":").Append(Json.Num(Needs.Social)).Append(",\"ng\":").Append(Json.Num(Needs.Seguridad)).Append(",\"na\":").Append(Json.Num(Needs.Autorrealizacion))
              .Append(",\"amb\":[");
            for (int i = 0; i < Ambiciones.Count; i++)
            {
                var a = Ambiciones[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"t\":\"").Append(Json.Escape(a.Texto)).Append("\",\"c\":\"").Append(a.Categoria).Append("\",\"ob\":\"").Append(Json.Escape(a.Objetivo))
                  .Append("\",\"pl\":").Append((int)a.Plazo).Append(",\"pr\":").Append(Json.Num(a.Progreso)).Append(",\"pi\":").Append(Json.Num(a.Prioridad)).Append(",\"ok\":").Append(a.Cumplida ? "true" : "false").Append('}');
            }
            return sb.Append("]}").ToString();
        }

        static double C(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }

        public static Persona FromJson(string linea)
        {
            object d;
            if (!Json.TryParse(linea, out d) || !(d is Dictionary<string, object>)) return null;
            var p = new Persona { Id = Json.Str(d, "id") };
            if (p.Id.Length == 0) return null;
            p.Nombre = Json.Str(d, "nombre"); p.Oraculo = Json.Str(d, "oraculo");
            p.Extroversion = C(Json.Num(d, "e", 0.5)); p.Amabilidad = C(Json.Num(d, "a", 0.5)); p.Escrupulosidad = C(Json.Num(d, "c", 0.5));
            p.Neuroticismo = C(Json.Num(d, "n", 0.5)); p.Apertura = C(Json.Num(d, "o", 0.5)); p.Reputacion = C(Json.Num(d, "rep", 0.5));
            p.Needs.Descanso = C(Json.Num(d, "nd", 0.8)); p.Needs.Social = C(Json.Num(d, "ns", 0.6)); p.Needs.Seguridad = C(Json.Num(d, "ng", 0.8)); p.Needs.Autorrealizacion = C(Json.Num(d, "na", 0.4));
            var l = Json.Lista(d, "amb");
            if (l != null)
                foreach (var o in l)
                {
                    string cat = Json.Str(o, "c");
                    if (Array.IndexOf(Ambicion.Categorias, cat) < 0) continue;      // una categoria desconocida (dato corrupto) se descarta
                    int pl = (int)Json.Num(o, "pl", 1);
                    p.Ambiciones.Add(new Ambicion
                    {
                        Texto = Json.Str(o, "t"), Categoria = cat, Objetivo = Json.Str(o, "ob"), Plazo = pl >= 0 && pl <= 2 ? (Plazo)pl : Plazo.Medio,
                        Progreso = C(Json.Num(o, "pr", 0)), Prioridad = C(Json.Num(o, "pi", 0.5)), Cumplida = Json.Str(o, "ok") == "true"
                    });
                }
            return p;
        }
    }

    // Generacion de personas: determinista a partir de la ficha (funciona sin LLM) y refinable con el LLM.
    public static class PersonaGen
    {
        static double R(string clave, double lo, double hi) { return lo + (hi - lo) * ((FichaGen.Hash(clave) % 1000) / 999.0); }
        static double C(double v) { return Math.Round(v < 0.02 ? 0.02 : v > 0.98 ? 0.98 : v, 2); }

        public static string CategoriaDeMeta(string meta)
        {
            string m = (meta ?? "").ToLowerInvariant();
            if (m.Contains("amor")) return "casarse";
            if (m.Contains("saber") || m.Contains("descubrir")) return "descubrir";
            if (m.Contains("vengar")) return "vengar";
            if (m.Contains("enriquec")) return "enriquecerse";
            if (m.Contains("proteger")) return "proteger";
            if (m.Contains("respeto") || m.Contains("importante")) return "mandar";
            if (m.Contains("aprender")) return "aprender";
            return "paz";
        }

        public static Persona Desde(Ficha f)
        {
            var p = new Persona { Id = f.Id, Nombre = f.Nombre };
            p.Extroversion = C(0.6 * f.Locuacidad + 0.4 * R(f.Id + "e", 0.1, 0.9));
            p.Amabilidad = C(0.6 * (1 - f.Rencor) + 0.4 * R(f.Id + "a", 0.1, 0.9));
            p.Escrupulosidad = C(R(f.Id + "c", 0.1, 0.9));
            p.Neuroticismo = C(0.5 * f.Rencor + 0.5 * R(f.Id + "n", 0.1, 0.9));
            p.Apertura = C(0.5 * f.Ambicion + 0.5 * R(f.Id + "o", 0.1, 0.9));
            p.Oraculo = f.Rasgos.Count > 0 ? "el " + f.Rasgos[0] : "el callado";
            foreach (var a in new[] { "descanso", "social", "seguridad", "autorrealizacion" }) p.Needs.Pon(a, R(f.Id + a, 0.4, 0.9));
            for (int i = 0; i < f.Metas.Count && i < 2; i++)
                p.Ambiciones.Add(new Ambicion { Texto = f.Metas[i], Categoria = CategoriaDeMeta(f.Metas[i]), Plazo = i == 0 ? Plazo.Medio : Plazo.Largo, Prioridad = i == 0 ? 0.7 : 0.4 });
            if (f.Ambicion >= 0.7 && !p.Ambiciones.Exists(x => x.Categoria == "mandar"))
                p.Ambiciones.Add(new Ambicion { Texto = "llegar a mandar en el reino", Categoria = "mandar", Plazo = Plazo.Largo, Prioridad = 0.5 + 0.3 * f.Ambicion });
            if (p.Ambiciones.Count == 0) p.Ambiciones.Add(new Ambicion { Texto = "vivir en paz", Categoria = "paz", Plazo = Plazo.Largo, Prioridad = 0.3 });
            return p;
        }

        public const string SistemaPersona =
            "Defines el mundo interior de un aldeano medieval. Responde SOLO un JSON: " +
            "{\"oraculo\":\"rasgo narrativo breve\",\"ambiciones\":[{\"texto\":\"...\",\"categoria\":\"" + "casarse|aprender|vengar|enriquecerse|mandar|proteger|descubrir|paz" + "\",\"plazo\":\"corto|medio|largo\"}]}. " +
            "Entre 1 y 3 ambiciones, en espanol, coherentes con la ficha. Nada de explicaciones.";

        // Mezcla la respuesta del LLM sobre la persona determinista. Categoria desconocida => ambicion descartada.
        public static Persona DesdeLlm(Ficha f, string raw)
        {
            var p = Desde(f);
            var d = Json.ParseObjeto(raw);
            if (d == null) return p;
            string orac = Json.UnaLinea(Json.Str(d, "oraculo"));
            if (orac.Length > 0 && orac.Length < 50) p.Oraculo = orac;
            var l = Json.Lista(d, "ambiciones");
            if (l == null) return p;
            var nuevas = new List<Ambicion>();
            foreach (var o in l)
            {
                if (nuevas.Count >= 3) break;
                string cat = Json.Str(o, "categoria").ToLowerInvariant(), t = Json.UnaLinea(Json.Str(o, "texto"));
                if (Array.IndexOf(Ambicion.Categorias, cat) < 0 || t.Length == 0 || t.Length > 90) continue;
                string pl = Json.Str(o, "plazo").ToLowerInvariant();
                nuevas.Add(new Ambicion { Texto = t, Categoria = cat, Plazo = pl == "corto" ? Plazo.Corto : pl == "largo" ? Plazo.Largo : Plazo.Medio, Prioridad = Math.Max(0.2, 0.8 - 0.2 * nuevas.Count) });
            }
            if (nuevas.Count > 0) p.Ambiciones = nuevas;
            return p;
        }
    }

    // Personas en disco: una linea por persona, la ultima gana; se reescribe entero al guardar.
    public sealed class AlmacenPersonas
    {
        const string FICHERO = "personas.jsonl";
        readonly IStorage disco;
        readonly Dictionary<string, Persona> por = new Dictionary<string, Persona>();
        readonly object cerrojo = new object();
        public int LineasCorruptas { get; private set; }

        public AlmacenPersonas(IStorage disco)
        {
            this.disco = disco;
            foreach (var l in disco.ReadLines(FICHERO))
            {
                var p = Persona.FromJson(l);
                if (p == null) { LineasCorruptas++; continue; }
                por[p.Id] = p;
            }
        }

        public int Count { get { lock (cerrojo) { return por.Count; } } }
        public Persona Get(string id) { lock (cerrojo) { Persona p; return por.TryGetValue(id, out p) ? p : null; } }

        public Persona GetOCrea(Ficha f)
        {
            lock (cerrojo)
            {
                Persona p;
                if (!por.TryGetValue(f.Id, out p)) { p = PersonaGen.Desde(f); por[f.Id] = p; }
                return p;
            }
        }

        public void Pon(Persona p) { lock (cerrojo) { por[p.Id] = p; } }
        public List<Persona> Todas() { lock (cerrojo) { return new List<Persona>(por.Values); } }

        public void Guarda()
        {
            lock (cerrojo)
            {
                var l = new List<string>();
                var ids = new List<string>(por.Keys); ids.Sort(StringComparer.Ordinal);
                foreach (var id in ids) l.Add(por[id].ToJson());
                disco.Rewrite(FICHERO, l);
            }
        }
    }
}
