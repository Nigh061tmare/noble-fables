using System;
using System.Collections.Generic;

namespace Pecera.Core
{
    // Estado social completo en UN fichero (mundo.jsonl), reescrito de forma atomica. Sin esto, al reiniciar el juego los pawns
    // perdian sus planes, la cronica, las normas, los secretos y lo aprendido. Cada linea lleva "k" (tipo) y la primera la version.
    // Cualquier componente puede faltar (null): se omite al guardar y se ignora al cargar.
    public sealed class EstadoSocial
    {
        public const int Version = 1;
        public Agenda Agenda; public Normas Normas; public Director Director; public Cultura Cultura; public Cronica Cronica;
        public Costumbres Costumbres; public RedSecretos Secretos; public RecuerdosFuertes Fuertes; public Experiencia Exp; public Preferencias Prefs; public Prejuicios Prejuicios;

        public List<string> Serializa()
        {
            var l = new List<string> { "{\"k\":\"v\",\"v\":" + Version + "}" };
            if (Agenda != null) l.AddRange(Agenda.Serializa());
            if (Normas != null) l.AddRange(Normas.Serializa());
            if (Director != null) l.Add(Director.Serializa());
            if (Cultura != null) l.AddRange(Cultura.Serializa());
            if (Cronica != null) l.AddRange(Cronica.Serializa());
            if (Costumbres != null) l.AddRange(Costumbres.Serializa());
            if (Secretos != null) l.AddRange(Secretos.Serializa());
            if (Fuertes != null) l.AddRange(Fuertes.Serializa());
            if (Exp != null) l.AddRange(Exp.Serializa());
            if (Prefs != null) l.AddRange(Prefs.Serializa());
            if (Prejuicios != null) l.AddRange(Prejuicios.Serializa());
            return l;
        }

        // Devuelve las lineas ilegibles. Si el fichero es de una version MAS NUEVA no carga nada (y avisa) para no estropearlo.
        public int Carga(string[] lineas, out bool versionFutura)
        {
            versionFutura = false;
            int malas = 0;
            foreach (var linea in lineas)
            {
                object d;
                if (!Json.TryParse(linea, out d)) { malas++; continue; }
                string k = Json.Str(d, "k");
                switch (k)
                {
                    case "v": if (Json.Num(d, "v", 1) > Version) { versionFutura = true; return malas; } break;
                    case "ag": case "agc": case "agx": case "agh": if (Agenda != null) Agenda.Carga(d); break;
                    case "no": if (Normas != null) Normas.Carga(d); break;
                    case "di": if (Director != null) Director.Carga(d); break;
                    case "cu": case "cf": case "cm": if (Cultura != null) Cultura.Carga(d); break;
                    case "cr": if (Cronica != null) Cronica.Carga(d); break;
                    case "co": if (Costumbres != null) Costumbres.Carga(d); break;
                    case "se": if (Secretos != null) Secretos.Carga(d); break;
                    case "rf": if (Fuertes != null) Fuertes.Carga(d); break;
                    case "ex": if (Exp != null) Exp.Carga(d); break;
                    case "pj": if (Prefs != null) Prefs.Carga(d); break;
                    case "pg": case "pa": if (Prejuicios != null) Prejuicios.Carga(d); break;
                    default: malas++; break;
                }
            }
            return malas;
        }
    }
}
