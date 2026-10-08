using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    // Heuristica pura (sin reflexion) para reconocer, por NOMBRE, miembros que probablemente identifican a un objeto.
    // Se separa en tokens camelCase: "pawnId" -> pawn,id (cuenta); "valid" -> valid (no cuenta); "keyCode" no cuenta.
    public static class NombreMiembro
    {
        static readonly string[] Tokens = { "id", "guid", "uid", "uuid", "uniqueid" };

        public static List<string> TokensDe(string nombre)
        {
            var r = new List<string>(); var sb = new StringBuilder();
            if (nombre == null) return r;
            for (int i = 0; i < nombre.Length; i++)
            {
                char c = nombre[i];
                bool separador = c == '_' || c == '<' || c == '>' || c == '.';
                bool corte = i > 0 && char.IsUpper(c) && (char.IsLower(nombre[i - 1]) || (i + 1 < nombre.Length && char.IsLower(nombre[i + 1]) && char.IsUpper(nombre[i - 1])));
                if ((corte || separador) && sb.Length > 0) { r.Add(sb.ToString().ToLowerInvariant()); sb.Length = 0; }
                if (!separador) sb.Append(c);
            }
            if (sb.Length > 0) r.Add(sb.ToString().ToLowerInvariant());
            return r;
        }

        public static bool PareceId(string nombre)
        {
            foreach (var t in TokensDe(nombre)) if (Array.IndexOf(Tokens, t) >= 0) return true;
            return false;
        }
    }
}
