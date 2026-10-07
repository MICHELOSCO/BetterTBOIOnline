# For every desync dump folder: frame, players, differing entity types, and the log lines just before detection.
param([string[]]$roots)
foreach ($root in $roots) {
    Get-ChildItem $root -Directory -Recurse -Filter "desyncs" -ErrorAction SilentlyContinue | ForEach-Object {
        Get-ChildItem $_.FullName -Directory | Sort-Object Name | ForEach-Object {
            $d = $_.FullName
            $diff = "$d\desync_diff.txt"
            if (-not (Test-Path $diff)) { return }
            $txt = Get-Content $diff
            $frame = ($txt | Select-String "Desync detected on frame: (\d+)").Matches.Groups[1].Value
            $rng = ($txt | Select-String "Global RNG checksum \((\w+)\)" -AllMatches | ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[1].Value }) -join "/"
            $ents = ($txt | Select-String "^Entity (\d+):" -AllMatches | Measure-Object).Count
            $types = ($txt | Select-String "Player 0:\s+Type: \(([\d.]+)\)" | ForEach-Object { $_.Matches.Groups[1].Value } | Select-Object -Unique) -join ","
            $users = ($txt | Select-String "Username = (\S+)" | ForEach-Object { $_.Matches.Groups[1].Value }) -join "+"
            "=== $($_.Name)  frame=$frame rng=$rng ents=$ents types=$types [$users]"
            $log = "$d\desync_log.txt"
            if (Test-Path $log) {
                $lines = Get-Content $log
                $idx = -1
                for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match "Desync detected! desyncFrame") { $idx = $i; break } }
                if ($idx -gt 0) {
                    $from = [Math]::Max(0, $idx - 9)
                    $lines[$from..($idx - 1)] | Where-Object { $_ -notmatch "BTO-DIAG\] t=\d+ f=\d+ trace|Lua mem usage|Queued Path" } | ForEach-Object { "    $_" }
                }
            }
        }
    }
}
