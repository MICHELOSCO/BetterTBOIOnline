# Offline self-check of bto-sync.ps1 on fake mods folders: powershell -NoProfile -ExecutionPolicy Bypass -File sync\test-sync.ps1
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\bto-sync.ps1"
$Yes = $true; $Offline = $true
function Assert($ok, $what) { if (-not $ok) { throw "FALLITO: $what" }; Write-Host "pass $what" }
function New-Mod($dir, $folder, $name, $id, $body) {
	New-Item -ItemType Directory "$dir\$folder" -Force | Out-Null
	Set-Content "$dir\$folder\metadata.xml" "<metadata><name>$name</name><id>$id</id><version>1.0</version></metadata>"
	Set-Content "$dir\$folder\main.lua" $body
}
$t = Join-Path ([IO.Path]::GetTempPath()) ("bto-sync-test-" + [guid]::NewGuid())
$hostDir = "$t\host\mods"; $me = "$t\me\mods"
try {
	New-Mod $hostDir 'bto-x' 'BTO X' '' 'print(1)'
	New-Mod $hostDir 'bto-y' 'BTO Y' '' 'print(2)'
	New-Mod $hostDir 'wsmod_111' 'Workshop Mod' '111' 'print(3)'
	New-Mod $me 'bto-x' 'BTO X' '' 'print(1)'; New-Item "$me\bto-x\disable.it" | Out-Null  # same files, disabled
	New-Mod $me 'bto-y' 'BTO Y' '' 'print(22)'                                             # different files
	New-Mod $me 'extra' 'Extra' '' 'print(4)'                                              # host does not have it
	New-Mod $me 'old' 'Old' '' 'print(5)'; New-Item "$me\old\disable.it" | Out-Null        # disabled, ignored

	Assert ((Get-ModHash "$hostDir\bto-x") -eq (Get-ModHash "$me\bto-x")) 'hash uguale con stessi file (disable.it escluso)'
	Assert ((Get-ModHash "$hostDir\bto-y") -ne (Get-ModHash "$me\bto-y")) 'hash diverso con file diversi'
	New-Item -ItemType Directory "$t\lf\m", "$t\crlf\m" -Force | Out-Null
	[IO.File]::WriteAllText("$t\lf\m\main.lua", "a`nb`n"); [IO.File]::WriteAllText("$t\crlf\m\main.lua", "a`r`nb`r`n")
	Assert ((Get-ModHash "$t\lf\m") -eq (Get-ModHash "$t\crlf\m")) 'hash uguale con fine riga CRLF o LF'

	$CodeFile = "$t\code.txt"
	Invoke-Export $hostDir | Out-Null
	$code = Get-Content $CodeFile -Raw
	Assert ($code.StartsWith('BTO1:')) 'export scrive il codice BTO1:'
	$hostMods = @((ConvertFrom-Code ("``" + $code + "``")).mods)
	Assert ($hostMods.Count -eq 3) 'il codice (anche tra backtick) contiene le 3 mod attive'

	$plan = Get-SyncPlan $hostMods @(Get-LocalMods $me) @{}
	$kinds = ($plan | ForEach-Object { "$($_.kind):$($_.mod.folder)" } | Sort-Object) -join ' '
	Assert ($kinds -eq 'disable:extra download:bto-y enable:bto-x subscribe:wsmod_111') "piano: $kinds"

	Invoke-Sync $me $code | Out-Null
	Assert (-not (Test-Path "$me\bto-x\disable.it")) 'bto-x riattivata'
	Assert (Test-Path "$me\extra\disable.it") 'extra disattivata'
	Assert (Test-Path "$me\old\disable.it") 'old resta disattivata'
	Write-Host "OK" -ForegroundColor Green
} finally { Remove-Item $t -Recurse -Force -ErrorAction SilentlyContinue }
