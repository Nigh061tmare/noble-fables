using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using HarmonyLib;

namespace Pecera
{
    // =========================================================================
    //  PARCHES  -  los cuatro momentos donde el juego "cuenta algo"
    // =========================================================================
    //  No sustituimos el comportamiento: solo le pegamos una pegatina con lo
    //  que se dicen. Si algo falla, el juego sigue igual.
    // =========================================================================

    public static class Parche
    {
        const string INSTRUCCIONES =
          "Eres el narrador de una VILLA MEDIEVAL de los siglos XIII al XV, en la "
          + "Peninsula Iberica. Escribes UNA replica breve que un aldeano pronuncia "
          + "AHORA MISMO. OBLIGATORIO:\n"
          + "- Responde SIEMPRE en espanol.\n"
          + "- UNA o DOS frases. Ni una mas.\n"
          + "- Lenguaje de epoca: 'vosotros', 'don', 'dona', 'senor', 'hermano', "
          + "'vecino'. NO uses lenguaje moderno ni colloquial actual (nada de "
          + "'puta', 'gime', 'chaval', 'tio', 'crack', 'flipar', 'mola'). "
          + "Nada de insultos familiares.\n"
          + "- Sin markdown, sin comillas, sin emojis.\n"
          + "- NO menciones que eres una IA.\n"
          + "- NO controles al otro personaje: solo su replica.\n"
          + "- Responde SOLO el JSON {\"texto\":\"...\",\"motivo\":\"...\"}.\n"
          + "  El campo MOTIVO es lo que el aldeano PENSE de verdad y jamas diria: "
          + "una frase breve. El TEXTO es lo que sale de su boca ante el otro.";

        static readonly string[] Rasgos = {
            "Agresivo", "Ansioso", "Caotico", "Amigable", "Amoroso"
        };

        // -------------------------------------------------------------------
        //  Discussion entre dos NPCs
        // -------------------------------------------------------------------
        [HarmonyPatch(typeof(PersonalRelationship), "SetPerformedArgument")]
        static class Discusion
        {
            static void Postfix(PersonalRelationship __instance)
            {
                Pide(__instance, "discusion");
            }
        }

        // -------------------------------------------------------------------
        //  Pelea entre dos NPCs
        // -------------------------------------------------------------------
        [HarmonyPatch(typeof(PersonalRelationship), "SetPerformedFight")]
        static class Pelea
        {
            static void Postfix(PersonalRelationship __instance)
            {
                Pide(__instance, "pelea");
            }
        }

        // -------------------------------------------------------------------
        //  Se enamoran: solo cuando el resultado es true
        // -------------------------------------------------------------------
        [HarmonyPatch(typeof(PersonalRelationship), "TryCreateRomance")]
        static class Romance
        {
            static void Postfix(PersonalRelationship __instance, bool __result)
            {
                if (__result) Pide(__instance, "romance");
            }
        }

        // -------------------------------------------------------------------
        //  El nucleo: monta el contexto, pregunta al LLM en segundo plano
        //  y guarda el resultado. Nunca bloquea el juego.
        // -------------------------------------------------------------------
        static void Pide(PersonalRelationship rel, string tipo)
        {
            if (!Plugin.Encendido) return;

            string a, b, rolA, rolB, prompt;
            float fval = 0f;
            int idA = 0, idB = 0;
            try
            {
                // El que 'inicia' es participantA en la mayoria de los casos;
                // usamos la afinidad para decidir quien habla.
                var A = rel.participantA;
                var B = rel.participantB;
                if (A == null || B == null) return;

                a = Nombre(A); b = Nombre(B);
                rolA = Rasgo(A) + ", " + Clase(A);
                rolB = Rasgo(B) + ", " + Clase(B);
                var val = Campo(rel, "relationshipValue");
                try { fval = Convert.ToSingle(val, CultureInfo.InvariantCulture); } catch { }
                prompt = Monta(tipo, a, rolA, b, rolB, fval);
                try { idA = Convert.ToInt32(Campo(A, "id")); idB = Convert.ToInt32(Campo(B, "id")); } catch { }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log("[Pecera] no pude leer el contexto: " + e.Message);
                return;
            }

            var t = new Thread(() => { Trabaja(tipo, a, b, prompt, fval, rel, idA, idB); });
            t.IsBackground = true;
            t.Start();
        }

        static void Trabaja(string tipo, string a, string b, string prompt,
                            float val, PersonalRelationship rel, int idA, int idB)
        {
            string txt = null;
            try { txt = Llm.Ask(INSTRUCCIONES, prompt); }
            catch { }

            // La AFINIDAD es lo que mide la propagacion: si cae tras una pelea
            // y sube tras un romance, la society se esta moviendo de verdad.
            string despues = "?";
            try { despues = Campo(rel, "relationshipValue").ToString(); }
            catch { }
            var senales = Flags(rel);
            // Los secretos: chance de que uno le cuente al otro lo que sea.
            try
            {
                // ids ya resueltos arriba
                Secretos.SeCuenta(idA, idB, tipo, val);
            }
            catch { }
            var motivo = Llm.Extrae(txt ?? "", "motivo");
            var dicese = Llm.Extrae(txt ?? "", "texto");
            if (string.IsNullOrEmpty(dicese)) dicese = txt ?? "";

            var linea = string.Format(CultureInfo.InvariantCulture,
                "{{\"t\":\"{0}\",\"tipo\":\"{1}\",\"a\":\"{2}\",\"b\":\"{3}\","
                + "\"af_antes\":{4},\"af_despues\":\"{5}\",\"senales\":\"{6}\","
                + "\"dice\":\"{7}\",\"piensa\":\"{8}\"}}",
                DateTime.Now.ToString("s"), tipo, a, b, val, despues, senales,
                Limpia(dicese), Limpia(motivo));

            try
            {
                File.AppendAllText(Path.Combine(Plugin.CarpetaLogs, "dialogos.jsonl"),
                                   linea + Environment.NewLine, Encoding.UTF8);
            }
            catch { }

            UnityEngine.Debug.Log("[Pecera] " + tipo + ": " + a + " -> " + b +
                                 " | " + (txt ?? "(sin respuesta)"));

            // Que se vea en pantalla. Va en el hilo principal porque Unity no
            // deja tocar la UI desde un hilo secundario; lo metemos en la cola.
            if (!string.IsNullOrEmpty(dicese))
                A_la_cola(a + ": " + dicese);
        }

        static string Monta(string tipo, string a, string rolA, string b,
                            string rolB, float val)
        {
            string fait;
            switch (tipo)
            {
                case "pelea":
                    fait = "Acaban de PEGARSE. Grita, amenaza o se burla.";
                    break;
                case "romance":
                    fait = "Van a ENAMORARSE. Declara tu sentimiento con timidez.";
                    break;
                default:
                    fait = "Acaban de DISPUTIR en publico. Protesta o se justifica.";
                    break;
            }
            var afin = val < -20 ? "se odian" :
                       val < 0 ? "no se llevan bien" :
                       val > 20 ? "se quieren mucho" : "se Carry on";
            var sb = new StringBuilder();
            sb.AppendLine("Hecho: " + fait);
            sb.AppendLine("Aldeano que habla: " + a + " (" + rolA + ")");
            sb.AppendLine("El otro: " + b + " (" + rolB + ")");
            sb.AppendLine("Su relacion: " + afin + " (afinidad " +
                          val.ToString("0", CultureInfo.InvariantCulture) + ")");
            sb.AppendLine("Escribe la replica de " + a + ".");
            return sb.ToString();
        }

        // Las señales asimetricas: puede ser que A admire a B y B lo odie.
        // Si las dos se mueven a la vez, hay un chisme en marcha.
        static string Flags(PersonalRelationship rel)
        {
            var sb = new StringBuilder();
            try
            {
                if (Convert.ToBoolean(Campo(rel, "specialInterestAtoB"))) sb.Append("A->B ");
                if (Convert.ToBoolean(Campo(rel, "specialInterestBtoA"))) sb.Append("B->A ");
                if (Convert.ToBoolean(Campo(rel, "wasPartners"))) sb.Append("pareja ");
                if (Convert.ToBoolean(Campo(rel, "wasRomance"))) sb.Append("romance ");
                if (Convert.ToBoolean(Campo(rel, "badAncestry"))) sb.Append("mala_sangre ");
            }
            catch { }
            var s = sb.ToString().Trim();
            return s.Length == 0 ? "-" : s;
        }

        public static string Nombre(BaseNPC n)
        {
            try
            {
                var m = n.GetType().GetMethod("GetFullNameWithTitle",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m != null)
                {
                    var s = m.Invoke(n, null) as string;
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { }
            var fn = Campo(n, "firstName") as string;
            var ln = Campo(n, "fixedLastName") as string;
            return ((fn ?? "?") + " " + (ln ?? "")).Trim();
        }

        static string Rasgo(BaseNPC n)
        {
            var v = Campo(n, "personalityType");
            int i = 0;
            try { i = Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { }
            return (i >= 0 && i < Rasgos.Length) ? Rasgos[i] : "?";
        }

        static string Clase(BaseNPC n)
        {
            var v = Campo(n, "socialClassType");
            return v == null ? "plebeyo" : v.ToString();
        }

        // Los campos del juego son privados y varios viven en la clase BASE,
        // asi que un GetField normal sobre la subclase no los encuentra.
        // Hay que subir por la jerarquia.
        public static string Limpia(string s)
        {
            return (s ?? "").Replace("\n", " ").Replace("\r", " ").Replace("\"", "'").Trim();
        }

        // Unity solo deja tocar la interfaz desde el hilo principal. El LLM
        // responde en un hilo secundario, asi que encolamos el texto y lo
        // pintamos desde el propio bucle del juego.
        static readonly System.Collections.Concurrent.ConcurrentQueue<string>
            COLA = new System.Collections.Concurrent.ConcurrentQueue<string>();

        public static void A_la_cola(string texto)
        {
            COLA.Enqueue(texto);
        }

        public static void VaciaCola()
        {
            string t;
            while (COLA.TryDequeue(out t)) Burbujas.Muestra(t);
        }

        public static object Campo(object o, string nombre)
        {
            for (var t = o.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(nombre,
                        BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(o);
            }
            return null;
        }
    }
}