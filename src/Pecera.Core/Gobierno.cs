using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    // Metas del reino: pesos 0..1 por categoria. Las ajusta el bucle de metas segun el
    // deficit de cada metrica (H).
    public sealed class MetasReino
    {
        public readonly Dictionary<string, double> Pesos = new Dictionary<string, double>();

        public static readonly string[] Categorias = { "crecimiento", "defensa", "cultura", "economia", "cohesion" };

        public MetasReino()
        {
            foreach (var c in Categorias) Pesos[c] = 0.5;
        }

        public double Peso(string categoria)
        {
            double v; return Pesos.TryGetValue(categoria ?? "", out v) ? v : 0.3;
        }

        // Reparte atencion en proporcion al deficit; siempre queda un suelo para que nada
        // se abandone del todo.
        public void Ajusta(MetricasReino m)
        {
            double[] def = {
                1 - m.Poblacion,        // crecimiento
                1 - m.Seguridad,        // defensa
                1 - m.Conocimiento,     // cultura
                1 - m.Riqueza,          // economia
                1 - Math.Min(m.Cohesion, m.Estabilidad) };   // cohesion
            for (int i = 0; i < Categorias.Length; i++)
                Pesos[Categorias[i]] = Math.Round(0.15 + 0.85 * Math.Max(0, Math.Min(1, def[i])), 4);
        }
    }

    // Metricas normalizadas a 0..1 (1 = muy bien). Las de dominio del juego las aporta el
    // adaptador (si no las conoce, usa 0.5 y lo dice en la telemetria).
    public sealed class MetricasReino
    {
        public double Poblacion = 0.5, Seguridad = 0.5, Conocimiento = 0.5, Riqueza = 0.5;
        public double Cohesion, Estabilidad;
        public int Facciones;

        // Cohesion: sentimiento medio entre todos los pares. Estabilidad: ausencia de rencor.
        public static void Calcula(ModeloAfectivo m, IList<string> ids, MetricasReino r)
        {
            double s = 0, rencor = 0; int n = 0, altos = 0;
            foreach (var a in ids) foreach (var b in ids)
            {
                if (a == b) continue;
                s += m.Sentimiento(a, b);
                Par p = m.Get(a, b);
                rencor += p.Rencor;
                if (p.Rencor > Par.UmbralRencor) altos++;
                n++;
            }
            if (n == 0) { r.Cohesion = 0.5; r.Estabilidad = 1; return; }
            r.Cohesion = Math.Max(0, Math.Min(1, 0.5 + 0.5 * s / n * 2));
            double mediaRencor = rencor / n;
            r.Estabilidad = Math.Max(0, Math.Min(1, 1 - mediaRencor - 0.5 * ((double)altos / n)));
        }

        public string ToJson()
        {
            return "{\"poblacion\":" + Json.Num(Poblacion) + ",\"seguridad\":" + Json.Num(Seguridad) + ",\"conocimiento\":" + Json.Num(Conocimiento)
                + ",\"riqueza\":" + Json.Num(Riqueza) + ",\"cohesion\":" + Json.Num(Cohesion) + ",\"estabilidad\":" + Json.Num(Estabilidad)
                + ",\"facciones\":" + Facciones + "}";
        }
    }

    public sealed class Peticion
    {
        public string Id = "", Tipo = "", Solicitante = "", Texto = "";
        public double Importancia = 0.5;     // 0..1
        public string Categoria = "cohesion";  // a que meta del reino afecta
    }

    public sealed class Veredicto
    {
        public bool Aprueba;
        public double Puntuacion;
        public string Razon = "";
    }

    public sealed class OpcionElegible
    {
        public string Id = "", Nombre = "", Categoria = "";
        public double Coste = 1;
        public bool Disponible = true;
    }

    // Gobierno autonomo («Rey dormido», G). Logica de decision pura y determinista: el
    // LLM solo REDACTA la razon (opcional); la decision no depende de el. Asi se prueba aqui
    // y funciona con el LLM caido. Los efectos pasan por la Compuerta (tope, veto).
    public sealed class Consejo
    {
        readonly ModeloAfectivo afectos;
        public double UmbralAprobacion = 0.45;
        public Func<string, bool> Favorecido;       // ordenes del jugador: «favorece a X»

        public Consejo(ModeloAfectivo afectos) { this.afectos = afectos; }

        // soberano: id del pawn que gobierna (de quien se mide el afecto).
        public Veredicto Evalua(Peticion p, string soberano, MetasReino metas)
        {
            double estima = (afectos.Sentimiento(soberano, p.Solicitante) + 1) / 2;       // 0..1
            double meta = metas.Peso(p.Categoria);
            double rencor = afectos.Get(soberano, p.Solicitante).Rencor;
            double deuda = Math.Max(0, afectos.Get(soberano, p.Solicitante).Deuda);         // gratitud
            bool fav = Favorecido != null && Favorecido(p.Solicitante);
            double puntos = 0.30 * p.Importancia + 0.25 * estima + 0.30 * meta + 0.15 * deuda - 0.25 * rencor + (fav ? 0.15 : 0);
            var v = new Veredicto { Puntuacion = Math.Round(puntos, 4), Aprueba = puntos >= UmbralAprobacion };
            var sb = new StringBuilder();
            sb.Append(v.Aprueba ? "Aprobada" : "Denegada").Append(": importancia ").Append(Json.Num(p.Importancia))
              .Append(", meta '").Append(p.Categoria).Append("' ").Append(Json.Num(meta))
              .Append(", estima ").Append(Json.Num(estima));
            if (rencor > 0.3) sb.Append(", rencor ").Append(Json.Num(rencor));
            if (deuda > 0.1) sb.Append(", gratitud ").Append(Json.Num(deuda));
            if (fav) sb.Append(", favorecido por el soberano");
            v.Razon = sb.ToString();
            return v;
        }

        // Elige la opcion con mejor valor por coste segun las metas. Desempate por id.
        public OpcionElegible Elige(IList<OpcionElegible> opciones, MetasReino metas)
        {
            OpcionElegible mejor = null; double mv = double.NegativeInfinity;
            foreach (var o in opciones)
            {
                if (!o.Disponible) continue;
                double v = metas.Peso(o.Categoria) / Math.Max(0.1, o.Coste);
                if (mejor == null || v > mv + 1e-12 || (Math.Abs(v - mv) <= 1e-12 && string.CompareOrdinal(o.Id, mejor.Id) < 0)) { mejor = o; mv = v; }
            }
            return mejor;
        }

        public const string SistemaConsejero =
            "Eres un consejero de un reino medieval. Explicas en UNA frase, en espanol y en lenguaje de epoca, " +
            "por que se ha tomado una decision. Sin markdown. Responde SOLO la frase.";

        public static string UsuarioRazon(string consejero, string ficha, Peticion p, Veredicto v)
        {
            return "Consejero " + consejero + (string.IsNullOrEmpty(ficha) ? "" : " (" + ficha + ")") + ". Peticion de tipo '" + p.Tipo + "': " + p.Texto
                 + ". Decision: " + (v.Aprueba ? "APROBAR" : "DENEGAR") + ". Motivos: " + v.Razon + ".";
        }
    }
}
