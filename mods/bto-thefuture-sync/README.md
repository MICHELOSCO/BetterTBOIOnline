# BTO The Future Sync

I nemici di The Future che sparano anelli di proiettili li sparano uguali su tutti i PC, e la
stanza di Steven compare per tutti o per nessuno, nella stessa stanza.

**Attiva:** Insieme a [The Future](https://steamcommunity.com/sharedfiles/filedetails/?id=2993558907): BTO Sync la installa e la attiva con lei, e la spegne senza.

## Cosa succedeva

Un anello di proiettili (modalità 9 di `FireProjectiles`) parte da un angolo che, se non viene
indicato, il gioco sceglie a caso. Rubber, un nemico di The Future, prepara i suoi parametri una
volta sola, al caricamento della mod, quando il generatore di ogni PC è in uno stato diverso.
Così il suo anello di morte era ruotato in modo diverso su ogni PC (desync del 07/10: 5 proiettili
a 6 gradi di distanza).

Nel 2-2, una volta su tre (di più con l'oggetto Steven), The Future trasforma una stanza senza
uscita nella stanza di Steven, da cui si entra nel piano The Future. Sia il tiro sia la scelta
della stanza usavano un generatore casuale della mod mai legato al seme della run, il cui stato
dipende da cosa ha fatto ogni PC da quando ha aperto il gioco. Nel desync del 19/10 (3 giocatori)
un PC ha avuto la stanza di Steven al posto di una stanza normale, gli altri due no.

## Cosa fa

Nel momento dello sparo fissa l'angolo dell'anello a partire dal seme del nemico, che è uguale su
tutti i PC. Nevermore imposta già il suo, e le altre modalità di sparo non usano l'angolo.

Per la stanza di Steven spegne la funzione di The Future che la crea e prende la stessa decisione
(stesse condizioni, stessa probabilità, stesse stanze possibili) con un generatore che parte dal
seme del piano, uguale su tutti i PC.

## Cosa cambia per chi gioca

Niente di visibile: gli anelli sono identici per tutti, e la stanza di Steven c'è o non c'è per
tutti allo stesso modo. Con lo stesso seme la stanza di Steven è sempre nello stesso posto.

## Come vedere che funziona

Nel `log.txt` del gioco (Documenti\My Games\Binding of Isaac Repentance+) all'avvio compare
`[bto-thefuture-sync] loaded`, e nel 2-2 con la stanza di Steven `[bto-thefuture-sync] Steven room at <indice>`,
con lo stesso indice su tutti i PC. Il codice, con i dettagli tecnici e i riferimenti ai log, è in [`main.lua`](main.lua).

## Limiti

- Ripara i nemici di The Future trovati nei log; un aggiornamento di The Future con nuovi nemici
  potrebbe avere lo stesso problema.
- La decisione sulla stanza di Steven è copiata da The Future 1.8: se un aggiornamento cambia le
  sue condizioni, questa mod continua a usare quelle vecchie.

## Va installata su tutti i PC

Tutti i giocatori della partita devono avere le stesse mod BTO alla stessa versione. All'inizio
di ogni run online ogni mod BTO "consuma" dal generatore casuale sincronizzato un numero di
estrazioni che dipende dall'elenco delle mod BTO attive e dalle loro versioni. Se qualcuno ha un
elenco diverso, la partita si divide subito al primo frame ("Checksum mismatch") invece che a metà
run. Nel log c'è la riga `[BTO] handshake draws=… mods=…`, che deve essere uguale su tutti i PC.

Non va installata a mano: la installa e la aggiorna [BTO Sync](../../README.md).
