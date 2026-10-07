# -*- coding: utf-8 -*-
"""VIGILANTE: enciende la IA cuando abres Going Medieval y la apaga al cerrar."""
import os, subprocess, sys, time, socket, datetime

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass


class _SinSalida(object):
    """Con pythonw NO hay consola y sys.stdout es None: cualquier print()
    reventaria el proceso. Le damos un sumidero mudo."""
    def write(self, *a):
        pass
    def flush(self, *a):
        pass
    def reconfigure(self, *a, **k):
        pass
    def isatty(self):
        return False


if sys.stdout is None:
    sys.stdout = _SinSalida()
if sys.stderr is None:
    sys.stderr = sys.stdout

AQUI = os.path.dirname(os.path.abspath(__file__))
IA = r"C:\Users\Jose Luis\kenshi-ia"
OLL = r"C:\Users\Jose Luis\agentes-backup\ollama\ollama.exe"
MODELOS = os.path.join(os.environ["USERPROFILE"], ".ollama", "models")
LOG = os.path.join(AQUI, "vigilante.log")
JUEGO = "Going Medieval"
JUEGO_DIR = r"F:\Steam\steamapps\common\Going Medieval"
SIN_VENTANA = 0x08000000

def log(m):
    with open(LOG, "a", encoding="utf-8") as f:
        f.write("%s  %s\n" % (datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"), m))


def libre(p):
    s = socket.socket()
    try:
        s.connect(("127.0.0.1", p))
        return False
    except Exception:
        return True
    finally:
        s.close()


import psutil

def corriendo():
    for proc in psutil.process_iter(['name']):
        try:
            name = proc.info.get('name') or ''
            if JUEGO.lower() in name.lower():
                return True
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            pass
    return False


def kenshi():
    for proc in psutil.process_iter(['name']):
        try:
            name = proc.info.get('name') or ''
            if 'kenshi' in name.lower():
                return True
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            pass
    return False

def pids_agente():
    r = []
    for proc in psutil.process_iter(['pid', 'cmdline']):
        try:
            cmdline = proc.info.get('cmdline') or []
            cmd_str = ' '.join(cmdline).lower()
            if "agente_autonomo" in cmd_str and proc.info['pid'] != os.getpid():
                r.append(proc.info['pid'])
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            pass
    return r


def encender_ia():
    if libre(11434):
        log("arranco Ollama")
        env = dict(os.environ, OLLAMA_MODELS=MODELOS)
        subprocess.Popen([OLL, "serve"], env=env, creationflags=SIN_VENTANA)
        for _ in range(40):
            time.sleep(1)
            if not libre(11434):
                break
    if libre(11436):
        log("arranco el proxy")
        subprocess.Popen([sys.executable, "proxy_kenshi.py"], cwd=IA,
                         creationflags=SIN_VENTANA)

def arrancar_agente(extra=None):
    if pids_agente():
        return
    log("despierto al agente")
    out = open(os.path.join(AQUI, "agente.log"), "a", encoding="utf-8")
    cmd = [sys.executable, "-u", "agente_autonomo.py", "--actuar"]
    subprocess.Popen(cmd, cwd=AQUI, stdout=out, stderr=out,
                     creationflags=SIN_VENTANA)


def matar_agente():
    for p in pids_agente():
        try:
            psutil.Process(p).kill()
            log("agente parado (pid %d)" % p)
        except Exception:
            pass

def apagar_ia():
    """Apaga Ollama y el proxy. NO lo hace si Kenshi esta abierto:
    alli tambien se usa la misma IA."""
    if kenshi():
        log("Kenshi esta abierto: dejo la IA encendida")
        return
    for pat in ("proxy_kenshi",):
        for x in buscar(pat):
            try:
                psutil.Process(x).kill()
            except Exception:
                pass
    for proc in psutil.process_iter(['name']):
        try:
            name = (proc.info.get('name') or '').lower()
            if name in ("llama-server.exe", "ollama.exe"):
                proc.kill()
        except Exception:
            pass
    log("IA apagada (VRAM liberada)")


def buscar(pat):
    r = []
    for proc in psutil.process_iter(['pid', 'cmdline']):
        try:
            cmdline = proc.info.get('cmdline') or []
            cmd_str = ' '.join(cmdline).lower()
            if pat.lower() in cmd_str and proc.info['pid'] != os.getpid():
                r.append(proc.info['pid'])
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            pass
    return r


def ya_hay_otro():
    """Cerradura: si ya hay un vigilante, este se retira.
    Sin esto, el registro de Windows + un doble clic = varios vigilantes
    compitiendo por arrancar y parar la misma IA."""
    for proc in psutil.process_iter(['pid', 'cmdline']):
        try:
            cl = ' '.join(proc.info.get('cmdline') or [])
            if 'vigilante.py' in cl and proc.info['pid'] != os.getpid():
                return proc.info['pid']
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            pass
    return None


def main():
    otro = ya_hay_otro()
    if otro:
        log("ya habia un vigilante (pid %d): este se retira" % otro)
        return
    print("VIGILANTE de Going Medieval")
    print("  vigilando... (Ctrl+C para salir)")
    print("  log: %s" % LOG)
    log("=== vigilante arrancado ===")
    dentro = False
    while True:
        try:
            hay = corriendo()
            if hay and not dentro:
                dentro = True
                log("juego ABIERTO -> enciendo la IA")
                encender_ia()
                time.sleep(4)
                arrancar_agente()
            elif not hay and dentro:
                dentro = False
                log("juego CERRADO -> paro")
                matar_agente()
                apagar_ia()
        except Exception as e:
            # Con pythonw NO hay salida: si esto revienta, moriria en
            # silencio y nadie se enteraria. Lo dejamos por escrito.
            log("ERROR en el bucle: %r" % (e,))
        time.sleep(5)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\n vigilante parado.")




