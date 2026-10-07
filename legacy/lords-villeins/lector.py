# -*- coding: utf-8 -*-
"""
LECTOR DE LA VILLA  -  convierte lo que ocurre en el juego en informacion.

    python lector.py              informe general
    python lector.pyMetricas      estadisticas
    python lector.py Drama        quien se ha peleado con quien
    python lector.py voz <nombre> todo lo que ha dicho uno
    python lector.py buscar <txt> busca una palabra en los dialogos
    python lector.py 흐름

Cada evento es una linea de dialogos.jsonl con esta forma:
  {"t","tipo","a","b","af_antes","af_despues","senales","dice"}
"""
import json, os, sys, collections, io

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

BASE = r"D:\SteamLibrary\steamapps\common\Lords & Villeins\BepInEx\plugins"
FICHERO = os.path.join(BASE, "pecera_datos", "dialogos.jsonl")
MODELO = "qwen9b-silly:latest"


def cargar():
    ev = []
    if not os.path.exists(FICHERO):
        return ev
    for linea in io.open(FICHERO, encoding="utf-8-sig"):
        linea = linea.strip()
        if not linea:
            continue
        try:
            ev.append(json.loads(linea))
        except Exception:
            pass
    return ev


def num(x):
    try:
        return float(x)
    except Exception:
        return 0.0


def tipos(ev):
    return collections.Counter(e.get("tipo", "?") for e in ev)


def informe(ev):
    print("=" * 66)
    print("  LA VILLA - informe")
    print("=" * 66)
    if not ev:
        print("\n  Todavia no hay eventos.")
        print("  Losectable cuando dos NPCs discutan, se peleen o se")
        print("  enamoren. Cargala partida y deja correr el reloj.")
        print("\n  fichero: %s" % FICHERO)
        return

    t = tipos(ev)
    print("\n  Eventos: %d" % len(ev))
    for k, v in t.most_common():
        print("     %-12s %d" % (k, v))

    gente = set()
    for e in ev:
        gente.add(e.get("a", "?")); gente.add(e.get("b", "?"))
    print("\n  NPCs implicados: %d" % len(gente))

    print("\n--- deltas de relacion (lo que de verdad se mueve) ---")
    filas = []
    for e in ev:
        d = num(e.get("af_despues")) - num(e.get("af_antes"))
        filas.append((d, e))
    filas.sort(key=lambda x: x[0])
    for d, e in filas[:6]:
        print("  %+7.1f  %-11s %s <-> %s" % (d, e.get("tipo"), e.get("a"), e.get("b")))
    if len(filas) > 12:
        print("  ...")
        for d, e in filas[-3:]:
            print("  %+7.1f  %-11s %s <-> %s" % (d, e.get("tipo"), e.get("a"), e.get("b")))


def drama(ev):
    print("=" * 66)
    print("  QUIEN CON QUIEN")
    print("=" * 66)
    pares = collections.Counter()
    for e in ev:
        k = tuple(sorted([e.get("a", "?"), e.get("b", "?")]))
        pares[k] += 1
    if not pares:
        print("  sin datos")
        return
    for (x, y), n in pares.most_common(18):
        barra = "#" * min(n, 30)
        print("  %3d %-24s <-> %-24s %s" % (n, x, y, barra))


def metricas(ev):
    print("=" * 66)
    print("  METRICAS")
    print("=" * 66)
    t = tipos(ev)
    total = max(1, len(ev))

    print("\n  reparto de eventos")
    for k, v in t.most_common():
        print("     %-12s %3d  (%.0f%%)" % (k, v, 100.0 * v / total))

    quien = collections.Counter()
    for e in ev:
        quien[e.get("a", "?")] += 1
    print("\n  quien mas habla (quien 'inicia'):")
    for k, v in quien.most_common(10):
        print("     %-26s %d" % (k, v))

    print("\n  senales asimetricas detectadas:")
    sen = collections.Counter()
    for e in ev:
        s = e.get("senales", "-")
        if s and s != "-":
            for p in s.split():
                sen[p] += 1
    if sen:
        for k, v in sen.most_common():
            print("     %-16s %d" % (k, v))
    else:
        print("     ninguna aun")

    print("\n  deltas de relacion")
    ds = [num(e.get("af_despues")) - num(e.get("af_antes")) for e in ev]
    if ds:
        print("     media  %+.2f" % (sum(ds) / len(ds)))
        print("     min    %+.2f" % min(ds))
        print("     max    %+.2f" % max(ds))


def voz(ev, nombre):
    n = nombre.lower()
    print("=" * 66)
    print("  TODO LO QUE HA DICHO %s" % nombre.upper())
    print("=" * 66)
    k = 0
    for e in ev:
        if n in e.get("a", "").lower() or n in e.get("b", "").lower():
            k += 1
            print("\n  [%s] %s -> %s" % (e.get("tipo"), e.get("a"), e.get("b")))
            print("      \"%s\"" % e.get("dice", ""))
    if not k:
        print("  no ha aparecido")


def buscar(ev, txt):
    t = txt.lower()
    print("=" * 66)
    print("  BUSCANDO \"%s\"" % txt)
    print("=" * 66)
    k = 0
    for e in ev:
        if t in e.get("dice", "").lower():
            k += 1
            print("  %s | %s: \"%s\"" % (e.get("t", "")[:16], e.get("a"),
                                        e.get("dice", "")))
    print("\n  %d resultados" % k)


def main():
    ev = cargar()
    modo = sys.argv[1].lower() if len(sys.argv) > 1 else "informe"
    if modo in ("informe", ""):
        informe(ev)
    elif modo == "drama":
        drama(ev)
    elif modo == "metricas":
        metricas(ev)
    elif modo == "voz" and len(sys.argv) > 2:
        voz(ev, sys.argv[2])
    elif modo == "buscar" and len(sys.argv) > 2:
        buscar(ev, sys.argv[2])
    else:
        print(__doc__)


if __name__ == "__main__":
    main()