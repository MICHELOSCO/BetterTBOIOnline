// npm test: the Worker under `wrangler dev`, with the pretend GitHub of fake-github.mjs, on fake players and ids.
// Then the Action's script on what got "committed". Exit code 0 = ok.
import { spawn } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";
import assert from "node:assert/strict";

const here = import.meta.dirname, out = path.join(here, "out");
const state = path.join(here, ".state");  // wrangler's local storage, rate limit counts included: fresh each run
for (const d of [out, state]) fs.rmSync(d, { recursive: true, force: true });
const kids = [];
const run = (cmd, args, opts) => {
  const p = spawn(cmd, args, { cwd: path.join(here, ".."), ...opts });
  kids.push(p);
  return p;
};
const ready = (p, text) =>
  new Promise((ok, fail) => {
    let log = "";
    const t = setTimeout(() => fail(new Error("non parte:\n" + log)), 90_000);
    const see = (c) => { log += c; if (log.includes(text)) { clearTimeout(t); ok(); } };
    p.stdout.on("data", see);
    p.stderr.on("data", see);
  });

const A = "7656119" + "0000000001", B = "7656119" + "0000000002"; // fake Steam IDs, split so the source holds none
const game = (desyncAt, extra) =>
  [
    "[INFO] - RNG Start Seed: AAAA BBBB (1) [Net, 3]",
    "[INFO] - [Frame 0] User " + A + " (Ann) joined lobby 1",
    "[INFO] - [Frame 1] Received Player Info message from user Bob [" + B + "]",
    "[INFO] - Loading C:\\Users\\mario\\Documents\\x.lua",
    "[INFO] - [Frame 5] SpawnRNG seed: 100",
    "[INFO] - Lua Debug: [BTO-DIAG] t=111 f=3 new room idx=84 players=2",
    "[INFO] - [Frame 9] Spawn Entity with Type(5), Variant(" + extra + "), Pos(1.0,2.0)",
    `[ERROR] - Desync detected on frame: ${desyncAt} `,
    "[ERROR] -  - Player 0: Username = Ann ID = " + A,
  ].join("\n");
const payload = (id, log, mods) => ({
  v: 1, app: "2.8.0", id, code: "BTO3:AAE", seed: "AAAABBBB", time: "2026-10-17T18:00:00Z", desyncFrame: 12, desyncs: 1,
  luaErrors: { "thefuture_1": 2 }, mods, log: zlib.gzipSync(log).toString("base64"),
});
const mods = (v) => [{ folder: "bto-join-sync", id: "", name: "BTO Join Sync", version: v, hash: "abcdef01" }];

try {
  const gh = run("node", ["test/fake-github.mjs", "8788"]);
  await ready(gh, "finto GitHub");
  const w = run("node", ["node_modules/wrangler/bin/wrangler.js", "dev", "--port", "8787", "--ip", "127.0.0.1", "--var", "GITHUB_API:http://127.0.0.1:8788", "--var", "GITHUB_TOKEN:finto", "--persist-to", state]);
  await ready(w, "Ready on");
  const post = (body) => fetch("http://127.0.0.1:8787/", { method: "POST", headers: { "content-type": "application/json" }, body: typeof body === "string" ? body : JSON.stringify(body) });

  assert.equal((await fetch("http://127.0.0.1:8787/")).status, 405, "GET");
  let r = await post(payload("aaaaaaaaaaaa", game(12, 1), mods("1.0")));  // a log nobody cleaned (old or modified client)
  assert.equal(r.status, 201, await r.text());
  r = await post(payload("bbbbbbbbbbbb", game(12, 2), mods("1.1")));
  assert.equal(r.status, 201);
  r = await post(payload("aaaaaaaaaaaa", game(12, 1), mods("1.0")));
  assert.equal((await r.json()).path, "da-analizzare/2026-10-17_seed-AAAABBBB/aaaaaaaaaaaa-2", "same player twice: -2");
  assert.equal((await post("{non json")).status, 400, "bad JSON");
  assert.equal((await post({ ...payload("cccccccccccc", "x", []), seed: "../../x" })).status, 400, "bad seed");
  assert.equal((await post("x".repeat(2_100_000))).status, 413, "too big");
  let limited = false;
  for (let i = 0; i < 6 && !limited; i++) limited = (await post("{}")).status === 429;
  assert.ok(limited, "rate limit per IP");
  console.log("pass Worker: POST, 405/400/413/429, -2 per lo stesso giocatore");

  const dir = path.join(out, "da-analizzare", "2026-10-17_seed-AAAABBBB");
  for (const f of fs.readdirSync(dir)) {
    const t = f.endsWith(".gz") ? zlib.gunzipSync(fs.readFileSync(path.join(dir, f))).toString() : fs.readFileSync(path.join(dir, f), "utf8");
    assert.ok(!/7656119|Ann|Bob|mario/.test(t), "cleaned: " + f);
  }
  const one = zlib.gunzipSync(fs.readFileSync(path.join(dir, "aaaaaaaaaaaa.log.gz"))).toString();
  assert.ok(one.includes("User Giocatore 1 (Giocatore 1)") && one.includes("C:\\Users\\<utente>\\Documents"), one);
  console.log("pass Worker: niente Steam ID, nomi e cartella utente nei file salvati");

  const a = spawn("node", [path.join(here, "..", "repo-template", "scripts", "analisi.mjs"), path.join(out, "da-analizzare")], { stdio: "inherit" });
  await new Promise((ok) => a.on("exit", ok));
  const md = fs.readFileSync(path.join(dir, "ANALISI.md"), "utf8");
  assert.ok(md.includes("Al frame **9**") && md.includes("Variant(1)") && md.includes("Variant(2)"), md);
  assert.ok(md.includes("⚠ BTO Join Sync | 1.0 abcdef01 | 1.0 abcdef01 | 1.1 abcdef01"), md);
  assert.ok(md.includes("| thefuture_1 | 2 | 2 | 2 |"), md);
  console.log("pass Action: ANALISI.md con frame di divergenza, mod diverse ed errori Lua");
  console.log("OK");
} catch (e) {
  console.error(e.message);
  process.exitCode = 1;
} finally {
  for (const k of kids) {
    if (process.platform === "win32") spawn("taskkill", ["/pid", String(k.pid), "/t", "/f"]);
    else k.kill();
  }
}
