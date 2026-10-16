# BTO Fair Co-op Crash Fix

Dopo uno split non crashano più tutti insieme ("runtime error").

**Attiva:** Insieme a [Fair Co-op Items](https://steamcommunity.com/sharedfiles/filedetails/?id=2678027825): BTO Sync la installa e la attiva con lei, e la spegne senza.

## Cosa succedeva

Fair Co-op Items tiene una sua lista dei giocatori, aggiornata solo entrando in una stanza, e la
scorre a ogni frame disegnato nelle stanze del tesoro, dell'angelo e del planetario. Quando un
desync divide la lobby il gioco toglie subito gli altri giocatori, e al frame dopo la mod usa
giocatori che non esistono più: crash di tutti i PC nello stesso istante (09/10, stanza
dell'angelo, tre PC su tre).

## Cosa fa

Dopo che un giocatore viene tolto, fa rispondere "no" al controllo "sono in una di quelle stanze?"
di Fair Co-op Items fino al prossimo cambio di stanza, dove la mod ricostruisce la sua lista.
I negozi e i boss non usano quella lista e non cambiano.

## Cosa cambia per chi gioca

Niente di visibile, finché non c'è uno split: a quel punto ognuno resta in gioco da solo invece di crashare.

## Come vedere che funziona

Nel `log.txt` del gioco (Documenti\My Games\Binding of Isaac Repentance+) all'avvio compare
`[bto-faircoop-crash-fix] loaded`, `stale-player guard installed`. Il codice, con i dettagli tecnici e i riferimenti ai log, è in [`main.lua`](main.lua).

## Limiti

- Evita il crash, non lo split: per rientrare vedi [BTO Join Sync](../bto-join-sync/README.md).

## Va installata su tutti i PC

Tutti i giocatori della partita devono avere le stesse mod BTO alla stessa versione. All'inizio
di ogni run online ogni mod BTO "consuma" dal generatore casuale sincronizzato un numero di
estrazioni che dipende dall'elenco delle mod BTO attive e dalle loro versioni. Se qualcuno ha un
elenco diverso, la partita si divide subito al primo frame ("Checksum mismatch") invece che a metà
run. Nel log c'è la riga `[BTO] handshake draws=… mods=…`, che deve essere uguale su tutti i PC.

Non va installata a mano: la installa e la aggiorna [BTO Sync](../../README.md).
