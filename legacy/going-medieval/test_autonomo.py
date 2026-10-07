# -*- coding: utf-8 -*-
"""Prueba el agente autonomo con una colonia de ejemplo."""
import json, os, sys

sys.path.insert(0, r"C:\Users\Jose Luis\going-medieval-ia")
import agente_autonomo as A

MCP = A.MCP
os.makedirs(MCP, exist_ok=True)


def poner(n, o):
    with open(os.path.join(MCP, n), "w", encoding="utf-8") as f:
        json.dump(o, f, ensure_ascii=False)


poner("settlers.json", [
    {"name": "Aldric", "mood": "furia", "health": "herido grave", "hunger": "hambriento"},
    {"name": "Bertha", "mood": "feliz", "health": "sana", "hunger": "saciada"},
    {"name": "Cedric", "mood": "triste", "health": "sana", "hunger": "hambriento"},
])
poner("summary.json", {"dia": 12, "poblacion": 3, "comida": 4, "madera": 180,
                       "piedra": 30, "animo_medio": 40})
poner("warnings.json", ["Comida casi agotada (4)", "Aldric herido sin tratamiento",
                        "Sin camas suficientes"])
poner("events.json", [{"tipo": "raid", "texto": "Bandidos avistados al norte"},
                      {"tipo": "enfermedad", "texto": "Fiebre en el ganado"}])
poner("social.json", [{"a": "Aldric", "b": "Bertha", "afinidad": -60, "nota": "se odian"},
                      {"a": "Bertha", "b": "Cedric", "afinidad": 70}])

print("=== PERCEPCION ===")
print(A.contexto()[:700])
print()
print("=== DECISION ===")
d = A.piensa(A.contexto(), A.MEM)
print(json.dumps(d, ensure_ascii=False, indent=2))
print()
print("=== EJECUCION ===")
print(A.ejecutar(d))
print()
print("=== command.json ===")
print(open(os.path.join(MCP, "command.json"), encoding="utf-8").read())
