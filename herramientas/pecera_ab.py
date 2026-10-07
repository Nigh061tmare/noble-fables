#!/usr/bin/env python3
"""A/B del coste del mod: compara fps.csv con el mod ON y OFF (misma partida y escena).

Uso:  python pecera_ab.py fps_on.csv fps_off.csv

Como se obtienen los dos ficheros: ver VERIFICACION_PENDIENTE.md (prueba A/B).
La logica replica Pecera.Core.Informe.CompararAB (misma regla de veredicto, probada en tests/).
"""
import csv
import math
import sys


def lee(ruta):
    v = []
    with open(ruta, newline="", encoding="utf-8") as f:
        for fila in csv.reader(f):
            if len(fila) >= 3:
                try:
                    v.append(float(fila[2]))   # fps_instantanea
                except ValueError:
                    pass                       # cabecera
    return v


def stats(v):
    s = sorted(v)
    n = len(s)
    media = sum(s) / n
    desv = math.sqrt(sum((x - media) ** 2 for x in s) / (n - 1)) if n > 1 else 0.0
    return dict(n=n, media=media, p5=s[int(math.floor(0.05 * (n - 1)))], p1=s[int(math.floor(0.01 * (n - 1)))], min=s[0], desv=desv)


def compara(on, off):
    a, b = stats(on), stats(off)
    if a["n"] < 30 or b["n"] < 30:
        return a, b, 0.0, 0.0, "insuficiente (hacen falta >=30 muestras por brazo)"
    delta = 100.0 * (a["media"] - b["media"]) / b["media"] if b["media"] > 0 else 0.0
    se = math.sqrt(a["desv"] ** 2 / a["n"] + b["desv"] ** 2 / b["n"])
    t = (a["media"] - b["media"]) / se if se > 0 else 0.0
    if delta < -3 and t < -2:
        v = "caida medible"
    elif abs(delta) < 3 or abs(t) < 2:
        v = "sin caida medible"
    else:
        v = "mejora (revisar el montaje)"
    return a, b, delta, t, v


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    a, b, delta, t, v = compara(lee(sys.argv[1]), lee(sys.argv[2]))
    for nombre, s in (("ON ", a), ("OFF", b)):
        print("%s n=%d media=%.1f p5=%.1f p1=%.1f min=%.1f" % (nombre, s["n"], s["media"], s["p5"], s["p1"], s["min"]))
    print("delta=%.2f%%  t=%.2f  -> %s" % (delta, t, v))
