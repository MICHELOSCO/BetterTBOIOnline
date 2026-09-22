# Research notes

## Isaac-Online-Modded (xADDBx) — come sblocca le mod online

Fonte: https://github.com/xADDBx/Isaac-Online-Modded, letto al commit `a90af38` (31/08/2026).
Il repo è attivo: il brief lo dava per fermo da un anno, ma l'ultimo commit risale ad agosto 2026.
Qui è usato solo come materiale di studio. Nessun codice è copiato in questo repo.

### Cos'è

È un'app WPF in C#. Trova `isaac-ng.exe` (Steam, GOG o Epic) e lo modifica sul disco con una
**patch per pattern**: cerca una sequenza di byte originale e la sostituisce con una della stessa
lunghezza. Non conosce indirizzi fissi. Per questo sopravvive alle patch del gioco finché il codice
attorno non cambia. Per sicurezza applica la patch solo se il pattern originale compare **esattamente
una volta**. Se il pattern è già patchato risponde "già fatto", in tutti gli altri casi "versione non
supportata". Scrive su un file temporaneo e poi lo rinomina, così un errore a metà non lascia un exe
corrotto.

Contiene tre patch indipendenti:

1. **Coop / mod online.** È quella che serve al progetto.
2. **Analytics.** Una funzione ritorna subito, senza eseguire nulla.
3. **Coop characters.** È sperimentale e aggiunta ad agosto 2026: sblocca più personaggi in coop.
   Riscrive blocchi di codice più lunghi.

C'è anche un `EIDPatcher`: modifica il Lua della mod External Item Descriptions per farla
funzionare in coop. Non tocca l'exe.

### La patch "mod online", analizzata sulla 1.9.7.17.J460

L'ho verificata sul nostro `isaac-ng.exe.bak`, che è il file vanilla.

- Funzione a **VA 0x0090E7D0** (offset file 0x50DBD0).
- Legge un intero di stato: `[[0x00C7169C] + 0x4A91C]`, cioè un campo di un oggetto globale
  del gioco (probabilmente il manager di piattaforma/mod).
- Si comporta come uno `switch` su quel valore:
  - `2` → mostra `##PLATFORM###ONLINE_MODS_BANNED`
  - `3` → mostra `##PLATFORM###ONLINE_MODS_PERMABANNED`
  - `4` → mostra `##PLATFORM###ONLINE_MODS_NEEDS_RESTART`
  - in questi tre casi prepara un popup (i float -1.0 sono probabilmente posizione/dimensione di
    default) e **ritorna `true`**, cioè "blocca l'accesso all'online";
  - in ogni altro caso ritorna `false`.
- La patch sostituisce i tre salti condizionali (`je`) con `nop nop`. Il codice finisce sempre nel
  ramo `xor al,al`, quindi la funzione ritorna sempre `false`. Il gioco non vede mai lo stato
  "mod bannate" e lascia entrare online con le mod attive.

In parole semplici: il gioco non disattiva le mod "per scelta tecnica" a ogni frame. Esiste un
**unico punto di controllo** che, se le mod sono attive, mostra un messaggio e rifiuta l'accesso.
La patch spegne quel controllo e nient'altro.

**Patch analytics:** VA 0x00905C80 (offset file 0x505080). Il primo byte `push ebp` diventa `ret`.
La funzione non viene mai eseguita. Il tool la chiama "analytics", cioè invio di telemetria.

### Rischi documentati dall'autore e confermati dai nostri log

- Le mod possono causare **desync**: il gioco è deterministico in lockstep, quindi ogni client
  deve simulare esattamente la stessa cosa. Se un peer ha una mod che cambia lo stato del gioco e
  l'altro no, lo stato diverge.
- Una mod solo "visiva" (musica, descrizioni) è in genere sicura. Una mod che tocca oggetti, RNG o
  entità lo è molto meno.

### Idee per una reimplementazione indipendente (fase futura, `mod-online-patcher/`)

- Stessa idea di base: pattern unico, verifica prima e dopo, scrittura atomica, backup.
- Per non toccare l'exe su disco si potrebbe applicare la patch **in memoria** all'avvio, con un
  launcher o un proxy DLL. Così le verifiche di integrità di Steam restano pulite e non serve
  ripatchare dopo ogni aggiornamento: basta che il pattern si trovi ancora.
- Si potrebbe anche patchare meno, per esempio forzare a `0` solo lo stato letto invece di cambiare
  il flusso della funzione. È da valutare in futuro.

## REPENTOGON

Dettagli in [findings.md](findings.md). In sintesi: gira solo su una copia del gioco riportata alla
1.9.7.12, senza EOS, e non ha callback di rete. Il progetto non lo usa.

## I log online del gioco

`Documents/My Games/Binding of Isaac Repentance+/online_logs/`

- `sessions/<data>/log.txt` contiene il log completo di ogni sessione online: join/leave della lobby,
  messaggi di rete (Save Data, Player Info), desync, recovery, `NotifyGameEnd`. C'è anche lo snapshot
  del salvataggio a inizio e fine sessione.
- `desyncs/<data>__<utente>/` contiene per ogni desync `desync_diff.txt` (quale entità diverge e
  come), `desync_log.txt`, `desync_rng_history.txt`, `desync_framestate.txt` e uno screenshot.

Nell'exe ci sono le classi `KAGE::Network::MultiplayerManager`, `NetDesyncHandler`,
`ServiceEOS`/`ServiceSteam`/`ServiceLocalhost`, `LobbyEOS`/`LobbySteam`. Esistono quindi due
backend, EOS e Steam, più un backend locale, forse per i test.

## Analisi statica (sola lettura) di v1.9.7.17.J460 — desync e rientro

Strumenti: `tools/xref.py` (trova il codice che usa una stringa) e `tools/refs.py`
(`calls` / `disp` / `dis`). Basati su capstone. Tutti gli indirizzi sono VA del `.bak` vanilla, ImageBase 0x400000.

### Scelta SYNC/DROP dopo un desync: VA 0x008FDC7C–0x008FDD3F

Per ogni peer remoto `edi`, con `local = [ebp-0x14]` e `remote = [edi+0x3BC]` (stato del frame):

```
SYNC  se  remote.checksum(+0x14) == local.checksum
      e   remote.globalSeed(+0x10) == local.globalSeed
      e   remoteSaveChecksum([edi+0x3C0]) == localSaveChecksum
DROP  altrimenti
```

"SYNC" significa "questo peer è uguale a me, tienilo", non "riallinealo". Il gioco non ha nessun
trasferimento di stato per correggere chi ha divergito. **In 2 giocatori, un desync vero porta
sempre a DROP e quindi alla scissione: è una scelta di progetto, non un bug.**
Per evitare la scissione bisogna fornire noi una risincronizzazione vera. Il candidato naturale è
il meccanismo di join in corsa, che già trasferisce lo stato di gioco (`Join Existing Game`,
`Save Data Request`, `Report Game States`).

### Perché il rientro resta in "waiting"

- Il gestore del messaggio "Player Info" (VA ~0x916A34) registra `Adding pending join` quando il
  messaggio ha il flag `0x02` (join in una partita esistente). Inserisce il giocatore in una mappa a
  `game+0x4CC3C`, dove `game = [0x00C7169C]`.
- `NetManager = game+0x4B3D8`. Il campo `NetManager+0x1868` (= `game+0x4CC40`) è la
  **dimensione di quella mappa**: diventa 1 appena c'è un join in coda.
- L'elaborazione (`Processing pending joins` → `Adding %d new players`) è in
  **`NetManager::ProcessPendingJoins` a VA 0x0090DF40** (`bool force`). Esce subito se la mappa
  è vuota e `force == 0`.
- Ha 4 chiamanti: `0x006FB21A`, `0x006FB33E` (force=1), `0x0092FE3C` (force=0) e `0x00958DB6` (force=1).
  **Tre di questi chiamanti** (`0x6FB33E` indirettamente, `0x92FE3C`, `0x958DB6`) la chiamano solo se una lista del
  NetManager (`[NetManager+0] != [NetManager+4]`, un vector) **non è vuota**. Per `0x6FB21A` la
  condizione non è ancora stata ricostruita.
- **Ipotesi forte:** quel vector è l'elenco dei giocatori remoti nel match. Dopo un DROP l'host resta
  senza giocatori remoti, il vector è vuoto e `ProcessPendingJoins` non viene più chiamata.
  Il join resta in coda per sempre. Questo spiega anche l'unico `Processing pending joins` al
  frame 4 subito dopo lo split: in quel momento la rimozione era forse ancora in corso.
- Da verificare: che cosa contiene esattamente `[NetManager+0..+4]` e dove viene svuotato
  (`RemoveRemotePlayer`).

### Implicazioni per i fix (fase futura, richiede patch/hook, oggi fuori perimetro)

1. **Sbloccare il rientro:** quando la mappa dei pending join non è vuota, chiamare
   `ProcessPendingJoins(force=1)` anche se l'host è rimasto solo. In alternativa, togliere la
   condizione sul vector nel chiamante `0x92FE3C`.
2. **Evitare la scissione:** al posto di DROP + nuova lobby, tenere la lobby e far rientrare il peer
   divergente con il percorso join-in-progress, così l'host gli invia lo stato.
3. **Recuperare il personaggio:** il DROP rimuove il personaggio del peer, e il join-in-progress
   lo farebbe rientrare con un personaggio nuovo. Una mod Lua su entrambi i client può fotografare
   ogni personaggio (oggetti, cuori, consumabili) fino al frame del desync, quando i client sono
   ancora identici, e riapplicarlo al rientro.

## Vincolo: coesistenza con la patch mod-online già applicata

L'`isaac-ng.exe` dell'utente ha già la patch mod-online (7 byte, vedi sopra e findings.md):
telemetria disattivata (VA 0x905C80) e controllo "mod bannate" neutralizzato (tre `je`->`nop` a ~0x90DC08).

Qualunque patcher futuro di questo progetto (fix rientro, anti-split, o reimplementazione mod-online)
**deve tenerne conto**: riconoscere e conservare quei byte, oppure inglobare anche quella patch in un
unico strumento. Non deve assumere un exe vanilla né sovrascrivere la patch esistente. Un patcher a
pattern di byte dovrebbe accettare sia il pattern originale sia quello già patchato, e per il sito
mod-online applicarla solo se non già presente. Le zone patchate non si sovrappongono al sito del fix
rientro (~0x958DBD), quindi le patch possono coesistere.
