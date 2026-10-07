using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Pecera.Core;

namespace PeceraNF
{
    // Sondas de SOLO LECTURA. No llaman a ningun metodo del juego ni escriben en su estado:
    // solo reflexion sobre nombres de tipos/miembros y valores escalares ya existentes.
    // Sirven para cerrar las "firmas pendientes de confirmar" con datos reales en vez de adivinar.
    public static class Sonda
    {
        // Una vez, con el primer pawn visto: miembros de Pawn y Character con su valor si es escalar.
        public static void PawnUnaVez(Pawn p)
        {
            if (p == null) return;
            try
            {
                var sb = new StringBuilder("{\"pawn\":[");
                Miembros(sb, p, p.GetType());
                sb.Append("],\"character\":[");
                var pr = typeof(Pawn).GetProperty("character", Aux.TODO);
                object ch = pr != null ? pr.GetValue(p, null) : null;
                if (ch != null) Miembros(sb, ch, ch.GetType());
                sb.Append("],\"miembro_id_usado\":\"").Append(Json.Escape(Identidad.MiembroUsado)).Append("\"}");
                Estado.Disco.Rewrite("sonda_pawn.json", new[] { sb.ToString() });
                Estado.Ev.Ok("sonda_pawn", Identidad.MiembroUsado);
            }
            catch (TargetInvocationException e) { Estado.Ev.Fail("sonda_pawn", e.Message); }
            catch (ArgumentException e) { Estado.Ev.Fail("sonda_pawn", e.Message); }
        }

        static void Miembros(StringBuilder sb, object o, Type t)
        {
            bool primero = true;
            int n = 0;
            for (; t != null && t != typeof(object) && n < 400; t = t.BaseType)
            {
                foreach (var m in t.GetMembers(Aux.TODO | BindingFlags.DeclaredOnly))
                {
                    var f = m as FieldInfo; var pr = m as PropertyInfo;
                    if (f == null && pr == null) continue;
                    if (pr != null && pr.GetIndexParameters().Length > 0) continue;
                    Type tipo = f != null ? f.FieldType : pr.PropertyType;
                    string valor = null;
                    if (tipo.IsPrimitive || tipo == typeof(string) || tipo == typeof(Guid) || tipo.IsEnum)
                    {
                        try { object v = f != null ? f.GetValue(o) : (pr.CanRead ? pr.GetValue(o, null) : null); valor = v == null ? null : v.ToString(); }
                        catch (TargetInvocationException) { valor = "(lanza)"; }
                    }
                    if (!primero) sb.Append(',');
                    primero = false; n++;
                    sb.Append("{\"en\":\"").Append(Json.Escape(t.Name)).Append("\",\"n\":\"").Append(Json.Escape(m.Name)).Append("\",\"tipo\":\"").Append(Json.Escape(tipo.Name))
                      .Append("\",\"valor\":").Append(valor == null ? "null" : "\"" + Json.Escape(valor.Length > 60 ? valor.Substring(0, 60) : valor) + "\"").Append('}');
                }
            }
        }

        static readonly string[] Interes = { "Scheme", "Petition", "Research", "PlanManager", "Mission", "Conversation", "Wants", "Kingdom", "Season", "Calendar", "GameTime", "TimeManager" };

        // F11: firmas de los tipos que necesitan las fases 3, 4, 6 y 7 + SchemeType con su clasificacion sugerida.
        public static void Tipos()
        {
            try
            {
                var sb = new StringBuilder("{\"tipos\":[");
                var esquemas = new List<string>();
                bool primero = true;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!asm.GetName().Name.StartsWith("Assembly-CSharp", StringComparison.Ordinal)) continue;
                    Type[] ts;
                    try { ts = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException e) { ts = Array.FindAll(e.Types, x => x != null); }
                    foreach (var t in ts)
                    {
                        if (t.Name == "SchemeType" && t.IsEnum) foreach (var nom in Enum.GetNames(t)) esquemas.Add(nom);
                        bool vale = false;
                        foreach (var k in Interes) if (t.Name.IndexOf(k, StringComparison.Ordinal) >= 0) { vale = true; break; }
                        if (!vale || t.Name.Length > 60) continue;
                        if (!primero) sb.Append(',');
                        primero = false;
                        sb.Append("{\"tipo\":\"").Append(Json.Escape(t.FullName)).Append("\",\"miembros\":[");
                        bool pm = true;
                        foreach (var m in t.GetMembers(Aux.TODO | BindingFlags.DeclaredOnly))
                        {
                            string desc = Describe(m);
                            if (desc == null) continue;
                            if (!pm) sb.Append(',');
                            pm = false;
                            sb.Append('"').Append(Json.Escape(desc)).Append('"');
                        }
                        sb.Append("]}");
                    }
                }
                sb.Append("],\"SchemeType\":[");
                for (int i = 0; i < esquemas.Count; i++) { if (i > 0) sb.Append(','); sb.Append('"').Append(Json.Escape(esquemas[i])).Append('"'); }
                sb.Append("]}");
                Estado.Disco.Rewrite("sonda.json", new[] { sb.ToString() });
                var sug = new List<string> { "# SUGERENCIAS para esquemas.txt. Son heuristicas por nombre, NO autoridad: revisa cada linea.",
                                             "# Formato: Nombre|seguro/riesgo/prohibido|hostil/amistoso/neutro. Solo los 'seguro' se disparan." };
                foreach (var e in esquemas) sug.Add(CatalogoEsquemas.Sugiere(e));
                Estado.Disco.Rewrite("esquemas.sugeridos.txt", sug);
                UnityEngine.Debug.Log("[Pecera] sonda: " + esquemas.Count + " SchemeType; ver sonda.json y esquemas.sugeridos.txt");
            }
            catch (ReflectionTypeLoadException e) { UnityEngine.Debug.Log("[Pecera] sonda fallo: " + e.Message); }
        }

        static string Describe(MemberInfo m)
        {
            var mi = m as MethodInfo;
            if (mi != null)
            {
                if (mi.IsSpecialName) return null;
                var ps = new List<string>();
                foreach (var p in mi.GetParameters()) ps.Add((p.IsOut ? "out " : "") + p.ParameterType.Name + " " + p.Name);
                return mi.ReturnType.Name + " " + mi.Name + "(" + string.Join(", ", ps.ToArray()) + ")";
            }
            var f = m as FieldInfo;
            if (f != null) return "campo " + f.FieldType.Name + " " + f.Name;
            var pr = m as PropertyInfo;
            if (pr != null) return "prop " + pr.PropertyType.Name + " " + pr.Name;
            return null;
        }
    }
}
