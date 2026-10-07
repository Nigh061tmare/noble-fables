using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    public enum ClaseSeguridad { Seguro, Riesgo, Prohibido }
    public enum Talante { Hostil, Amistoso, Neutro }

    public sealed class TipoEsquema
    {
        public string Nombre = "";
        public ClaseSeguridad Clase = ClaseSeguridad.Prohibido;
        public Talante Talante = Talante.Neutro;
    }

    // Catalogo de SchemeType que el mod puede disparar. Se carga de esquemas.txt, que
    // RELLENA EL USUARIO tras ejecutar la sonda de solo lectura (Game/Sonda.cs lista los
    // SchemeType reales). Sin fichero -> catalogo vacio -> el mod no dispara nada.
    // Formato:  Nombre|seguro|hostil      (clase: seguro/riesgo/prohibido; talante: hostil/amistoso/neutro)
    public sealed class CatalogoEsquemas
    {
        readonly List<TipoEsquema> tipos = new List<TipoEsquema>();
        public readonly List<string> Avisos = new List<string>();
        public IList<TipoEsquema> Tipos { get { return tipos; } }

        public static CatalogoEsquemas Parse(string texto)
        {
            var c = new CatalogoEsquemas();
            if (texto == null) return c;
            foreach (string linea in texto.Split('\n'))
            {
                string l = linea.Trim();
                if (l.Length == 0 || l[0] == '#') continue;
                string[] p = l.Split('|');
                if (p.Length < 2 || p[0].Trim().Length == 0) { c.Avisos.Add("linea invalida: " + l); continue; }
                var t = new TipoEsquema { Nombre = p[0].Trim() };
                string cl = p[1].Trim().ToLowerInvariant();
                if (cl == "seguro") t.Clase = ClaseSeguridad.Seguro;
                else if (cl == "riesgo") t.Clase = ClaseSeguridad.Riesgo;
                else if (cl == "prohibido") t.Clase = ClaseSeguridad.Prohibido;
                else { c.Avisos.Add("clase desconocida (" + cl + ") en " + t.Nombre + ": queda prohibido"); }
                string ta = p.Length > 2 ? p[2].Trim().ToLowerInvariant() : "neutro";
                t.Talante = ta == "hostil" ? Talante.Hostil : ta == "amistoso" ? Talante.Amistoso : Talante.Neutro;
                c.tipos.Add(t);
            }
            return c;
        }

        // Solo los SEGUROS son disparables. 'riesgo' exige modo=dios y confirmacion explicita.
        public List<TipoEsquema> Seguros(Talante t)
        {
            return tipos.FindAll(x => x.Clase == ClaseSeguridad.Seguro && x.Talante == t);
        }

        // SUGERENCIA para que el usuario revise la lista que vuelca la sonda. No es autoridad:
        // el catalogo solo vale lo que el usuario deje en esquemas.txt.
        public static string Sugiere(string nombre)
        {
            string n = (nombre ?? "").ToLowerInvariant();
            string[] malo = { "assassin", "murder", "kill", "poison", "kidnap", "coup", "rebel", "revolt", "arson", "betray", "sabot", "usurp", "execut", "banish", "war" };
            string[] hostilSeguro = { "insult", "gossip", "rumor", "prank", "spy", "snub", "shun", "blackmail", "spread" };
            string[] amistoso = { "court", "gift", "befriend", "feast", "serenade", "romance", "friend", "party", "toast", "mentor" };
            foreach (var m in malo) if (n.Contains(m)) return nombre + "|prohibido|hostil";
            foreach (var m in hostilSeguro) if (n.Contains(m)) return nombre + "|riesgo|hostil";
            foreach (var m in amistoso) if (n.Contains(m)) return nombre + "|seguro|amistoso";
            return nombre + "|prohibido|neutro";
        }
    }

    public sealed class PropuestaEsquema
    {
        public string Ejecutor = "", Objetivo = "", Tipo = "", Razon = "";
        public Talante Talante;
    }

    // Convierte rencores y amores sostenidos en propuestas. NO ejecuta nada: devuelve
    // propuestas que pasan por la Compuerta (tope diario, enfriamiento, veto).
    public sealed class MotorEsquemas
    {
        public double MinDiasRencor = 5, MinRencor = 0.6;
        public double MinRomance = 0.5, MinAfecto = 0.4;
        public int MaxPropuestas = 3;

        public List<PropuestaEsquema> Propone(ModeloAfectivo m, CatalogoEsquemas cat)
        {
            var r = new List<PropuestaEsquema>();
            var hostiles = cat.Seguros(Talante.Hostil);
            if (hostiles.Count > 0)
            {
                foreach (var kv in m.RencoresSostenidos(MinDiasRencor, MinRencor))
                {
                    if (r.Count >= MaxPropuestas) break;
                    string a, b; ModeloAfectivo.Separa(kv.Key, out a, out b);
                    var t = hostiles[FichaGen.Hash(a + b) % hostiles.Count];
                    r.Add(new PropuestaEsquema
                    {
                        Ejecutor = a, Objetivo = b, Tipo = t.Nombre, Talante = Talante.Hostil,
                        Razon = "rencor " + Json.Num(kv.Value.Rencor) + " sostenido " + Json.Num(kv.Value.DiasRencorAlto) + " dias"
                    });
                }
            }
            var amistosos = cat.Seguros(Talante.Amistoso);
            if (amistosos.Count > 0)
            {
                foreach (var kv in m.Todos())
                {
                    if (r.Count >= MaxPropuestas) break;
                    if (kv.Value.Romance < MinRomance || kv.Value.Afecto < MinAfecto) continue;
                    string a, b; ModeloAfectivo.Separa(kv.Key, out a, out b);
                    var t = amistosos[FichaGen.Hash(b + a) % amistosos.Count];
                    r.Add(new PropuestaEsquema
                    {
                        Ejecutor = a, Objetivo = b, Tipo = t.Nombre, Talante = Talante.Amistoso,
                        Razon = "romance " + Json.Num(kv.Value.Romance) + " y afecto " + Json.Num(kv.Value.Afecto)
                    });
                }
            }
            return r;
        }
    }
}
