# BTO Render RNG Guard

I numeri casuali pescati mentre si disegna lo schermo non toccano più quelli sincronizzati.

**Attiva:** Sempre (gioco base). BTO Sync te la propone se manca.

## Cosa succedeva

Il disegno dello schermo (le callback di render) gira un numero di volte diverso su ogni PC, perché
dipende dagli fps. Se una mod pesca un numero casuale sincronizzato mentre disegna, i PC ne
consumano quantità diverse e le partite si separano. Lo fanno, per esempio, StageAPI e The Future.

## Cosa fa

Durante il render, `Random()`, `RandomVector()`, `math.random` e i generatori di StageAPI e The
Future usano un generatore locale, mai quello sincronizzato. Fuori dal render tutto resta
com'era, e `math.random` viene riallineato a ogni frame a partire da seme della run e frame, così
dipende solo dalla partita condivisa.

## Cosa cambia per chi gioca

Niente di visibile: al massimo un effetto grafico casuale (particelle, scintille) può essere
diverso da un PC all'altro, che è innocuo.

## Come vedere che funziona

Nel `log.txt` del gioco (Documenti\My Games\Binding of Isaac Repentance+) all'avvio compare
`[bto-render-rng-guard] loaded: Random, RandomVector, math.random guarded while rendering`. Il codice, con i dettagli tecnici e i riferimenti ai log, è in [`main.lua`](main.lua).

## Limiti

- Una mod che creasse entità *durante* il render confonderebbe il riconoscimento della fase di
  disegno. Nessuna delle mod che usiamo lo fa.

## Va installata su tutti i PC

Tutti i giocatori della partita devono avere le stesse mod BTO alla stessa versione. All'inizio
di ogni run online ogni mod BTO "consuma" dal generatore casuale sincronizzato un numero di
estrazioni che dipende dall'elenco delle mod BTO attive e dalle loro versioni. Se qualcuno ha un
elenco diverso, la partita si divide subito al primo frame ("Checksum mismatch") invece che a metà
run. Nel log c'è la riga `[BTO] handshake draws=… mods=…`, che deve essere uguale su tutti i PC.

Non va installata a mano: la installa e la aggiorna [BTO Sync](../../README.md).
