using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Pecera.Core;

namespace PeceraNF
{
    // Clave estable de un pawn. El informe NO documenta un id persistente del Pawn y la primera version solo probaba
    // 10 nombres fijos (resultado en el PC real: miembro_id = "(ninguno)"). Ahora:
    //   1. Se buscan TODOS los miembros escalares de Pawn y de su Character cuyo nombre contenga un TOKEN de
    //      identificador (camelCase: "pawnId" -> pawn,id; "valid" NO cuenta), sin depender de una lista cerrada.
    //   2. Se prueban en orden de preferencia; el primero que da valor valido se usa.
    //   3. UNICIDAD en vivo: si dos pawns distintos (manejador de sesion distinto) producen la misma clave, el miembro
    //      se descarta y se pasa al siguiente (evidencia "id_clave_unica").
    //   4. Si ninguno sirve, Core separa homonimos por manejador de sesion (RegistroIds), como antes.
    // Que la clave sea estable ENTRE SESIONES lo sigue midiendo RegistroIds (evidencia "id_clave_estable").
    // NO VERIFICADO en el juego: sonda_pawn.json lista los candidatos para decidir con datos.
    public static class Identidad
    {
        sealed class Cand
        {
            public MemberInfo M; public bool EnCharacter; public string Nombre; public bool Descartado;
        }

        static List<Cand> _cands;
        static Cand _actual;
        public static string MiembroUsado = "(ninguno)";
        public static string Candidatos = "";
        public static int Colisiones;
        static readonly Dictionary<string, int> ClaveAManejador = new Dictionary<string, int>();
        static readonly HashSet<int> Manejadores = new HashSet<int>();

        public static string Id(Pawn p, string nombre)
        {
            string clave = Clave(p);
            return Estado.Ids.Resuelve(clave, nombre, p != null ? p.GetHashCode() : 0);
        }

        static string Clave(Pawn p)
        {
            if (p == null) return null;
            try
            {
                if (_cands == null) Busca(p);
                int manejador = p.GetHashCode();
                foreach (var c in _cands)
                {
                    if (c.Descartado) continue;
                    object origen = c.EnCharacter ? EnCharacter(p) : (object)p;
                    if (origen == null) continue;
                    string k = Texto(Lee(c.M, origen));
                    if (k == null) continue;
                    // unicidad: la misma clave en dos objetos distintos => el miembro no identifica a nadie
                    int otro;
                    if (ClaveAManejador.TryGetValue(c.Nombre + "=" + k, out otro) && otro != manejador)
                    {
                        c.Descartado = true; Colisiones++;
                        Estado.Ev.Fail("id_clave_unica", c.Nombre + " repite la clave " + k + " en dos pawns");
                        MiembroUsado = "(ninguno)";
                        continue;
                    }
                    ClaveAManejador[c.Nombre + "=" + k] = manejador;
                    if (Manejadores.Add(manejador) && Manejadores.Count == 20) Estado.Ev.Ok("id_clave_unica", c.Nombre + ": 20 pawns sin colision");
                    _actual = c; MiembroUsado = c.Nombre;
                    return k;
                }
                MiembroUsado = "(ninguno)";
                return null;
            }
            catch (TargetInvocationException) { return null; }
        }

        static object EnCharacter(Pawn p)
        {
            var pr = typeof(Pawn).GetProperty("character", Aux.Todos);
            return pr != null ? pr.GetValue(p, null) : null;
        }

        static void Busca(Pawn p)
        {
            _cands = new List<Cand>();
            AnadeDe(typeof(Pawn), false, p);
            object ch = EnCharacter(p);
            if (ch != null) AnadeDe(ch.GetType(), true, ch);
            // preferencia: nombres exactos conocidos primero, luego el resto por orden alfabetico (determinista)
            string[] pref = { "guid", "uniqueid", "uid", "uuid", "id" };
            _cands.Sort((a, b) =>
            {
                int pa = Array.IndexOf(pref, SoloNombre(a).ToLowerInvariant()), pb = Array.IndexOf(pref, SoloNombre(b).ToLowerInvariant());
                if (pa < 0) pa = 99; if (pb < 0) pb = 99;
                int c = pa.CompareTo(pb);
                return c != 0 ? c : string.CompareOrdinal(a.Nombre, b.Nombre);
            });
            var sb = new StringBuilder();
            foreach (var c in _cands) { if (sb.Length > 0) sb.Append(", "); sb.Append(c.Nombre); }
            Candidatos = sb.ToString();
        }

        static string SoloNombre(Cand c) { int i = c.Nombre.IndexOf('.'); return i >= 0 ? c.Nombre.Substring(i + 1) : c.Nombre; }

        static void AnadeDe(Type tipo, bool enCharacter, object ejemplar)
        {
            for (Type t = tipo; t != null && t != typeof(object); t = t.BaseType)
                foreach (var m in t.GetMembers(Aux.Todos | BindingFlags.DeclaredOnly))
                {
                    var f = m as FieldInfo; var pr = m as PropertyInfo;
                    if (f == null && pr == null) continue;
                    if (f != null && f.IsStatic) continue;
                    if (pr != null && (pr.GetIndexParameters().Length > 0 || !pr.CanRead)) continue;
                    string nombre = m.Name;
                    if (nombre.EndsWith("k__BackingField", StringComparison.Ordinal)) continue;   // la propiedad ya esta
                    if (!NombreMiembro.PareceId(nombre)) continue;
                    Type tt = f != null ? f.FieldType : pr.PropertyType;
                    if (!(tt == typeof(int) || tt == typeof(long) || tt == typeof(uint) || tt == typeof(ulong) || tt == typeof(short) || tt == typeof(string) || tt == typeof(Guid))) continue;
                    string valor = null;
                    try { valor = Texto(Lee(m, ejemplar)); } catch (TargetInvocationException) { }
                    if (valor == null) continue;                    // un candidato que en el primer pawn vale 0/null no sirve
                    _cands.Add(new Cand { M = m, EnCharacter = enCharacter, Nombre = (enCharacter ? "Character." : "Pawn.") + nombre });
                }
        }

        static object Lee(MemberInfo m, object o)
        {
            var f = m as FieldInfo;
            if (f != null) return f.GetValue(o);
            return ((PropertyInfo)m).GetValue(o, null);
        }

        // Solo escalares: un objeto cualquiera daria su nombre de tipo, que no identifica a nadie.
        static string Texto(object v)
        {
            if (v == null) return null;
            if (v is int || v is long || v is uint || v is ulong || v is short) { string s = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture); return s == "0" ? null : s; }
            if (v is Guid) return ((Guid)v) == Guid.Empty ? null : v.ToString();
            var str = v as string;
            return string.IsNullOrEmpty(str) ? null : str;
        }
    }
}
