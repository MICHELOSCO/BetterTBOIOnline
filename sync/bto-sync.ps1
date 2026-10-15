# BTO Sync: aligns the Isaac mods of this PC to the host's (Windows PowerShell 5.1, no install needed).
#   bto-sync.ps1                       menu
#   bto-sync.ps1 -Export               host: copies the code of the active mods to the clipboard
#   bto-sync.ps1 -From <code|file>     friends: aligns to the host (default: code in the clipboard)
# Keep this file ASCII-only: PowerShell 5.1 reads BOM-less scripts as ANSI.
param(
	[switch]$Export,
	[string]$From,
	[string]$CodeFile,  # -Export: also write the code to this file
	[string]$ModsDir,
	[switch]$Yes,       # do not ask for confirmation
	[switch]$Offline    # no GitHub/Steam: only enable/disable (used by test-sync.ps1)
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$Repo = 'MICHELOSCO/BetterTBOIOnline'
$AppId = '250900'

function Find-ModsDir {
	$steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
	if ($steam) {
		$libs = @($steam)
		$vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
		if (Test-Path $vdf) {
			$libs += [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"') | ForEach-Object { $_.Groups[1].Value -replace '\\\\', '\' }
		}
		foreach ($lib in $libs) {
			$d = Join-Path $lib 'steamapps\common\The Binding of Isaac Rebirth\mods'
			if (Test-Path $d) { return (Resolve-Path $d).Path }
		}
	}
	throw "Cartella mods di Isaac non trovata: rilancia con -ModsDir <percorso>"
}

# Short hash of the mod's files (paths + contents, disable.it excluded), identical on every PC with the same files.
# Text files are hashed with LF line endings: git (autocrlf), zips and editors change CRLF/LF without changing the mod.
$TextExt = '.lua', '.xml', '.txt', '.json', '.md', '.anm2', '.ini', '.csv'
$Latin1 = [Text.Encoding]::GetEncoding(28591)
function Get-ModHash($path) {
	$root = (Resolve-Path -LiteralPath $path).Path.TrimEnd('\')
	$sha = [Security.Cryptography.SHA256]::Create()
	[string[]]$lines = @(Get-ChildItem -LiteralPath $root -Recurse -File -Force | Where-Object { $_.Name -ne 'disable.it' } | ForEach-Object {
		$h = if ($TextExt -contains $_.Extension.ToLowerInvariant()) {
			$s = $Latin1.GetString([IO.File]::ReadAllBytes($_.FullName)).Replace("`r", '')  # Latin1 = one char per byte, lossless
			[BitConverter]::ToString($sha.ComputeHash($Latin1.GetBytes($s))).Replace('-', '')
		} else { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
		$_.FullName.Substring($root.Length + 1).Replace('\', '/').ToLowerInvariant() + ' ' + $h
	})
	[Array]::Sort($lines, [StringComparer]::Ordinal)  # culture-independent order
	[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($lines -join "`n"))).Replace('-', '').Substring(0, 16)
}

# Workshop manifest id per installed item, from Steam's appworkshop_250900.acf (same value as the API's hcontent_file).
function Get-InstalledManifests($modsDir) {
	$acf = Join-Path (Split-Path (Split-Path (Split-Path $modsDir))) "workshop\appworkshop_$AppId.acf"
	$t = @{}
	if (Test-Path $acf) {
		foreach ($x in [regex]::Matches((Get-Content $acf -Raw), '"(\d+)"\s*\{[^{}]*?"manifest"\s*"(\d+)"')) {
			if (-not $t.ContainsKey($x.Groups[1].Value)) { $t[$x.Groups[1].Value] = $x.Groups[2].Value }
		}
	}
	$t
}

# ponytail: hashes every mod on every run (a few seconds for ~500 MB); cache by size+mtime if it gets slow.
function Get-LocalMods($modsDir) {
	$manifests = Get-InstalledManifests $modsDir
	Get-ChildItem -LiteralPath $modsDir -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'metadata.xml') } | ForEach-Object {
		$m = ([xml](Get-Content -LiteralPath (Join-Path $_.FullName 'metadata.xml') -Raw -Encoding UTF8)).metadata
		$id = "$($m.id)".Trim()
		[pscustomobject]@{
			key      = $(if ($id) { $id } else { $_.Name.ToLowerInvariant() })
			folder   = $_.Name
			name     = "$($m.name)".Trim()
			id       = $id
			version  = "$($m.version)".Trim()
			hash     = Get-ModHash $_.FullName
			manifest = $(if ($id) { "$($manifests[$id])" } else { '' })
			enabled  = -not (Test-Path -LiteralPath (Join-Path $_.FullName 'disable.it'))
			link     = [bool]($_.Attributes -band [IO.FileAttributes]::ReparsePoint)
			path     = $_.FullName
		}
	}
}

function ConvertTo-Code($obj) {
	$b = [Text.Encoding]::UTF8.GetBytes(($obj | ConvertTo-Json -Compress -Depth 4))
	$ms = New-Object IO.MemoryStream
	$gz = New-Object IO.Compression.GZipStream($ms, [IO.Compression.CompressionMode]::Compress)
	$gz.Write($b, 0, $b.Length); $gz.Close()
	'BTO1:' + [Convert]::ToBase64String($ms.ToArray())
}

function ConvertFrom-Code($code) {
	$code = "$code" -replace '[\s`]', ''  # Discord may wrap it in backticks
	if (-not $code.StartsWith('BTO1:')) { throw "Codice non valido: deve iniziare con BTO1: (fattelo ricopiare dall'host)" }
	$ms = New-Object IO.MemoryStream(, [Convert]::FromBase64String($code.Substring(5)))
	$gz = New-Object IO.Compression.GZipStream($ms, [IO.Compression.CompressionMode]::Decompress)
	(New-Object IO.StreamReader($gz, [Text.Encoding]::UTF8)).ReadToEnd() | ConvertFrom-Json
}

function Get-WorkshopDetails($ids) {
	$body = @{ itemcount = $ids.Count }
	for ($i = 0; $i -lt $ids.Count; $i++) { $body["publishedfileids[$i]"] = $ids[$i] }
	$r = Invoke-RestMethod -Method Post -Uri 'https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/' -Body $body
	$d = @{}
	foreach ($f in $r.response.publishedfiledetails) { $d["$($f.publishedfileid)"] = $f }
	$d
}

# Release assets of the repo, newest release first: name -> download url ("<folder>-<version>.zip").
function Get-BtoAssets {
	$a = @{}
	foreach ($r in (Invoke-RestMethod "https://api.github.com/repos/$Repo/releases?per_page=100" -Headers @{ 'User-Agent' = 'bto-sync' })) {
		foreach ($x in $r.assets) { if (-not $a.ContainsKey($x.name)) { $a[$x.name] = $x.browser_download_url } }
	}
	$a
}

function Install-BtoMod($h, $url, $modsDir) {
	$tmp = Join-Path ([IO.Path]::GetTempPath()) ("bto-sync-" + [guid]::NewGuid())
	New-Item -ItemType Directory $tmp | Out-Null
	try {
		Invoke-WebRequest $url -OutFile "$tmp\mod.zip" -UseBasicParsing -Headers @{ 'User-Agent' = 'bto-sync' }
		Expand-Archive "$tmp\mod.zip" "$tmp\x"
		$src = Join-Path "$tmp\x" $h.folder
		if (-not (Test-Path -LiteralPath (Join-Path $src 'metadata.xml'))) { throw "zip senza la cartella $($h.folder)" }
		$dst = Join-Path $modsDir $h.folder
		if (Test-Path -LiteralPath $dst) { Remove-Item -LiteralPath $dst -Recurse -Force }
		Move-Item -LiteralPath $src $dst
		if ((Get-ModHash $dst) -ne $h.hash) { Write-Host "   ATTENZIONE: $($h.folder) dell'host e' diversa dalla release $($h.version): chiedigli la cartella." -ForegroundColor Yellow }
	} finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
}

function Invoke-Export($modsDir) {
	$mods = @(Get-LocalMods $modsDir | Where-Object enabled | Select-Object key, folder, name, id, version, hash, manifest)
	$code = ConvertTo-Code @{ v = 1; mods = $mods }
	Write-Host "Mod attive dell'host ($($mods.Count)):"
	$mods | ForEach-Object { Write-Host "   $($_.name) $($_.version)" }
	if ($CodeFile) { Set-Content -LiteralPath $CodeFile $code -Encoding Ascii }
	try { Set-Clipboard $code; Write-Host "`nCodice copiato negli appunti: incollalo su Discord agli altri giocatori." -ForegroundColor Green } catch { }
	Write-Host "`n$code"
}

# Returns the list of actions (kind = enable | disable | download | subscribe, mod) and prints the notes.
function Get-SyncPlan($hostMods, $local, $ws) {
	$byKey = @{}; foreach ($l in $local) { $byKey[$l.key] = $l }
	$hostKeys = @{}; foreach ($h in $hostMods) { $hostKeys[$h.key] = $true }
	$todo = @(); $notes = @()
	foreach ($h in $hostMods) {
		$l = $byKey[$h.key]
		if ($l -and $l.link) { $notes += "$($h.name): e' un collegamento (junction), non la tocco"; continue }
		$latest = if ($h.id -and $ws[$h.id]) { "$($ws[$h.id].hcontent_file)" } else { '' }
		if ($l -and $l.hash -eq $h.hash) {
			if (-not $l.enabled) { $todo += @{ kind = 'enable'; mod = $l; text = "attiva     $($h.name)" } }
			if ($latest -and $l.manifest -and $l.manifest -ne $latest) { $notes += "$($h.name): uguale all'host, ma sul Workshop c'e' un aggiornamento (per tutti)" }
			continue
		}
		if ($h.folder -like 'bto-*' -or $h.folder -eq 'bettertboionline-diagnostics') {
			$todo += @{ kind = 'download'; mod = $h; text = "scarica    $($h.folder) $($h.version) da GitHub" }
		} elseif ($h.id) {
			if (-not $l) {
				$todo += @{ kind = 'subscribe'; mod = $h; text = "iscriviti  $($h.name) (si apre la pagina Workshop: premi Iscriviti)" }
				continue
			}
			if ($latest -and $l.manifest -ne $latest) { $notes += "$($h.name): la TUA copia e' vecchia. Apri Steam, aspetta l'aggiornamento (Libreria > Download), avvia Isaac una volta e rilancia BTO Sync" }
			elseif ($latest -and $h.manifest -ne $latest) { $notes += "$($h.name): e' l'HOST ad avere la versione vecchia: deve aggiornarla e rifare l'esportazione" }
			else { $notes += "$($h.name): file diversi da quelli dell'host (modificati a mano?): disiscriviti e reiscriviti sul Workshop" }
			if (-not $l.enabled) { $todo += @{ kind = 'enable'; mod = $l; text = "attiva     $($h.name)" } }
		} else {
			$notes += "$($h.name) ($($h.folder)): non e' sul Workshop ne' su GitHub: chiedi la cartella all'host"
		}
	}
	foreach ($l in $local | Where-Object { $_.enabled -and -not $hostKeys[$_.key] }) {
		if ($l.link) { $notes += "$($l.name): in piu' ma e' un collegamento (junction): disattivala dal menu Mods"; continue }
		$todo += @{ kind = 'disable'; mod = $l; text = "disattiva  $($l.name) (l'host non ce l'ha)" }
	}
	foreach ($n in $notes) { Write-Host " ! $n" -ForegroundColor Yellow }
	, $todo
}

function Invoke-Sync($modsDir, $code) {
	$hostMods = @((ConvertFrom-Code $code).mods)
	$local = @(Get-LocalMods $modsDir)
	Write-Host "Mod dell'host: $($hostMods.Count). Mod in questa cartella: $($local.Count)."
	$ws = @{}
	$wsIds = @($hostMods | Where-Object id | ForEach-Object id)
	if (-not $Offline -and $wsIds.Count) {
		try { $ws = Get-WorkshopDetails $wsIds } catch { Write-Host "Workshop non raggiungibile: salto il controllo versioni." -ForegroundColor Yellow }
	}
	$todo = Get-SyncPlan $hostMods $local $ws
	if (-not $todo.Count) { Write-Host "Nient'altro da fare: se sopra non ci sono avvisi, sei allineato all'host." -ForegroundColor Green; return }
	Write-Host "`nDa fare:"; foreach ($t in $todo) { Write-Host "   $($t.text)" }
	if (-not $Yes -and (Read-Host "`nProcedo? (s/n)") -notmatch '^[sSyY]') { Write-Host "Annullato."; return }
	$assets = $null
	foreach ($t in $todo) {
		$m = $t.mod
		try {
			switch ($t.kind) {
				'enable'  { Remove-Item -LiteralPath (Join-Path $m.path 'disable.it') }
				'disable' { New-Item -ItemType File -Path (Join-Path $m.path 'disable.it') -Force | Out-Null }
				'subscribe' { if (-not $Offline) { Start-Process "steam://url/CommunityFilePage/$($m.id)" } }
				'download' {
					if ($Offline) { throw "offline" }
					if (-not $assets) { $assets = Get-BtoAssets }
					$url = $assets["$($m.folder)-$($m.version).zip"]
					if (-not $url) { throw "release $($m.folder)-$($m.version).zip non trovata su GitHub: chiedi la cartella all'host" }
					Install-BtoMod $m $url $modsDir
				}
			}
			Write-Host " ok   $($t.text)" -ForegroundColor Green
		} catch { Write-Host " ERR  $($t.text): $($_.Exception.Message)" -ForegroundColor Red }
	}
	Write-Host "`nFatto. Se ti sei iscritto a mod nuove: aspetta il download su Steam, avvia Isaac una volta, chiudilo e rilancia BTO Sync per verificare."
}

if ($MyInvocation.InvocationName -ne '.') {  # dot-sourcing loads only the functions (test-sync.ps1)
	try {
		if (Get-Process isaac-ng -ErrorAction SilentlyContinue) { throw "Chiudi Isaac prima di usare BTO Sync (le mod si caricano all'avvio)." }
		if (-not $ModsDir) { $ModsDir = Find-ModsDir }
		Write-Host "Cartella mods: $ModsDir`n"
		if (-not $Export -and -not $From) {
			Write-Host "1) Sono l'HOST: copia il codice delle mie mod"
			Write-Host "2) Allinea le mie mod a quelle dell'host (prima copia il suo codice)"
			$Export = (Read-Host "Scelta") -eq '1'
		}
		if ($Export) { Invoke-Export $ModsDir; return }
		if (-not $From) { $From = Get-Clipboard -Raw }
		if ($From -and (Test-Path -LiteralPath $From.Trim() -ErrorAction SilentlyContinue)) { $From = Get-Content -LiteralPath $From.Trim() -Raw }
		Invoke-Sync $ModsDir $From
	} catch { Write-Host "ERRORE: $($_.Exception.Message)" -ForegroundColor Red; exit 1 }
}
