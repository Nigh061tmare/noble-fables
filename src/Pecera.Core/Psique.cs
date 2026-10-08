using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    // ======================================================================================================
    //  RECUERDOS FUERTES (Dwarf Fortress): 8 huecos de corto plazo y 8 de largo plazo por pawn; por categoria solo el mas
    //  intenso ocupa hueco. Tras un tiempo en corto plazo, un recuerdo pasa a largo plazo si es mas fuerte que el mas debil de
    //  alli; si no, se olvida. Los de largo plazo VUELVEN periodicamente (se «revive» el recuerdo) y mueven el animo y el
    //  estres; los traumas fuertes que se reviven cambian poco a poco la personalidad (acotado).
    // ======================================================================================================
    public sealed class RecuerdoFuerte
    {
        public string Tipo = "", Texto = "";
        public double Valencia;      // -1..1
        public double Intensidad;    // 0..10
        public int Dia;
        public bool Largo;
    }

    public sealed class RecuerdosFuertes
    {
        public const int Huecos = 8;
        public int DiasPromocion = 60;        // DF usa un anio; aqui el anio de juego es mas corto
        readonly Dictionary<string, List<RecuerdoFuerte>> por = new Dictionary<string, List<RecuerdoFuerte>>();

        public IList<RecuerdoFuerte> De(string id) { List<RecuerdoFuerte> l; return por.TryGetValue(id, out l) ? l : new List<RecuerdoFuerte>(); }

        List<RecuerdoFuerte> Get(string id) { List<RecuerdoFuerte> l; if (!por.TryGetValue(id, out l)) { l = new List<RecuerdoFuerte>(); por[id] = l; } return l; }

        public void Anota(string id, string tipo, string texto, double valencia, double intensidad, int dia)
        {
            if (intensidad < 3) return;                                   // lo trivial no deja huella
            var l = Get(id);
            var nuevo = new RecuerdoFuerte { Tipo = tipo, Texto = Json.UnaLinea(texto), Valencia = Math.Max(-1, Math.Min(1, valencia)), Intensidad = Math.Min(10, intensidad), Dia = dia };
            var mismo = l.Find(x => !x.Largo && x.Tipo == tipo);
            if (mismo != null) { if (mismo.Intensidad < nuevo.Intensidad) { l.Remove(mismo); l.Add(nuevo); } return; }
            var cortos = l.FindAll(x => !x.Largo);
            if (cortos.Count >= Huecos)
            {
                RecuerdoFuerte debil = cortos[0]; foreach (var x in cortos) if (x.Intensidad < debil.Intensidad) debil = x;
                if (debil.Intensidad >= nuevo.Intensidad) return;
                l.Remove(debil);
            }
            l.Add(nuevo);
        }

        // Pasa a largo plazo lo que lleva DiasPromocion en corto y es mas fuerte que lo mas debil de largo plazo.
        public void Promueve(string id, int dia)
        {
            var l = Get(id);
            foreach (var r in l.FindAll(x => !x.Largo && dia - x.Dia >= DiasPromocion))
            {
                l.Remove(r);
                var largos = l.FindAll(x => x.Largo);
                var mismo = largos.Find(x => x.Tipo == r.Tipo);
                if (mismo != null) { if (mismo.Intensidad < r.Intensidad) { l.Remove(mismo); r.Largo = true; l.Add(r); } continue; }
                if (largos.Count >= Huecos)
                {
                    RecuerdoFuerte debil = largos[0]; foreach (var x in largos) if (x.Intensidad < debil.Intensidad) debil = x;
                    if (debil.Intensidad >= r.Intensidad) continue;
                    l.Remove(debil);
                }
                r.Largo = true; l.Add(r);
            }
        }

        // Revive un recuerdo de largo plazo (el mas intenso que no se revivio hace poco). Devuelve el efecto en estres (+ malo, - bueno)
        // y aplica la deriva de personalidad. El tiempo cura: cada vez que se revive, un recuerdo negativo pierde un 10 % de intensidad.
        public double Revive(Persona p, int dia, out string texto)
        {
            texto = "";
            var largos = Get(p.Id).FindAll(x => x.Largo);
            if (largos.Count == 0) return 0;
            largos.Sort((a, b) => { int c = b.Intensidad.CompareTo(a.Intensidad); return c != 0 ? c : a.Dia.CompareTo(b.Dia); });
            var r = largos[(dia / 7) % largos.Count];        // rotacion determinista: no siempre el mismo
            double efecto = -r.Valencia * r.Intensidad * 3;  // un trauma de 10 suma 30 de estres; un gran recuerdo feliz resta 30
            texto = p.Nombre + (r.Valencia < 0 ? " no puede dejar de pensar en que " : " recuerda con cariño que ") + r.Texto;
            if (r.Valencia < 0) { r.Intensidad *= 0.9; if (r.Intensidad >= 7) Psique.DerivaNeuroticismo(p, +0.01); }
            else { r.Intensidad *= 0.97; if (r.Intensidad >= 7) Psique.DerivaAmabilidad(p, +0.005); }
            return efecto;
        }

        public IEnumerable<string> Serializa()
        {
            var ids = new List<string>(por.Keys); ids.Sort(StringComparer.Ordinal);
            foreach (var id in ids)
                foreach (var r in por[id])
                    yield return "{\"k\":\"rf\",\"id\":\"" + Json.Escape(id) + "\",\"t\":\"" + Json.Escape(r.Tipo) + "\",\"tx\":\"" + Json.Escape(r.Texto) + "\",\"v\":" + Json.Num(r.Valencia)
                        + ",\"i\":" + Json.Num(r.Intensidad) + ",\"d\":" + r.Dia + ",\"l\":" + (r.Largo ? "true" : "false") + "}";
        }

        public void Carga(object d)
        {
            string id = Json.Str(d, "id"); if (id.Length == 0) return;
            Get(id).Add(new RecuerdoFuerte { Tipo = Json.Str(d, "t"), Texto = Json.Str(d, "tx"), Valencia = Json.Num(d, "v", 0), Intensidad = Json.Num(d, "i", 0), Dia = (int)Json.Num(d, "d", 0), Largo = Json.Str(d, "l") == "true" });
        }
    }

    // ======================================================================================================
    //  ESTRES (Crusader Kings 3): actuar CONTRA tu personalidad estresa; el estres sube por niveles y al cruzar uno hay crisis
    //  (un mecanismo de afrontamiento elegido por el caracter) que libera parte. Los estresados evitan aun mas lo que va contra
    //  su naturaleza. Niveles: 0 (<100), 1 (100-199), 2 (200-299), 3 (>=300). La disipacion diaria depende del neuroticismo
    //  (Dwarf Fortress: la ansiedad fija lo deprisa que se disipa).
    // ======================================================================================================
    public static class Estres
    {
        public static int Nivel(double e) { return e >= 300 ? 3 : e >= 200 ? 2 : e >= 100 ? 1 : 0; }

        // Coste (o alivio, si es negativo) de hacer una intencion para ESTA persona.
        public static double Coste(Persona p, TipoIntencion t)
        {
            switch (t)
            {
                case TipoIntencion.Vengarse: return 70 * p.Amabilidad * p.Amabilidad;
                case TipoIntencion.Intrigar: return 45 * p.Amabilidad * p.Amabilidad + 15 * p.Escrupulosidad;
                case TipoIntencion.Charlar: case TipoIntencion.Visitar: return 18 * Sq(1 - p.Extroversion) - 8 * p.Extroversion;
                case TipoIntencion.Celebrar: return 25 * Sq(1 - p.Extroversion) - 15 * p.Extroversion;
                case TipoIntencion.Pedir: return 20 * Sq(1 - p.Extroversion);
                case TipoIntencion.Trabajar: return 20 * Sq(1 - p.Escrupulosidad) - 5 * p.Escrupulosidad;
                case TipoIntencion.Aprender: return 15 * Sq(1 - p.Apertura) - 6 * p.Apertura;
                case TipoIntencion.Consolar: return 15 * Sq(1 - p.Amabilidad) - 6 * p.Amabilidad;
                case TipoIntencion.Cortejar: return 15 * Sq(1 - p.Extroversion);
                case TipoIntencion.Descansar: return -20;
                default: return 0;
            }
        }

        static double Sq(double x) { return x * x; }

        // Multiplicador de prioridad: lo incongruente pierde peso, mas cuanto mas estresado esta.
        public static double Factor(Persona p, TipoIntencion t)
        {
            double c = Coste(p, t);
            if (c <= 0) return 1 + Math.Min(0.3, -c / 100 * (1 + Nivel(p.Estres)));    // lo que alivia atrae mas al estresado
            return Math.Max(0.25, 1 - Math.Min(0.75, c / 100 * (1 + Nivel(p.Estres))));
        }

        // CK3 pierde el estres despacio: aqui 1..3 por dia segun neuroticismo (360..1100 al ano).
        public static double Disipacion(Persona p) { return 1 + 2 * (1 - p.Neuroticismo); }

        // Estres por lo que le PASA (duelo, heridas, fracasos), no por lo que elige. No dispara la crisis en el acto: la detecta
        // Psique.Dia comparando el nivel con el ultimo visto, para que un suceso a mitad de un bucle no rompa a nadie fuera del dia.
        public static void Sufre(Persona p, double cantidad)
        {
            if (p == null || cantidad <= 0) return;
            p.Estres = Math.Min(400, p.Estres + cantidad * (0.6 + 0.8 * p.Neuroticismo));
        }

        // Coste de fracasar en algo: el que se lo toma a pecho (neuroticismo) y lo que le importaba (escrupulosidad).
        public static double CosteFracaso(Persona p) { return 1 + 5 * p.Neuroticismo * (0.5 + p.Escrupulosidad); }

        // Aplica coste o alivio; devuelve la ruptura (crisis) si se cruza un nivel hacia arriba.
        public static Ruptura Suma(Persona p, double cantidad)
        {
            int antes = Math.Max(Nivel(p.Estres), p.NivelEstres);
            p.Estres = Math.Max(0, Math.Min(400, p.Estres + cantidad));
            int despues = Nivel(p.Estres);
            if (despues <= antes) { p.NivelEstres = Math.Min(p.NivelEstres, despues); return Ruptura.Ninguna; }
            return Crisis(p, despues);
        }

        // Crisis al cruzar un nivel hacia arriba: el afrontamiento depende del caracter y libera estres (coping mechanisms de CK3).
        public static Ruptura Crisis(Persona p, int despues)
        {
            p.NivelEstres = despues;
            // Crisis: el afrontamiento depende del caracter. Libera estres (como los coping mechanisms de CK3).
            p.Estres = Math.Max(0, p.Estres - 80);
            p.NivelEstres = Nivel(p.Estres);
            if (despues >= 3) return Ruptura.Hundimiento;
            if ((1 - p.Amabilidad) + p.Neuroticismo > 1.05) return Ruptura.Arrebato;    // irascible: lo paga otro
            if (despues >= 2 && p.Neuroticismo >= 0.6) return Ruptura.Hundimiento;        // fragil: se viene abajo antes
            return Ruptura.Retiro;
        }
    }

    // ======================================================================================================
    //  EXPERIENCIA (Voyager: biblioteca de habilidades y curriculo automatico, version de reglas): cada pawn aprende que le sale
    //  bien. Exitos y fallos por tipo de intencion con prior Beta(1,1). El planificador multiplica la prioridad por 0.75..1.25 segun
    //  su tasa de exito y la siguiente ambicion se elige entre las coherentes con su caracter, favoreciendo lo que domina.
    // ======================================================================================================
    public sealed class Experiencia
    {
        readonly Dictionary<string, int[]> por = new Dictionary<string, int[]>();   // pawn|tipo -> {exitos, fallos}

        static string K(string id, TipoIntencion t) { return id + "|" + (int)t; }

        public void Anota(string id, TipoIntencion t, bool exito)
        {
            int[] v; if (!por.TryGetValue(K(id, t), out v)) { v = new int[2]; por[K(id, t)] = v; }
            if (exito) v[0]++; else v[1]++;
        }

        public double Tasa(string id, TipoIntencion t)
        {
            int[] v; if (!por.TryGetValue(K(id, t), out v)) return 0.5;
            return (v[0] + 1.0) / (v[0] + v[1] + 2.0);
        }

        public int Intentos(string id, TipoIntencion t) { int[] v; return por.TryGetValue(K(id, t), out v) ? v[0] + v[1] : 0; }

        public double Factor(string id, TipoIntencion t) { return 0.75 + 0.5 * Tasa(id, t); }

        // Curriculo: siguiente categoria de ambicion entre las candidatas, ponderando caracter y destreza.
        public string Siguiente(Persona p, IList<string> candidatas)
        {
            string mejor = candidatas[0]; double mv = double.NegativeInfinity;
            foreach (var c in candidatas)
            {
                double v;
                switch (c)
                {
                    case "aprender": v = p.Apertura + Tasa(p.Id, TipoIntencion.Aprender); break;
                    case "descubrir": v = p.Apertura + 0.5 * Tasa(p.Id, TipoIntencion.Pedir) + 0.5 * Tasa(p.Id, TipoIntencion.Aprender); break;
                    case "enriquecerse": v = p.Escrupulosidad + Tasa(p.Id, TipoIntencion.Trabajar); break;
                    case "mandar": v = p.Extroversion + (1 - p.Amabilidad) * 0.5 + Tasa(p.Id, TipoIntencion.Pedir); break;
                    case "proteger": v = p.Neuroticismo + p.Escrupulosidad * 0.5 + Tasa(p.Id, TipoIntencion.Pedir) * 0.5; break;
                    case "casarse": v = p.Extroversion + Tasa(p.Id, TipoIntencion.Cortejar); break;
                    default: v = p.Amabilidad + Tasa(p.Id, TipoIntencion.Celebrar); break;   // paz
                }
                if (v > mv + 1e-12) { mv = v; mejor = c; }
            }
            return mejor;
        }

        public IEnumerable<string> Serializa()
        {
            var ks = new List<string>(por.Keys); ks.Sort(StringComparer.Ordinal);
            foreach (var k in ks) yield return "{\"k\":\"ex\",\"c\":\"" + Json.Escape(k) + "\",\"e\":" + por[k][0] + ",\"f\":" + por[k][1] + "}";
        }

        public void Carga(object d)
        {
            string c = Json.Str(d, "c"); if (c.Length == 0) return;
            por[c] = new[] { (int)Json.Num(d, "e", 0), (int)Json.Num(d, "f", 0) };
        }
    }

    // ======================================================================================================
    //  ELECCION (The Sims): la utilidad «anuncia» opciones y el estado las pondera; para no parecer un robot, se elige al azar
    //  ENTRE las mejores con probabilidad creciente con su puntuacion (softmax con temperatura). Con temperatura 0 = siempre la mejor.
    // ======================================================================================================
    public static class Eleccion
    {
        public static Intencion Elige(IList<Intencion> opciones, Rng rng, double temperatura)
        {
            if (opciones == null || opciones.Count == 0) return null;
            if (temperatura <= 1e-9 || opciones.Count == 1) return opciones[0];
            double max = double.NegativeInfinity; foreach (var o in opciones) max = Math.Max(max, o.Prioridad);
            var w = new double[opciones.Count]; double suma = 0;
            for (int i = 0; i < w.Length; i++) { w[i] = Math.Exp((opciones[i].Prioridad - max) / temperatura); suma += w[i]; }
            double r = rng.Next() * suma;
            for (int i = 0; i < w.Length; i++) { r -= w[i]; if (r <= 0) return opciones[i]; }
            return opciones[opciones.Count - 1];
        }
    }

    // ======================================================================================================
    //  PREFERENCIAS DEL JUGADOR: el reino aprende de tus vetos. Cada decision de la Compuerta que vetas o dejas pasar actualiza una
    //  tasa de aceptacion por (clase, etiqueta). Lo que vetas a menudo se propone menos; lo que aceptas, igual o algo mas.
    // ======================================================================================================
    public sealed class Preferencias
    {
        readonly Dictionary<string, int[]> por = new Dictionary<string, int[]>();   // clave -> {aceptadas, vetadas}

        public static string Clave(string clase, string etiqueta) { return clase + ":" + (etiqueta ?? ""); }

        public void Registra(string clase, string etiqueta, bool vetada)
        {
            int[] v; string k = Clave(clase, etiqueta);
            if (!por.TryGetValue(k, out v)) { v = new int[2]; por[k] = v; }
            if (vetada) v[1]++; else v[0]++;
        }

        // 0.2..1.2. Sin datos = 1.
        public double Factor(string clase, string etiqueta)
        {
            int[] v;
            if (!por.TryGetValue(Clave(clase, etiqueta), out v)) return 1;
            double tasa = (v[0] + 1.0) / (v[0] + v[1] + 2.0);
            return Math.Max(0.2, Math.Min(1.2, 2 * tasa));
        }

        // Si el jugador lo ha vetado casi siempre (al menos 3 veces), ni se propone.
        public bool Silenciada(string clase, string etiqueta)
        {
            int[] v;
            return por.TryGetValue(Clave(clase, etiqueta), out v) && v[1] >= 3 && Factor(clase, etiqueta) < 0.45;
        }

        public IEnumerable<string> Serializa()
        {
            var ks = new List<string>(por.Keys); ks.Sort(StringComparer.Ordinal);
            foreach (var k in ks) yield return "{\"k\":\"pj\",\"c\":\"" + Json.Escape(k) + "\",\"a\":" + por[k][0] + ",\"v\":" + por[k][1] + "}";
        }

        public void Carga(object d)
        {
            string c = Json.Str(d, "c"); if (c.Length == 0) return;
            por[c] = new[] { (int)Json.Num(d, "a", 0), (int)Json.Num(d, "v", 0) };
        }

        public string Resumen()
        {
            var sb = new StringBuilder();
            var ks = new List<string>(por.Keys); ks.Sort(StringComparer.Ordinal);
            foreach (var k in ks) { if (sb.Length > 0) sb.Append(", "); sb.Append(k).Append(" (").Append(por[k][0]).Append(" si / ").Append(por[k][1]).Append(" veto)"); }
            return sb.ToString();
        }
    }

    public static class Psique
    {
        // Un dia de vida interior: el estres se disipa (segun neuroticismo), los recuerdos maduran y, cada ~10 dias (escalonado por
        // pawn), uno fuerte vuelve a la mente y suma o resta estres. Devuelve la crisis si la hay y el texto del recuerdo revivido.
        public static Ruptura Dia(Persona p, RecuerdosFuertes rf, int dia, out string revivido)
        {
            revivido = "";
            // lo sufrido desde ayer (Estres.Sufre) puede haber cruzado un nivel: la crisis llega hoy
            int nivel = Estres.Nivel(p.Estres);
            if (nivel > p.NivelEstres) return Estres.Crisis(p, nivel);
            p.Estres = Math.Max(0, p.Estres - Estres.Disipacion(p));
            p.NivelEstres = Math.Min(p.NivelEstres, Estres.Nivel(p.Estres));
            if (rf == null) return Ruptura.Ninguna;
            rf.Promueve(p.Id, dia);
            if ((dia + FichaGen.Hash(p.Id) % 10) % 10 != 0) return Ruptura.Ninguna;
            double efecto = rf.Revive(p, dia, out revivido);
            return Math.Abs(efecto) < 1e-9 ? Ruptura.Ninguna : Estres.Suma(p, efecto);
        }

        public const double DerivaMax = 0.2;

        public static void DerivaNeuroticismo(Persona p, double d) { p.Neuroticismo = Acota(p.Neuroticismo + d, p.BaseNeuroticismo); }
        public static void DerivaAmabilidad(Persona p, double d) { p.Amabilidad = Acota(p.Amabilidad + d, p.BaseAmabilidad); }

        static double Acota(double v, double baseV)
        {
            v = Math.Max(baseV - DerivaMax, Math.Min(baseV + DerivaMax, v));
            return Math.Max(0, Math.Min(1, v));
        }
    }
}
