# BTO The Future Sync

I nemici di The Future che sparano anelli di proiettili li sparano uguali su tutti i PC.

**Attiva:** Insieme a [The Future](https://steamcommunity.com/sharedfiles/filedetails/?id=2993558907): BTO Sync la installa e la attiva con lei, e la spegne senza.

## Cosa succedeva

Un anello di proiettili (modalità 9 di `FireProjectiles`) parte da un angolo che, se non viene
indicato, il gioco sceglie a caso. Rubber, un nemico di The Future, prepara i suoi parametri una
volta sola, al caricamento della mod, quando il generatore di ogni PC è in uno stato diverso.
Così il suo anello di morte era ruotato in modo diverso su ogni PC (desync del 07/10: 5 proiettili
a 6 gradi di distanza).

## Cosa fa

Nel momento dello sparo fissa l'angolo dell'anello a partire dal seme del nemico, che è uguale su
tutti i PC. Nevermore imposta già il suo, e le altre modalità di sparo non usano l'angolo.

## Cosa cambia per chi gioca

Niente di visibile: gli anelli sono semplicemente identici per tutti.

## Come vedere che funziona

Nel `log.txt` del gioco (Documenti\My Games\Binding of Isaac Repentance+) all'avvio compare
`[bto-thefuture-sync] loaded`. Il codice, con i dettagli tecnici e i riferimenti ai log, è in [`main.lua`](main.lua).

## Limiti

- Ripara i nemici di The Future trovati nei log; un aggiornamento di The Future con nuovi nemici
  potrebbe avere lo stesso problema.

## Va installata su tutti i PC

Tutti i giocatori della partita devono avere le stesse mod BTO alla stessa versione. All'inizio
di ogni run online ogni mod BTO "consuma" dal generatore casuale sincronizzato un numero di
estrazioni che dipende dall'elenco delle mod BTO attive e dalle loro versioni. Se qualcuno ha un
elenco diverso, la partita si divide subito al primo frame ("Checksum mismatch") invece che a metà
run. Nel log c'è la riga `[BTO] handshake draws=… mods=…`, che deve essere uguale su tutti i PC.

Non va installata a mano: la installa e la aggiorna [BTO Sync](../../README.md).
