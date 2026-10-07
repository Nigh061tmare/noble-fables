using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    // Parentesco minimo: padres, conyuge, hijos. El adaptador lo alimentara cuando se lea la
    // API de familia del juego (firma pendiente); aqui se prueba con datos sinteticos.
    public sealed class Linaje
    {
        readonly Dictionary<string, List<string>> hijos = new Dictionary<string, List<string>>();
        readonly Dictionary<string, string> conyuge = new Dictionary<string, string>();
        readonly Dictionary<string, int> nacimiento = new Dictionary<string, int>();
        readonly HashSet<string> vivos = new HashSet<string>();

        public void Nace(string id, int dia, string padre, string madre)
        {
            vivos.Add(id); nacimiento[id] = dia;
            foreach (var p in new[] { padre, madre }) if (!string.IsNullOrEmpty(p)) { List<string> l; if (!hijos.TryGetValue(p, out l)) { l = new List<string>(); hijos[p] = l; } if (!l.Contains(id)) l.Add(id); }
        }

        public void Casa(string a, string b) { conyuge[a] = b; conyuge[b] = a; }
        public void Muere(string id) { vivos.Remove(id); }
        public bool Vivo(string id) { return vivos.Contains(id); }
        public string Conyuge(string id) { string c; return conyuge.TryGetValue(id, out c) && vivos.Contains(c) ? c : null; }

        public List<string> HijosVivos(string id)
        {
            var r = new List<string>(); List<string> l;
            if (hijos.TryGetValue(id, out l)) foreach (var h in l) if (vivos.Contains(h)) r.Add(h);
            r.Sort((x, y) => { int c = nacimiento[x].CompareTo(nacimiento[y]); return c != 0 ? c : string.CompareOrdinal(x, y); });
            return r;
        }

        // Orden legal de herederos: hijos vivos por edad; si no hay, conyuge; si no, nadie.
        public List<string> Herederos(string difunto)
        {
            var r = HijosVivos(difunto);
            if (r.Count == 0) { var c = Conyuge(difunto); if (c != null) r.Add(c); }
            return r;
        }

        // Reparto de bienes enteros SIN perder ni crear nada: el resto va a los primeros por orden.
        // El conyuge superviviente recibe 1/3 si hay hijos.
        public static Dictionary<string, int> Reparte(int bienes, IList<string> hijosVivos, string conyuge)
        {
            var r = new Dictionary<string, int>();
            if (bienes < 0) bienes = 0;
            int restante = bienes;
            if (conyuge != null && hijosVivos.Count > 0) { int c = bienes / 3; r[conyuge] = c; restante -= c; }
            else if (conyuge != null) { r[conyuge] = bienes; return r; }
            if (hijosVivos.Count == 0) return r;
            int cuota = restante / hijosVivos.Count, resto = restante - cuota * hijosVivos.Count;
            for (int i = 0; i < hijosVivos.Count; i++) r[hijosVivos[i]] = cuota + (i < resto ? 1 : 0);
            return r;
        }
    }

    public sealed class ResultadoSucesion
    {
        public string Sucesor = "";
        public bool Disputada;
        public string Rival = "";
        public double Margen;
        public string Razon = "";
    }

    // Sucesion del titular (soberano / lider de faccion). Puntua a los candidatos con derecho
    // legal, carisma, ambicion y estima del reino. Si el margen es pequeño hay DISPUTA: se
    // anotan eventos de competencia y los partidarios se reparten (el modelo lo refleja).
    public static class Sucesion
    {
        public static ResultadoSucesion Elige(string titular, Linaje linaje, IList<string> reino, ModeloAfectivo m, Func<string, Ficha> fichas, double margenDisputa)
        {
            var legales = linaje.Herederos(titular);
            var cand = new List<string>(legales);
            // Si no hay herederos legales, cualquiera del reino puede aspirar (no el difunto).
            if (cand.Count == 0) foreach (var id in reino) if (id != titular && linaje.Vivo(id)) cand.Add(id);
            var res = new ResultadoSucesion();
            if (cand.Count == 0) { res.Razon = "sin candidatos"; return res; }
            var puntos = new List<KeyValuePair<string, double>>();
            foreach (var c in cand)
            {
                double estima = 0; int n = 0;
                foreach (var o in reino) if (o != c && linaje.Vivo(o)) { estima += m.Sentimiento(o, c); n++; }
                estima = n > 0 ? (estima / n + 1) / 2 : 0.5;
                Ficha f = fichas(c);
                double carisma = f != null ? f.Carisma : 0.5, ambicion = f != null ? f.Ambicion : 0.5;
                double derecho = legales.Contains(c) ? 1.0 - 0.1 * legales.IndexOf(c) : 0.2;
                puntos.Add(new KeyValuePair<string, double>(c, 0.35 * derecho + 0.25 * estima + 0.2 * carisma + 0.2 * ambicion));
            }
            puntos.Sort((x, y) => { int d = y.Value.CompareTo(x.Value); return d != 0 ? d : string.CompareOrdinal(x.Key, y.Key); });
            res.Sucesor = puntos[0].Key;
            if (puntos.Count > 1)
            {
                res.Rival = puntos[1].Key; res.Margen = puntos[0].Value - puntos[1].Value;
                res.Disputada = res.Margen < margenDisputa;
                if (res.Disputada)
                {
                    m.Evento(res.Sucesor, res.Rival, TipoEvento.Competencia, 0.8);
                    m.Evento(res.Rival, res.Sucesor, TipoEvento.Competencia, 0.8);
                    m.Evento(res.Rival, res.Sucesor, TipoEvento.Agravio, 0.3);
                }
            }
            res.Razon = "puntos " + Json.Num(puntos[0].Value) + (puntos.Count > 1 ? " frente a " + Json.Num(puntos[1].Value) : "") + (res.Disputada ? " (DISPUTA)" : "");
            return res;
        }
    }

    // Mentoria: empareja aprendices con quien sabe mas y les cae bien. Las habilidades las
    // aportara el adaptador (pendiente de leer del binario); aqui son un diccionario.
    public sealed class Mentoria
    {
        public sealed class Lazo { public string Mentor = "", Aprendiz = "", Habilidad = ""; public double Progreso; }

        readonly List<Lazo> lazos = new List<Lazo>();
        public int MaxAprendicesPorMentor = 2;
        public double Ritmo = 0.02;   // fraccion de la brecha que se cierra por dia
        public IList<Lazo> Lazos { get { return lazos; } }

        public void Empareja(IList<string> ids, Func<string, string, double> nivel, string[] habilidades, ModeloAfectivo m, double brechaMinima)
        {
            var ocupados = new HashSet<string>(); foreach (var l in lazos) ocupados.Add(l.Aprendiz);
            foreach (var h in habilidades)
            {
                var orden = new List<string>(ids); orden.Sort(StringComparer.Ordinal);
                foreach (var ap in orden)
                {
                    if (ocupados.Contains(ap)) continue;
                    string mejor = null; double mp = double.NegativeInfinity;
                    foreach (var me in orden)
                    {
                        if (me == ap) continue;
                        int carga = 0; foreach (var l in lazos) if (l.Mentor == me) carga++;
                        if (carga >= MaxAprendicesPorMentor) continue;
                        double brecha = nivel(me, h) - nivel(ap, h);
                        if (brecha < brechaMinima) continue;
                        if (m.Sentimiento(ap, me) < 0 || m.Sentimiento(me, ap) < -0.1) continue;   // sin simpatia no hay maestro
                        double p = brecha + 0.5 * m.Sentimiento(ap, me);
                        if (p > mp + 1e-12) { mp = p; mejor = me; }
                    }
                    if (mejor != null) { lazos.Add(new Lazo { Mentor = mejor, Aprendiz = ap, Habilidad = h }); ocupados.Add(ap); }
                }
            }
        }

        // Devuelve el nivel NUEVO del aprendiz para que lo aplique el adaptador/mundo. Nunca alcanza al mentor.
        public double Avanza(Lazo l, double nivelMentor, double nivelAprendiz, double dias, ModeloAfectivo m)
        {
            double brecha = nivelMentor - nivelAprendiz;
            if (brecha <= 0.01) { l.Progreso = 1; return nivelAprendiz; }
            double nuevo = nivelAprendiz + brecha * (1 - Math.Exp(-Ritmo * dias)) * 0.98;
            m.Evento(l.Aprendiz, l.Mentor, TipoEvento.Ayuda, 0.02 * dias);
            m.Evento(l.Mentor, l.Aprendiz, TipoEvento.Aprecio, 0.01 * dias);
            l.Progreso = Math.Min(1, (nuevo - 0) / Math.Max(1e-9, nivelMentor));
            return nuevo;
        }

        public void Termina(IList<string> ids, Func<string, string, double> nivel)
        {
            lazos.RemoveAll(l => nivel(l.Mentor, l.Habilidad) - nivel(l.Aprendiz, l.Habilidad) <= 0.01);
        }
    }
}
