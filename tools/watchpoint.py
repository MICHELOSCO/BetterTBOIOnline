"""usage: watchpoint.py <pid> [seconds] [offset-from-g_Game, default 0x22ed0]: hardware write-watchpoint on the running 32-bit isaac-ng.
Attaches as a debugger, reports the EIP (and a few stack return addresses) of every write, then detaches cleanly."""
import ctypes, ctypes.wintypes as w, sys, struct, time, collections
k = ctypes.windll.kernel32
pid = int(sys.argv[1]); seconds = float(sys.argv[2]) if len(sys.argv) > 2 else 4.0
DWORD = w.DWORD
class WOW64_CONTEXT(ctypes.Structure):
    _fields_ = [("ContextFlags", DWORD), ("Dr0", DWORD), ("Dr1", DWORD), ("Dr2", DWORD), ("Dr3", DWORD), ("Dr6", DWORD), ("Dr7", DWORD),
                ("FloatSave", ctypes.c_byte * 112), ("SegGs", DWORD), ("SegFs", DWORD), ("SegEs", DWORD), ("SegDs", DWORD),
                ("Edi", DWORD), ("Esi", DWORD), ("Ebx", DWORD), ("Edx", DWORD), ("Ecx", DWORD), ("Eax", DWORD),
                ("Ebp", DWORD), ("Eip", DWORD), ("SegCs", DWORD), ("EFlags", DWORD), ("Esp", DWORD), ("SegSs", DWORD),
                ("ExtendedRegisters", ctypes.c_byte * 512)]
class DEBUG_EVENT(ctypes.Structure):
    _fields_ = [("code", DWORD), ("pid", DWORD), ("tid", DWORD), ("u", ctypes.c_byte * 176)]
CTX_I386 = 0x10000; CTX_CONTROL = CTX_I386 | 1; CTX_INTEGER = CTX_I386 | 2; CTX_DEBUG = CTX_I386 | 0x10
PROCESS_ALL = 0x1F0FFF; THREAD_ALL = 0x1F03FF
k.OpenProcess.restype = w.HANDLE; k.OpenThread.restype = w.HANDLE
k.ReadProcessMemory.argtypes = [w.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]
hp = k.OpenProcess(PROCESS_ALL, False, pid)
def rd(addr, n):
    buf = ctypes.create_string_buffer(n); got = ctypes.c_size_t(0)
    k.ReadProcessMemory(hp, ctypes.c_void_p(addr), buf, n, ctypes.byref(got)); return buf.raw[:got.value]
# module base via toolhelp
class MODULEENTRY32(ctypes.Structure):
    _fields_ = [("dwSize", DWORD), ("th32ModuleID", DWORD), ("th32ProcessID", DWORD), ("GlblcntUsage", DWORD), ("ProccntUsage", DWORD),
                ("modBaseAddr", ctypes.c_void_p), ("modBaseSize", DWORD), ("hModule", ctypes.c_void_p), ("szModule", ctypes.c_char * 256), ("szExePath", ctypes.c_char * 260)]
snap = k.CreateToolhelp32Snapshot(0x8 | 0x10, pid)  # TH32CS_SNAPMODULE | SNAPMODULE32
me = MODULEENTRY32(); me.dwSize = ctypes.sizeof(MODULEENTRY32)
k.Module32First(snap, ctypes.byref(me)); base = me.modBaseAddr; k.CloseHandle(snap)
g_game = struct.unpack("<I", rd(base + (0xC71678 - 0x400000), 4))[0]
target = g_game + (int(sys.argv[3], 0) if len(sys.argv) > 3 else 0x22ed0)
print(f"base=0x{base:X} g_Game=0x{g_game:X} watch addr=0x{target:X} (static VA of writers = eip - 0x{base:X} + 0x400000)")
class THREADENTRY32(ctypes.Structure):
    _fields_ = [("dwSize", DWORD), ("cntUsage", DWORD), ("th32ThreadID", DWORD), ("th32OwnerProcessID", DWORD), ("tpBasePri", ctypes.c_long), ("tpDeltaPri", ctypes.c_long), ("dwFlags", DWORD)]
def threads():
    s = k.CreateToolhelp32Snapshot(0x4, 0); te = THREADENTRY32(); te.dwSize = ctypes.sizeof(THREADENTRY32); out = []
    if k.Thread32First(s, ctypes.byref(te)):
        while True:
            if te.th32OwnerProcessID == pid: out.append(te.th32ThreadID)
            if not k.Thread32Next(s, ctypes.byref(te)): break
    k.CloseHandle(s); return out
def set_dr(tid, enable):
    ht = k.OpenThread(THREAD_ALL, False, tid)
    if not ht: return
    ctx = WOW64_CONTEXT(); ctx.ContextFlags = CTX_DEBUG
    if k.Wow64GetThreadContext(ht, ctypes.byref(ctx)):
        ctx.Dr0 = target if enable else 0
        ctx.Dr7 = 0x10001 if enable else 0   # L0 | RW0=write, LEN0=1 byte
        ctx.Dr6 = 0
        k.Wow64SetThreadContext(ht, ctypes.byref(ctx))
    k.CloseHandle(ht)
k.DebugSetProcessKillOnExit(False)
if not k.DebugActiveProcess(pid): print("DebugActiveProcess failed", ctypes.GetLastError()); sys.exit(1)
ev = DEBUG_EVENT(); hits = collections.Counter(); samples = {}
armed = False; t0 = time.time()
while time.time() - t0 < seconds:
    if not k.WaitForDebugEvent(ctypes.byref(ev), 200): continue
    status = 0x00010002  # DBG_CONTINUE
    if ev.code == 1:  # EXCEPTION_DEBUG_EVENT
        exc = struct.unpack_from("<I", bytes(ev.u), 0)[0]
        if exc in (0x80000003, 0x4000001F) and not armed:  # initial breakpoint: arm all threads while paused
            for t in threads(): set_dr(t, True)
            armed = True
        else:
            ht = k.OpenThread(THREAD_ALL, False, ev.tid); ctx = WOW64_CONTEXT(); ctx.ContextFlags = CTX_CONTROL | CTX_INTEGER | CTX_DEBUG
            k.Wow64GetThreadContext(ht, ctypes.byref(ctx))
            ours = (ctx.Dr6 & 1) != 0 or exc in (0x80000004, 0x4000001E)
            print(f"  exception code=0x{exc:08X} eip=0x{ctx.Eip - base + 0x400000:08X} dr6=0x{ctx.Dr6:X} ours={ours}", flush=True)
            if ours:
                ctx.Dr6 = 0; ctx.ContextFlags = CTX_DEBUG; k.Wow64SetThreadContext(ht, ctypes.byref(ctx))
            k.CloseHandle(ht)
            if not ours:
                status = 0x80010001
            else:
                eip = ctx.Eip; hits[eip] += 1
            if ours and eip not in samples:
                stack = rd(ctx.Esp, 0x100); rets = []
                for i in range(0, len(stack) - 3, 4):
                    v = struct.unpack_from("<I", stack, i)[0]
                    if base <= v < base + 0x900000: rets.append(f"{v - base + 0x400000:08X}")
                samples[eip] = (rd(target, 1), rets[:8]); print(f'  hit eip=0x{eip - base + 0x400000:08X} t={time.time()-t0:.2f}s value_after={rd(target,1).hex()}', flush=True)
    elif ev.code in (2, 3):  # CREATE_THREAD / CREATE_PROCESS: arm this thread
        set_dr(ev.tid, True)
    k.ContinueDebugEvent(ev.pid, ev.tid, status)
# disarm and detach
for t in threads(): set_dr(t, False)
k.DebugActiveProcessStop(pid)
for eip, n in hits.most_common():
    val, rets = samples[eip]
    print(f"writer eip=0x{eip:08X} static=0x{eip - base + 0x400000:08X} hits={n} value_after={val.hex() if val else '?'} stack_rets={rets}")
if not hits: print("no writes observed")
