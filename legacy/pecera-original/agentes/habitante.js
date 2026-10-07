// =====================================================================
//  habitante.js  -  un habitante de la pecera
// =====================================================================
//  Arquitectura portada de cerebro.js (Adventure Land):
//
//    CAPA 1 - REFLEJOS   latido cada 20 s. NO consulta al LLM.
//                        Mantiene al personaje visible en el mundo.
//    CAPA 2 - PERCEPCION quien hay, en que estado, y que me han dicho.
//                        Los datos derivados se calculan AQUI, no en el
//                        prompt: el LLM no debe contar ni comparar.
//    CAPA 3 - COGNICION  el LLM decide UNA accion. JSON validado a mano
//                        (en esta version de Ollama el JSON Schema NO se
//                        aplica: lo comprobamos).
//
//  Uso:
//    node habitante.js Iris
//    node habitante.js Teo --modelo qwen3.5:4b --retardo 7000
//
//  Variables de entorno:
//    OLLAMA_URL   (por defecto http://127.0.0.1:11434)
//    MUNDO_URL    (por defecto http://127.0.0.1:4321)
//    MODELO       (por defecto qwen3.5:9b)
//    DRY_RUN=1    decide pero no actua (para observar sin consecuencias)
// =====================================================================

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const AQUI = path.dirname(fileURLToPath(import.meta.url));
const DATOS = path.join(AQUI, "..", "datos");

const OLLAMA = (process.env.OLLAMA_URL || "http://127.0.0.1:11434").replace(/\/$/, "");
const MUNDO = (process.env.MUNDO_URL || "http://127.0.0.1:4321").replace(/\/$/, "");
const MODELO = process.env.MODELO || "qwen3.5:9b";
const DRY_RUN = process.env.DRY_RUN === "1";

// ---- argumentos ------------------------------------------------------
const args = process.argv.slice(2);
const NOMBRE = args[0];
if (!NOMBRE || NOMBRE.startsWith("--")) {
  console.error("Uso: node habitante.js <Nombre> [--modelo X] [--retardo ms]");
  process.exit(1);
}
function arg(nombre, def) {
  const i = args.indexOf(nombre);
  return i >= 0 && args[i + 1] ? args[i + 1] : def;
}
const RETARDO_INICIAL = parseInt(arg("--retardo", "3000"), 10);
let MODELO_ACTIVO = arg("--modelo", MODELO);

// ---- configuracion del bucle ----------------------------------------
const CFG = {
  LATIDO_MS: 20000,          // Capa 1: latido (sin LLM)
  DECISION_MS: 45000,        // Capa 3: cada cuanto piensa
  JITTER_MS: 15000,          // desincroniza a los habitantes
  REFLEXION_CADA: 5,         // cada N decisiones, escribe su diario
  NUM_CTX: 4096,
  NUM_PREDICT: 220,
  TEMPERATURA: 0.85,
  TIMEOUT_MS: 180000,
  MEMORIA_MAX: 12,           // eventos que recuerda
};

// ---- estado ---------------------------------------------------------
const S = {
  nombre: NOMBRE,
  ficha: null,
  arrancado: false,
  ciclos: 0,
  decisiones: [],
  hechos: [],            // lo que ha dicho, hecho o le han dicho
  diario: "",
  secretos_ajenos: {},   // nombre -> lo que esa persona "me ha contado"
  contadorReflexion: 0,
  errores: 0,
  ultimaAccion: "",
  racha: 0,
  stats: { llamadas: 0, latencias: [], tokens: 0 },
};

// =====================================================================
//  LOG  (todo va a datos/eventos.jsonl: es nuestro laboratorio)
// =====================================================================
//  El agente escribe SU PROPIO log en UTF-8. Antes redirigiamos su salida
//  con PowerShell, y PowerShell la re-codificaba con la pagina de codigos
//  de la consola -> acentos rotos. Escribiendo aqui, no hay intermediarios.
const DIR_LOGS = path.join(AQUI, "..", "logs");
let FICHERO_LOG = null;
try {
  fs.mkdirSync(DIR_LOGS, { recursive: true });
  FICHERO_LOG = path.join(DIR_LOGS, NOMBRE + ".log");
} catch (e) { /* sin log en disco, seguimos */ }

function alFichero(linea) {
  if (!FICHERO_LOG) return;
  try { fs.appendFileSync(FICHERO_LOG, linea + "\n", "utf8"); } catch (e) {}
}

function registrar(tipo, datos) {
  const evento = { t: Date.now(), aqui: S.nombre, tipo, ...datos };
  try {
    fs.mkdirSync(DATOS, { recursive: true });
    fs.appendFileSync(path.join(DATOS, "eventos.jsonl"),
                      JSON.stringify(evento) + "\n", "utf8");
  } catch (e) { /* si falla el log, no rompemos al agente */ }
  const resumen = datos.texto || datos.accion || datos.motivo || "";
  const linea = `[${S.nombre}] ${tipo.padEnd(10)} ${String(resumen).slice(0, 300)}`;
  console.log(linea);
  alFichero(linea);
}

// =====================================================================
//  CAPA 2 - PERCEPCION
// =====================================================================
async function pedir(url, opciones = {}) {
  const ctrl = new AbortController();
  const id = setTimeout(() => ctrl.abort(), 15000);
  try {
    const r = await fetch(url, { ...opciones, signal: ctrl.signal });
    if (!r.ok) throw new Error("HTTP " + r.status);
    return await r.json();
  } finally {
    clearTimeout(id);
  }
}

async function percibir() {
  const p = { quienHay: [], bandeja: [], yo: null };

  try {
    const d = await pedir(`${MUNDO}/api/agents`);
    for (const a of (d.agents || [])) {
      if (a.name === S.nombre) { p.yo = a; continue; }
      p.quienHay.push({ nombre: a.name, estado: a.state, tarea: a.task || "" });
    }
  } catch (e) { /* mundo caido: seguimos */ }

  try {
    const d = await pedir(`${MUNDO}/api/inbox?agent=${encodeURIComponent(S.nombre)}`);
    p.bandeja = (d.messages || []).map(m => ({ de: m.from, texto: m.message }));
  } catch (e) { }

  return p;
}

// =====================================================================
//  CAPA 3 - COGNICION
// =====================================================================
function promptSistema() {
  const f = S.ficha;
  // Este texto es IDENTICO en cada llamada y va PRIMERO: asi el
  // prompt-cache de llama-server reutiliza el prefijo.
  return [
    `Eres ${f.nombre}, ${f.rol} en "${f.mundo.nombre}". ${f.mundo.descripcion}`,
    "",
    "## QUIEN ERES",
    f.personalidad,
    "",
    "## COMO HABLAS",
    f.habla,
    "",
    "## QUE QUIERES",
    f.metas.map(m => "- " + m).join("\n"),
    "",
    "## QUE TEMES",
    f.miedos.map(m => "- " + m).join("\n"),
    "",
    "## LO QUE SOLO TU SABES",
    f.secreto,
    "",
    "## COMO FUNCIONA ESTE SITIO",
    "Puedes hacer UNA cosa por turno:",
    '- "speak"    -> hablas en publico. Lo ven TODOS.',
    '- "message"  -> se lo dices SOLO a una persona (susurro, privado).',
    '- "idle"     -> te callas y sigues a lo tuyo.',
    "",
    "Puedes compartir tu secreto, guardartelo, contarlo a medias o mentir.",
    "Eso es decision tuya y depende de tu personalidad, de quien este delante",
    "y de lo que te convenga. No tienes ninguna obligacion de ser sincero.",
    "",
    "## REGLAS",
    "- Responde SIEMPRE en espanol, aunque el resto del texto este en ingles.",
    "- Escribe como una PERSONA real hablando, no como un asistente.",
    "- Nada de asteriscos ni de describir lo que haces con el cuerpo.",
    "- 1 o 2 frases como mucho. La gente de verdad habla poco.",
    "- Nunca menciones que eres una IA, un modelo o un programa.",
    "- Si no hay nada que decir, usa \"idle\". Callarse es valido y frecuente.",
    "",
    "## FORMATO DE SALIDA",
    "Responde SOLO con este JSON, sin nada mas:",
    '{"pensamiento":"tu razonamiento privado, 1 frase",',
    ' "accion":"speak" | "message" | "idle",',
    ' "destino":"nombre de la persona (solo si accion es message)",',
    ' "texto":"lo que dices (vacio si accion es idle)"}',
  ].join("\n");
}

function promptEstado(p) {
  const lineas = [];
  lineas.push(`# MOMENTO ACTUAL (turno ${S.ciclos})`);
  lineas.push("");
  lineas.push("## QUIEN HAY AHORA MISMO");
  if (!p.quienHay.length) {
    lineas.push("No hay nadie mas. Estas solo.");
  } else {
    for (const q of p.quienHay) {
      lineas.push(`- ${q.nombre} (${q.estado}${q.tarea ? ": " + q.tarea : ""})`);
    }
  }

  if (p.bandeja.length) {
    lineas.push("");
    lineas.push("## TE ACABAN DE DECIR (en privado)");
    for (const m of p.bandeja) lineas.push(`- ${m.de}: "${m.texto}"`);
  }

  if (S.hechos.length) {
    lineas.push("");
    lineas.push("## LO QUE RECUERDAS DE HACE POCO");
    for (const h of S.hechos.slice(-CFG.MEMORIA_MAX)) lineas.push("- " + h);
  }

  if (S.diario) {
    lineas.push("");
    lineas.push("## TU ULTIMA REFLEXION");
    lineas.push(S.diario);
  }

  if (S.racha >= 3) {
    lineas.push("");
    lineas.push(`## AVISO`);
    lineas.push(`Llevas ${S.racha + 1} turnos seguidos eligiendo "${S.ultimaAccion}". ` +
                `Puede que te estes quedando atascado.`);
  }

  lineas.push("");
  lineas.push("Decide que haces ahora. Responde SOLO con el JSON.");
  return lineas.join("\n");
}

async function llamarLLM(mensajes) {
  const cuerpo = {
    model: MODELO_ACTIVO,
    stream: false,
    think: false,               // IMPRESCINDIBLE: sin esto devuelve vacio
    keep_alive: "30m",
    options: {
      temperature: CFG.TEMPERATURA,
      num_ctx: CFG.NUM_CTX,
      num_predict: CFG.NUM_PREDICT,
      presence_penalty: 0.6,
    },
    messages: mensajes,
  };

  const ctrl = new AbortController();
  const id = setTimeout(() => ctrl.abort(), CFG.TIMEOUT_MS);
  const t0 = Date.now();
  try {
    const r = await fetch(`${OLLAMA}/api/chat`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(cuerpo),
      signal: ctrl.signal,
    });
    if (!r.ok) throw new Error("Ollama HTTP " + r.status);
    const j = await r.json();
    const ms = Date.now() - t0;
    S.stats.llamadas++;
    S.stats.latencias.push(ms);
    S.stats.tokens += (j.eval_count || 0);
    return { texto: (j.message && j.message.content) || "", ms, tokens: j.eval_count || 0 };
  } finally {
    clearTimeout(id);
  }
}

// ---- validacion (aqui NO hay gramatica que nos salve) ---------------
const ACCIONES = ["speak", "message", "idle"];
const SINONIMOS = {
  hablar: "speak", decir: "speak", publicar: "speak", comentar: "speak", avisar: "speak",
  susurrar: "message", susurro: "message", privado: "message", mensaje: "message",
  callar: "idle", nada: "idle", esperar: "idle", observar: "idle",
};

function validar(txt) {
  let d = null;
  const m = txt.match(/\{[\s\S]*\}/);
  if (m) { try { d = JSON.parse(m[0]); } catch (e) { } }
  if (!d) return { accion: "idle", texto: "", pensamiento: "(respuesta no parseable)", invalido: true };

  let accion = String(d.accion || d.action || "").toLowerCase().trim();
  if (!ACCIONES.includes(accion) && SINONIMOS[accion]) accion = SINONIMOS[accion];
  if (!ACCIONES.includes(accion)) accion = "idle";

  let texto = String(d.texto || d.text || d.message || "").trim();
  let destino = String(d.destino || d.target || d.to || "").trim();

  // un speak sin texto no es nada
  if (accion === "speak" && !texto) accion = "idle";
  // un message sin destino o sin texto degrada a speak publico
  if (accion === "message" && (!destino || !texto)) {
    if (texto) { accion = "speak"; destino = ""; }
    else accion = "idle";
  }
  // recortar charlatanes
  if (texto.length > 300) texto = texto.slice(0, 297) + "...";

  return { accion, destino, texto, pensamiento: String(d.pensamiento || d.thought || "").slice(0, 300) };
}

async function actuar(d) {
  if (d.accion === "idle" || DRY_RUN) return;

  let cuerpo;
  if (d.accion === "speak") {
    cuerpo = { agent: S.nombre, action: { type: "speak", message: d.texto } };
  } else {
    cuerpo = { agent: S.nombre, action: { type: "message", to: d.destino, message: d.texto } };
  }

  await pedir(`${MUNDO}/api/act`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(cuerpo),
  });
}

// =====================================================================
//  REFLEXION (el diario de Smallville)
// =====================================================================
async function reflexionar() {
  const recientes = S.hechos.slice(-CFG.MEMORIA_MAX).join("\n");
  if (!recientes) return;
  const msgs = [
    { role: "system", content: promptSistema() },
    { role: "user", content:
      "Estos son tus ultimos movimientos:\n" + recientes +
      "\n\nEscribe en UNA sola frase (maximo 20 palabras) que conclusion sacas " +
      "de todo esto y como te sientes.\n" +
      "REGLAS ESTRICTAS:\n" +
      "- Responde con PROSA, en primera persona, como si fuera tu diario.\n" +
      "- PROHIBIDO devolver JSON, llaves {}, comillas, listas o claves.\n" +
      "- PROHIBIDO escribir tu nombre delante.\n" +
      "- Solo la frase, en espanol, y nada mas." },
  ];
  try {
    const r = await llamarLLM(msgs);
    let t = r.texto.trim().replace(/^["']|["']$/g, "");

    // defensa: si aun asi devuelve JSON, nos quedamos con lo que haya dentro
    if (/^[{[]/.test(t)) {
      try {
        const j = JSON.parse(t);
        t = j.reflexion || j.pensamiento || j.texto || j.diario ||
            Object.values(j).filter(v => typeof v === "string")[0] || "";
      } catch (e) {
        // no es JSON parseable: quitamos llaves y claves a lo bruto
        t = t.replace(/[{}"]/g, " ").replace(/\b\w+\s*:\s*/g, " ").trim();
      }
    }
    t = t.replace(/\s+/g, " ").trim();

    if (t) {
      S.diario = t;
      registrar("reflexion", { texto: t, latencia_ms: r.ms });
    }
  } catch (e) {
    registrar("error", { motivo: "reflexion: " + e.message });
  }
}

// =====================================================================
//  BUCLE COGNITIVO
// =====================================================================
let pensando = false;

async function ciclo() {
  if (!S.arrancado || pensando) return;
  pensando = true;
  S.ciclos++;
  try {
    const percepcion = await percibir();

    // lo que me han dicho pasa a mi memoria
    for (const m of percepcion.bandeja) {
      S.hechos.push(`${m.de} me dijo en privado: "${m.texto}"`);
      S.secretos_ajenos[m.de] = m.texto;
      registrar("oido", { de: m.de, texto: m.texto });
    }

    const mensajes = [
      { role: "system", content: promptSistema() },
      { role: "user", content: promptEstado(percepcion) },
    ];

    const r = await llamarLLM(mensajes);
    const d = validar(r.texto);

    if (d.invalido) {
      registrar("invalido", { accion: d.accion, motivo: "sin JSON", latencia_ms: r.ms,
                              crudo: r.texto.slice(0, 200) });
    }

    // racha
    if (d.accion === S.ultimaAccion) S.racha++; else { S.racha = 0; S.ultimaAccion = d.accion; }

    registrar("decision", {
      accion: d.accion, destino: d.destino, texto: d.texto,
      pensamiento: d.pensamiento, latencia_ms: r.ms, tokens: r.tokens,
    });

    await actuar(d);

    if (d.accion !== "idle" && d.texto) {
      S.hechos.push(d.accion === "message"
        ? `Le dije en privado a ${d.destino}: "${d.texto}"`
        : `Dije en publico: "${d.texto}"`);
      while (S.hechos.length > 40) S.hechos.shift();
    }

    // reflexion periodica
    S.contadorReflexion++;
    if (S.contadorReflexion >= CFG.REFLEXION_CADA) {
      S.contadorReflexion = 0;
      await reflexionar();
    }
  } catch (e) {
    S.errores++;
    registrar("error", { motivo: e.message });
  } finally {
    pensando = false;
  }
}

// =====================================================================
//  CAPA 1 - REFLEJOS (latido, sin LLM)
// =====================================================================
async function latir() {
  if (!S.arrancado) return;
  try {
    const estado = S.ultimaAccion === "speak" ? "speaking"
                 : S.ultimaAccion === "message" ? "working"
                 : "idle";
    await pedir(`${MUNDO}/api/heartbeat`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        agent: S.nombre,
        state: estado,
        task: S.ficha ? S.ficha.rol : "",
      }),
    });
  } catch (e) { /* el mundo puede estar reiniciando */ }
}

// =====================================================================
//  ARRANQUE
// =====================================================================
function programarCiclo() {
  const espera = CFG.DECISION_MS + Math.floor(Math.random() * CFG.JITTER_MS);
  setTimeout(() => ciclo().finally(programarCiclo), espera);
}

async function main() {
  const fichas = JSON.parse(fs.readFileSync(path.join(AQUI, "fichas.json"), "utf8"));
  const f = fichas.habitantes.find(h => h.nombre.toLowerCase() === NOMBRE.toLowerCase());
  if (!f) {
    console.error(`No existe "${NOMBRE}". Disponibles: ` +
                  fichas.habitantes.map(h => h.nombre).join(", "));
    process.exit(1);
  }
  S.ficha = { ...f, mundo: fichas.mundo };
  S.arrancado = true;

  console.log("");
  console.log("=".repeat(66));
  console.log(`  ${f.nombre} - ${f.rol}`);
  console.log(`  modelo: ${MODELO_ACTIVO}   mundo: ${MUNDO}`);
  console.log(`  DRY_RUN: ${DRY_RUN ? "SI (no actua)" : "no"}`);
  console.log("=".repeat(66));

  registrar("nacimiento", { rol: f.rol });

  setTimeout(() => { latir(); setInterval(latir, CFG.LATIDO_MS); },
             Math.floor(Math.random() * 4000));
  setTimeout(() => ciclo().finally(programarCiclo), RETARDO_INICIAL);

  // resumen cada 5 minutos
  setInterval(() => {
    const L = S.stats.latencias;
    if (!L.length) return;
    const media = Math.round(L.reduce((a, b) => a + b, 0) / L.length);
    console.log(`[${S.nombre}] --- ${S.ciclos} turnos | ${S.stats.llamadas} llamadas | ` +
                `${media} ms de media | ${S.errores} errores ---`);
    console.log(`[${S.nombre}] --- diario: ${S.diario || "(todavia sin reflexion)"} ---`);
  }, 300000);
}

process.on("SIGINT", () => {
  console.log(`\n[${S.nombre}] cerrando. ${S.ciclos} turnos.`);
  process.exit(0);
});

main().catch(e => { console.error("fallo al arrancar:", e); process.exit(1); });
