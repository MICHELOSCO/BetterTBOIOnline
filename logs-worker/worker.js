// BTO logs: receives a game log from BTO Sync (Core.LogPayload), cleans it again and commits it to the private
// repository env.REPO with the GitHub Contents API. The token lives only in the Worker's secrets (GITHUB_TOKEN).

const MAX_BODY = 2_000_000; // bytes of JSON as received
const MAX_LOG = 40_000_000; // bytes of the log once unzipped (a gzip bomb stops here)

const json = (status, body) => new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });

export default {
  async fetch(req, env) {
    if (req.method !== "POST") return json(405, { error: "solo POST" });
    if (+(req.headers.get("content-length") || 0) > MAX_BODY) return json(413, { error: "troppo grande" });
    if (env.LIMITER) {
      const { success } = await env.LIMITER.limit({ key: req.headers.get("cf-connecting-ip") || "?" });
      if (!success) return json(429, { error: "troppi invii, riprova tra un minuto" });
    }
    let body;
    try {
      body = JSON.parse(new TextDecoder().decode(await readCapped(req.body, MAX_BODY)));
    } catch (e) {
      return e.message === "troppo grande" ? json(413, { error: e.message }) : json(400, { error: "JSON non valido" });
    }
    const meta = check(body);
    if (typeof meta === "string") return json(400, { error: meta });
    let log;
    try {
      log = new TextDecoder().decode(await readCapped(new Blob([b64decode(body.log)]).stream().pipeThrough(new DecompressionStream("gzip")), MAX_LOG));
    } catch (e) {
      return e.message === "troppo grande" ? json(413, { error: "log troppo grande" }) : json(400, { error: "log non leggibile" });
    }
    const gz = await new Response(new Blob([clean(log)]).stream().pipeThrough(new CompressionStream("gzip"))).arrayBuffer();
    const metaText = clean(JSON.stringify({ ...meta, received: new Date().toISOString() }, null, 2)) + "\n";

    const dir = `da-analizzare/${meta.time.slice(0, 10)}_seed-${meta.seed}`;  // once read, a game folder is moved to analizzati/ by hand
    for (let n = 1; n <= 9; n++) {
      const name = n === 1 ? meta.id : `${meta.id}-${n}`; // the same player twice in one game folder: -2, -3...
      const r = await put(env, `${dir}/${name}.json`, b64encode(new TextEncoder().encode(metaText)), `log: ${dir}/${name}`);
      if (r.status === 422) continue; // already there
      if (!r.ok) return json(502, { error: `GitHub ha risposto ${r.status}` });
      const l = await put(env, `${dir}/${name}.log.gz`, b64encode(new Uint8Array(gz)), `log: ${dir}/${name}`);
      if (!l.ok) return json(502, { error: `GitHub ha risposto ${l.status}` });
      return json(201, { ok: true, path: `${dir}/${name}` });
    }
    return json(409, { error: "troppi log uguali per questa partita" });
  },
};

function put(env, path, content, message) {
  return fetch(`${env.GITHUB_API || "https://api.github.com"}/repos/${env.REPO}/contents/${path}`, {
    method: "PUT",
    headers: {
      authorization: `Bearer ${env.GITHUB_TOKEN}`,
      accept: "application/vnd.github+json",
      "x-github-api-version": "2022-11-28",
      "user-agent": "bto-logs-worker",
      "content-type": "application/json",
    },
    body: JSON.stringify({ message, content }),
  });
}

// Only known fields, each checked: what ends up in the repository is never free text from the request.
export function check(b) {
  if (!b || typeof b !== "object" || b.v !== 1) return "versione dei dati sconosciuta";
  const str = (s, re, max = 200) => typeof s === "string" && s.length <= max && re.test(s);
  if (!str(b.id, /^[0-9a-f]{12}$/)) return "id non valido";
  if (!str(b.seed, /^([A-Z0-9]{8}|nessuno)$/)) return "seed non valido";
  if (!str(b.time, /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$/) || isNaN(Date.parse(b.time))) return "ora non valida";
  if (!str(b.app, /^\d{1,4}\.\d{1,4}\.\d{1,4}$/)) return "versione non valida";
  if (!str(b.code, /^BTO3:[A-Za-z0-9+/]*$/, 20000)) return "codice delle mod non valido";
  if (typeof b.log !== "string" || !/^[A-Za-z0-9+/]*={0,2}$/.test(b.log)) return "log mancante";
  const int = (n) => Number.isInteger(n) && n >= -1 && n < 1e9;
  if (!int(b.desyncFrame) || !int(b.desyncs)) return "desync non valido";
  if (!Array.isArray(b.mods) || b.mods.length > 500) return "mod non valide";
  const mods = [];
  for (const m of b.mods) {
    if (!m || typeof m !== "object") return "mod non valide";
    const f = {};
    for (const k of ["folder", "id", "name", "version", "hash"]) {
      if (m[k] != null && typeof m[k] !== "string") return "mod non valide";
      f[k] = (m[k] || "").slice(0, 120);
    }
    mods.push(f);
  }
  const lua = {};
  if (!b.luaErrors || typeof b.luaErrors !== "object" || Object.keys(b.luaErrors).length > 500) return "errori Lua non validi";
  for (const [k, n] of Object.entries(b.luaErrors)) {
    if (k.length > 120 || !int(n)) return "errori Lua non validi";
    lua[k] = n;
  }
  return { v: 1, app: b.app, id: b.id, seed: b.seed, time: b.time, code: b.code, desyncFrame: b.desyncFrame, desyncs: b.desyncs, luaErrors: lua, mods };
}

// the same cleaning as Core.CleanLog in BTO Sync, again here: a client could be old or modified
const NAME_LINES = [
  /User (?<id>7656119\d{10}) \((?<n>[^)\r\n]+)\)/g,
  /from user (?<n>[^\[\r\n]+?) \[(?<id>7656119\d{10})\]/g,
  /"(?<n>[^"\r\n]+)" (?<id>7656119\d{10})/g,
  /Username = (?<n>.+?),? ID = (?<id>7656119\d{10})/g,
];

export function clean(text) {
  const label = new Map();
  for (const [id] of text.matchAll(/7656119\d{10}/g)) if (!label.has(id)) label.set(id, `Giocatore ${label.size + 1}`);
  const who = new Map();
  for (const re of NAME_LINES)
    for (const m of text.matchAll(re)) {
      const n = m.groups.n.trim();
      if (n && !who.has(n)) who.set(n, label.get(m.groups.id));
    }
  for (const n of [...who.keys()].sort((a, b) => b.length - a.length))
    text = text.replace(new RegExp(`(?<![\\p{L}\\p{N}_])${n.replace(/[.*+?^${}()|[\]\\/]/g, "\\$&")}(?![\\p{L}\\p{N}_])`, "gu"), () => who.get(n));
  text = text.replace(/7656119\d{10}/g, (id) => label.get(id));
  return text.replace(/\b([a-z]):([\\/]+)Users[\\/]+[^\\/\r\n"]+/gi, "$1:$2Users$2<utente>");
}

async function readCapped(stream, max) {
  if (!stream) return new Uint8Array();
  const parts = [];
  let size = 0;
  for await (const c of stream) {
    size += c.length;
    if (size > max) throw new Error("troppo grande");
    parts.push(c);
  }
  const out = new Uint8Array(size);
  let at = 0;
  for (const p of parts) {
    out.set(p, at);
    at += p.length;
  }
  return out;
}

function b64decode(s) {
  const bin = atob(s);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}

function b64encode(bytes) {
  let bin = "";
  for (let i = 0; i < bytes.length; i += 0x8000) bin += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
  return btoa(bin);
}
