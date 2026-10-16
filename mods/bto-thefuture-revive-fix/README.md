# BTO The Future Revive Fix

Online, la Revive Machine (la macchina delle monete nella stanza iniziale) funziona anche dentro The Future.

**Attiva:** Insieme a [The Future](https://steamcommunity.com/sharedfiles/filedetails/?id=2993558907): BTO Sync la installa e la attiva con lei, e la spegne senza.

## Cosa succedeva

09/10: in The Future, dopo aver donato alla Revive Machine, nessuno poteva scegliere chi far
rinascere e il gioco ignorava ogni comando. The Future colora l'HUD disegnandolo da sé: lo rende
visibile, lo disegna e lo nasconde di nuovo. Così, durante ogni aggiornamento del gioco, l'HUD
risulta nascosto. Il menu "chi far rinascere" fa parte dell'HUD: si aggiorna solo se l'HUD è
visibile, e finché è aperto il gioco blocca i comandi di tutti.

## Cosa fa

Con 2 o più giocatori, dentro la funzione di The Future salta il "nascondi" e il disegno manuale:
l'HUD lo disegna il gioco come al solito e il menu funziona.

## Cosa cambia per chi gioca

Online, in The Future l'HUD non viene scurito dal suo effetto (solo estetica). Da soli non cambia nulla.

## Come vedere che funziona

Nel `log.txt` del gioco (Documenti\My Games\Binding of Isaac Repentance+) all'avvio compare
`[bto-thefuture-revive-fix] loaded: HUD SetVisible/Render wrapped`. Il codice, con i dettagli tecnici e i riferimenti ai log, è in [`main.lua`](main.lua).

## Limiti

- Se una versione futura di The Future disegnasse l'HUD in un altro modo, la mod lo scrive nel log
  (`fix off`) e non fa nulla.

## Va installata su tutti i PC

Tutti i giocatori della partita devono avere le stesse mod BTO alla stessa versione. All'inizio
di ogni run online ogni mod BTO "consuma" dal generatore casuale sincronizzato un numero di
estrazioni che dipende dall'elenco delle mod BTO attive e dalle loro versioni. Se qualcuno ha un
elenco diverso, la partita si divide subito al primo frame ("Checksum mismatch") invece che a metà
run. Nel log c'è la riga `[BTO] handshake draws=… mods=…`, che deve essere uguale su tutti i PC.

Non va installata a mano: la installa e la aggiorna [BTO Sync](../../README.md).
