# BTO Join Sync

Rientrare in una run già iniziata dopo uno split, senza un nuovo desync.

**Attiva:** Sempre (gioco base). BTO Sync te la propone se manca. **Sperimentale.**

## Cosa succedeva

Quando un giocatore rientra, il gioco ricarica lo stato della partita su tutti i PC, ma l'evento
"inizio partita" (`MC_POST_GAME_STARTED`) arriva solo a chi entra. Quindi solo il suo PC esegue il
codice di inizio run delle mod (il precaricamento di StageAPI, il riseme di The Future…), e circa
50 frame dopo, al caricamento del piano successivo, la partita si divide di nuovo.

## Cosa fa

I PC che erano già in partita riconoscono il ricaricamento (tutti i loro giocatori rimossi, più
giocatori di prima, a run iniziata) ed eseguono lo stesso evento di inizio partita, nello stesso
punto di chi entra. Il rientro avviene solo durante il cambio di piano, quindi i dati delle mod
del piano vecchio vengono comunque scartati.

## Cosa cambia per chi gioca

Dopo uno split, chi vuole rientrare chiude il gioco, lo riapre ed entra nella lobby dell'host.
L'host scende con una botola normale al piano dopo: solo allora il rientro viene elaborato. Uscire
da The Future non basta, serve una botola in più.

## Come vedere che funziona

Nel `log.txt` del gioco (Documenti\My Games\Binding of Isaac Repentance+) all'avvio compare
`[bto-join-sync] loaded`, e al rientro le righe di `join mirror`. Il codice, con i dettagli tecnici e i riferimenti ai log, è in [`main.lua`](main.lua).

## Limiti

- È la parte meno collaudata: se dopo un rientro la partita si divide di nuovo al piano
  successivo, mandate i log di tutti.
- I ricaricamenti per una rinascita (stesso numero di giocatori) e i giocatori in locale non
  vengono toccati.

## Va installata su tutti i PC

Tutti i giocatori della partita devono avere le stesse mod BTO alla stessa versione. All'inizio
di ogni run online ogni mod BTO "consuma" dal generatore casuale sincronizzato un numero di
estrazioni che dipende dall'elenco delle mod BTO attive e dalle loro versioni. Se qualcuno ha un
elenco diverso, la partita si divide subito al primo frame ("Checksum mismatch") invece che a metà
run. Nel log c'è la riga `[BTO] handshake draws=… mods=…`, che deve essere uguale su tutti i PC.

Non va installata a mano: la installa e la aggiorna [BTO Sync](../../README.md).
