# BetterTBOIOnline Diagnostics

Una mod che **non cambia il gioco**: scrive nel log quello che succede, per capire perché una
partita online si è divisa.

**Attiva:** se installata, BTO Sync la tiene aggiornata e attiva. Non serve per giocare: serve a
chi analizza i desync. Non partecipa al controllo delle mod BTO all'inizio della run.

## Cosa fa

- `main.lua` scrive righe `[BTO-DIAG]` sugli eventi di gioco (inizio e fine run, piani, stanze,
  giocatori, nemici, oggetti, proiettili). Ogni riga ha `f=<frame>`, lo stesso numero delle righe `[Frame N]` del log di
  rete del gioco, così i due log si possono confrontare.
- Dalla 0.5 scrive anche, quando cambiano, le azioni di ogni giocatore come le legge Lua (righe
  `pN input`), per la [prova sull'input in online](../docs/prova-input-online.md).
- `snapshot.lua` fotografa ogni personaggio a ogni stanza (righe `[BTO-SNAP]`) e, quando la
  partita si divide, congela l'ultima foto.

È in sola lettura: non tocca lo stato del gioco né il generatore casuale.

## Dove trovare i log

`Documenti\My Games\Binding of Isaac Repentance+\log.txt`, oppure, online,
`online_logs\sessions\<data>\log.txt`. Dopo un desync mandate tutta la cartella `online_logs`
compressa e `log.txt`, copiato **prima** di riaprire il gioco (viene sovrascritto a ogni avvio).
Non contengono password: solo la partita, le mod e i nomi Steam.

## Recupero manuale (console)

Con `EnableDebugConsole=1` in `options.ini`, in partita: `bto_dump` elenca le foto e
`bto_restore <indice>` riapplica una foto al giocatore 0. `bto_restore` **cambia lo stato**: usalo
solo da solo dopo uno split, mai durante una partita online.

Dettagli per lo sviluppo: [docs/sviluppo.md](../docs/sviluppo.md).
