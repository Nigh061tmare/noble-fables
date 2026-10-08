using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    public sealed class Resolucion
    {
        public bool Resuelta;          // el maestro la resolvio (social/interna)
        public bool RequiereJuego;     // necesita una accion real del juego (peticion, esquema, investigacion...)
        public bool Exito;
        public string Texto = "";
        public string Objetivo = "";
        public Conversacion Conversacion;
        public Ruptura Crisis = Ruptura.Ninguna;
    }

    // Maestro de juego (Concordia, DeepMind: un «game master» que convierte intenciones en desenlaces plausibles). Resuelve las
    // intenciones SOCIALES dentro de la capa social de Pecera (conversar, cortejar, consolar, celebrar, visitar, descansar): eso
    // no escribe en el juego, cambia relaciones, memoria y animo. Las que necesitan el mundo real (pedir, intrigar, vengarse,
    // trabajar, aprender) las devuelve como RequiereJuego para que las ejecute un adaptador (si existe) o se queden en intencion.
    // ES EL MISMO CODIGO en el simulador y en el juego: lo que se mide es lo que corre.
    public sealed class MaestroDeJuego
    {
        public ModeloAfectivo Afectos;
        public Func<string, Persona> Persona;
        public Func<string, string> Nombre = id => id;
        public FrenoConversacion Freno = new FrenoConversacion();
        public Memoria Mem;                     // opcional
        public RecuerdosFuertes Fuertes;        // opcional
        public Experiencia Exp = new Experiencia();
        public RedSecretos Secretos;            // opcional (rumores)
        public int Conversaciones, Exitos;

        public Resolucion Resuelve(Intencion i, int dia, Rng rng, IList<string> vivos)
        {
            var r = new Resolucion { Objetivo = i.Objetivo };
            Persona p = Persona(i.Pawn);
            if (p == null) return r;
            switch (i.Tipo)
            {
                case TipoIntencion.Charlar:
                case TipoIntencion.Visitar:
                case TipoIntencion.Cortejar:
                case TipoIntencion.Consolar:
                case TipoIntencion.Celebrar:
                {
                    string obj = i.Objetivo;
                    if (obj.Length == 0 || !Contiene(vivos, obj)) obj = Otro(i.Pawn, vivos, rng);
                    Persona b = obj.Length > 0 ? Persona(obj) : null;
                    if (b == null || !Freno.Puede(i.Pawn, obj, dia)) { r.Resuelta = true; r.Exito = false; r.Texto = "no encontro ocasion"; break; }
                    Tema t = i.Tipo == TipoIntencion.Cortejar ? Tema.Cortejo : i.Tipo == TipoIntencion.Consolar ? Tema.Consuelo
                           : i.Tipo == TipoIntencion.Celebrar ? Tema.Celebracion : i.Tipo == TipoIntencion.Visitar ? Tema.Saludo : Dialogo.Elige(p, b, Afectos, rng);
                    var c = Dialogo.Resuelve(p, b, t, Afectos, rng, dia);
                    Freno.Anota(i.Pawn, obj, dia);
                    Conversaciones++; if (c.Exito) Exitos++;
                    r.Resuelta = true; r.Exito = c.Exito; r.Objetivo = obj; r.Conversacion = c;
                    r.Texto = p.Nombre + " y " + b.Nombre + ": " + c.Efecto;
                    if (Mem != null)
                    {
                        Mem.Registra(p.Id, "conversacion", obj, "hable de " + t.ToString().ToLowerInvariant() + " con " + b.Nombre + ": " + c.Efecto, c.Exito ? 1 : 2.5);
                        Mem.Registra(obj, "conversacion", p.Id, p.Nombre + " hablo conmigo de " + t.ToString().ToLowerInvariant() + ": " + c.Efecto, c.Exito ? 1 : 2.5);
                    }
                    if (Fuertes != null && t == Tema.Cortejo && c.Exito && Afectos.Get(p.Id, obj).Romance > 0.6)
                        Fuertes.Anota(p.Id, "amor", "me enamore de " + b.Nombre, 1, 7, dia);
                    if (Secretos != null) Secretos.Conversan(p.Id, obj);
                    break;
                }
                case TipoIntencion.Descansar:
                    p.Needs.Pon("descanso", p.Needs.Descanso + 0.25);
                    r.Resuelta = true; r.Exito = true; r.Texto = p.Nombre + " descansa";
                    break;
                default:
                    r.RequiereJuego = true;
                    return r;      // ni experiencia ni estres: no se ha hecho nada todavia
            }
            Exp.Anota(i.Pawn, i.Tipo, r.Exito);
            r.Crisis = Estres.Suma(p, Estres.Coste(p, i.Tipo) + (r.Exito ? 0 : Estres.CosteFracaso(p)));
            return r;
        }

        // Para las que ejecuta un adaptador (o el simulador): misma contabilidad de experiencia y estres.
        public Ruptura Cierra(Intencion i, bool exito)
        {
            Persona p = Persona(i.Pawn);
            if (p == null) return Ruptura.Ninguna;
            Exp.Anota(i.Pawn, i.Tipo, exito);
            return Estres.Suma(p, Estres.Coste(p, i.Tipo) + (exito ? 0 : Estres.CosteFracaso(p)));
        }

        static bool Contiene(IList<string> l, string id) { for (int k = 0; k < l.Count; k++) if (l[k] == id) return true; return false; }

        static string Otro(string yo, IList<string> vivos, Rng rng)
        {
            var o = new List<string>(); foreach (var v in vivos) if (v != yo) o.Add(v);
            o.Sort(StringComparer.Ordinal);
            return o.Count == 0 ? "" : o[rng.Next(o.Count)];
        }
    }
}
