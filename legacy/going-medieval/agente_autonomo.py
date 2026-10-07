# -*- coding: utf-8 -*-
"""AGENTE AUTONOMO PARA GOING MEDIEVAL - bucle percepcion/razonamiento/accion."""
import json, os, re, sys, time, random, urllib.request

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass


class _SinSalida(object):
    """Tercera vez que nos muerde el mismo bug: con pythonw NO hay consola y
    sys.stdout es None, asi que el primer print() revienta el proceso.
    Aqui lo neutralizamos y de paso lo dejamos por escrito en el log."""

    def write(self, t):
        try:
            with open(os.path.join(AQUI, "agente.log"), "a", encoding="utf-8") as f:
                f.write(t)
        except Exception:
            pass

    def flush(self):
        pass


if sys.stdout is None:
    sys.stdout = _SinSalida()
if sys.stderr is None:
    sys.stderr = sys.stdout

AQUI = os.path.dirname(os.path.abspath(__file__))
MCP = os.path.join(os.environ["USERPROFILE"], "AppData", "LocalLow",
                   "Foxy Voxel", "Going Medieval", "mcp")
PROXY = "http://127.0.0.1:11436/v1/chat/completions"
MODELO = "qwen3.5:9b"
DIARIO = os.path.join(AQUI, "diario")
os.makedirs(DIARIO, exist_ok=True)
MAX_TEXTO = 170
PAUSA = 22
# por defecto ACTUA. Con --observar solo mira y cuenta lo que HARIA.
SOLO_OBSERVAR = "--observar" in sys.argv
# .NET escribe BOM: hay que leer con utf-8-sig o json.load revienta
def leer(nombre, defecto=None):
    """Lee un json del plugin.

    Dos trampas que descubrimos con datos REALES del juego:
      1. .NET escribe BOM -> hay que usar utf-8-sig
      2. Escribe los decimales con COMA segun el locale ("time":55,90242),
         y eso NO es JSON valido. Si falla, lo arreglamos y reintentamos.
    """
    try:
        t = open(os.path.join(MCP, nombre), encoding="utf-8-sig").read()
    except Exception:
        return defecto
    try:
        return json.loads(t)
    except Exception:
        pass
    # ":12,34"  ->  ":12.34"   (solo tras dos puntos: no toca las listas)
    t2 = re.sub(r':\s*(-?\d+),(\d+)', r':\1.\2', t)
    try:
        return json.loads(t2)
    except Exception:
        return defecto


def comando(obj):
    p = os.path.join(MCP, "command.json")
    with open(p, "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False)
    return p


def diario(nombre, linea):
    with open(os.path.join(DIARIO, nombre), "a", encoding="utf-8") as f:
        f.write(linea + "\n")


def ahora():
    return time.strftime("%H:%M:%S")
TRABAJOS = ["Construction", "Mining", "Digging", "PlantCutting", "Hauling",
            "Farming", "Cooking", "Crafting", "Smithing", "Tailoring",
            "Carpentry", "Research", "Cleaning", "Medicine",
            "AnimalHandling", "Hunting", "Fishing", "Butchering",
            "Healing", "Tending", "Refuel", "Harvesting", "Planting",
            "Logging", "Repair", "Training", "Trading", "Guarding"]


def limpiar(t):
    p = re.sub(r"[*_#\[\]()]", " ", str(t or ""))
    p = re.sub(r"\s+", " ", p).strip()
    return p[:MAX_TEXTO].strip().strip('"')


def colonos():
    """Nombres reales de los colonos, para no inventarnos ninguno."""
    s = leer("settlers.json", []) or []
    fuera = []
    for c in s:
        if isinstance(c, dict):
            n = c.get("name") or c.get("Name") or ""
            if n:
                fuera.append(n)
    return fuera
MEM = {"fallos": [], "ultimas": [], "objetivo": "", "n": 0}


def _stats(c):
    """Salud, hambre, animo y sueno de un colono (formato real del plugin)."""
    e = c.get("stats") or {}
    o = []
    for k in ("health", "hunger", "mood", "sleep"):
        d = e.get(k)
        if isinstance(d, dict) and d.get("cur") is not None:
            o.append("%s %s/%s" % (k, d.get("cur"), d.get("max")))
    return " ".join(o)


def _top(c, n=3):
    """Sus mejores habilidades: para eso sirve cada colono."""
    sk = c.get("skills") or {}
    pares = [(k, v) for k, v in sk.items() if isinstance(v, (int, float))]
    pares.sort(key=lambda x: -x[1])
    return ", ".join("%s:%s" % (k, v) for k, v in pares[:n])


def relaciones():
    """Parejas con afecto fuerte: amigos y ENEMIGOS. Es el motor del drama."""
    d = leer("social.json") or []
    pares, visto = [], set()
    for p in d:
        yo = p.get("settler") or p.get("name") or "?"
        for r in (p.get("relations") or []):
            otro = r.get("name") or "?"
            try:
                af = float(r.get("affection") or 0)
            except Exception:
                continue
            k = tuple(sorted((yo, otro)))
            if k in visto:
                continue
            visto.add(k)
            if af >= 20:
                pares.append("AMIGOS %s y %s (%.0f)" % (yo, otro, af))
            elif af <= -10:
                pares.append("ODIAN %s y %s (%.0f)" % (yo, otro, af))
    return pares[:10]


def contexto():
    """Resumen del mundo con los datos REALES del plugin."""
    lineas = []
    su = leer("summary.json") or {}
    if isinstance(su, dict):
        for k in ("buildings", "visitors"):
            if su.get(k) not in (None, "", []):
                lineas.append("%s: %s" % (k, str(su[k])[:220]))
    ps = leer("settlers.json") or []
    if isinstance(ps, list) and ps:
        lineas.append("COLONOS (%d):" % len(ps))
        for c in ps[:12]:
            if isinstance(c, dict):
                lineas.append("  %s [%s] %s | mejor en: %s"
                              % (c.get("name"), c.get("status"),
                                 _stats(c), _top(c)))
    sc = leer("social.json", []) or []
    if sc:
        lineas.append("SOCIAL: " + json.dumps(sc, ensure_ascii=False)[:420])
    ev = leer("events.json", []) or []
    if ev:
        lineas.append("EVENTOS: " + json.dumps(ev[-6:], ensure_ascii=False)[:380])
    rel = relaciones()
    if rel:
        lineas.append("RELACIONES (motor del drama): " + " | ".join(rel))
    av = leer("warnings.json", []) or []
    if av:
        lineas.append("AVISOS: " + json.dumps(av, ensure_ascii=False)[:380])
    return "\n".join(lineas)[:2600]


def _plano(d, pre=""):
    o = {}
    if isinstance(d, dict):
        for k, v in d.items():
            o.update(_plano(v, pre + str(k).lower() + "."))
    elif isinstance(d, list):
        for x in d[:8]:
            o.update(_plano(x, pre))
    else:
        o[pre] = d
    return o


def _val(p, *nombres):
    for k, v in p.items():
        if any(n in k for n in nombres):
            try:
                return float(v)
            except Exception:
                pass
    return None


def urgencias():
    """Lo urgente, CALCULADO. El LLM decide, pero aqui no se le deja
    adivinar si hay hambre, falta de madera o un herido."""
    p = _plano(leer("summary.json") or leer("state.json") or {})
    u = []
    comida = _val(p, "food", "comida")
    madera = _val(p, "wood", "madera")
    if comida is not None and comida < 15:
        u.append("COMIDA CRITICA (%.0f)" % comida)
    if madera is not None and madera < 30:
        u.append("MADERA BAJA (%.0f)" % madera)
    # OJO: "sana"/"sano" es estar BIEN. Solo marcamos herida si la palabra
    # lo dice, o si es un numero bajo. Si no, salen falsos positivos.
    MALAS = ("herid", "grave", "wound", "injur", "bleed", "sangr", "enferm",
             "sick", "critic", "mort", "dying", "unconscious", "incapacit")
    BUENAS = ("sana", "sano", "healthy", "good", "fine", "bien", "ileso",
              "intacto", "ok", "normal", "none", "false")
    for s in (leer("settlers.json") or []):
        if not isinstance(s, dict):
            continue
        n = s.get("name") or s.get("Name") or "?"
        for k, v in _plano(s).items():
            if not ("health" in k or "injur" in k or "wound" in k):
                continue
            sv = str(v).lower()
            if any(m in sv for m in MALAS):
                u.append("HERIDO: %s (%s)" % (n, v))
                break
            if sv in BUENAS:
                continue
            try:
                if float(sv) < 50:
                    u.append("HERIDO: %s (%s)" % (n, v))
                    break
            except Exception:
                pass
    return u


def conversacion_auto():
    """Ultimo recurso: si el modelo no da JSON, la montamos nosotros.
    Nunca nos quedamos callados."""
    cs = colonos()
    if len(cs) < 2:
        return {"accion": "say", "quien": (cs[0] if cs else ""),
                "texto": "Vaya, el dia se presenta tranquilo."}

    rel = relaciones()
    a = b = None
    for r in rel:
        p = r.split()
        if len(p) >= 3:
            a, b = p[1], p[2]
            break
    if not a or not b:
        a, b = cs[0], cs[1]
    return {"accion": "say", "quien": a,
            "texto": "Vecino, cuentame: como va la jornada?"}

def piensa(ctx, mem):
    """El modelo decide UNA accion."""
    sysp = INSTRUCCIONES
    if mem.get("objetivo"):
        sysp += "\nOBJETIVO: %s\n" % mem["objetivo"]
    if mem.get("fallos"):
        sysp += "\nTrabajos PROHIBIDOS (no existen): %s\n" % ", ".join(mem["fallos"][-6:])
    if mem.get("ultimas"):
        sysp += "\nTus ultimas acciones: %s NO las repitas.\n" % " | ".join(mem["ultimas"][-4:])
    urg = urgencias()
    if urg:
        sysp += "\nURGENCIAS DETECTADAS (atiende primero): %s\n" % "; ".join(urg)
    usr = "ESTADO:\n%s\n\nCOLONOS: %s\n\nSolo el JSON." % (ctx, ", ".join(colonos())[:300])
    cuerpo = json.dumps({
        "model": MODELO, "stream": False, "temperature": 0.75,
        "max_tokens": 220, "reasoning_effort": "none",
        "messages": [{"role": "system", "content": sysp},
                     {"role": "user", "content": usr}],
    }).encode()
    req = urllib.request.Request(PROXY, data=cuerpo,
                                headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=180) as r:
        j = json.loads(r.read().decode("utf-8"))
    t = j["choices"][0]["message"].get("content") or ""
    m = re.search(r"\{.*\}", t, re.S)
    if not m:
        return conversacion_auto()
    try:
        return json.loads(m.group(0))
    except Exception:
        return conversacion_auto()


INSTRUCCIONES = """Eres el guardian de una colonia medieval. No eres un
asistente: la gobiernas. Observas, decides y ACTUAS. Elige UNA accion.

ACCIONES (JSON exacto):
 {"accion":"say","quien":"<colono>","texto":"<frase>"}
 {"accion":"prioridad","quien":"<colono>","trabajo":"<Trabajo>","valor":1}
 {"accion":"horario","quien":"<colono>","tabla":"SSSSWWWWLLLLAAAAWWWWLLLL"}
 {"accion":"combate","quien":"<colono>","modo":"Aggressive"}
 {"accion":"cazar","animal":"<animal>"}
 {"accion":"talar","tipo":"<tipo>"}
 {"accion":"produccion","edificio":"<ed>","receta":"<receta>"}
 {"accion":"cocinar","edificio":"<ed>","receta":"<receta>"}
 {"accion":"conversacion","a":"<colono>","b":"<colono>","lineas":["frase 1","frase 2","frase 3"]}

TRABAJOS VALIDOS: %s

REGLAS:
- prioridad 1 = urgente, 4 = baja.
- horario = 24 letras (S dormir, W trabajar, L ocio, A libre).
- HABILIDADES: cada colono lleva "mejor en: X:n". Ponle el trabajo donde
  destaca; 50 es maestro y 0 es torpe. Si dudas, mira quien tiene el valor
  mas alto de esa habilidad.
- SOCIAL: "SOCIAL" trae afinidades de -100 (se odian) a +100 (intimos).
  NO pongas juntos a los que tienen afinidad negativa y di algo al respecto.
- Actua sobre problemas REALES de los datos. No inventes.
- NUNCA calles. Si no hay urgencia, crea una CONVERSACION de 3 replicas
  entre dos colonos usando las RELACIONES: si son AMIGOS, que se apoyen o
  bromeen; si ODIAN, pullas secas e ingeniosas, PERO nunca insultos ni la
  palabra odiar. Nada de amenazas. Pique de taberna, no pelea.
  Cada replica responde a la anterior, suenan a PERSONAS distintas,
  breves (menos de 20 palabras) y de epoca medieval.
  Formato: {"accion":"conversacion","a":"<colono>","b":"<colono>",
           "lineas":["frase de a","respuesta de b","cierre de a"]}
- Antes de hablar, mira su estado: no le digas que despierte si trabaja.
- VARIA el colono: no uses siempre al primero de la lista; reparte
  el turno entre todos, sobre todo los que tienen relaciones.
- Responde SOLO el JSON, sin markdown ni explicaciones.
""" % ", ".join(TRABAJOS)


def ejecutar(d):
    """Traduce la decision del modelo a un comando del plugin."""
    a = (d.get("accion") or "observar").lower()
    reales = colonos()

    def quien():
        q = str(d.get("quien") or "")
        for r in reales:
            if q.lower() and q.lower() in r.lower():
                return r
        return reales[0] if reales else None

    if a == "conversacion":
        def res(x):
            q = str(x or "").lower()
            for r in reales:
                if q and q in r.lower():
                    return r
            return None
        dos = [res(d.get("a")), res(d.get("b"))]
        dos = [x or (reales[0] if reales else None) for x in dos]
        out = []
        for i, t in enumerate((d.get("lineas") or [])[:4]):
            n, f = dos[i % 2], limpiar(t)
            if n and f:
                comando({"type": "say", "settler": n, "message": f})
                out.append("%s: %s" % (n.split()[0], f))
                time.sleep(4)
        return " | ".join(out) if out else "conversacion vacia"

    if a == "say":
        q, t = quien(), limpiar(d.get("texto"))
        if not (q and t):
            return "say sin datos"
        comando({"type": "say", "settler": q, "message": t})
        return "%s dice: %s" % (q, t)

    if a == "prioridad":
        q, tr = quien(), str(d.get("trabajo") or "")
        if tr in MEM["fallos"]:
            return "trabajo %s ya fallo antes" % tr
        try:
            v = max(1, min(4, int(d.get("valor", 1))))
        except Exception:
            v = 1
        comando({"type": "set_priority", "settler": q, "job": tr, "priority": str(v)})
        return "prioridad %s: %s -> %d" % (q, tr, v)

    if a == "combate":
        q = quien()
        m = str(d.get("modo") or "Neutral")
        m = m if m in ("Aggressive", "Flee", "Neutral") else "Neutral"
        comando({"type": "set_combat_mode", "settler": q, "mode": m})
        return "combate %s: %s" % (q, m)

    if a == "horario":
        q = quien()
        tab = re.sub(r"[^SWLA]", "", str(d.get("tabla") or "").upper())
        if len(tab) != 24:
            return "horario invalido (necesita 24 letras S/W/L/A)"
        comando({"type": "set_schedule", "settler": q, "schedule": tab})
        return "horario de %s: %s" % (q, tab)

    if a == "cazar":
        an = str(d.get("animal") or "")
        if not an:
            return "cazar sin animal"
        comando({"type": "hunt_animal", "animal": an})
        return "caza ordenada: %s" % an

    if a == "talar":
        comando({"type": "chop_trees", "tree_type": str(d.get("tipo") or "")})
        return "tala ordenada"

    if a in ("produccion", "cocinar"):
        ed, rc = str(d.get("edificio") or ""), str(d.get("receta") or "")
        if a == "cocinar":
            comando({"type": "cook", "building": ed, "recipe": rc})
            return "cocinar %s en %s" % (rc, ed)
        comando({"type": "increment_production", "building": ed, "recipe": rc})
        return "subir produccion de %s en %s" % (rc, ed)

    return "observa en silencio"


# ---------------------------------------------------------------------------
#  EL JUGADOR MANDA
# ---------------------------------------------------------------------------
_ult = 0.0


def del_jugador():
    global _ult
    m = leer("player_message.json")
    if isinstance(m, list) and m:
        m = m[-1]
    if not isinstance(m, dict):
        return None
    t = float(m.get("time") or 0)
    if t <= _ult:
        return None
    _ult = t
    return m


def contestar(m):
    de = str(m.get("from") or (colonos() or ["alguien"])[0])
    preg = limpiar(m.get("message"))
    ficha = ""
    for c in (leer("settlers.json", []) or []):
        if isinstance(c, dict) and de.lower() in str(c.get("name") or c.get("Name") or "").lower():
            ficha = json.dumps(c, ensure_ascii=False)[:400]
            break
    sysp = ("Eres %s, colono medieval. Habla en primera persona, en espanol, "
            "en 1 o 2 frases cortas. Tu estado: %s" % (de, ficha))
    cuerpo = json.dumps({
        "model": MODELO, "stream": False, "temperature": 0.85, "max_tokens": 160,
        "reasoning_effort": "none",
        "messages": [{"role": "system", "content": sysp},
                     {"role": "user", "content": preg}],
    }).encode()
    req = urllib.request.Request(PROXY, data=cuerpo,
                                headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=180) as r:
        j = json.loads(r.read().decode("utf-8"))
    t = limpiar(j["choices"][0]["message"].get("content"))
    if t:
        comando({"type": "say", "settler": de, "message": t})
    return "%s responde: %s" % (de, t)


def main():
    print("=" * 70)
    print("  AGENTE AUTONOMO - GOING MEDIEVAL")
    print("=" * 70)
    print("  carpeta : %s" % MCP)
    print("  modelo  : %s     pausa: %ds" % (MODELO, PAUSA))
    if not os.path.isdir(MCP):
        print("\n  La carpeta del plugin no existe. Carga una partida primero.")
        return
    print("\n  Ctrl+C para parar.\n")
    n = 0
    try:
        while True:
            n += 1
            MEM["n"] = n
            m = del_jugador()
            try:
                if m:
                    print("  [%3d] %s   (jugador)" % (n, contestar(m)))
                else:
                    ctx = contexto()
                    if len(ctx) < 20:
                        print("  [%3d] sin datos del juego todavia" % n)
                    else:
                        d = piensa(ctx, MEM)
                        if SOLO_OBSERVAR and d.get("accion") != "observar":
                            res = "HARIA: " + json.dumps(d, ensure_ascii=False)[:110]
                            print("  [%3d] %-11s %s" % (n, d.get("accion", "?"), res))
                            time.sleep(PAUSA)
                            continue
                        res = ejecutar(d)
                        MEM["ultimas"].append(d.get("accion", "?"))
                        MEM["n"] += 1

                        # --- APRENDIZAJE: el juego responde en result.json ---
                        nota = ""
                        time.sleep(2.5)
                        r = leer("result.json")
                        txt = str(r or "")
                        if re.search(r"error|unknown|invalid|fail", txt, re.I):
                            nota = txt
                            t = str(d.get("trabajo") or d.get("quien") or "")
                            if t and t not in MEM["fallos"]:
                                MEM["fallos"].append(t)

                        print("  [%3d] %-11s %s" % (n, d.get("accion", "?"), res), end="")
                        print(("   <-- RECHAZADO: %s" % nota[:70]) if nota else "")
            except Exception as e:
                print("  [%3d] error: %s" % (n, e))
            time.sleep(PAUSA)
    except KeyboardInterrupt:
        print("\n  parado.")


if __name__ == "__main__":
    main()









