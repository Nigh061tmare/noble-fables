# -*- coding: utf-8 -*-
"""
Pone un SECRETO a los NPC que ya existen (los que se generaron antes de que
anadiéramos los secretos al prompt).

Uso:
   python retrofit_secretos.py             -> informe: cuantos faltan
   python retrofit_secretos.py aplicar     -> los genera con qwen3.5:9b
   python retrofit_secretos.py aplicar 20  -> solo 20 (para probar)

Hace backup de cada JSON antes de tocarlo.
"""
import json, os, re, sys, glob, time, datetime, urllib.request, shutil

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

SAND = r"F:\Steam\steamapps\common\Kenshi\mods\SentientSands\server\campaigns"
URL = "http://127.0.0.1:11435/v1/chat/completions"
MODELO = "qwen3.5:9b"


def tiene_secreto(b):
    return bool(re.search(r"\[SECRETO:", b or ""))


def sacar_secreto(txt):
    m = re.search(r"\[SECRETO:\s*(.+?)(?:\]|$)", txt or "", re.S)
    return m.group(1).strip() if m else None


def ficheros():
    out = []
    for camp in sorted(os.listdir(SAND)):
        c = os.path.join(SAND, camp, "characters")
        if os.path.isdir(c):
            out += glob.glob(os.path.join(c, "*.json"))
    return out


def pedir_secreto(p):
    prompt = (
        "Eres un historiador del lore de Kenshi. Inventa el SECRETO de este NPC.\n"
        "Es lo unico que jamas diria en voz alta. Concreto y especifico, no un estado "
        "de animo. Tiene que poder filtrarse y convertirse en un rumor que le haria dano.\n\n"
        "NOMBRE: %s\nRAZA: %s\nFACCION: %s\nOFICIO: %s\nPERSONALIDAD: %s\nTRASFONDO: %s\n\n"
        "Devuelve UNA sola frase en espanol, sin comillas, sin prefijos, empezando "
        "directamente por el contenido del secreto. Nada mas."
        % (p.get("Name", "?"), p.get("Race", "?"), p.get("Faction", "?"),
           p.get("Job", "?"), (p.get("Personality") or "")[:300],
           re.sub(r"\[SECRETO:.*", "", p.get("Backstory") or "")[:400])
    )
    cuerpo = json.dumps({"model": MODELO,
                         "messages": [{"role": "user", "content": prompt}],
                         "max_tokens": 120, "temperature": 0.9}).encode()
    req = urllib.request.Request(URL, data=cuerpo, headers={
        "Content-Type": "application/json", "Authorization": "Bearer ollama"})
    j = json.loads(urllib.request.urlopen(req, timeout=300).read().decode())
    t = (j["choices"][0]["message"].get("content") or "").strip()
    t = t.strip('"').replace("\n", " ")
    t = re.sub(r"^\[SECRETO:\s*", "", t).rstrip("]").strip()
    return t


def main():
    modo = sys.argv[1].lower() if len(sys.argv) > 1 else "informe"
    limite = int(sys.argv[2]) if len(sys.argv) > 2 else 10 ** 9
    fs = ficheros()

    pendientes = []
    for f in fs:
        try:
            d = json.load(open(f, encoding="utf-8", errors="replace"))
        except Exception:
            continue
        if not tiene_secreto(d.get("Backstory")):
            pendientes.append((f, d))

    print("NPC totales ....... %d" % len(fs))
    print("sin secreto ....... %d" % len(pendientes))

    if modo != "aplicar":
        print("\nNada cambiado. Para ponérselos:  python retrofit_secretos.py aplicar")
        return

    marca = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    hechos = 0
    t0 = time.time()
    for f, d in pendientes[:limite]:
        try:
            s = pedir_secreto(d)
            if not s or len(s) < 12:
                continue
            shutil.copy2(f, f + ".bak_" + marca)
            base = re.sub(r"\[SECRETO:.*", "", d.get("Backstory") or "").strip()
            d["Backstory"] = "[SECRETO: %s] %s" % (s, base)
            json.dump(d, open(f, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
            hechos += 1
            print("  [%d/%d] %-18s %s" % (hechos, min(len(pendientes), limite),
                                          d.get("Name", "?"), s[:90]))
        except Exception as e:
            print("  fallo con %s: %s" % (d.get("Name", "?"), e))

    print("\n%d NPC ahora tienen secreto en %.0f s" % (hechos, time.time() - t0))
    print("Backups: <cada>.json.bak_%s" % marca)


if __name__ == "__main__":
    main()
