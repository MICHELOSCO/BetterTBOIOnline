"""In-memory patch for Repentance+ v1.9.7.17.J460: let a split (post-desync) game accept a pending rejoin.

Nothing on disk is modified; closing the game undoes it. See docs/research-notes.md.

Why: at every stage start the game calls NetManager::ProcessPendingJoins(force=1) (VA 0x90DF40)
only if it still considers itself in an online match. After a desync split the host is "offline",
so the else-branch just sets a flag and queued joins wait forever ("waiting").
Patch: the else-branch (VA 0x958DBD) jumps to a cave that keeps the flag write and additionally
calls ProcessPendingJoins(force=0), which returns immediately when no join is queued.

usage: python tools/rejoin_patch.py [--revert | --status | --selftest]
"""
import ctypes
import ctypes.wintypes as wt
import struct
import sys

IMAGE_BASE = 0x400000                      # VAs below are from the unrelocated exe (ASLR is on)
SITE, CAVE = 0x958DBD, 0x9AFD89
PROCESS_JOINS, RESUME = 0x90DF40, 0x958DC4
SITE_ORIG = bytes.fromhex("c68330b1040001")    # mov byte ptr [ebx+0x4B130], 1
CAVE_LEN = 19


def rel32(src_next, dst):
    return struct.pack("<i", dst - src_next)


def build(base):
    """Return (site_bytes, cave_bytes) for a module loaded at `base`."""
    d = base - IMAGE_BASE
    site, cave = SITE + d, CAVE + d
    cave_code = (SITE_ORIG                                       # keep original flag write
                 + b"\x6a\x00"                                   # push 0 (force = false); ecx = NetManager
                 + b"\xe8" + rel32(cave + 14, PROCESS_JOINS + d)  # call ProcessPendingJoins (ret 4)
                 + b"\xe9" + rel32(cave + 19, RESUME + d))       # jmp back after the original instruction
    site_code = b"\xe9" + rel32(site + 5, cave) + b"\x90\x90"
    assert len(cave_code) == CAVE_LEN and len(site_code) == len(SITE_ORIG)
    return site_code, cave_code


def selftest():
    s, c = build(IMAGE_BASE)
    assert s == bytes.fromhex("e9c76f05009090"), s.hex()          # jmp 0x9AFD89
    assert c[9:14] == b"\xe8" + rel32(CAVE + 14, PROCESS_JOINS)
    assert struct.unpack("<i", c[15:19])[0] + CAVE + 19 == RESUME
    s2, c2 = build(0x1230000)                                     # relative code is base-independent
    assert s2 == s and c2 == c
    print("selftest ok")


# --- minimal Win32 process memory access ------------------------------------------------------
k32 = ctypes.WinDLL("kernel32", use_last_error=True)
TH32CS_SNAPPROCESS, TH32CS_SNAPMODULE, TH32CS_SNAPMODULE32 = 0x2, 0x8, 0x10
PROCESS_ALL = 0x0008 | 0x0010 | 0x0020 | 0x0400   # VM_OPERATION | VM_READ | VM_WRITE | QUERY_INFO
PAGE_EXECUTE_READWRITE = 0x40


class PROCESSENTRY32W(ctypes.Structure):
    _fields_ = [("dwSize", wt.DWORD), ("cntUsage", wt.DWORD), ("th32ProcessID", wt.DWORD),
                ("th32DefaultHeapID", ctypes.c_size_t), ("th32