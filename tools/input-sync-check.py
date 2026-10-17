"""Are Lua's Input reads the same on every PC online? Compares the "[BTO-DIAG] ... pN input" lines
(diagnostics mod >= 0.5) of the log.txt of two PCs that played the same online run.

usage: python tools/input-sync-check.py <log PC 1> <log PC 2>
       python tools/input-sync-check.py --selftest

For each player: act = actions as Lua's Input.IsActionPressed reads them, mv/sh = movement and shooting
input of the player entity (simulation state, the reference). Uses the last run of each log.
"""
import re
import sys

LINE = re.compile(r"\[BTO-DIAG\] t=\d+ f=(\d+) p(\d+) input ci=(-?\d+) act=(\d+) mv=(\S+) sh=(\S+)")
START = re.compile(r"\[BTO-DIAG\] t=\d+ f=\d+ game started")
SEED = re.compile(r"RNG Start Seed: (\S+ \S+)")
OFFSETS = range(-8, 9)


def parse(text):
    """-> (seed, {player: [(frame, ci, act, mvsh)]}) for the last run with input lines."""
    runs, cur, seed, seeds = [], None, None, []
    for line in text.splitlines():
        m = SEED.search(line)
        if m:
            seed = m.group(1)
        if START.search(line):
            cur = {}
            runs.append(cur)
            seeds.append(seed)
            continue
        m = LINE.search(line)
        if m and cur is not None:
            f, p, ci, act = (int(m.group(i)) for i in range(1, 5))
            mvsh = (m.group(5) + " " + m.group(6)).replace("-0.0", "0.0")
            cur.setdefault(p, []).append((f, ci, act, mvsh))
    for run, s in reversed(list(zip(runs, seeds))):
        if run:
            return s, run
    return None, {}


def at(events, f):
    """State (ci, act, mvsh) in force at frame f (events sorted by frame), None before the first one."""
    state = None
    for e in events:
        if e[0] > f:
            break
        state = e[1:]
    return state


def compare(a, b):
    """-> verdict, report lines. a, b: {player: events} of the two PCs."""
    out, verdicts = [], []
    for p in sorted(set(a) & set(b)):
        ea, eb = a[p], b[p]
        lo, hi = max(ea[0][0], eb[0][0]) + 8, min(ea[-1][0], eb[-1][0]) - 8
        if hi - lo < 30:
            out.append(f"p{p}: troppo pochi frame in comune ({max(hi - lo, 0)})")
            continue
        frames = range(lo, hi + 1)
        sa = {f: at(ea, f) for f in range(lo - 8, hi + 9)}
        sb = {f: at(eb, f) for f in range(lo - 8, hi + 9)}
        presses_a = sum(1 for f in frames if sa[f][1])
        presses_b = sum(1 for f in frames if sb[f][1])
        ref = sum(1 for f in frames if sa[f][2] == sb[f][2]) / len(frames)
        best = max(OFFSETS, key=lambda k: (sum(1 for f in frames if sa[f][1] == sb[f + k][1]), -abs(k)))
        agree = sum(1 for f in frames if sa[f][1] == sb[f + best][1]) / len(frames)
        out.append(f"p{p}: controller PC1={sa[lo][0]} PC2={sb[lo][0]}, frame con azioni premute PC1={presses_a} PC2={presses_b}, "
                   f"azioni uguali {agree:.1%} con sfasamento {best:+d}, movimento/sparo del personaggio uguali {ref:.1%}")
        if ref < 0.99:
            verdicts.append("desync")
        elif presses_a + presses_b == 0:
            verdicts.append("idle")
        elif agree < 0.99 or (presses_a == 0) != (presses_b == 0):
            verdicts.append("local")
        elif best != 0:
            verdicts.append("shifted")
        else:
            verdicts.append("synced")
    for v, msg in [("desync", "INCERTO: il movimento dei personaggi differisce tra i due PC, la partita era già divisa"),
                   ("local", "NON SINCRONIZZATE: Lua legge le azioni di un giocatore diversamente sui due PC"),
                   ("shifted", "SFASATE: le azioni coincidono ma con qualche frame di differenza tra i PC, non usabili così"),
                   ("idle", "INCERTO: nessuno ha premuto niente"),
                   ("synced", "SINCRONIZZATE: Lua legge le stesse azioni, allo stesso frame, su tutti i PC")]:
        if v in verdicts:
            return v, out + [msg]
    return "none", out + ["INCERTO: nessun giocatore in comune nei due log"]


def log(frames_by_player, start_seed="AAAA BBBB"):
    """Synthetic log: {player: [(frame, ci, act, mvsh)]} -> text."""
    lines = [f"[INFO] - RNG Start Seed: {start_seed} (1) [New, 1]", "[INFO] - Lua Debug: [BTO-DIAG] t=1 f=0 game started continued=false"]
    for p, evs in frames_by_player.items():
        for f, ci, act, mvsh in evs:
            mv, sh = mvsh.split()
            lines.append(f"[INFO] - Lua Debug: [BTO-DIAG] t=1 f={f} p{p} input ci={ci} act={act} mv={mv} sh={sh}")
    return "\n".join(lines)


def selftest():
    still, walk = "0.0,0.0 0.0,0.0", "-1.0,0.0 0.0,0.0"
    # p0 walks left (act bit 1) frames 50-80, p1 presses Drop (bit 2048) frames 60-70; mv follows act on both PCs
    p0 = [(1, 0, 0, still), (50, 0, 1, walk), (80, 0, 0, still), (200, 0, 0, still)]
    p1 = [(1, 1, 0, still), (60, 1, 2048, still), (70, 1, 0, still), (200, 1, 0, still)]
    a = parse(log({0: p0, 1: p1}))[1]
    assert compare(a, parse(log({0: p0, 1: p1}))[1])[0] == "synced"
    # PC2 never sees p1's Drop: Lua reads only local hardware
    p1_dead = [(1, 1, 0, still), (200, 1, 0, still)]
    assert compare(a, parse(log({0: p0, 1: p1_dead}))[1])[0] == "local"
    # PC2 sees the same presses 2 frames later
    p1_late = [(1, 1, 0, still), (62, 1, 2048, still), (72, 1, 0, still), (200, 1, 0, still)]
    assert compare(a, parse(log({0: p0, 1: p1_late}))[1])[0] == "shifted"
    # the simulation itself differs: no verdict on input
    p0_off = [(1, 0, 0, still), (50, 0, 1, "-0.9,0.0 0.0,0.0"), (80, 0, 0, still), (200, 0, 0, still)]
    assert compare(a, parse(log({0: p0_off, 1: p1}))[1])[0] == "desync"
    # last run of the log wins, -0.0 equals 0.0
    two = log({0: [(1, 0, 0, still), (200, 0, 0, still)]}) + "\n" + log({0: p0, 1: p1}, "CCCC DDDD").replace("mv=0.0,0.0", "mv=-0.0,0.0")
    seed, run = parse(two)
    assert seed == "CCCC DDDD" and set(run) == {0, 1} and compare(run, a)[0] == "synced"
    print("selftest ok")


if __name__ == "__main__":
    if sys.argv[1:] == ["--selftest"]:
        selftest()
        sys.exit(0)
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    logs = [open(p, encoding="utf-8", errors="replace").read() for p in sys.argv[1:]]
    (s1, r1), (s2, r2) = parse(logs[0]), parse(logs[1])
    print(f"run PC1: seed {s1}, giocatori {sorted(r1)} | run PC2: seed {s2}, giocatori {sorted(r2)}")
    if s1 != s2:
        print("ATTENZIONE: seed diversi, forse non è la stessa run")
    verdict, lines = compare(r1, r2)
    print("\n".join(lines))
    sys.exit(0 if verdict == "synced" else 1)
