using System;
using System.Reflection;
using Pecera.Core;

namespace PeceraNF
{
    // Clave estable de un pawn. El informe NO documenta un id persistente del Pawn
    // (firma pendiente de confirmar: la sonda F11 lista los miembros candidatos). Mientras
    // tanto se busca por REFLEXION un miembro escalar con nombre tipico de identificador, en
    // Pawn y en su Character, y se usa el primero que devuelva un valor valido. Si no hay
    // ninguno, Core separa homonimos por manejador de sesion (RegistroIds).
    // Que la clave sea de verdad estable entre sesiones lo comprueba RegistroIds
    // (ClavesCoinciden/ClavesDiscrepan -> evidencia "id_clave_estable"): NO VERIFICADO.
    public static class Identidad
    {
        static readonly string[] Candidatos = { "guid", "uniqueId", "uniqueID", "uid", "uuid", "id", "ID", "Id", "saveId", "persistentId" };
        static MemberInfo _miembro;
        static bool _enCharacter;
        static bool _buscado;
        public static string MiembroUsado = "(ninguno)";

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
                if (!_buscado) Busca(p);
                if (_miembro == null) return null;
                object origen = p;
                if (_enCharacter) origen = EnCharacter(p);
                if (origen == null) return null;
                object v = Lee(_miembro, origen);
                return Texto(v);
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
            _buscado = true;
            foreach (string n in Candidatos)
            {
                MemberInfo m = Miembro(typeof(Pawn), n);
                if (m != null && Texto(Lee(m, p)) != null) { _miembro = m; _enCharacter = false; MiembroUsado = "Pawn." + n; return; }
            }
            object ch = EnCharacter(p);
            if (ch == null) return;
            foreach (string n in Candidatos)
            {
                MemberInfo m = Miembro(ch.GetType(), n);
                if (m != null && Texto(Lee(m, ch)) != null) { _miembro = m; _enCharacter = true; MiembroUsado = "Character." + n; return; }
            }
        }

        static MemberInfo Miembro(Type t, string n)
        {
            for (; t != null; t = t.BaseType)
            {
                var f = t.GetField(n, Aux.Todos | BindingFlags.DeclaredOnly);
                if (f != null && !f.IsStatic) return f;
                var pr = t.GetProperty(n, Aux.Todos | BindingFlags.DeclaredOnly);
                if (pr != null && pr.GetIndexParameters().Length == 0 && pr.CanRead) return pr;
            }
            return null;
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
