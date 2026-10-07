using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Pecera.Core
{
    // Reloj inyectable: los tests y el simulador avanzan el tiempo a mano.
    public interface IClock
    {
        long NowTicks { get; }
    }

    public sealed class SystemClock : IClock
    {
        public long NowTicks { get { return DateTime.UtcNow.Ticks; } }
    }

    public sealed class ManualClock : IClock
    {
        public long Ticks;
        public long NowTicks { get { return Ticks; } }
        public void AdvanceSeconds(double s) { Ticks += (long)(s * TimeSpan.TicksPerSecond); }
    }

    // Almacenamiento por lineas (jsonl). Sin tocar el disco en tests.
    public interface IStorage
    {
        bool Exists(string name);
        string[] ReadLines(string name);
        void Append(string name, string line);
        void Rewrite(string name, IEnumerable<string> lines);
        long Length(string name);
        void Delete(string name);
    }

    public sealed class MemoryStorage : IStorage
    {
        public readonly Dictionary<string, List<string>> Files = new Dictionary<string, List<string>>();
        public bool Exists(string name) { return Files.ContainsKey(name); }
        public string[] ReadLines(string name)
        {
            List<string> l;
            return Files.TryGetValue(name, out l) ? l.ToArray() : new string[0];
        }
        public void Append(string name, string line)
        {
            List<string> l;
            if (!Files.TryGetValue(name, out l)) { l = new List<string>(); Files[name] = l; }
            l.Add(line);
        }
        public void Rewrite(string name, IEnumerable<string> lines) { Files[name] = new List<string>(lines); }
        public long Length(string name)
        {
            List<string> l;
            long n = 0;
            if (Files.TryGetValue(name, out l)) foreach (var x in l) n += x.Length + 1;
            return n;
        }
        public void Delete(string name) { Files.Remove(name); }
    }

    // Disco real: UTF-8 sin BOM, escritura serializada, rotacion por tamano, y
    // reescritura atomica (temporal + reemplazo) para que un cierre brusco del juego
    // nunca deje un fichero a medias.
    public sealed class DiskStorage : IStorage
    {
        readonly string dir;
        readonly long maxBytes;
        readonly object cerrojo = new object();
        static readonly Encoding Utf8 = new UTF8Encoding(false);

        public DiskStorage(string dir, long rotateBytes)
        {
            this.dir = dir;
            this.maxBytes = rotateBytes;
            Directory.CreateDirectory(dir);
        }

        string P(string name) { return Path.Combine(dir, name); }

        public bool Exists(string name) { return File.Exists(P(name)); }

        public string[] ReadLines(string name)
        {
            lock (cerrojo)
            {
                return File.Exists(P(name)) ? File.ReadAllLines(P(name), Utf8) : new string[0];
            }
        }

        public void Append(string name, string line)
        {
            lock (cerrojo)
            {
                var info = new FileInfo(P(name));
                if (maxBytes > 0 && info.Exists && info.Length > maxBytes)
                {
                    string viejo = P(name) + ".1";
                    if (File.Exists(viejo)) File.Delete(viejo);
                    File.Move(P(name), viejo);
                }
                File.AppendAllText(P(name), line + "\n", Utf8);
            }
        }

        public void Rewrite(string name, IEnumerable<string> lines)
        {
            lock (cerrojo)
            {
                string tmp = P(name) + ".tmp";
                var sb = new StringBuilder();
                foreach (var l in lines) sb.Append(l).Append('\n');
                File.WriteAllText(tmp, sb.ToString(), Utf8);
                if (File.Exists(P(name))) File.Delete(P(name));
                File.Move(tmp, P(name));
            }
        }

        public long Length(string name)
        {
            var info = new FileInfo(P(name));
            return info.Exists ? info.Length : 0;
        }

        public void Delete(string name)
        {
            lock (cerrojo) { if (File.Exists(P(name))) File.Delete(P(name)); }
        }
    }

    // Generador determinista (xorshift): el simulador y los tests con semilla fija.
    public sealed class Rng
    {
        ulong s;
        public Rng(int seed) { s = unchecked((ulong)(uint)seed * 2654435761UL + 88172645463325252UL); if (s == 0) s = 1; }
        public ulong NextU()
        {
            s ^= s << 13; s ^= s >> 7; s ^= s << 17;
            return s;
        }
        public double Next() { return (NextU() >> 11) / (double)(1UL << 53); }
        public int Next(int n) { return n <= 0 ? 0 : (int)(NextU() % (ulong)n); }
        public bool Chance(double p) { return Next() < p; }
        public double Range(double a, double b) { return a + (b - a) * Next(); }
    }
}
