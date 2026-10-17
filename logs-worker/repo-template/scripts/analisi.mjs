// node scripts/analisi.mjs <repo folder>: for each game folder (AAAA-MM-GG_seed-XXXX) with at least 2 logs writes
// ANALISI.md: first frame where the logs part ways, each player's mods and versions, Lua errors per mod.
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";

// Lines that must be the same on every client of one game, as they are compared (the clock, fps and controller
// numbers differ per PC and are removed). ponytail: a fixed list; add lines here when a desync hides behind others.
const SAME = /Spawn Entity|SpawnRNG seed|Initialized as:|Level::Init|Starting room transition|Starting stage transition|TriggerBossDeath|RNG Start Seed|\[BTO-DIAG\]/;
const norm = (l) =>
  l.replace(/^\[\w+\] - /, "").replace(/^Lua Debug: /, "").replace(/\[Frame:? \d+\] /, "").replace(/ (t|fr|ctrl)=-?\d+/g, "").trim();

export function events(log) {
  const out = [];
  let frame = 0;
  for (const l of log.split("\n")) {
    const f = l.match(/\[Frame:? (\d+)\]/);
    if (f) frame = +f[1];
    if (SAME.test(l)) out.push({ frame, line: norm(l) });
  }
  return out;
}

// the first event where b differs from a (null: the same as far as the shorter one goes)
export function divergence(a, b) {
  const n = Math.min(a.length, b.length);
  for (let i = 0; i < n; i++) if (a[i].line !== b[i].line) return { i, a: a[i], b: b[i] };
  return null;
}

const cell = (s) => String(s ?? "").replace(/\|/g, "\\|").replace(/\n/g, " ");
const modKey = (m) => (m.id ? `ws:${m.id}` : m.folder);

export function analyse(players) {
  const md = [];
  const { seed, time } = players[0].meta;
  md.push(`# Partita del ${time.slice(0, 10)}, seed ${seed}`, "");
  md.push(`${players.length} log. Scritta da scripts/analisi.mjs: non modificarla a mano.`, "");
  md.push("| Log | BTO Sync | Desync al frame | Desync | Mod attive |", "|---|---|---|---|---|");
  for (const p of players)
    md.push(`| ${p.name} | ${p.meta.app} | ${p.meta.desyncFrame < 0 ? "nessuno" : p.meta.desyncFrame} | ${p.meta.desyncs} | ${p.meta.mods.length} |`);

  md.push("", "## Primo frame di divergenza", "");
  const ev = players.map((p) => events(p.log));
  const diffs = players.slice(1).map((p, k) => ({ p, d: divergence(ev[0], ev[k + 1]) }));
  const found = diffs.filter((x) => x.d).sort((x, y) => Math.min(x.d.a.frame, x.d.b.frame) - Math.min(y.d.a.frame, y.d.b.frame));
  if (!found.length) md.push(`Nessuna differenza nelle righe confrontate (${Math.min(...ev.map((e) => e.length))} in comune).`);
  else {
    const { p, d } = found[0];
    md.push(`Al frame **${d.a.frame}** (${players[0].name}) / **${d.b.frame}** (${p.name}), evento n. ${d.i + 1}:`, "");
    md.push("```", `${players[0].name}: ${d.a.line}`, `${p.name}: ${d.b.line}`, "```");
    for (const x of found.slice(1)) md.push("", `Anche ${x.p.name}: frame ${x.d.b.frame}, \`${cell(x.d.b.line)}\` invece di \`${cell(x.d.a.line)}\`.`);
  }

  md.push("", "## Mod e versioni", "");
  const all = new Map();
  for (const p of players) for (const m of p.meta.mods) if (!all.has(modKey(m))) all.set(modKey(m), m.name || m.folder);
  md.push(`| Mod | ${players.map((p) => p.name).join(" | ")} |`, `|---|${players.map(() => "---|").join("")}`);
  const rows = [...all].map(([k, name]) => {
    const vals = players.map((p) => {
      const m = p.meta.mods.find((x) => modKey(x) === k);
      return m ? `${m.version || "-"} ${m.hash}`.trim() : "—";
    });
    return { name, vals, differ: new Set(vals).size > 1 };
  });
  rows.sort((a, b) => b.differ - a.differ || a.name.localeCompare(b.name));
  for (const r of rows) md.push(`| ${r.differ ? "⚠ " : ""}${cell(r.name)} | ${r.vals.map(cell).join(" | ")} |`);
  if (rows.some((r) => r.differ)) md.push("", "⚠ = diversa tra i giocatori (versione e inizio dell'hash dei file; — = non attiva).");

  md.push("", "## Errori Lua per mod", "");
  const lua = new Set(players.flatMap((p) => Object.keys(p.meta.luaErrors)));
  if (!lua.size) md.push("Nessun errore Lua.");
  else {
    md.push(`| Mod | ${players.map((p) => p.name).join(" | ")} |`, `|---|${players.map(() => "---|").join("")}`);
    for (const k of [...lua].sort()) md.push(`| ${cell(k || "il gioco")} | ${players.map((p) => p.meta.luaErrors[k] ?? 0).join(" | ")} |`);
    md.push("", "Errori diversi tra loro: lo stesso errore ripetuto conta una volta.");
  }
  return md.join("\n") + "\n";
}

function main(root) {
  if (!fs.existsSync(root)) return;  // nothing waiting to be read
  for (const dir of fs.readdirSync(root, { withFileTypes: true })) {
    if (!dir.isDirectory() || !/^\d{4}-\d{2}-\d{2}_seed-/.test(dir.name)) continue;
    const folder = path.join(root, dir.name);
    const players = fs
      .readdirSync(folder)
      .filter((f) => f.endsWith(".json") && fs.existsSync(path.join(folder, f.replace(/\.json$/, ".log.gz"))))
      .sort()
      .map((f) => ({
        name: f.replace(/\.json$/, ""),
        meta: JSON.parse(fs.readFileSync(path.join(folder, f), "utf8")),
        log: zlib.gunzipSync(fs.readFileSync(path.join(folder, f.replace(/\.json$/, ".log.gz")))).toString("utf8"),
      }));
    if (players.length < 2) continue;
    fs.writeFileSync(path.join(folder, "ANALISI.md"), analyse(players));
    console.log(`${dir.name}: ${players.length} log`);
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === path.resolve(import.meta.filename)) main(process.argv[2] || ".");
