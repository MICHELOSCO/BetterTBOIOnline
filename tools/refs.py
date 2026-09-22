"""Read-only: list direct calls to a function VA, and instructions using a given displacement.

usage: python tools/refs.py <exe> calls <VA>      e.g. calls 0x90DF40
       python tools/refs.py <exe> disp <DISP>     e.g. disp 0x1868
"""
import struct
import sys

from capstone import CS_ARCH_X86, CS_MODE_32, Cs

from xref import load, off2va

exe, mode, val = sys.argv[1], sys.argv[2], int(sys.argv[3], 0)
b, base, secs = load(exe)
_, tva, tvs, traw, trs = next(s for s in secs if s[0] == ".text")
md = Cs(CS_ARCH_X86, CS_MODE_32)


def show(off, before=0x30, after=0x18):
    lo = off - before
    for ins in md.disasm(b[lo:off + after], off2va(lo, base, secs)):
        print(f"   {ins.address:08X}: {ins.mnemonic:6} {ins.op_str}")


if mode == "calls":
    for off in range(traw, traw + trs - 5):
        if b[off] == 0xE8:
            rel = struct.unpack_from("<i", b, off + 1)[0]
            if off2va(off, base, secs) + 5 + rel == val:
                print(f"-- call @ {off2va(off, base, secs):08X}")
                show(off)
elif mode == "disp":
    needle = struct.pack("<I", val)
    seen = set()
    off = b.find(needle, traw, traw + trs)
    while off != -1:
        # confirm by disassembling a window and matching an operand with this displacement
        for ins in md.disasm(b[off - 8:off + 12], off2va(off - 8, base, secs)):
            if f"0x{val:x}]" in ins.op_str and ins.address not in seen:
                seen.add(ins.address)
                print(f"{ins.address:08X}: {ins.mnemonic:6} {ins.op_str}")
        off = b.find(needle, off + 1, traw + trs)
elif mode == "dis":  # dis <VA> <len>: disassemble, annotating immediates that point at strings
    from xref import va2off
    n = int(sys.argv[4], 0)
    off = va2off(val, base, secs)
    for ins in md.disasm(b[off:off + n], val):
        note = ""
        for tok in ins.op_str.replace("[", " ").replace("]", " ").replace(",", " ").split():
            if tok.startswith("0x") and len(tok) >= 8:
                so = va2off(int(tok, 16), base, secs)
                if so and 0x20 <= b[so] < 0x7F:
                    s = b[so:b.find(b"\0", so)][:70]
                    if len(s) > 4 and all(0x20 <= c < 0x7F or c in (9, 10) for c in s):
                        note = f'   ; "{s.decode().strip()}"'
        print(f"   {ins.address:08X}: {ins.mnemonic:6} {ins.op_str}{note}")
