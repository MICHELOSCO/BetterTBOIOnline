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
