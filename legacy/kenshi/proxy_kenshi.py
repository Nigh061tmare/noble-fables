# -*- coding: utf-8 -*-
"""
PROXY PARA KENSHI + SENTIENT SANDS + OLLAMA LOCAL
=================================================
SentientSands habla por el endpoint OpenAI (/v1/chat/completions).
Para desactivar el modo "pensar" de Qwen 3.x hay que enviar el
parametro ESTANDAR de OpenAI:

      "reasoning_effort": "none"

OJO: el campo nativo de Ollama "think" NO funciona por esa ruta
(comprobado: devuelve content vacio). Solo va "reasoning_effort".
Sin esto el mod recibe 200 OK con cuerpo vacio y falla.

Arranque:  python proxy_kenshi.py
En providers.json:  "base_url": "http://127.0.0.1:11436/v1"
"""

import http.client
import json
import os
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

OLLAMA_HOST = "127.0.0.1"
OLLAMA_PORT = 11434
ESCUCHA_HOST = "127.0.0.1"
ESCUCHA_PORT = 11436

AQUI = os.path.dirname(os.path.abspath(__file__))
FICHERO_LOG = os.path.join(AQUI, "proxy_kenshi.log")
_lock = threading.Lock()


def log(msg):
    linea = str(msg).rstrip()
    try:
        with _lock:
            print(linea, flush=True)
            with open(FICHERO_LOG, "a", encoding="utf-8") as f:
                f.write(linea + "\n")
    except Exception:
        pass


def inyectar(cuerpo):
    """Mete reasoning_effort=none. Devuelve (nuevo_cuerpo, resumen)."""
    if not cuerpo:
        return cuerpo, "sin cuerpo"
    try:
        j = json.loads(cuerpo.decode("utf-8"))
    except Exception:
        return cuerpo, "no parseable"
    if not isinstance(j, dict):
        return cuerpo, "no es objeto"
    cambios = []
    if j.get("reasoning_effort") != "none":
        j["reasoning_effort"] = "none"
        cambios.append("reasoning_effort=none")
    if "think" not in j:
        j["think"] = False
        cambios.append("think=false")
    modelo = j.get("model", "?")
    return json.dumps(j).encode("utf-8"), "modelo=%s %s" % (modelo, ",".join(cambios))


# ---------------------------------------------------------------------------
#  Modelos que SI queremos ofrecer al juego.
#  qwen3.8-35b (20 GB) y el abliterated 14B NO caben en 12 GB de VRAM: irian
#  a CPU y tardarian MINUTOS por frase. Mejor que no aparezcan en el
#  desplegable del mod y nadie los elija por error.
# ---------------------------------------------------------------------------
MODELOS_UTILES = ("qwen9b-silly:latest", "qwen4b-silly:latest",
                  "mimo-9b:q5", "mistral-nemo:latest")


class Proxy(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *a):
        pass

    def _filtrar_modelos(self, datos):
        """Recorta la lista de /v1/models a los que caben en la GPU."""
        try:
            j = json.loads(datos.decode("utf-8"))
        except Exception:
            return datos
        if not isinstance(j, dict) or "data" not in j:
            return datos
        antes = len(j["data"])
        j["data"] = [m for m in j["data"]
                     if m.get("id") in MODELOS_UTILES]
        log("[proxy] /v1/models: %d -> %d modelos (ocultos los que no caben)"
            % (antes, len(j["data"])))
        return json.dumps(j).encode("utf-8")

    def _hacer(self):
        largo = int(self.headers.get("Content-Length") or 0)
        cuerpo = self.rfile.read(largo) if largo else None
        detalle = ""
        if self.command == "POST" and cuerpo:
            cuerpo, detalle = inyectar(cuerpo)

        cabeceras = {}
        for k, v in self.headers.items():
            if k.lower() in ("host", "content-length", "connection",
                             "accept-encoding", "authorization"):
                continue
            cabeceras[k] = v
        cabeceras["Content-Type"] = "application/json"
        if cuerpo is not None:
            cabeceras["Content-Length"] = str(len(cuerpo))

        conn = http.client.HTTPConnection(OLLAMA_HOST, OLLAMA_PORT, timeout=600)
        try:
            conn.request(self.command, self.path, body=cuerpo, headers=cabeceras)
            resp = conn.getresponse()
            datos = resp.read()
            if self.path.startswith("/v1/models"):
                datos = self._filtrar_modelos(datos)
            self.send_response(resp.status)
            for k, v in resp.getheaders():
                if k.lower() in ("transfer-encoding", "connection", "content-length"):
                    continue
                self.send_header(k, v)
            self.send_header("Content-Length", str(len(datos)))
            self.end_headers()
            self.wfile.write(datos)
            log("[proxy] %s %s -> %s (%d bytes) %s"
                % (self.command, self.path, resp.status, len(datos), detalle))
        except Exception as e:
            log("[proxy] FALLO %s %s: %s" % (self.command, self.path, e))
            try:
                self.send_error(502, "proxy: %s" % e)
            except Exception:
                pass
        finally:
            conn.close()

    def do_GET(self):
        self._hacer()

    def do_POST(self):
        self._hacer()


def main():
    log("=" * 66)
    log("  PROXY KENSHI  -  inyecta reasoning_effort=none")
    log("  escucha en  http://%s:%d/v1  ->  Ollama %s:%d"
        % (ESCUCHA_HOST, ESCUCHA_PORT, OLLAMA_HOST, OLLAMA_PORT))
    log("=" * 66)
    srv = ThreadingHTTPServer((ESCUCHA_HOST, ESCUCHA_PORT), Proxy)
    srv.daemon_threads = True
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        log("parando...")
        srv.shutdown()


if __name__ == "__main__":
    main()

