# BTO Sirens Fix

[Fish] Siren's Items Over Mom's si carica senza errori Lua.

**Attiva:** Insieme a [[Fish] Siren's Items Over Mom's](https://steamcommunity.com/sharedfiles/filedetails/?id=3650363740): BTO Sync la installa e la attiva con lei, e la spegne senza.

## Cosa succedeva

L'ultima riga della mod registra una funzione che non esiste, e a ogni avvio del gioco compariva
l'errore `bad argument #3 to 'AddCallback' (function expected, got nil)`. Era innocuo, perché è
l'ultima riga e tutto il resto della mod funziona, ma ogni errore Lua fa inviare a BTO Sync il
log della partita, quindi il log partiva dopo ogni sessione.

## Cosa fa

Le mod si caricano in ordine di nome, e "BTO …" viene sempre prima di "[fish] …". Questa mod fa
ignorare solo quella registrazione vuota, e solo per Siren's: le altre mod ricevono l'errore come
prima.

## Cosa cambia per chi gioca

Niente: nomi e descrizioni degli oggetti di Siren's restano uguali.

## Come vedere che funziona

Nel `log.txt` del gioco (Documenti\My Games\Binding of Isaac Repentance+) all'avvio compare
`[bto-sirens-fix] skipped Siren's Complete Set's callback with no function`, e non c'è più la riga
`There was an error running the lua file` per Siren's.

## Va installata su tutti i PC

Tutti i giocatori della partita devono avere le stesse mod BTO alla stessa versione. All'inizio
di ogni run online ogni mod BTO "consuma" dal generatore casuale sincronizzato un numero di
estrazioni che dipende dall'elenco delle mod BTO attive e dalle loro versioni. Se qualcuno ha un
elenco diverso, la partita si divide subito al primo frame ("Checksum mismatch") invece che a metà
run. Nel log c'è la riga `[BTO] handshake draws=… mods=…`, che deve essere uguale su tutti i PC.

Non va installata a mano: la installa e la aggiorna [BTO Sync](../../README.md).
