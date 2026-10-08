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
                sb.Append("],\"candidatos_id\":\"").Append(Json.Escape(Identidad.Candidatos)).Append("\",\"colisiones_id\":").Append(Identidad.Colisiones).Append(",\"character\":[");
                var pr = typeof(Pawn).GetProperty("character", Aux.Todos);
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
                foreach (var m in t.GetMembers(Aux.Todos | BindingFlags.DeclaredOnly))
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

        // F11 (segunda parte): estado ESCALAR de los managers que importan (reloj, reino, soberano, investigacion, planes...)
        // y firmas de los metodos de Pawn relacionados con necesidades, familia, habilidades y titulos. Solo lectura.
        static readonly string[] InteresMundo = { "Time", "Calendar", "Season", "Kingdom", "Ruler", "Research", "PlanManager", "Mission", "Scheme", "Want", "Need", "Family", "Marriage", "Relationship", "Title", "Faction", "Trade", "Diplo", "Stockpile", "Skill", "Job" };

        public static void Mundo()
        {
            try
            {
                var sb = new StringBuilder("{\"managers\":[");
                bool primero = true;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!asm.GetName().Name.StartsWith("Assembly-CSharp", StringComparison.Ordinal)) continue;
                    Type[] ts;
                    try { ts = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException e) { ts = Array.FindAll(e.Types, x => x != null); }
                    foreach (var t in ts)
                    {
                        if (!t.Name.EndsWith("Manager", StringComparison.Ordinal) || t.IsAbstract || t.IsGenericTypeDefinition) continue;
                        bool vale = false; foreach (var k in InteresMundo) if (t.Name.IndexOf(k, StringComparison.Ordinal) >= 0) { vale = true; break; }
                        if (!vale) continue;
                        object inst = null;
                        try { var pi = t.GetProperty("Instance", Aux.Todos | BindingFlags.FlattenHierarchy); if (pi != null) inst = pi.GetValue(null, null); }
                        catch (TargetInvocationException) { }
                        if (!primero) sb.Append(','); primero = false;
                        sb.Append("{\"tipo\":\"").Append(Json.Escape(t.FullName)).Append("\",\"instancia\":").Append(inst != null ? "true" : "false").Append(",\"estado\":[");
                        bool pe = true;
                        if (inst != null)
                            foreach (var m in t.GetMembers(Aux.Todos | BindingFlags.DeclaredOnly))
                            {
                                var f = m as FieldInfo; var pr = m as PropertyInfo;
                                if (f == null && pr == null) continue;
                                if (pr != null && (pr.GetIndexParameters().Length > 0 || !pr.CanRead)) continue;
                                Type tt = f != null ? f.FieldType : pr.PropertyType;
                                string valor = null;
                                try
                                {
                                    object v = f != null ? f.GetValue(inst) : pr.GetValue(inst, null);
                                    if (v == null) valor = "null";
                                    else if (tt.IsPrimitive || tt == typeof(string) || tt.IsEnum) valor = v.ToString();
                                    else { var col = v as System.Collections.ICollection; valor = col != null ? tt.Name + " count=" + col.Count : tt.Name; }
                                }
                                catch (TargetInvocationException) { valor = "(lanza)"; }
                                if (!pe) sb.Append(','); pe = false;
                                sb.Append("{\"n\":\"").Append(Json.Escape(m.Name)).Append("\",\"v\":\"").Append(Json.Escape(valor.Length > 80 ? valor.Substring(0, 80) : valor)).Append("\"}");
                            }
                        sb.Append("],\"metodos\":[");
                        bool pm = true; int cuantos = 0;
                        foreach (var m in t.GetMembers(Aux.Todos | BindingFlags.DeclaredOnly))
                        {
                            if (m is FieldInfo || m is PropertyInfo || cuantos >= 60) continue;
                            string d = Describe(m); if (d == null) continue;
                            if (!pm) sb.Append(','); pm = false; cuantos++;
                            sb.Append('"').Append(Json.Escape(d)).Append('"');
                        }
                        sb.Append("]}");
                    }
                }
                sb.Append("],\"pawn_metodos\":[");
                bool pp = true;
                string[] clave = { "Need", "Want", "Skill", "Family", "Spouse", "Marri", "Relat", "Title", "Job", "Task", "Age", "Birth", "Gender", "Kin", "Faction", "Mood", "Trait" };
                foreach (var m in typeof(Pawn).GetMembers(Aux.Todos))
                {
                    bool ok = false; foreach (var k in clave) if (m.Name.IndexOf(k, StringComparison.Ordinal) >= 0) { ok = true; break; }
                    if (!ok) continue;
                    string d = Describe(m); if (d == null) { var f = m as FieldInfo; if (f != null) d = "campo " + f.FieldType.Name + " " + f.Name; }
                    if (d == null) continue;
                    if (!pp) sb.Append(','); pp = false;
                    sb.Append('"').Append(Json.Escape(d)).Append('"');
                }
                sb.Append("]}");
                Estado.Disco.Rewrite("sonda_mundo.json", new[] { sb.ToString() });
                UnityEngine.Debug.Log("[Pecera] sonda mundo: ver sonda_mundo.json");
            }
            catch (ReflectionTypeLoadException e) { UnityEngine.Debug.Log("[Pecera] sonda mundo fallo: " + e.Message); }
            catch (ArgumentException e) { UnityEngine.Debug.Log("[Pecera] sonda mundo fallo: " + e.Message); }
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
                Type tMgr = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!asm.GetName().Name.StartsWith("Assembly-CSharp", StringComparison.Ordinal)) continue;
                    Type[] ts;
                    try { ts = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException e) { ts = Array.FindAll(e.Types, x => x != null); }
                    foreach (var t in ts)
                    {
                        if (t.Name == "SchemeManager") tMgr = t;
                        bool vale = false;
                        foreach (var k in Interes) if (t.Name.IndexOf(k, StringComparison.Ordinal) >= 0) { vale = true; break; }
                        if (!vale || t.Name.Length > 60) continue;
                        if (!primero) sb.Append(',');
                        primero = false;
                        sb.Append("{\"tipo\":\"").Append(Json.Escape(t.FullName)).Append("\",\"miembros\":[");
                        bool pm = true;
                        foreach (var m in t.GetMembers(Aux.Todos | BindingFlags.DeclaredOnly))
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
                // SchemeType NO es un enum: es una clase OctDatGlobal cargada desde datos (LEIDO en el binario).
                // Las instancias reales estan en SchemeManager.Instance.types.
                esquemas.AddRange(NombresEsquemas(tMgr));
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

        // Nombres de los SchemeType cargados ("nombre (Clase)"). Solo lectura. Si el manager aun no
        // existe (partida sin cargar) devuelve vacio, y el log lo dice.
        static List<string> NombresEsquemas(Type tMgr)
        {
            var r = new List<string>();
            if (tMgr == null) return r;
            try
            {
                // Manager<T>.Instance: propiedad estatica heredada (LEIDO: Manager`1 tiene s_Instance / Instance).
                PropertyInfo pi = tMgr.GetProperty("Instance", Aux.Todos | BindingFlags.FlattenHierarchy);
                object inst = pi != null ? pi.GetValue(null, null) : null;
                if (inst == null) return r;
                PropertyInfo pt = tMgr.GetProperty("types", Aux.Todos);
                System.Collections.IEnumerable lista = pt != null ? pt.GetValue(inst, null) as System.Collections.IEnumerable : null;
                if (lista == null) return r;
                var vistos = new System.Collections.Generic.Dictionary<string, bool>();
                foreach (object o in lista)
                {
                    if (o == null) continue;
                    string clase = o.GetType().Name;
                    string nombre = LeeCampoTexto(o, "name");
                    if (string.IsNullOrEmpty(nombre)) nombre = LlamaSinArgs(o, "GetName");
                    if (string.IsNullOrEmpty(nombre)) nombre = LlamaSinArgs(o, "get_name");
                    if (string.IsNullOrEmpty(nombre)) nombre = LlamaSinArgs(o, "ToString");
                    if (string.IsNullOrEmpty(nombre) && clase.StartsWith("Scheme", StringComparison.Ordinal)) nombre = clase;

                    bool trigger; string tv;
                    tv = LeeCampoTexto(o, "triggeredOnly");
                    trigger = tv != null && tv.Equals("true", StringComparison.OrdinalIgnoreCase);
                    string etiqueta = (string.IsNullOrEmpty(nombre) ? clase : nombre) +
                                      (trigger ? " [triggered]" : "");
                    if (nombre == "SchemeType" || clase == "SchemeType" && string.IsNullOrEmpty(nombre))
                        etiqueta = clase;   // los hybridos puros no aportan nombre
                    if (vistos.ContainsKey(etiqueta)) continue;
                    vistos[etiqueta] = true;
                    r.Add(etiqueta + " (" + clase + ")");
                }
            }
            catch (TargetInvocationException) { }
            catch (ArgumentException) { }
            return r;
        }

        // Llama a un metodo sin argumentos del objeto o de su jerarquia, devolviendo su texto.
        static string LlamaSinArgs(object o, string metodo)
        {
            try
            {
                for (Type t = o.GetType(); t != null; t = t.BaseType)
                {
                    MethodInfo m = t.GetMethod(metodo, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy, null, Type.EmptyTypes, null);
                    if (m != null) { object v = m.Invoke(m.IsStatic ? null : o, null); return v == null ? null : v.ToString(); }
                }
            }
            catch (TargetInvocationException) { }
            catch (ArgumentException) { }
            return null;
        }

        // Campo de texto buscado en toda la jerarquia (los campos privados de la base no salen con GetField).
        static string LeeCampoTexto(object o, string campo)
        {
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(campo, Aux.Todos | BindingFlags.DeclaredOnly);
                if (f != null) { object v = f.GetValue(o); return v == null ? null : v.ToString(); }
            }
            return null;
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

        // Sonda SOLO LECTURA de la cola de peticiones REAL (verifica API sin tocar estado).
        // Firmas LEIDAS del binario 0.31.5.3: PetitionManager.Instance, .petitionQueue,
        // .<petition>k__BackingField, Petition.Complete(). NO llama a Complete() ni SetPetition().
        public static void Peticiones()
        {
            try
            {
                var t = BuscaTipos("PetitionManager");
                if (t == null) { UnityEngine.Debug.Log("[Pecera] sonda Peticiones: no existe PetitionManager"); return; }
                var pi = t.GetProperty("Instance", Aux.Todos | System.Reflection.BindingFlags.FlattenHierarchy);
                object mgr = pi != null ? pi.GetValue(null, null) : null;
                if (mgr == null) { UnityEngine.Debug.Log("[Pecera] sonda Peticiones: Instance null"); return; }

                var fQueue = t.GetField("petitionQueue", Aux.Todos);
                var q = fQueue != null ? fQueue.GetValue(mgr) as System.Collections.IEnumerable : null;
                int n = 0; var tipos = new System.Collections.Generic.List<string>();
                if (q != null) foreach (var it in q)
                {
                    n++;
                    var fT = it != null ? it.GetType().GetField("type", Aux.Todos) : null;
                    var fP = it != null ? it.GetType().GetField("petitioner", Aux.Todos) : null;
                    object tv = fT != null ? fT.GetValue(it) : null;
                    object pv = fP != null ? fP.GetValue(it) : null;
                    tipos.Add((tv != null ? tv.ToString() : "?") + "/" + (pv != null ? pv.ToString() : "?"));
                }

                var fCur = t.GetField("<petition>k__BackingField", Aux.Todos);
                object cur = fCur != null ? fCur.GetValue(mgr) : null;
                bool complete = false;
                if (cur != null)
                {
                    var m = cur.GetType().GetMethod("Complete", Aux.Todos);
                    var pC = cur.GetType().GetProperty("complete", Aux.Todos);
                    complete = pC != null ? (bool)pC.GetValue(cur, null) : (m != null);
                }

                // Verificacion POR TIPO (no depende de haber peticion activa): Petition.Complete()
                // y los campos de QueuedPetition (type/petitioner/context/at).
                var tPet = BuscaTipos("Petition");
                var tQueued = BuscaTipos("QueuedPetition");
                bool petComplete = tPet != null && tPet.GetMethod("Complete", Aux.Todos) != null;
                bool petCompleteProp = tPet != null &&
                    (tPet.GetProperty("complete", Aux.Todos) != null ||
                     tPet.GetField("complete", Aux.Todos) != null);
                string qCampos = "";
                if (tQueued != null)
                    qCampos = string.Join(",", new string[] {
                        (tQueued.GetField("type", Aux.Todos) != null ? "type" : "-"),
                        (tQueued.GetField("petitioner", Aux.Todos) != null ? "petitioner" : "-"),
                        (tQueued.GetField("context", Aux.Todos) != null ? "context" : "-"),
                        (tQueued.GetField("at", Aux.Todos) != null ? "at" : "-") });

                UnityEngine.Debug.Log("[Pecera] sonda Peticiones: petition.Complete()=" + petComplete +
                    " propComplete=" + petCompleteProp + " queuedPetition campos={" + qCampos + "}");

                UnityEngine.Debug.Log("[Pecera] sonda Peticiones: cola=" + n + " tipos=" +
                    string.Join(";", tipos.ToArray()) +
                    " actual=" + (cur != null ? "si" : "no") + " complete=" + complete +
                    " tenerCompleta=" + (cur != null ? cur.GetType().GetMethod("Complete", Aux.Todos) != null : false));
            }
            catch (System.Exception e) { UnityEngine.Debug.Log("[Pecera] sonda Peticiones FALLO: " + e.GetType().Name + ": " + e.Message); }
        }

        static Type BuscaTipos(string n)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { try { var t = a.GetType(n); if (t != null) return t; } catch { } }
            return null;
        }
    }
}
