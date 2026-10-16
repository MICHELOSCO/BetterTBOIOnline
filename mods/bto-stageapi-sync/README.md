# BTO StageAPI Sync

I piani personalizzati di StageAPI (come quelli di The Future) restano uguali su tutti i PC.

**Attiva:** Insieme a [StageAPI](https://steamcommunity.com/sharedfiles/filedetails/?id=1348031964): BTO Sync la installa e la attiva con lei, e la spegne senza.

## Cosa succedeva

1. Il generatore casuale di StageAPI viene inizializzato una volta sola, al caricamento, e
   dipende dalla storia di ogni PC. StageAPI lo usa per cose di gioco: la porta d'ingresso a caso
   quando la stanza non ha quella da cui arrivi (sposta *tutti* i giocatori; desync del 23/09 e
   dell'01/10), il prezzo dei patti col diavolo e le particelle delle porte.
2. L'animazione di cambio piano di StageAPI (per esempio uscendo da The Future) lancia il comando
   `stage` quando un contatore di frame *disegnati* arriva a 150. Il piano cambia quindi in un
   momento che dipende dagli fps.

## Cosa fa

1. Ridà un seme fisso al generatore di StageAPI a ogni piano e a ogni stanza, uguale su tutti i PC.
2. Con 2 o più giocatori salta l'animazione e cambia piano subito.

## Cosa cambia per chi gioca

Online, entrando o uscendo da The Future non si vede l'animazione di transizione di StageAPI.
È voluto. Da soli non cambia nulla.

## Come vedere che funziona

Nel `log.txt` del gioco (Documenti\My Games\Binding of Isaac Repentance+) all'avvio compare
`[bto-stageapi-sync] loaded`, e online `stage transition animation skipped (multiplayer)`. Il codice, con i dettagli tecnici e i riferimenti ai log, è in [`main.lua`](main.lua).

## Limiti

- Ripara gli usi del generatore di StageAPI trovati nei log; una versione futura di StageAPI
  potrebbe usarlo in punti nuovi.

## Va installata su tutti i PC

Tutti i giocatori della partita devono avere le stesse mod BTO alla stessa versione. All'inizio
di ogni run online ogni mod BTO "consuma" dal generatore casuale sincronizzato un numero di
estrazioni che dipende dall'elenco delle mod BTO attive e dalle loro versioni. Se qualcuno ha un
elenco diverso, la partita si divide subito al primo frame ("Checksum mismatch") invece che a metà
run. Nel log c'è la riga `[BTO] handshake draws=… mods=…`, che deve essere uguale su tutti i PC.

Non va installata a mano: la installa e la aggiorna [BTO Sync](../../README.md).
