"""Read-only static helper: find code referencing a string in isaac-ng.exe and disassemble around it.

usage: python tools/xref.py <exe> "<substring>" [bytes_before] [bytes_after]
"""
import struct
import sys

from capstone import CS_ARCH_X86, CS_MODE_32, Cs


def load(path):
    b = open(path, "rb").read()
    pe = struct.unpack_from("<I", b, 0x3C)[0]
    n = struct.unpack_from("<H", b, pe + 6)[0]
    opt = struct.unpack_from("<H", b, pe + 20)[0]
    base = struct.unpack_from("<I", b, pe + 24 + 28)[0]
    secs = []
    for i in range(n):
        o = pe + 24 + opt + 40 * i
        name = b[o:o + 8].rstrip(b"\0").decode()
        vsize, va, rsize, raw = struct.unpack_from("<IIII", b, o + 8)
        secs.append((name, va, vsize, raw, rsize))
    return b, base, secs


def off2va(off, base, secs):
    for _, va, vs, raw, rs in secs:
        if raw <= off < raw + rs:
            return base + va + off - raw


def va2off(v, base, secs):
    for _, va, vs, raw, rs in secs:
        if base + va <= v < base + va + vs:
            return v - base - va + raw


def main():
    exe, needle = sys.argv[1], sys.argv[2].encode()
    before = int(sys.argv[3], 0) if len(sys.argv) > 3 else 0x60
    after = int(sys.argv[4], 0) if len(sys.argv) > 4 else 0x60
    b, base, secs = load(exe)
    text = next(s for s in secs if s[0] == ".text")
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    pos = b.find(needle)
    while pos != -1:
        start = pos
        while start > 0 and b[start - 1] != 0:  # back to string start
            start -= 1
        sva = off2va(start, base, secs)
        print(f"== string @VA {sva:08X}: {b[start:b.find(b'\0', start)][:90]!r}")
        imm = struct.pack("<I", sva)
        t0, t1 = text[3], text[3] + text[4]
        r = b.find(imm, t0, t1)
        while r != -1:
            ins_off = r - 1  # usually push imm32 (68) / mov reg,imm32
            cva = off2va(ins_off, base, secs)
            print(f"-- ref @VA {cva:08X}")
            lo = ins_off - before
            for ins in md.disasm(b[lo:ins_off + after], off2va(lo, base, secs)):
                mark = "  <==" if ins.address == cva else ""
                print(f"   {ins.address:08X}: {ins.mnemonic:6} {ins.op_str}{mark}")
            r = b.find(imm, r + 1, t1)
        pos = b.find(needle, pos + 1)


if __name__ == "__main__":
    main()
