using System;
using System.Collections.Generic;
using System.Text;

namespace Pecera.Core
{
    // Telemetria de verificacion (K). El mod registra por si mismo la evidencia de que
    // cada funcion hizo lo que debia, y vuelca UN fichero (verificacion.json) que el
    // usuario puede devolver. Cada funcion tiene un "criterio": numero minimo de
    // exitos y numero maximo de fallos para considerarla VERIFICADA EN PARTIDA.
    public sealed class Evidence
    {
        sealed class Slot
        {
            public string Name;
            public int Ok, Fail, MinOk, MaxFail;
            public long FirstTicks, LastTicks;
            public string LastDetail = "", LastError = "";
        }

        readonly Dictionary<string, Slot> slots = new Dictionary<string, Slot>();
        readonly IClock clock;
        readonly object cerrojo = new object();

        public Evidence(IClock clock) { this.clock = clock; }

        // Declara un criterio de aceptacion. Idempotente.
        public void Criterio(string name, int minOk, int maxFail)
        {
            lock (cerrojo)
            {
                var s = Get(name);
                s.MinOk = minOk; s.MaxFail = maxFail;
            }
        }

        public void Ok(string name, string detail)
        {
            lock (cerrojo)
            {
                var s = Get(name);
                s.Ok++;
                long t = clock.NowTicks;
                if (s.FirstTicks == 0) s.FirstTicks = t;
                s.LastTicks = t;
                s.LastDetail = Cut(detail);
            }
        }

        public void Fail(string name, string error)
        {
            lock (cerrojo)
            {
                var s = Get(name);
                s.Fail++;
                s.LastTicks = clock.NowTicks;
                s.LastError = Cut(error);
            }
        }

        public int OkCount(string name)
        {
            lock (cerrojo) { Slot s; return slots.TryGetValue(name, out s) ? s.Ok : 0; }
        }

        public int FailCount(string name)
        {
            lock (cerrojo) { Slot s; return slots.TryGetValue(name, out s) ? s.Fail : 0; }
        }

        // "pasa": criterio cumplido con esta evidencia. "sin_datos": nunca se ejercito.
        public string Veredicto(string name)
        {
            lock (cerrojo)
            {
                Slot s;
                if (!slots.TryGetValue(name, out s) || (s.Ok == 0 && s.Fail == 0)) return "sin_datos";
                if (s.Fail > s.MaxFail) return "falla";
                return s.Ok >= s.MinOk ? "pasa" : "insuficiente";
            }
        }

        static string Cut(string s)
        {
            if (s == null) return "";
            s = Json.UnaLinea(s);
            return s.Length > 200 ? s.Substring(0, 200) : s;
        }

        Slot Get(string name)
        {
            Slot s;
            if (!slots.TryGetValue(name, out s)) { s = new Slot { Name = name, MinOk = 1 }; slots[name] = s; }
            return s;
        }

        public string ToJson(string version, string extra)
        {
            lock (cerrojo)
            {
                var names = new List<string>(slots.Keys);
                names.Sort(StringComparer.Ordinal);
                var sb = new StringBuilder();
                sb.Append("{\"version\":\"").Append(Json.Escape(version)).Append("\",");
                sb.Append("\"generado\":\"").Append(new DateTime(clock.NowTicks, DateTimeKind.Utc).ToString("s")).Append("\",");
                if (!string.IsNullOrEmpty(extra)) sb.Append(extra).Append(',');
                sb.Append("\"funciones\":[");
                for (int i = 0; i < names.Count; i++)
                {
                    var s = slots[names[i]];
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"nombre\":\"").Append(Json.Escape(s.Name)).Append("\"");
                    sb.Append(",\"veredicto\":\"").Append(VeredictoSinLock(s)).Append("\"");
                    sb.Append(",\"ok\":").Append(s.Ok).Append(",\"fallos\":").Append(s.Fail);
                    sb.Append(",\"min_ok\":").Append(s.MinOk).Append(",\"max_fallos\":").Append(s.MaxFail);
                    sb.Append(",\"ultimo\":\"").Append(Json.Escape(s.LastDetail)).Append("\"");
                    sb.Append(",\"ultimo_error\":\"").Append(Json.Escape(s.LastError)).Append("\"}");
                }
                sb.Append("]}");
                return sb.ToString();
            }
        }

        static string VeredictoSinLock(Slot s)
        {
            if (s.Ok == 0 && s.Fail == 0) return "sin_datos";
            if (s.Fail > s.MaxFail) return "falla";
            return s.Ok >= s.MinOk ? "pasa" : "insuficiente";
        }
    }
}
