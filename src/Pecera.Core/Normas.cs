using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    public sealed class Norma
    {
        public string Id = "", Texto = "";
        public int Desde;
        public double Apoyo;
    }

    // Normas del reino (Project Sid: los agentes adoptan y CAMBIAN reglas colectivas). Nadie las escribe: salen de los valores
    // culturales y del caracter de la gente, y se aprueban si hay apoyo suficiente ponderado por lideres. Tienen HISTERESIS
    // (se aprueban con >= 0.55 y se derogan con < 0.40) para que no parpadeen. Efecto: parametros del Planificador y la Agenda.
    //   paz_publica : las intrigas exigen mas rencor (+0.20) y se enfrian el doble.
    //   ojo_por_ojo : las intrigas exigen menos rencor (-0.10) y se enfrian a la mitad. Incompatible con paz_publica.
    //   hospitalidad: celebrar y consolar pesan mas (+0.10 de prioridad).
    public sealed class Normas
    {
        public const double Aprobar = 0.55, Derogar = 0.40, Margen = 0.10;
        public const int DiasMinimos = 90;       // una norma recien aprobada no se deroga antes (salvo apoyo < 0.30): las leyes no parpadean
        readonly List<Norma> activas = new List<Norma>();
        public IList<Norma> Activas { get { return activas; } }

        public bool Tiene(string id) { foreach (var n in activas) if (n.Id == id) return true; return false; }
        public double UmbralHostilExtra { get { return Tiene("paz_publica") ? 0.20 : Tiene("ojo_por_ojo") ? -0.10 : 0; } }
        public double FactorEnfriaHostil { get { return Tiene("paz_publica") ? 2.0 : Tiene("ojo_por_ojo") ? 0.5 : 1.0; } }
        public double BonoHospitalidad { get { return Tiene("hospitalidad") ? 0.10 : 0; } }

        // peso: influencia de cada pawn en la votacion (1 = ciudadano; lideres de faccion 3).
        public List<string> Evalua(int dia, Cultura cultura, IList<Persona> ps, Func<string, double> peso, int intrigasRecientes)
        {
            var cambios = new List<string>();
            double paz = Apoyo(ps, peso, p => p.Amabilidad), ojo = Apoyo(ps, peso, p => 1 - p.Amabilidad), hosp = Apoyo(ps, peso, p => p.Extroversion);
            var rep = cultura.Reparto();
            string dom = cultura.Dominante();
            // La cultura sesga el apoyo: una cultura clemente aprueba mas la paz; una vengativa, el ojo por ojo.
            paz += 0.15 * rep["clemencia"] + 0.1 * rep["comunidad"] + (intrigasRecientes >= 20 ? 0.1 : 0);
            ojo += 0.15 * rep["venganza"] + 0.1 * rep["honor"];
            hosp += 0.2 * rep["comunidad"];
            Decide("paz_publica", "Se declara la Paz Publica: quien intrigue tendra que justificarlo.", paz, dia, cambios, !(ojo > paz + Margen), dom);
            Decide("ojo_por_ojo", "Se acepta el Ojo por Ojo: las afrentas se pagan.", ojo, dia, cambios, !(paz > ojo + Margen), dom);
            Decide("hospitalidad", "Se proclama la Hospitalidad: toda mesa tiene un sitio mas.", hosp, dia, cambios, true, dom);
            return cambios;
        }

        void Decide(string id, string texto, double apoyo, int dia, List<string> cambios, bool permitida, string dom)
        {
            Norma n = activas.Find(x => x.Id == id);
            if (n != null)
            {
                n.Apoyo = apoyo;
                // incompatible con la contraria si esta gana por claro margen, o apoyo hundido
                bool vieja = dia - n.Desde >= DiasMinimos;
                if ((vieja && (apoyo < Derogar || !permitida)) || apoyo < 0.30) { activas.Remove(n); cambios.Add("Se deroga la norma " + id + " (apoyo " + Json.Num(apoyo) + ")."); }
                return;
            }
            if (permitida && apoyo >= Aprobar)
            {
                if (id == "paz_publica" && Tiene("ojo_por_ojo")) return;
                if (id == "ojo_por_ojo" && Tiene("paz_publica")) return;
                activas.Add(new Norma { Id = id, Texto = texto, Desde = dia, Apoyo = apoyo });
                cambios.Add(texto);
            }
        }

        static double Apoyo(IList<Persona> ps, Func<string, double> peso, Func<Persona, double> f)
        {
            double s = 0, w = 0;
            foreach (var p in ps) { double ww = Math.Max(0.1, peso(p.Id)); s += ww * f(p); w += ww; }
            return w > 0 ? s / w : 0;
        }
    }

    // Etapa de una relacion (para narrar y para el prompt del LLM).
    public static class Relaciones
    {
        public static string Etapa(ModeloAfectivo m, string a, string b)
        {
            Par ab = m.Get(a, b), ba = m.Get(b, a);
            if (ab.Romance >= 0.5 && ba.Romance >= 0.4 && ab.Afecto > 0) return "pareja";
            if (ab.Rencor >= 0.6) return "enemigo";
            if (ab.Rivalidad >= 0.4) return "rival";
            if (ab.Afecto >= 0.6 && ab.Confianza >= 0.7) return "mejor amigo";
            if (ab.Afecto >= 0.3) return "amigo";
            if (ab.Eventos >= 3) return "conocido";
            return "desconocido";
        }

        // «amigo: B; enemigo: C» para el prompt. Solo lo notable.
        public static string Resumen(ModeloAfectivo m, string yo, IList<string> vivos, Func<string, string> nombre)
        {
            string[] etapas = { "pareja", "mejor amigo", "enemigo", "rival", "amigo" };
            var vistos = new Dictionary<string, string>();
            foreach (var o in vivos)
            {
                if (o == yo) continue;
                string e = Etapa(m, yo, o);
                if (Array.IndexOf(etapas, e) < 0 || vistos.ContainsKey(e)) continue;
                vistos[e] = nombre(o);
            }
            var partes = new List<string>();
            foreach (var e in etapas) { string n; if (vistos.TryGetValue(e, out n)) partes.Add(e + ": " + n); }
            return string.Join("; ", partes.ToArray());
        }
    }

    // Freno de conversacion por pareja (el chatting_with_buffer de Generative Agents): la misma pareja no vuelve a hablar
    // hasta pasados N dias. Evita los bucles de charla A<->B.
    public sealed class FrenoConversacion
    {
        readonly Dictionary<string, int> ultimo = new Dictionary<string, int>();
        public int Dias = 2;
        static string K(string a, string b) { return string.CompareOrdinal(a, b) < 0 ? a + "\u001f" + b : b + "\u001f" + a; }
        public bool Puede(string a, string b, int dia) { int d; return !ultimo.TryGetValue(K(a, b), out d) || dia - d >= Dias; }
        public void Anota(string a, string b, int dia)
        {
            ultimo[K(a, b)] = dia;
            if (ultimo.Count > 5000) { var viejos = new List<string>(); foreach (var kv in ultimo) if (dia - kv.Value > Dias) viejos.Add(kv.Key); foreach (var k in viejos) ultimo.Remove(k); }
        }
    }

    public static class Roles
    {
        // Rol emergente a partir de lo que de verdad ha HECHO (no de lo que dice su ficha). Con < 10 intenciones: sin rol.
        public static string De(IDictionary<TipoIntencion, int> hechas)
        {
            int total = 0; foreach (var kv0 in hechas) if (kv0.Key != TipoIntencion.Descansar) total += kv0.Value;      // descansar no define un oficio
            if (total < 10) return "";
            string[] nombres = { "artesano", "erudito", "intrigante", "mediador", "cortesano", "peticionario" };
            double[] suma = new double[nombres.Length];
            foreach (var kv in hechas)
            {
                int g;
                switch (kv.Key)
                {
                    case TipoIntencion.Trabajar: g = 0; break;
                    case TipoIntencion.Aprender: g = 1; break;
                    case TipoIntencion.Intrigar: case TipoIntencion.Vengarse: g = 2; break;
                    case TipoIntencion.Consolar: case TipoIntencion.Celebrar: g = 3; break;
                    case TipoIntencion.Pedir: g = 5; break;
                    case TipoIntencion.Descansar: continue;
                    default: g = 4; break;
                }
                suma[g] += kv.Value;
            }
            int mejor = 0; for (int i = 1; i < suma.Length; i++) if (suma[i] > suma[mejor] + 1e-12) mejor = i;
            return suma[mejor] / total >= 0.4 ? nombres[mejor] : "";
        }
    }
}
