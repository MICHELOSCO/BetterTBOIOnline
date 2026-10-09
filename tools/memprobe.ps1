# Read-only probe of the running isaac-ng: HUD-visible byte (g_Game+0x22ed0), revive-menu state, RoomTransition dump. Run while the game is in a run.
Add-Type @"
using System; using System.Runtime.InteropServices;
public class Mem {
  [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
  [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int read);
  [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
}
"@
$p = Get-Process isaac-ng -ErrorAction Stop | Select-Object -First 1
$base = [int64]$p.MainModule.BaseAddress
$h = [Mem]::OpenProcess(0x10, $false, $p.Id)
function ReadMem([int64]$addr, [int]$n) { $b = New-Object byte[] $n; $r = 0; [void][Mem]::ReadProcessMemory($h, [IntPtr]$addr, $b, $n, [ref]$r); return $b }
$gGame = [BitConverter]::ToUInt32((ReadMem ($base + (0xC71678 - 0x400000)) 4), 0)
if ($gGame -eq 0) { "g_Game is null (no run)"; [void][Mem]::CloseHandle($h); exit }
$flag = (ReadMem ([int64]$gGame + 0x22ed0) 1)[0]
$menu = [BitConverter]::ToInt32((ReadMem ([int64]$gGame + 0x24ecc) 4), 0)
$menuCtrl = [BitConverter]::ToInt32((ReadMem ([int64]$gGame + 0x24ed0) 4), 0)
$rt = ReadMem ([int64]$gGame + 0x1b83c) 0xA0
$hex = ""
for ($i = 0; $i -lt $rt.Length; $i += 4) { if ($i % 32 -eq 0) { $hex += ("`n  +{0:x3}: " -f $i) }; $hex += ("{0:x8} " -f [BitConverter]::ToUInt32($rt, $i)) }
"flag22ed0=$flag reviveMenu(state=$menu ctrl=$menuCtrl) roomTransition@g_Game+1b83c (dwords):$hex"
[void][Mem]::CloseHandle($h)
