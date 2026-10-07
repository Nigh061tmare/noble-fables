using System;
using System.IO;
using System.Text;
using Pecera.Core;

namespace PeceraNF
{
    // Consola por FICHERO: escribes comandos en pecera_datos/consola.txt, el mod los ejecuta
    // (revisa cada 5 s), los borra del fichero y deja la respuesta en consola_salida.txt.
    // Se eligio fichero y no un cuadro de texto en pantalla porque IMGUI no puede capturar el
    // teclado sin que el juego tambien reciba esas teclas (NO verificado como evitarlo).
    public static class Consola
    {
        static DateTime _fecha = DateTime.MinValue;
        static readonly Pecera.Core.Consola Motor = new Pecera.Core.Consola(new Anfitrion());

        public static void Atiende()
        {
            try
            {
                string r = Path.Combine(Estado.Datos, "consola.txt");
                if (!File.Exists(r))
                {
                    Estado.Escribe(r, "# Comandos de Pecera, uno por linea. Escribe 'ayuda' para la lista. Se ejecutan solos y se borran.\r\n");
                    return;
                }
                DateTime t = File.GetLastWriteTimeUtc(r);
                if (t == _fecha) return;
                var salida = new StringBuilder();
                bool hubo = false;
                foreach (string l in File.ReadAllLines(r, new UTF8Encoding(false)))
                {
                    string x = l.Trim();
                    if (x.Length == 0 || x[0] == '#') continue;
                    hubo = true;
                    salida.Append(DateTime.Now.ToString("HH:mm:ss")).Append(" > ").Append(x).Append("\r\n").Append(Motor.Ejecuta(x)).Append("\r\n");
                }
                Estado.Escribe(r, "# Comandos de Pecera, uno por linea. Escribe 'ayuda' para la lista. Se ejecutan solos y se borran.\r\n");
                _fecha = File.GetLastWriteTimeUtc(r);
                if (hubo) File.AppendAllText(Path.Combine(Estado.Datos, "consola_salida.txt"), salida.ToString(), new UTF8Encoding(false));
            }
            catch (IOException e) { Estado.Avisos.Add("consola: " + e.Message); }
        }

        sealed class Anfitrion : IConsolaHost
        {
            public string Modo
            {
                get { return Estado.Cfg.Modo; }
                set { string err; Estado.Cfg.Set("modo", value, out err); }
            }

            string IConsolaHost.Estado()
            {
                return "modo " + Estado.Cfg.Modo + ", LLM " + (Estado.Salud.Degradado ? "DEGRADADO" : "ok") + ", escritura " + (Estado.EscrituraSegura ? "segura" : "BLOQUEADA")
                     + ", pawns " + Estado.Fichas.Count + ", pares " + Estado.Afectos.Pares + ", decisiones pendientes " + Estado.Compuerta.Pendientes().Count;
            }

            public bool Veta(int id) { return Estado.Compuerta.Veta(id); }

            public string Pendientes()
            {
                var l = Estado.Compuerta.Pendientes();
                if (l.Count == 0) return "ninguna";
                var sb = new StringBuilder();
                foreach (var d in l) sb.Append('#').Append(d.Id).Append(' ').Append(d.Clase).Append(": ").Append(d.Descripcion).Append(" (").Append(d.Razon).Append(")\r\n");
                return sb.ToString().TrimEnd();
            }

            public void PonDirectriz(string ambito, string clave, string texto)
            {
                if (ambito == "global") Estado.Dir.Global = texto;
                else if (ambito == "faccion") Estado.Dir.PorFaccion[clave] = texto;
                else Estado.Dir.PorPawn[clave] = texto;
                Guarda();
            }

            public string QuitaDirectriz(string ambito, string clave)
            {
                string previo = null;
                if (ambito == "global") { previo = Estado.Dir.Global.Length > 0 ? Estado.Dir.Global : null; Estado.Dir.Global = ""; }
                else
                {
                    var d = ambito == "faccion" ? Estado.Dir.PorFaccion : Estado.Dir.PorPawn;
                    if (d.TryGetValue(clave, out previo)) d.Remove(clave);
                }
                if (previo != null) Guarda();
                return previo;
            }

            static void Guarda()
            {
                Estado.Escribe(Path.Combine(Estado.Datos, "directriz.txt"), Estado.Dir.Render());
                Estado.CargaDirectriz();
            }
        }
    }
}
