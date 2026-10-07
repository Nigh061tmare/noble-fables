# -*- coding: utf-8 -*-
"""
PECERA - laboratorio social para Kenshi
=======================================
Lee todo lo que el juego va dejando en disco y lo convierte en informacion:
secretos de cada NPC, quien habla con quien, y cuando un secreto se filtra.

Uso:
   python pecera_informe.py                 -> resumen del mundo
   python pecera_informe.py secretos        -> todos los secretos y si se han filtrado
   python pecera_informe.py grafo           -> quien ha hablado con quien
   python pecera_informe.py buscar TEXTO    -> busca en todos los dialogos
   python pecera_informe.py vigilar         -> sigue mundo.jsonl en vivo
"""
import json, os, re, sys, glob, collections

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

SAND = r"F:\Steam\steamapps\common\Kenshi\mods\SentientSands\server\campaigns"
DATOS = r"F:\Steam\steamapps\common\Kenshi\PeceraDatos\mundo.jsonl"

VACIAS = set("""de la el los las un una unos unas que con por para como sus
del al es son era fue ser estar esta este esto ese esa eso muy mas pero
sin sobre entre cuando donde quien tiene tenia hace hacer todo toda
todos todas otro otra mismo misma desde hasta porque aunque entonces
luego ahora solo tambien tan mas ni pues ya les nos me te se lo su
""".split())


def cargar_personajes():
    """Devuelve [ {camino, campaña, datos...} ] de todas las campañas."""
    fuera = []
    for camp in sorted(os.listdir(SAND)) if os.path.isdir(SAND) else []:
        carpeta = os.path.join(SAND, camp, "characters")
        if not os.path.isdir(carpeta):
            continue
        for f in glob.glob(os.path.join(carpeta, "*.json")):
            try:
                d = json.load(open(f, encoding="utf-8", errors="replace"))
            except Exception:
                continue
            d["_campana"] = camp
            d["_fichero"] = f
            fuera.append(d)
    return fuera


def sacar_secreto(txt):
    """Extrae el texto de [SECRETO: ...] (hasta el cierre o el final)."""
    if not txt:
        return None
    m = re.search(r"\[SECRETO:\s*(.+?)(?:\]|$)", txt, re.S)
    return m.group(1).strip() if m else None


def palabras(txt):
    ws = re.findall(r"[a-záéíóúñü]{6,}", (txt or "").lower())
    return {w for w in ws if w not in VACIAS}


# ---------------------------------------------------------------------------
def cmd_resumen():
    ps = cargar_personajes()
    con_secreto = [p for p in ps if sacar_secreto(p.get("Backstory"))]
    con_hist = [p for p in ps if p.get("ConversationHistory")]
    lineas = sum(len(p.get("ConversationHistory") or []) for p in ps)

    print("=" * 72)
    print("  RESUMEN DEL MUNDO")
    print("=" * 72)
    print("  NPC con ficha guardada ...... %d" % len(ps))
    print("  NPC con SECRETO ............. %d  (%.0f%%)"
          % (len(con_secreto), 100.0 * len(con_secreto) / max(1, len(ps))))
    print("  NPC con conversaciones ...... %d" % len(con_hist))
    print("  Lineas de dialogo totales ... %d" % lineas)

    por_camp = collections.Counter(p["_campana"] for p in ps)
    print("\n  Campanas:")
    for c, n in por_camp.most_common():
        print("     %-42s %4d NPC" % (c, n))

    fac = collections.Counter(p.get("Faction", "?") for p in ps)
    print("\n  Facciones mas representadas:")
    for c, n in fac.most_common(12):
        print("     %-42s %4d" % (c, n))

    if os.path.exists(DATOS):
        n = sum(1 for _ in open(DATOS, encoding="utf-8", errors="replace"))
        print("\n  Eventos en mundo.jsonl ...... %d" % n)
    else:
        print("\n  (mundo.jsonl aun no existe: se crea al jugar con PeceraLog)")


def cmd_secretos():
    ps = cargar_personajes()
    con = [(p, sacar_secreto(p.get("Backstory"))) for p in ps]
    con = [(p, s) for p, s in con if s]

    print("=" * 72)
    print("  SECRETOS  (%d NPC los tienen)" % len(con))
    print("=" * 72)
    for p, s in con[:60]:
        print("\n  %s  [%s / %s]" % (p.get("Name", "?"), p.get("Race", "?"),
                                     p.get("Faction", "?")))
        print("     %s" % s[:220])
    if len(con) > 60:
        print("\n  ... y %d mas" % (len(con) - 60))


def cmd_grafo():
    ps = cargar_personajes()
    habla = collections.Counter()
    for p in ps:
        yo = p.get("Name", "?")
        for l in (p.get("ConversationHistory") or []):
            # quitamos los [ACTION: ...] para que no rompan el parseo del nombre
            limpio = re.sub(r"\[[^\]]*\]", "", l)
            m = re.match(r"\s*([^:|]{2,30}?)(?:\|\d+)?:\s", limpio)
            if m:
                otro = m.group(1).strip()
                if otro and otro != yo and not otro.startswith("[") and len(otro) < 30:
                    habla[tuple(sorted((yo, otro)))] += 1

    print("=" * 72)
    print("  QUIEN HA HABLADO CON QUIEN  (%d parejas)" % len(habla))
    print("=" * 72)
    for (a, b), n in habla.most_common(40):
        print("   %4d lineas   %s  <->  %s" % (n, a, b))


def cmd_buscar(texto):
    t = texto.lower()
    print("=" * 72)
    print("  BUSCANDO: %r" % texto)
    print("=" * 72)
    n = 0
    for p in cargar_personajes():
        for l in (p.get("ConversationHistory") or []):
            if t in l.lower():
                print("\n  [%s] %s" % (p.get("Name", "?"), l[:300]))
                n += 1
    if os.path.exists(DATOS):
        for l in open(DATOS, encoding="utf-8", errors="replace"):
            if t in l.lower():
                try:
                    d = json.loads(l)
                    print("\n  [%s] %s: %s" % (d.get("tipo"), d.get("nombre"),
                                               (d.get("texto") or "")[:300]))
                    n += 1
                except Exception:
                    pass
    print("\n  %d resultados" % n)


def cmd_fugas():
    """Cruza los secretos con TODAS las conversaciones: detecta filtraciones."""
    ps = cargar_personajes()
    con = [(p, sacar_secreto(p.get("Backstory"))) for p in ps]
    con = [(p, s) for p, s in con if s]

    todas = []
    for p in ps:
        for l in (p.get("ConversationHistory") or []):
            todas.append((p.get("Name", "?"), l))

    print("=" * 72)
    print("  FILTRACIONES DE SECRETOS")
    print("=" * 72)
    hallado = 0
    for p, s in con:
        kw = palabras(s)
        if len(kw) < 3:
            continue
        for autor, l in todas:
            if autor == p.get("Name"):
                continue
            low = l.lower()
            coinciden = sum(1 for w in kw if w in low)
            if coinciden >= 3:
                print("\n  SECRETO DE %s  (%.0f%% de coincidencia)" % (p.get("Name"), 100.0 * coinciden / len(kw)))
                print("     secreto : %s" % s[:150])
                print("     lo dice : %s" % autor)
                print("     texto   : %s" % l[:250])
                hallado += 1
                break
    if not hallado:
        print("\n  Todavia no hay filtraciones detectadas.")
        print("  (hace falta mas partida: los secretos se filtran al hablar)")


def cmd_vigilar():
    print("vigilando %s  (Ctrl+C para parar)\n" % DATOS)
    if not os.path.exists(DATOS):
        print("  aun no existe. Juega con PeceraLog activo.")
        return
    import time
    f = open(DATOS, encoding="utf-8", errors="replace")
    f.seek(0, 2)
    while True:
        l = f.readline()
        if not l:
            time.sleep(1)
            continue
        try:
            d = json.loads(l)
            etq = d.get("tipo", "?")
            if etq == "habla":
                print("  %s  %-18s %s" % (d.get("t", "")[11:19], d.get("nombre", "?"),
                                          (d.get("texto") or "")[:120]))
            else:
                print("  %s  [%s] %s" % (d.get("t", "")[11:19], etq,
                                         d.get("nombre", "")))
        except Exception:
            print("  " + l.rstrip())


if __name__ == "__main__":
    modo = sys.argv[1].lower() if len(sys.argv) > 1 else "resumen"
    if modo == "resumen":
        cmd_resumen()
    elif modo == "secretos":
        cmd_secretos()
    elif modo == "grafo":
        cmd_grafo()
    elif modo == "fugas":
        cmd_fugas()
    elif modo == "buscar" and len(sys.argv) > 2:
        cmd_buscar(" ".join(sys.argv[2:]))
    elif modo == "vigilar":
        cmd_vigilar()
    else:
        print(__doc__)
