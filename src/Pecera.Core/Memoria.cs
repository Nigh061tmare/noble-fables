using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    public sealed class Episodio
    {
        public long T;            // marca de tiempo (ticks del reloj inyectado)
        public string Tipo = "";  // opinion | rumor | favor | trauma | ...
        public string Con = "";   // id del otro pawn, si lo hay
        public string Texto = "";
        public double Peso = 1;   // importancia (|delta|)
    }

    // Memoria episodica por id de pawn con tres niveles:
    //   recientes (episodios crudos)  ->  medio (resumenes)  ->  largo (un parrafo)
    // El resumen lo redacta el LLM, pero la memoria NUNCA depende de el: si no hay LLM,
    // la poda determinista mantiene el tamano acotado. Persistencia: registro de
    // operaciones (memoria.jsonl) que se repite al arrancar y se compacta al crecer.
    public sealed class Memoria
    {
        const string FICHERO = "memoria.jsonl";
        const int VERSION = 1;

        sealed class Pawn
        {
            public List<Episodio> Recientes = new List<Episodio>();
            public List<string> Medio = new List<string>();
            public string Largo = "";
            public double Acumulada;      // importancia acumulada desde la ultima reflexion (no se persiste)
        }

        readonly IStorage disco;
        readonly IClock reloj;
        readonly Dictionary<string, Pawn> por = new Dictionary<string, Pawn>();
        readonly object cerrojo = new object();
        int opsDesdeCompactar;
        long ultimoT;   // T estrictamente creciente: AplicaSum corta por T y no puede confundir episodios

        public int MaxRecientes = 6;     // al pasarse se pide resumen
        public int MantenerCrudos = 3;   // tras resumir, estos quedan sin resumir
        public int MaxMedio = 4;         // al pasarse se pide fusion en "largo"
        public int MaxLargo = 400;       // caracteres
        public int CompactarCada = 400;

        public Memoria(IStorage disco, IClock reloj)
        {
            this.disco = disco; this.reloj = reloj;
            Carga();
        }

        public int Pawns { get { lock (cerrojo) { return por.Count; } } }

        public int Recientes(string id) { lock (cerrojo) { Pawn p; return por.TryGetValue(id, out p) ? p.Recientes.Count : 0; } }
        public int NivelesMedio(string id) { lock (cerrojo) { Pawn p; return por.TryGetValue(id, out p) ? p.Medio.Count : 0; } }
        public string Largo(string id) { lock (cerrojo) { Pawn p; return por.TryGetValue(id, out p) ? p.Largo : ""; } }

        public void Registra(string id, string tipo, string con, string texto, double peso)
        {
            if (string.IsNullOrEmpty(id)) return;
            lock (cerrojo)
            {
                ultimoT = Math.Max(reloj.NowTicks, ultimoT + 1);
                var e = new Episodio { T = ultimoT, Tipo = tipo ?? "", Con = con ?? "", Texto = Corta(texto, 160), Peso = peso };
                Aplica(id, e);
                disco.Append(FICHERO, "{\"op\":\"ep\",\"id\":\"" + Json.Escape(id) + "\",\"t\":" + e.T + ",\"k\":\"" + Json.Escape(e.Tipo)
                    + "\",\"con\":\"" + Json.Escape(e.Con) + "\",\"tx\":\"" + Json.Escape(e.Texto) + "\",\"w\":" + Json.Num(peso) + "}");
                Poda(id);
                TocaCompactar();
            }
        }

        // Texto de memoria para el prompt: largo + ultimo medio + recientes, acotado.
        public string ParaPrompt(string id, int maxChars)
        {
            lock (cerrojo)
            {
                Pawn p;
                if (!por.TryGetValue(id, out p)) return "";
                var partes = new List<string>();
                if (p.Largo.Length > 0) partes.Add(p.Largo);
                if (p.Medio.Count > 0) partes.Add(p.Medio[p.Medio.Count - 1]);
                int desde = Math.Max(0, p.Recientes.Count - 3);
                for (int i = desde; i < p.Recientes.Count; i++) partes.Add(p.Recientes[i].Texto);
                return Corta(string.Join("; ", partes.ToArray()), maxChars);
            }
        }

        public IList<string> TextosRecientes(string id)
        {
            lock (cerrojo)
            {
                var r = new List<string>();
                Pawn p;
                if (por.TryGetValue(id, out p)) foreach (var e in p.Recientes) r.Add(e.Texto);
                return r;
            }
        }

        // ---- recuperacion (Generative Agents, Park et al. 2023) ----
        // Puntua cada recuerdo con recencia (0.5), relevancia (3) e importancia (2), cada componente normalizado a [0,1]
        // (min-max; si todos son iguales, 0.5), como en reverie/backend_server/persona/cognitive_modules/retrieve.py.
        // La relevancia usa solapamiento de palabras (no hay embeddings aun; /api/embeddings de Ollama es el siguiente paso).
        // Incluye los resumenes medio y largo como recuerdos de importancia alta.
        public List<string> Recupera(string id, string consulta, int n)
        {
            lock (cerrojo)
            {
                Pawn p;
                var res = new List<string>();
                if (!por.TryGetValue(id, out p)) return res;
                var items = new List<KeyValuePair<string, double[]>>();   // texto -> {recencia, relevancia, importancia}
                var q = Palabras(consulta);
                int total = p.Recientes.Count + p.Medio.Count + (p.Largo.Length > 0 ? 1 : 0);
                int k = 0;
                if (p.Largo.Length > 0) items.Add(Item(p.Largo, k++, total, q, 6, DecaimientoRecencia));
                foreach (var m in p.Medio) items.Add(Item(m, k++, total, q, 4, DecaimientoRecencia));
                foreach (var e in p.Recientes) items.Add(Item(e.Texto, k++, total, q, e.Peso, DecaimientoRecencia));
                if (items.Count == 0) return res;
                var rec = Norm(items, 0); var rel = Norm(items, 1); var imp = Norm(items, 2);
                var punt = new List<KeyValuePair<int, double>>();
                for (int i = 0; i < items.Count; i++) punt.Add(new KeyValuePair<int, double>(i, 0.5 * rec[i] + 3 * rel[i] + 2 * imp[i]));
                punt.Sort((a, b) => { int c = b.Value.CompareTo(a.Value); return c != 0 ? c : b.Key.CompareTo(a.Key); });   // empate: el mas reciente
                for (int i = 0; i < punt.Count && res.Count < n; i++) res.Add(items[punt[i].Key].Key);
                return res;
            }
        }

        public double DecaimientoRecencia = 0.97;

        static KeyValuePair<string, double[]> Item(string texto, int pos, int total, HashSet<string> q, double peso, double decay)
        {
            double rec = Math.Pow(decay, total - 1 - pos);                 // el mas nuevo ~1
            double rel = 0;
            if (q.Count > 0)
            {
                var w = Palabras(texto); int comunes = 0; foreach (var x in w) if (q.Contains(x)) comunes++;
                rel = w.Count == 0 ? 0 : (double)comunes / Math.Sqrt(w.Count * q.Count);
            }
            return new KeyValuePair<string, double[]>(texto, new[] { rec, rel, peso });
        }

        static double[] Norm(List<KeyValuePair<string, double[]>> items, int c)
        {
            double mn = double.MaxValue, mx = double.MinValue;
            foreach (var it in items) { mn = Math.Min(mn, it.Value[c]); mx = Math.Max(mx, it.Value[c]); }
            var r = new double[items.Count];
            for (int i = 0; i < r.Length; i++) r[i] = mx - mn < 1e-12 ? 0.5 : (items[i].Value[c] - mn) / (mx - mn);
            return r;
        }

        static HashSet<string> Palabras(string s)
        {
            var h = new HashSet<string>();
            if (string.IsNullOrEmpty(s)) return h;
            var sb = new StringBuilder();
            foreach (char c in s.ToLowerInvariant() + " ")
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else { if (sb.Length >= 4) h.Add(sb.ToString()); sb.Length = 0; }
            }
            return h;
        }

        // ---- reflexion por IMPORTANCIA ACUMULADA (Generative Agents): no se reflexiona "cada dia" sino cuando ha pasado
        // lo bastante importante desde la ultima vez. Se acumula en Registra() y se consulta/reinicia aqui.
        public double UmbralReflexion = 12;

        public double ImportanciaAcumulada(string id) { lock (cerrojo) { Pawn p; return por.TryGetValue(id, out p) ? p.Acumulada : 0; } }

        public bool ToqueReflexion(string id)
        {
            lock (cerrojo)
            {
                Pawn p;
                if (!por.TryGetValue(id, out p) || p.Acumulada < UmbralReflexion) return false;
                p.Acumulada = 0;
                return true;
            }
        }

        // Recuerdos recientes agrupados por la otra persona: (con, cuantos, peso total). Para extraer conclusiones.
        public List<KeyValuePair<string, double>> PesoPorPersona(string id)
        {
            lock (cerrojo)
            {
                var d = new Dictionary<string, double>(); Pawn p;
                if (!por.TryGetValue(id, out p)) return new List<KeyValuePair<string, double>>();
                foreach (var e in p.Recientes) if (e.Con.Length > 0 && e.Tipo != "reflexion") { double v; d.TryGetValue(e.Con, out v); d[e.Con] = v + e.Peso; }
                var l = new List<KeyValuePair<string, double>>(d);
                l.Sort((a, b) => { int c = b.Value.CompareTo(a.Value); return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key); });
                return l;
            }
        }

        // ---- resumen escalonado ----
        public bool NecesitaResumen(string id)
        {
            lock (cerrojo) { Pawn p; return por.TryGetValue(id, out p) && p.Recientes.Count > MaxRecientes; }
        }

        // Pawn con mas episodios por encima del tope (para repartir el cupo del LLM).
        public string SiguienteParaResumir()
        {
            lock (cerrojo)
            {
                string mejor = null; int exceso = 0;
                foreach (var kv in por)
                {
                    int x = kv.Value.Recientes.Count - MaxRecientes;
                    if (x > exceso) { exceso = x; mejor = kv.Key; }
                }
                return mejor;
            }
        }

        public sealed class PeticionResumen
        {
            public string Id, Previo;
            public IList<string> Textos;
            public long Hasta;
        }

        public PeticionResumen PreparaResumen(string id)
        {
            lock (cerrojo)
            {
                Pawn p;
                if (!por.TryGetValue(id, out p) || p.Recientes.Count <= MantenerCrudos) return null;
                int n = p.Recientes.Count - MantenerCrudos;
                var textos = new List<string>();
                for (int i = 0; i < n; i++) textos.Add(p.Recientes[i].Texto);
                return new PeticionResumen
                {
                    Id = id,
                    Previo = p.Medio.Count > 0 ? p.Medio[p.Medio.Count - 1] : p.Largo,
                    Textos = textos,
                    Hasta = p.Recientes[n - 1].T
                };
            }
        }

        // Aplica un resumen ya redactado. Si 'texto' esta vacio (LLM caido) usa el fallback.
        public void AplicaResumen(PeticionResumen q, string texto)
        {
            lock (cerrojo)
            {
                Pawn p;
                if (q == null || !por.TryGetValue(q.Id, out p)) return;
                if (string.IsNullOrEmpty(texto)) texto = Corta(string.Join("; ", new List<string>(q.Textos).ToArray()), 160);
                texto = Corta(Json.UnaLinea(texto), 240);
                AplicaSum(q.Id, texto, q.Hasta);
                disco.Append(FICHERO, "{\"op\":\"sum\",\"id\":\"" + Json.Escape(q.Id) + "\",\"tx\":\"" + Json.Escape(texto) + "\",\"hasta\":" + q.Hasta + "}");
                TocaCompactar();
            }
        }

        public bool NecesitaFusion(string id)
        {
            lock (cerrojo) { Pawn p; return por.TryGetValue(id, out p) && p.Medio.Count > MaxMedio; }
        }

        public string SiguienteParaFusionar()
        {
            lock (cerrojo)
            {
                foreach (var kv in por) if (kv.Value.Medio.Count > MaxMedio) return kv.Key;
                return null;
            }
        }

        public string TextoParaFusion(string id)
        {
            lock (cerrojo)
            {
                Pawn p;
                if (!por.TryGetValue(id, out p)) return "";
                return (p.Largo.Length > 0 ? p.Largo + " " : "") + string.Join(" ", p.Medio.ToArray());
            }
        }

        public void AplicaFusion(string id, string texto)
        {
            lock (cerrojo)
            {
                Pawn p;
                if (!por.TryGetValue(id, out p)) return;
                if (string.IsNullOrEmpty(texto)) texto = TextoParaFusionSinLock(p);
                texto = Corta(Json.UnaLinea(texto), MaxLargo);
                AplicaLar(id, texto);
                disco.Append(FICHERO, "{\"op\":\"lar\",\"id\":\"" + Json.Escape(id) + "\",\"tx\":\"" + Json.Escape(texto) + "\"}");
                TocaCompactar();
            }
        }

        static string TextoParaFusionSinLock(Pawn p)
        {
            return (p.Largo.Length > 0 ? p.Largo + " " : "") + string.Join(" ", p.Medio.ToArray());
        }

        // ---- internos (siempre bajo cerrojo) ----
        Pawn Get(string id)
        {
            Pawn p;
            if (!por.TryGetValue(id, out p)) { p = new Pawn(); por[id] = p; }
            return p;
        }

        void Aplica(string id, Episodio e) { var p = Get(id); p.Recientes.Add(e); if (e.T > ultimoT) ultimoT = e.T; if (e.Tipo != "reflexion") p.Acumulada += e.Peso; }

        void AplicaSum(string id, string texto, long hasta)
        {
            var p = Get(id);
            p.Recientes.RemoveAll(x => x.T <= hasta);
            p.Medio.Add(texto);
        }

        void AplicaLar(string id, string texto)
        {
            var p = Get(id);
            p.Largo = texto;
            p.Medio.Clear();
        }

        // Poda dura: sin LLM la memoria sigue acotada (2x tope de recientes) descartando
        // lo menos importante; nunca crece sin limite.
        void Poda(string id)
        {
            var p = Get(id);
            int tope = MaxRecientes * 2;
            while (p.Recientes.Count > tope)
            {
                int peor = 0;
                for (int i = 1; i < p.Recientes.Count - MantenerCrudos; i++)
                    if (p.Recientes[i].Peso < p.Recientes[peor].Peso) peor = i;
                p.Recientes.RemoveAt(peor);
            }
            while (p.Medio.Count > MaxMedio * 2) p.Medio.RemoveAt(0);
        }

        void TocaCompactar()
        {
            if (++opsDesdeCompactar < CompactarCada) return;
            Compacta();
        }

        public void Compacta()
        {
            lock (cerrojo)
            {
                opsDesdeCompactar = 0;
                var l = new List<string>();
                l.Add("{\"op\":\"v\",\"v\":" + VERSION + "}");
                foreach (var kv in por)
                {
                    string id = Json.Escape(kv.Key);
                    if (kv.Value.Largo.Length > 0)
                        l.Add("{\"op\":\"lar\",\"id\":\"" + id + "\",\"tx\":\"" + Json.Escape(kv.Value.Largo) + "\"}");
                    foreach (var m in kv.Value.Medio)
                        l.Add("{\"op\":\"med\",\"id\":\"" + id + "\",\"tx\":\"" + Json.Escape(m) + "\"}");
                    foreach (var e in kv.Value.Recientes)
                        l.Add("{\"op\":\"ep\",\"id\":\"" + id + "\",\"t\":" + e.T + ",\"k\":\"" + Json.Escape(e.Tipo) + "\",\"con\":\"" + Json.Escape(e.Con)
                            + "\",\"tx\":\"" + Json.Escape(e.Texto) + "\",\"w\":" + Json.Num(e.Peso) + "}");
                }
                disco.Rewrite(FICHERO, l);
            }
        }

        public int LineasCorruptas { get; private set; }

        void Carga()
        {
            foreach (var l in disco.ReadLines(FICHERO))
            {
                object d;
                if (!Json.TryParse(l, out d)) { LineasCorruptas++; continue; }   // p. ej. cierre a mitad de escritura
                string op = Json.Str(d, "op"), id = Json.Str(d, "id");
                if (op == "v") continue;
                if (id.Length == 0) { LineasCorruptas++; continue; }
                if (op == "ep")
                    Aplica(id, new Episodio { T = (long)Json.Num(d, "t", 0), Tipo = Json.Str(d, "k"), Con = Json.Str(d, "con"), Texto = Json.Str(d, "tx"), Peso = Json.Num(d, "w", 1) });
                else if (op == "sum") AplicaSum(id, Json.Str(d, "tx"), (long)Json.Num(d, "hasta", 0));
                else if (op == "med") Get(id).Medio.Add(Json.Str(d, "tx"));
                else if (op == "lar") AplicaLar(id, Json.Str(d, "tx"));
                else LineasCorruptas++;
            }
        }

        static string Corta(string s, int n)
        {
            if (s == null) return "";
            s = Json.UnaLinea(s);
            return s.Length <= n ? s : s.Substring(0, n);
        }
    }
}
