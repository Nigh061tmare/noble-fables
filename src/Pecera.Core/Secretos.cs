using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    public sealed class Secreto
    {
        public int Id;
        public string Sujeto = "";     // de quien trata (y quien lo guarda)
        public string Texto = "";
        public double Gravedad = 0.5;  // 0..1
        public readonly Dictionary<string, Conocimiento> Saben = new Dictionary<string, Conocimiento>();
    }

    public sealed class Conocimiento
    {
        public string Version = "";    // lo que ESE pawn cree (puede estar distorsionado)
        public double Fidelidad = 1;   // 1 = exacto
        public string Fuente = "";     // quien se lo conto ("" = el sujeto)
        public int Saltos;             // 0 = sujeto, 1 = primer receptor...
        public long Cuando;
    }

    public sealed class Fuga
    {
        public int SecretoId;
        public string Emisor = "", Receptor = "", Sujeto = "";
        public string Texto = "";
        public double Fidelidad;
        public int Saltos;
        public bool SujetoSeEntera;
        public string Motivo = "";

        public string ToJson()
        {
            return "{\"tipo\":\"fuga\",\"secreto\":" + SecretoId + ",\"emisor\":\"" + Json.Escape(Emisor) + "\",\"receptor\":\"" + Json.Escape(Receptor)
                + "\",\"sujeto\":\"" + Json.Escape(Sujeto) + "\",\"fidelidad\":" + Json.Num(Fidelidad) + ",\"saltos\":" + Saltos
                + ",\"sujeto_se_entera\":" + (SujetoSeEntera ? "true" : "false") + ",\"motivo\":\"" + Json.Escape(Motivo)
                + "\",\"texto\":\"" + Json.Escape(Texto) + "\"}";
        }
    }

    // Secretos, rumores y reputacion (D). Evolucion de Secretos.cs de Lords & Villeins:
    //  - claves por id estable de pawn (no por id de NPC del otro juego);
    //  - la probabilidad de contar depende de la confianza, el rencor y la locuacidad,
    //    en vez de un umbral fijo de afinidad;
    //  - cada salto DISTORSIONA el texto y baja la fidelidad;
    //  - hay consecuencias medibles en el modelo afectivo (receptor cambia de opinion
    //    del sujeto; si el sujeto se entera, rompe con el chismoso);
    //  - todo queda en fugas.jsonl y se mide la red de chismes.
    public sealed class RedSecretos
    {
        readonly ModeloAfectivo afectos;
        readonly Func<string, Ficha> fichas;
        readonly IClock reloj;
        readonly Rng rng;
        readonly Dictionary<int, Secreto> secretos = new Dictionary<int, Secreto>();
        readonly Dictionary<string, int> porSujeto = new Dictionary<string, int>();
        readonly List<Fuga> fugas = new List<Fuga>();
        int sig = 1;

        // Aristas del grafo de chismes: emisor -> receptor : numero de fugas.
        readonly Dictionary<string, int> aristas = new Dictionary<string, int>();

        public double ProbBase = 0.08;
        public double ConfianzaConfidencia = 0.55;   // confianza minima para confiar el propio secreto
        public double ProbConfidencia = 0.05;        // por conversacion elegible
        public int MaxFugasPorLlamada = 1;

        public RedSecretos(ModeloAfectivo afectos, Func<string, Ficha> fichas, IClock reloj, Rng rng)
        {
            this.afectos = afectos; this.fichas = fichas; this.reloj = reloj; this.rng = rng;
        }

        public int Count { get { return secretos.Count; } }
        public IList<Fuga> Fugas { get { return fugas; } }
        public Secreto Get(int id) { Secreto s; return secretos.TryGetValue(id, out s) ? s : null; }
        public bool TieneSecreto(string sujeto) { return porSujeto.ContainsKey(sujeto); }

        public Secreto Asigna(string sujeto, string texto, double gravedad)
        {
            if (porSujeto.ContainsKey(sujeto)) return secretos[porSujeto[sujeto]];
            var s = new Secreto { Id = sig++, Sujeto = sujeto, Texto = Json.UnaLinea(texto), Gravedad = Math.Max(0, Math.Min(1, gravedad)) };
            s.Saben[sujeto] = new Conocimiento { Version = s.Texto, Fidelidad = 1, Fuente = "", Saltos = 0, Cuando = reloj.NowTicks };
            secretos[s.Id] = s; porSujeto[sujeto] = s.Id;
            return s;
        }

        // Secreto de reserva sin LLM: determinista a partir del id.
        public static string SecretoDeReserva(string id)
        {
            string[] t = {
                "robo en secreto grano del almacen del reino",
                "debe una fortuna a un prestamista de fuera",
                "ama en secreto a alguien de otra casa",
                "no es quien dice ser: su familia es falsa",
                "huyo de un crimen cometido en su tierra natal",
                "traiciono una vez a un amigo por dinero" };
            return t[FichaGen.Hash(id) % t.Length];
        }

        // Probabilidad de que 'emisor' le cuente a 'receptor' un secreto de otro.
        public double ProbContar(string emisor, string receptor, string sujeto)
        {
            Ficha f = fichas(emisor);
            double loc = f != null ? f.Locuacidad : 0.5;
            Par er = afectos.Get(emisor, receptor);
            double confianza = er.Confianza;                  // fiarse del receptor
            double malicia = afectos.Get(emisor, sujeto).Rencor;   // odio al sujeto: quiere hacer dano
            double cercania = Math.Max(0, er.Afecto);
            double p = ProbBase * (0.3 + loc) * (0.4 + 0.6 * confianza) * (0.5 + 0.5 * cercania) * (1 + 1.5 * malicia);
            return Math.Max(0, Math.Min(0.95, p));
        }

        // El adaptador llama aqui cuando dos pawns conversan. Devuelve las fugas producidas.
        public List<Fuga> Conversan(string a, string b)
        {
            var res = new List<Fuga>();
            if (a == b) return res;
            Cuenta(a, b, res);
            if (res.Count < MaxFugasPorLlamada) Cuenta(b, a, res);
            return res;
        }

        void Cuenta(string emisor, string receptor, List<Fuga> res)
        {
            if (res.Count >= MaxFugasPorLlamada) return;
            // Confidencia: con mucha confianza y afecto, el emisor confia SU propio secreto
            // al receptor. Es la semilla de toda cadena de chismes; sin ella nada se filtraria.
            Secreto propio;
            if (porSujeto.ContainsKey(emisor) && (propio = secretos[porSujeto[emisor]]) != null && !propio.Saben.ContainsKey(receptor))
            {
                Par er = afectos.Get(emisor, receptor);
                if (er.Confianza >= ConfianzaConfidencia && er.Afecto >= 0.3 && rng.Chance(ProbConfidencia))
                {
                    propio.Saben[receptor] = new Conocimiento { Version = propio.Texto, Fidelidad = 1, Fuente = emisor, Saltos = 1, Cuando = reloj.NowTicks };
                    afectos.Evento(receptor, emisor, TipoEvento.Aprecio, 0.2);     // que te confien algo une
                    var cf = new Fuga { SecretoId = propio.Id, Emisor = emisor, Receptor = receptor, Sujeto = emisor, Texto = propio.Texto, Fidelidad = 1, Saltos = 1, SujetoSeEntera = true, Motivo = "confesion" };
                    fugas.Add(cf); res.Add(cf);
                    return;
                }
            }
            // Candidatos: secretos que el emisor conoce, de un sujeto que no es ni el emisor
            // (nadie se delata a si mismo aqui) ni el receptor, y que el receptor aun no conoce.
            var cand = new List<Secreto>();
            foreach (var s in secretos.Values)
                if (s.Sujeto != emisor && s.Sujeto != receptor && s.Saben.ContainsKey(emisor) && !s.Saben.ContainsKey(receptor))
                    cand.Add(s);
            if (cand.Count == 0) return;
            cand.Sort((x, y) => x.Id.CompareTo(y.Id));          // orden estable -> determinismo
            Secreto sec = cand[rng.Next(cand.Count)];
            double p = ProbContar(emisor, receptor, sec.Sujeto) * (0.5 + sec.Gravedad);
            if (!rng.Chance(Math.Min(0.95, p))) return;

            Conocimiento ck = sec.Saben[emisor];
            double fid = ck.Fidelidad * rng.Range(0.75, 0.97);          // cada boca lo cambia
            string version = Distorsiona(ck.Version, fid, rng);
            sec.Saben[receptor] = new Conocimiento { Version = version, Fidelidad = fid, Fuente = emisor, Saltos = ck.Saltos + 1, Cuando = reloj.NowTicks };

            // Consecuencia 1: el receptor piensa peor del sujeto, en proporcion a lo grave
            // y a lo fiable que le parece quien se lo conto.
            double peso = sec.Gravedad * fid * (0.4 + 0.6 * afectos.Get(receptor, emisor).Confianza);
            afectos.Evento(receptor, sec.Sujeto, TipoEvento.Agravio, 0.5 * peso);

            // Consecuencia 2: el sujeto puede enterarse (mas probable cuanto mas grave).
            bool seEntera = rng.Chance(0.15 + 0.35 * sec.Gravedad);
            string motivo = afectos.Get(emisor, sec.Sujeto).Rencor > 0.4 ? "malicia" : "confidencia";
            if (seEntera)
            {
                afectos.Evento(sec.Sujeto, emisor, TipoEvento.Traicion, 0.4 + 0.5 * sec.Gravedad);
                // Y la reputacion del chismoso baja ante quien lo sabe: confianza del receptor hacia el emisor
                // no cambia; la del sujeto si (ya aplicado). Lo marcamos en la fuga.
            }
            var f = new Fuga { SecretoId = sec.Id, Emisor = emisor, Receptor = receptor, Sujeto = sec.Sujeto, Texto = version, Fidelidad = fid, Saltos = ck.Saltos + 1, SujetoSeEntera = seEntera, Motivo = motivo };
            fugas.Add(f); res.Add(f);
            string ak = emisor + "\u001f" + receptor; int n; aristas.TryGetValue(ak, out n); aristas[ak] = n + 1;
        }

        // Escandalo (lo dispara el Director de drama): el secreto mas grave que 'receptor' no conoce sale a la luz por boca de alguien
        // que ya lo sabia (el sujeto no puede ser el propio receptor). Mismas consecuencias que un chisme normal. null si no hay secretos.
        public Fuga Escandalo(string receptor)
        {
            Secreto mejor = null;
            foreach (var s in secretos.Values)
                if (s.Sujeto != receptor && !s.Saben.ContainsKey(receptor) && (mejor == null || s.Gravedad > mejor.Gravedad + 1e-12 || (Math.Abs(s.Gravedad - mejor.Gravedad) <= 1e-12 && s.Id < mejor.Id))) mejor = s;
            if (mejor == null) return null;
            var emisores = new List<string>(); foreach (var k in mejor.Saben.Keys) if (k != receptor) emisores.Add(k);
            emisores.Sort(StringComparer.Ordinal);
            string emisor = emisores[rng.Next(emisores.Count)];
            Conocimiento ck = mejor.Saben[emisor];
            double fid = ck.Fidelidad * rng.Range(0.8, 0.97);
            string version = Distorsiona(ck.Version, fid, rng);
            mejor.Saben[receptor] = new Conocimiento { Version = version, Fidelidad = fid, Fuente = emisor, Saltos = ck.Saltos + 1, Cuando = reloj.NowTicks };
            afectos.Evento(receptor, mejor.Sujeto, TipoEvento.Agravio, 0.6 * mejor.Gravedad * fid);
            bool seEntera = rng.Chance(0.5 + 0.4 * mejor.Gravedad);
            if (seEntera && emisor != mejor.Sujeto) afectos.Evento(mejor.Sujeto, emisor, TipoEvento.Traicion, 0.4 + 0.5 * mejor.Gravedad);
            var f = new Fuga { SecretoId = mejor.Id, Emisor = emisor, Receptor = receptor, Sujeto = mejor.Sujeto, Texto = version, Fidelidad = fid, Saltos = ck.Saltos + 1, SujetoSeEntera = seEntera, Motivo = "escandalo" };
            fugas.Add(f);
            return f;
        }

        // Espionaje (F): 'espia' intenta enterarse del secreto de 'objetivo' por su cuenta. El exito
        // depende del carisma del espia y de la locuacidad del objetivo; si el objetivo lo descubre
        // lo toma como una traicion. Devuelve la fuga si hubo exito, o null.
        public double ProbEspiar(string espia, string objetivo)
        {
            Ficha fe = fichas(espia), fo = fichas(objetivo);
            double carisma = fe != null ? fe.Carisma : 0.5, loc = fo != null ? fo.Locuacidad : 0.5;
            return Math.Max(0.02, Math.Min(0.8, 0.15 + 0.35 * carisma + 0.25 * loc));
        }

        public Fuga Espia(string espia, string objetivo, out bool descubierto)
        {
            descubierto = false;
            if (espia == objetivo || !porSujeto.ContainsKey(objetivo)) return null;
            Secreto s = secretos[porSujeto[objetivo]];
            if (s.Saben.ContainsKey(espia)) return null;
            if (!rng.Chance(ProbEspiar(espia, objetivo)))
            {
                descubierto = rng.Chance(0.3);
                if (descubierto) afectos.Evento(objetivo, espia, TipoEvento.Traicion, 0.5);
                return null;
            }
            double fid = 0.9;
            s.Saben[espia] = new Conocimiento { Version = Distorsiona(s.Texto, fid, rng), Fidelidad = fid, Fuente = "espionaje", Saltos = 1, Cuando = reloj.NowTicks };
            descubierto = rng.Chance(0.15);
            if (descubierto) afectos.Evento(objetivo, espia, TipoEvento.Traicion, 0.4 + 0.4 * s.Gravedad);
            var f = new Fuga { SecretoId = s.Id, Emisor = "espionaje", Receptor = espia, Sujeto = objetivo, Texto = s.Saben[espia].Version, Fidelidad = fid, Saltos = 1, SujetoSeEntera = descubierto, Motivo = "espionaje" };
            fugas.Add(f);
            return f;
        }

        // Distorsion determinista: a menor fidelidad, mas hedging y mas exageracion.
        public static string Distorsiona(string texto, double fidelidad, Rng rng)
        {
            if (fidelidad >= 0.97) return texto;
            string t = texto;
            string[,] cambios = {
                { "robo", "saqueo" }, { "debe", "esta arruinado por" }, { "ama", "se acuesta con" },
                { "crimen", "asesinato" }, { "traiciono", "vendio a su gente" }, { "falsa", "inventada" } };
            int n = cambios.GetLength(0);
            int aplicar = fidelidad < 0.6 ? 2 : 1;
            for (int i = 0, hechos = 0; i < n && hechos < aplicar; i++)
            {
                int k = (rng.Next(n) + i) % n;
                if (t.IndexOf(cambios[k, 0], StringComparison.Ordinal) >= 0)
                {
                    t = t.Replace(cambios[k, 0], cambios[k, 1]);
                    hechos++;
                }
            }
            if (fidelidad < 0.85) t = "dicen que " + t;
            if (fidelidad < 0.6) t = "se rumorea, aunque nadie lo jura, que " + t.Replace("dicen que ", "");
            return t;
        }

        // ---- persistencia: quien sabe que (con su version distorsionada) ----
        public IEnumerable<string> Serializa()
        {
            var ids = new List<int>(secretos.Keys); ids.Sort();
            foreach (var id in ids)
            {
                var s = secretos[id];
                var sb = new StringBuilder("{\"k\":\"se\",\"id\":").Append(s.Id).Append(",\"s\":\"").Append(Json.Escape(s.Sujeto)).Append("\",\"tx\":\"").Append(Json.Escape(s.Texto))
                    .Append("\",\"g\":").Append(Json.Num(s.Gravedad)).Append(",\"sb\":[");
                var qs = new List<string>(s.Saben.Keys); qs.Sort(StringComparer.Ordinal);
                for (int i = 0; i < qs.Count; i++)
                {
                    var c = s.Saben[qs[i]];
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"q\":\"").Append(Json.Escape(qs[i])).Append("\",\"v\":\"").Append(Json.Escape(c.Version)).Append("\",\"f\":").Append(Json.Num(c.Fidelidad))
                      .Append(",\"fu\":\"").Append(Json.Escape(c.Fuente)).Append("\",\"sa\":").Append(c.Saltos).Append('}');
                }
                yield return sb.Append("]}").ToString();
            }
        }

        public void Carga(object d)
        {
            int id = (int)Json.Num(d, "id", 0); string su = Json.Str(d, "s");
            if (id <= 0 || su.Length == 0 || secretos.ContainsKey(id) || porSujeto.ContainsKey(su)) return;
            var s = new Secreto { Id = id, Sujeto = su, Texto = Json.Str(d, "tx"), Gravedad = Math.Max(0, Math.Min(1, Json.Num(d, "g", 0.5))) };
            var l = Json.Lista(d, "sb");
            if (l != null) foreach (var o in l) { string q = Json.Str(o, "q"); if (q.Length > 0) s.Saben[q] = new Conocimiento { Version = Json.Str(o, "v"), Fidelidad = Json.Num(o, "f", 1), Fuente = Json.Str(o, "fu"), Saltos = (int)Json.Num(o, "sa", 0) }; }
            if (!s.Saben.ContainsKey(su)) s.Saben[su] = new Conocimiento { Version = s.Texto, Fidelidad = 1 };
            secretos[id] = s; porSujeto[su] = id; sig = Math.Max(sig, id + 1);
        }

        // ---- metricas de la red de chismes ----
        public double Alcance(int secretoId)
        {
            Secreto s; if (!secretos.TryGetValue(secretoId, out s)) return 0;
            return s.Saben.Count;
        }

        public double FidelidadMedia(int secretoId)
        {
            Secreto s; if (!secretos.TryGetValue(secretoId, out s) || s.Saben.Count == 0) return 1;
            double t = 0; foreach (var c in s.Saben.Values) t += c.Fidelidad;
            return t / s.Saben.Count;
        }

        public int SaltosMaximos()
        {
            int m = 0; foreach (var s in secretos.Values) foreach (var c in s.Saben.Values) if (c.Saltos > m) m = c.Saltos;
            return m;
        }

        // Grado de salida (cuanto chismea cada uno): los "nodos chismosos".
        public List<KeyValuePair<string, int>> TopChismosos(int n)
        {
            var d = new Dictionary<string, int>();
            foreach (var kv in aristas)
            {
                string e = kv.Key.Substring(0, kv.Key.IndexOf('\u001f'));
                int v; d.TryGetValue(e, out v); d[e] = v + kv.Value;
            }
            var l = new List<KeyValuePair<string, int>>(d);
            l.Sort((x, y) => y.Value.CompareTo(x.Value));
            if (l.Count > n) l.RemoveRange(n, l.Count - n);
            return l;
        }

        public Dictionary<string, int> Aristas { get { return aristas; } }
    }
}
