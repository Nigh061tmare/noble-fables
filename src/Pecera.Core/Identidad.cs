using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    // Ficha de un personaje: lo que NO cambia cada minuto. Se genera una vez (con el LLM
    // o, sin el, de forma determinista a partir del id) y se guarda.
    public sealed class Ficha
    {
        public string Id = "", Nombre = "";
        public List<string> Rasgos = new List<string>();
        public List<string> Metas = new List<string>();
        public List<string> Miedos = new List<string>();
        public string Voz = "";           // como habla
        public double Carisma = 0.5;      // 0..1, base del liderazgo
        public double Rencor = 0.5;       // 0..1, cuanto le cuesta olvidar (modula el decaimiento)
        public double Locuacidad = 0.5;   // 0..1, propension a contar secretos
        public double Ambicion = 0.5;     // 0..1, empuja sucesiones y sueños

        public string Resumen()
        {
            var sb = new StringBuilder();
            if (Rasgos.Count > 0) sb.Append(string.Join(", ", Rasgos.ToArray()));
            if (Metas.Count > 0) { if (sb.Length > 0) sb.Append("; "); sb.Append("quiere ").Append(string.Join(" y ", Metas.ToArray())); }
            if (Miedos.Count > 0) { if (sb.Length > 0) sb.Append("; "); sb.Append("teme ").Append(string.Join(" y ", Miedos.ToArray())); }
            if (Voz.Length > 0) { if (sb.Length > 0) sb.Append("; "); sb.Append("habla ").Append(Voz); }
            return sb.ToString();
        }

        public string ToJson()
        {
            return "{\"v\":1,\"id\":\"" + Json.Escape(Id) + "\",\"nombre\":\"" + Json.Escape(Nombre) + "\","
                 + "\"rasgos\":" + Arr(Rasgos) + ",\"metas\":" + Arr(Metas) + ",\"miedos\":" + Arr(Miedos) + ","
                 + "\"voz\":\"" + Json.Escape(Voz) + "\",\"carisma\":" + Json.Num(Carisma)
                 + ",\"rencor\":" + Json.Num(Rencor) + ",\"locuacidad\":" + Json.Num(Locuacidad) + ",\"ambicion\":" + Json.Num(Ambicion) + "}";
        }

        public static Ficha FromJson(string linea)
        {
            object d;
            if (!Json.TryParse(linea, out d) || !(d is Dictionary<string, object>)) return null;
            var f = new Ficha();
            f.Id = Json.Str(d, "id");
            if (f.Id.Length == 0) return null;
            f.Nombre = Json.Str(d, "nombre");
            f.Rasgos = Strs(Json.Lista(d, "rasgos"));
            f.Metas = Strs(Json.Lista(d, "metas"));
            f.Miedos = Strs(Json.Lista(d, "miedos"));
            f.Voz = Json.Str(d, "voz");
            f.Carisma = Clamp01(Json.Num(d, "carisma", 0.5));
            f.Rencor = Clamp01(Json.Num(d, "rencor", 0.5));
            f.Locuacidad = Clamp01(Json.Num(d, "locuacidad", 0.5));
            f.Ambicion = Clamp01(Json.Num(d, "ambicion", 0.5));   // opcional: fichas antiguas no la traen
            return f;
        }

        static string Arr(List<string> l)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < l.Count; i++) { if (i > 0) sb.Append(','); sb.Append('"').Append(Json.Escape(l[i])).Append('"'); }
            return sb.Append(']').ToString();
        }

        static List<string> Strs(List<object> l)
        {
            var r = new List<string>();
            if (l != null) foreach (var o in l) { var s = o as string; if (!string.IsNullOrEmpty(s)) r.Add(s); }
            return r;
        }

        static double Clamp01(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
    }

    // Generador determinista de fichas: sin LLM el reino sigue teniendo personalidades.
    public static class FichaGen
    {
        static readonly string[] Rasgos = { "orgulloso", "leal", "rencoroso", "generoso", "desconfiado", "ambicioso", "piadoso", "bromista", "huraño", "valiente", "cobarde", "curioso" };
        static readonly string[] Metas = { "ganarse el respeto del reino", "enriquecerse", "proteger a los suyos", "ser alguien importante", "vivir en paz", "vengar un agravio", "encontrar el amor", "descubrir el saber antiguo" };
        static readonly string[] Miedos = { "la pobreza", "la soledad", "la guerra", "la deshonra", "la enfermedad", "ser olvidado", "el hambre" };
        static readonly string[] Voces = { "grave y pausado", "vivo y sarcastico", "humilde y cortes", "seco y directo", "solemne" };

        public static int Hash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in s) { h ^= c; h *= 16777619; }
                return (int)(h & 0x7fffffff);
            }
        }

        public static Ficha Determinista(string id, string nombre)
        {
            var r = new Rng(Hash(id));
            var f = new Ficha { Id = id, Nombre = nombre };
            f.Rasgos.Add(Rasgos[r.Next(Rasgos.Length)]);
            string r2 = Rasgos[r.Next(Rasgos.Length)];
            if (!f.Rasgos.Contains(r2)) f.Rasgos.Add(r2);
            f.Metas.Add(Metas[r.Next(Metas.Length)]);
            f.Miedos.Add(Miedos[r.Next(Miedos.Length)]);
            f.Voz = Voces[r.Next(Voces.Length)];
            f.Carisma = Math.Round(r.Range(0.1, 0.95), 2);
            f.Rencor = Math.Round(r.Range(0.1, 0.95), 2);
            f.Locuacidad = Math.Round(r.Range(0.1, 0.95), 2);
            f.Ambicion = Math.Round(r.Range(0.1, 0.95), 2);   // al final: no altera lo sorteado antes
            return f;
        }

        public const string SistemaFicha =
            "Creas la personalidad de un aldeano medieval. Responde SOLO un JSON: " +
            "{\"rasgos\":[\"..\",\"..\"],\"meta\":\"..\",\"miedo\":\"..\",\"voz\":\"..\"}. " +
            "Rasgos de una palabra, meta y miedo de pocas palabras, voz = como habla. Espanol.";

        // Mezcla la respuesta del LLM sobre la ficha determinista (lo que falte se queda).
        public static Ficha DesdeLlm(string id, string nombre, string raw)
        {
            var f = Determinista(id, nombre);
            var d = Json.ParseObjeto(raw);
            if (d == null) return f;
            var rs = Json.Lista(d, "rasgos");
            if (rs != null && rs.Count > 0)
            {
                var nuevos = new List<string>();
                foreach (var o in rs) { var s = o as string; if (!string.IsNullOrEmpty(s) && s.Length < 30 && nuevos.Count < 3) nuevos.Add(s.Trim()); }
                if (nuevos.Count > 0) f.Rasgos = nuevos;
            }
            string meta = Json.UnaLinea(Json.Str(d, "meta")); if (meta.Length > 0 && meta.Length < 80) { f.Metas.Clear(); f.Metas.Add(meta); }
            string miedo = Json.UnaLinea(Json.Str(d, "miedo")); if (miedo.Length > 0 && miedo.Length < 80) { f.Miedos.Clear(); f.Miedos.Add(miedo); }
            string voz = Json.UnaLinea(Json.Str(d, "voz")); if (voz.Length > 0 && voz.Length < 60) f.Voz = voz;
            return f;
        }
    }

    // Id estable por pawn. LIMITE CONOCIDO: el juego puede no exponer un id persistente
    // (firma pendiente de confirmar con sonda). Mientras tanto el adaptador pasa la mejor
    // clave que encuentre; si solo hay nombre, dos pawns con el mismo nombre se detectan
    // por su manejador de sesion y se separan (id con sufijo #n) y se marca "ambiguo".
    public sealed class RegistroIds
    {
        const string FICHERO = "ids.jsonl";
        readonly IStorage disco;
        readonly Dictionary<string, string> claveAId = new Dictionary<string, string>();
        readonly Dictionary<string, HashSet<int>> manejadores = new Dictionary<string, HashSet<int>>();
        readonly HashSet<string> ambiguos = new HashSet<string>();
        readonly Dictionary<string, string> nombres = new Dictionary<string, string>();
        readonly object cerrojo = new object();

        public RegistroIds(IStorage disco)
        {
            this.disco = disco;
            foreach (var l in disco.ReadLines(FICHERO))
            {
                object d;
                if (!Json.TryParse(l, out d)) continue;
                string k = Json.Str(d, "k"), id = Json.Str(d, "id");
                if (k.Length > 0 && id.Length > 0)
                {
                    claveAId[k] = id; nombres[id] = Json.Str(d, "n");
                    if (k.StartsWith("g:", StringComparison.Ordinal)) claveDeNombrePrevio[Json.Str(d, "n")] = k;
                }
            }
        }

        public int Ambiguos { get { lock (cerrojo) { return ambiguos.Count; } } }

        // Evidencia de que la clave del juego es ESTABLE entre sesiones: al ver un pawn con
        // clave estable cuyo nombre ya estaba registrado de una sesion anterior, la clave
        // coincide (id estable) o no (el juego renumera: no sirve como id).
        public int ClavesCoinciden { get; private set; }
        public int ClavesDiscrepan { get; private set; }
        readonly HashSet<string> comprobados = new HashSet<string>();
        readonly Dictionary<string, string> claveDeNombrePrevio = new Dictionary<string, string>();

        // claveEstable: id persistente del juego si se encontro, o null.
        // manejadorSesion: p. ej. GetHashCode() del objeto, solo valido durante la sesion.
        public string Resuelve(string claveEstable, string nombre, int manejadorSesion)
        {
            lock (cerrojo)
            {
                bool estable = !string.IsNullOrEmpty(claveEstable);
                string baseKey = estable ? "g:" + claveEstable : "n:" + nombre;
                string key = baseKey;
                if (!estable)
                {
                    HashSet<int> hs;
                    if (!manejadores.TryGetValue(baseKey, out hs)) { hs = new HashSet<int>(); manejadores[baseKey] = hs; }
                    hs.Add(manejadorSesion);
                    if (hs.Count > 1)
                    {
                        // Mismo nombre, objetos distintos: se separan por orden de aparicion.
                        var orden = new List<int>(hs); orden.Sort();
                        int idx = orden.IndexOf(manejadorSesion);
                        if (idx > 0) key = baseKey + "#" + (idx + 1);
                        ambiguos.Add(baseKey);
                    }
                }
                if (estable && comprobados.Add(key))
                {
                    string previa;
                    if (claveDeNombrePrevio.TryGetValue(nombre, out previa)) { if (previa == key) ClavesCoinciden++; else ClavesDiscrepan++; }
                }
                string id;
                if (!claveAId.TryGetValue(key, out id))
                {
                    id = "p" + FichaGen.Hash(key).ToString("x8");
                    // Colision de hash (rarisima): se alarga hasta ser unica.
                    while (claveAId.ContainsValue(id)) id += "x";
                    claveAId[key] = id; nombres[id] = nombre;
                    disco.Append(FICHERO, "{\"k\":\"" + Json.Escape(key) + "\",\"id\":\"" + id + "\",\"n\":\"" + Json.Escape(nombre) + "\"}");
                }
                return id;
            }
        }

        public string Nombre(string id)
        {
            lock (cerrojo) { string n; return nombres.TryGetValue(id, out n) ? n : id; }
        }
    }

    // Fichas en disco (ultima linea por id gana).
    public sealed class AlmacenFichas
    {
        const string FICHERO = "fichas.jsonl";
        readonly IStorage disco;
        readonly Dictionary<string, Ficha> fichas = new Dictionary<string, Ficha>();
        readonly object cerrojo = new object();

        public AlmacenFichas(IStorage disco)
        {
            this.disco = disco;
            foreach (var l in disco.ReadLines(FICHERO))
            {
                var f = Ficha.FromJson(l);
                if (f != null) fichas[f.Id] = f;
            }
        }

        public int Count { get { lock (cerrojo) { return fichas.Count; } } }

        public Ficha Get(string id) { lock (cerrojo) { Ficha f; return fichas.TryGetValue(id, out f) ? f : null; } }

        public Ficha GetOCrea(string id, string nombre)
        {
            lock (cerrojo)
            {
                Ficha f;
                if (fichas.TryGetValue(id, out f)) return f;
                f = FichaGen.Determinista(id, nombre);
                Guarda(f);
                return f;
            }
        }

        public void Pon(Ficha f) { lock (cerrojo) { Guarda(f); } }

        void Guarda(Ficha f) { fichas[f.Id] = f; disco.Append(FICHERO, f.ToJson()); }

        public List<Ficha> Todas() { lock (cerrojo) { return new List<Ficha>(fichas.Values); } }
    }
}
