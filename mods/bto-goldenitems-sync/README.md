# BTO Golden Items Sync

Gli oggetti dorati di Golden Items escono uguali su tutti i PC.

**Attiva:** Insieme a [Golden Items](https://steamcommunity.com/sharedfiles/filedetails/?id=3338495603): BTO Sync la installa e la attiva con lei, e la spegne senza.

## Cosa succedeva

Golden Items decide se un piedistallo è dorato con un suo generatore casuale, che prepara una sola
volta per ogni avvio del gioco, al primo piedistallo, e poi non riparte più. Chi aveva già fatto
una partita, chi entrava a partita iniziata o chi tornava dopo uno split aveva il generatore in un
punto diverso: oggetti dorati diversi da un PC all'altro, e quindi desync.

## Cosa fa

Decide lei, prima di Golden Items, se il piedistallo è dorato, partendo dal seme del piedistallo,
che è uguale su tutti i PC. Golden Items trova la risposta già scritta e non usa più il suo
generatore. Le regole sono le stesse: 0,6% di probabilità, mai sul piano di Death Certificate,
mai sugli oggetti di missione né sugli attivi.

## Cosa cambia per chi gioca

Niente: gli oggetti dorati escono con la stessa frequenza, solo che ora sono uguali per tutti.

## Come vedere che funziona

Nel `log.txt` del gioco (Documenti\My Games\Binding of Isaac Repentance+) all'avvio compare
`[bto-goldenitems-sync] loaded`. Il codice, con i riferimenti alle righe di Golden Items, è in [`main.lua`](main.lua).

## Va installata su tutti i PC

Tutti i giocatori della partita devono avere le stesse mod BTO alla stessa versione. All'inizio
di ogni run online ogni mod BTO "consuma" dal generatore casuale sincronizzato un numero di
estrazioni che dipende dall'elenco delle mod BTO attive e dalle loro versioni. Se qualcuno ha un
elenco diverso, la partita si divide subito al primo frame ("Checksum mismatch") invece che a metà
run. Nel log c'è la riga `[BTO] handshake draws=… mods=…`, che deve essere uguale su tutti i PC.

Non va installata a mano: la installa e la aggiorna [BTO Sync](../../README.md).
