using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Pecera
{
    // Acceso unico al estado vivo del juego. Rutas verificadas en el binario:
    //   PlayerManager.rulingActiveOrganization -> ActiveOrganization
    //   ActiveOrganization.worldOrganization   -> WorldOrganization (workgroups)
    //   ActiveOrganization.GetActiveMembers()  -> NPCs de la villa
    //   BaseNPC.personalRelationships          -> Dictionary de relaciones
    public static class Mundo
    {
        const BindingFlags TODO = BindingFlags.Instance | BindingFlags.Static |
                                  BindingFlags.Public | BindingFlags.NonPublic;

        public static Type Tipo(string n)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = a.GetType(n); } catch { }
                if (t != null) return t;
            }
            return null;
        }

        // Singleton: campo estatico del propio tipo; si no, objeto vivo en escena.
        public static object Instancia(string nombre)
        {
            var t = Tipo(nombre);
            if (t == null) return null;
            foreach (var f in t.GetFields(TODO))
                if (f.IsStatic && f.FieldType == t)
                {
                    var v = f.GetValue(null);
                    if (v != null) return v;
                }
            try
            {
                var objs = UnityEngine.Object.FindObjectsOfType(t);
                if (objs != null && objs.Length > 0) return objs[0];
            }
            catch { }
            return null;
        }

        public static object Campo(object o, string nombre)
        {
            if (o == null) return null;
            for (var t = o.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(nombre, TODO | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(o);
            }
            return null;
        }

        public static object Llama(object o, string metodo)
        {
            if (o == null) return null;
            for (var t = o.GetType(); t != null; t = t.BaseType)
            {
                var m = t.GetMethod(metodo, TODO | BindingFlags.DeclaredOnly,
                                    null, Type.EmptyTypes, null);
                if (m != null) return m.Invoke(m.IsStatic ? null : o, null);
            }
            return null;
        }

        public static object OrgActiva()
        {
            return Campo(Instancia("PlayerManager"), "rulingActiveOrganization");
        }

        public static object OrgMundo()
        {
            return Campo(OrgActiva(), "worldOrganization");
        }

        // De un ActiveNPC/WorldNPC saca el BaseNPC (campo de ese tipo en la jerarquia).
        public static BaseNPC Base(object o)
        {
            if (o == null) return null;
            var b = o as BaseNPC;
            if (b != null) return b;
            for (var t = o.GetType(); t != null; t = t.BaseType)
                foreach (var f in t.GetFields(TODO | BindingFlags.DeclaredOnly))
                    if (typeof(BaseNPC).IsAssignableFrom(f.FieldType))
                    {
                        var v = f.GetValue(o) as BaseNPC;
                        if (v != null) return v;
                    }
            return null;
        }

        public static List<BaseNPC> Npcs()
        {
            var r = new List<BaseNPC>();
            try
            {
                var lista = Llama(OrgActiva(), "GetActiveMembers") as IEnumerable;
                if (lista == null) return r;
                foreach (var o in lista)
                {
                    var b = Base(o);
                    if (b != null && !r.Contains(b)) r.Add(b);
                }
            }
            catch { }
            return r;
        }

        public static List<PersonalRelationship> Relaciones()
        {
            var r = new List<PersonalRelationship>();
            try
            {
                foreach (var n in Npcs())
                {
                    var d = Campo(n, "personalRelationships") as IDictionary;
                    if (d == null) continue;
                    foreach (var v in d.Values)
                    {
                        var p = v as PersonalRelationship;
                        if (p != null && !r.Contains(p)) r.Add(p);
                    }
                }
            }
            catch { }
            return r;
        }
    }
}
