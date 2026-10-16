# Sviluppo e ricerca

Note per chi lavora sul progetto. Per usare BTO Sync basta il [README](../README.md).

| Cartella | Contenuto |
|---|---|
| `mods/bto-*/` | Mod di fix online, una per problema (`-sync` = evita un desync, `-fix` = evita crash/blocchi). Si distribuiscono solo con BTO Sync (asset `<cartella>-<versione>.zip` delle release), legate alla mod del Workshop che sistemano (`Core.Ties`); quelle del gioco base sempre attive. |
| `sync/` | BTO Sync, app Windows (WPF, .NET Framework 4.8, Facepunch.Steamworks): elenco mod con attiva/disattiva (`disable.it`) e aggiornamenti Workshop; codice dell'host e allineamento automatico (mod BTO dalle release GitHub, mod Workshop scaricate via Steam e copiate in `mods/<directory>_<id>` come fa il gioco); ricerca e installazione dal Workshop; profili; salvataggi dei 3 slot (backup, copia tra slot, ripristino anche dalle copie giornaliere del gioco, con i `data/<mod>/saveN.dat`); mod BTO installate/attivate/disattivate da sole insieme alla mod del Workshop che sistemano; controllo della patch online in `isaac-ng.exe` (per pattern di byte) con download di Isaac-Online-Modded se manca; aggiornamento di sé stesso dalle release `sync-vX.Y.Z` (asset `BTO-Sync-X.Y.Z.zip`, creato da `build-zip.sh`). Build `dotnet build -c Release` in `sync/`, autotest `BTO-Sync.exe --selftest`, istruzioni `sync/LEGGIMI.txt`. |
| `diagnostics-mod/` | Mod Lua: logger eventi (`main.lua`) + snapshot personaggio (`snapshot.lua`). |
| `mod-online-patcher/` | Reimplementazione futura della patch "mod online" (vuota per ora). |
| `tools/` | Script di analisi statica dell'exe (sola lettura). |
| `docs/findings.md` | Risultati sperimentali: ambiente, log, diagnosi, piano del fix. |
| `docs/research-notes.md` | Tecniche: Isaac-Online-Modded, REPENTOGON, analisi statica del netcode. |

## La mod (diagnostica + snapshot)

`main.lua` logga gli eventi; include `snapshot.lua`, che fotografa ogni personaggio a ogni stanza e,
allo split, congela l'ultima foto. Durante il gioco è in sola lettura. Righe di log: `[BTO-DIAG]` e
`[BTO-SNAP]` in `Documents\My Games\Binding of Isaac Repentance+\log.txt` (o in
`online_logs\sessions\<data>\log.txt` quando online).

### Installazione
Sul PC di sviluppo (junction al repo):
```bat
mklink /J "F:\SteamLibrary\steamapps\common\The Binding of Isaac Rebirth\mods\bettertboionline-diagnostics" "%CD%\diagnostics-mod"
```
Sul secondo PC (Megumi): copiare la **cartella** `diagnostics-mod` (i tre file `main.lua`,
`snapshot.lua`, `metadata.xml`) in
`...\The Binding of Isaac Rebirth\mods\bettertboionline-diagnostics\`. Deve essere la **stessa versione**
su entrambi i PC, e le liste di mod devono coincidere (il gioco penalizza le liste diverse).

### Recupero manuale (console)
Serve `EnableDebugConsole=1` in `options.ini`. Poi in partita:
- `bto_dump` — elenca le foto disponibili.
- `bto_restore <indice>` — riapplica una foto al giocatore locale 0.

`bto_restore` **modifica lo stato**: usarlo solo da soli dopo uno split, mai durante una partita online.
`AUTO_RESTORE` in `snapshot.lua` è `false`: al rientro la mod scrive solo cosa recupererebbe. Va messo
`true` solo dopo aver validato il rientro in una sessione di test.

## Sessione di test del rientro (serve un secondo giocatore)
Protocollo dettagliato in [findings.md](findings.md). In breve: mod identiche su entrambi,
console abilitata, provocare uno split, tentare il rientro (l'host cambia stanza), poi raccogliere i
log dei due lati.
