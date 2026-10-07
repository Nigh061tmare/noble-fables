# -*- coding: utf-8 -*-
"""
AGENTE LOCAL PARA GOING MEDIEVAL
================================
Sustituye a Claude Code + Docker + MCP por un bucle local con Ollama.

Como funciona (arquitectura del mod GoingMedievalMCP):
  * El plugin BepInEx escribe JSON con el estado de la colonia.
  * El plugin LEE command.json y ejecuta la orden (say / set_priority / ...).
  * Aqui leemos esos JSON, le preguntamos a Ollama, y escribimos el comando.

Asi los colonos hablan solos, en espanol, sin nube y sin coste.
"""
import json, os, sys, time, random, urllib.request

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

MCP = os.path.join(os.environ["USERPROFILE"], "AppData", "LocalLow",
                   "Foxy Voxel", "Going Medieval", "mcp")
PROXY = "http://127.0.0.1:11436/v1/chat/completions"
MODELO = "qwen3.5:9b"
CADA = 25          # segundos entre turnos
MAX_TEXTO = 160    # longitud maxima de una frase


def leer(nombre, defecto=None):
    """Lee un json del plugin.

    OJO: .NET (el plugin) y PowerShell escriben BOM al principio del fichero.
    Con encoding='utf-8' a secas, json.load revienta con BOM. 'utf-8-sig' lo
    tolera tanto con BOM como sin el. Es un fallo real que aparecio al probar.
    """
    p = os.path.join(MCP, nombre)
    try:
        with open(p, "r", encoding="utf-8-sig") as f:
            return json.load(f)
    except Exception:
        return defecto


def escribir_comando(obj):
    # 'utf-8' en Python NUNCA escribe BOM: el plugin lo leera sin problema.
    p = os.path.join(MCP, "command.json")
    with open(p, "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False)
    return p
def preguntar(mensajes):
    """Llama a Ollama por el proxy. reasoning_effort=none es OBLIGATORIO:
    sin el, Qwen 3.5 devuelve el content VACIO (se lo gasta razonando)."""
    cuerpo = json.dumps({
        "model": MODELO,
        "messages": mensajes,
        "temperature": 0.9,
        "max_tokens": 120,
        "reasoning_effort": "none",
        "think": False,
    }).encode("utf-8")
    req = urllib.request.Request(PROXY, data=cuerpo,
                                 headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=120) as r:
        j = json.loads(r.read().decode("utf-8"))
    return (j["choices"][0]["message"].get("content") or "").strip()
def contexto():
    """Resumen del estado de la colonia tal y como lo ve el plugin."""
    sett = leer("settlers.json", []) or []
    ev = leer("events.json", []) or []
    warn = leer("warnings.json", []) or []
    soc = leer("social.json", []) or []
    lineas = []
    for s in sett[:8]:
        if isinstance(s, dict):
            n = s.get("name") or s.get("Name") or "?"
            m = s.get("mood") or s.get("Mood") or "?"
            lineas.append("- %s (animo: %s)" % (n, m))
    if ev:
        lineas.append("Ultimos sucesos: " + "; ".join(str(x)[:60] for x in ev[-3:]))
    if warn:
        lineas.append("Avisos: " + "; ".join(str(x)[:60] for x in warn[-3:]))
    return sett, "\n".join(lineas)


def turno():
    sett, ctx = contexto()
    if not sett:
        return "sin datos (carga una partida en el juego)"
    s = random.choice([x for x in sett if isinstance(x, dict)])
    nombre = s.get("name") or s.get("Name") or "?"
    msgs = [
        {"role": "system", "content":
            "Eres un colono medieval en un asentamiento de Going Medieval. "
            "Hablas en espanol, en primera persona, con naturalidad y sin ser "
            "cursi. UNA sola frase de menos de 20 palabras. Nunca menciones "
            "que eres una IA. No uses asteriscos ni acotaciones."},
        {"role": "user", "content":
            "Estado de la colonia:\n%s\n\nDi en voz alta algo que %s diria "
            "ahora mismo, segun su animo y lo que esta pasando." % (ctx, nombre)},
    ]
    frase = preguntar(msgs)
    frase = frase.replace("\n", " ").strip().strip('"')[:MAX_TEXTO]
    if not frase:
        return "respuesta vacia (revisa que el proxy este en 11436)"
    escribir_comando({"type": "say", "settler": nombre, "message": frase})
    return "%s: %s" % (nombre, frase)
# ---------------------------------------------------------------------------
#  MENSAJES DEL JUGADOR
#  ---------------------------------------------------------------------------
#  El plugin anade un CHAT al panel Social del juego. Cuando escribes a un
#  colono, deja el mensaje en player_message.json. Aqui lo leemos y el colono
#  te responde con una burbuja en el juego.
# ---------------------------------------------------------------------------
_ultimo_jugador = [0.0]


def mensaje_del_jugador():
    m = leer("player_message.json")
    if not m:
        return None
    t = float(m.get("time") or 0)
    if t <= _ultimo_jugador[0] or not m.get("message"):
        return None
    _ultimo_jugador[0] = t
    return m


def responder_al_jugador(m):
    de = m.get("from", "alguien")
    texto = m.get("message", "")
    sett = leer("settlers.json", []) or []
    yo = None
    for s in sett:
        if (s.get("name") or s.get("Name")) == de:
            yo = s
            break
    ficha = ""
    if yo:
        ficha = ("%s: animo %s, salud %s, hambre %s." % (
            de, yo.get("mood", yo.get("Mood", "?")),
            yo.get("health", yo.get("Health", "?")),
            yo.get("hunger", yo.get("Hunger", "?"))))
    sys_p = (
        "Eres %s, un colono de un asentamiento medieval. El jugador (tu se\u00f1or) "
        "te acaba de hablar. Responde EN ESPA\u00d1OL, en PRIMERA persona, en UNA sola "
        "frase corta y natural. Se leal pero con caracter: puedes quejarte, pedir algo "
        "o comentar tu estado. %s No uses comillas ni asteriscos." % (de, ficha))
    frase = preguntar([{"role": "system", "content": sys_p},
                       {"role": "user", "content": texto}])
    frase = (frase or "").replace("\n", " ").strip().strip('"')[:MAX_TEXTO]
    if not frase:
        return "no responde"
    escribir_comando({"type": "say", "settler": de, "message": frase})
    return "%s responde al se\u00f1or: %s" % (de, frase)


PAUSA = 25          # segundos entre frases


def main():
    print("=" * 66)
    print("  AGENTE LOCAL PARA GOING MEDIEVAL")
    print("=" * 66)
    print("  carpeta del plugin : %s" % MCP)
    print("  existe             : %s" % os.path.isdir(MCP))
    print("  modelo             : %s" % MODELO)
    print()

    try:
        urllib.request.urlopen(PROXY.replace("/v1/chat/completions", "/v1/models"),
                               timeout=6)
        print("  proxy 11436        : OK")
    except Exception as e:
        print("  proxy 11436        : NO responde (%s)" % e)
        print("\n  -> arranca antes:  ARRANCAR_IA_KENSHI.bat")
        return

    if not os.path.isdir(MCP):
        print("\n  La carpeta del plugin no existe todavia.")
        print("  -> abre Going Medieval y CARGA UNA PARTIDA.")
        print("     El plugin crea la carpeta al empezar a jugar.")
        return

    print("  Ctrl+C para parar. Una frase cada %d s.\n" % PAUSA)

    # modo de una sola frase:  python agente_gm.py una
    if len(sys.argv) > 1 and sys.argv[1] == "una":
        print("  [1] %s" % turno())
        return

    n = 0
    try:
        while True:
            n += 1
            # 1) si el jugador ha escrito, se responde SIEMPRE primero
            m = mensaje_del_jugador()
            try:
                r = responder_al_jugador(m) if m else turno()
            except Exception as e:
                r = "error: %s" % e
            print("  [%3d] %s" % (n, r))
            time.sleep(PAUSA)
    except KeyboardInterrupt:
        print("\n  parado.")


if __name__ == "__main__":
    main()




