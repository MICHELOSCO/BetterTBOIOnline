# BTO Trinket Sync

Online, raccogliere o scambiare un trinket non divide più la partita.

**Attiva:** Sempre (gioco base). BTO Sync te la propone se manca.

## Cosa succedeva

Tutti i desync con il generatore casuale identico trovati nei log (19/09, 20/09, 24/09 e 07/10)
sono avvenuti il frame dopo che un giocatore con un trinket in mano ne aveva raccolto un altro.
Il gioco applica le statistiche del nuovo trinket in un frame diverso sul PC di chi lo raccoglie
rispetto agli altri PC. Per un attimo velocità e lacrime di quel giocatore sono diverse (meno
dell'1%), e basta questo perché le partite si separino.

## Cosa fa

Con 2 o più giocatori salta la raccolta del gioco e fa tutto lo scambio da sé, nel frame della
collisione e allo stesso modo su ogni PC: il vecchio trinket cade ai piedi del giocatore e quello
nuovo entra subito nello slot. L'animazione con il trinket alzato sopra la testa resta.

## Cosa cambia per chi gioca

Si gioca come sempre: passi sopra un trinket e lo prendi, e se ne avevi già uno ti cade ai piedi.
L'unica differenza è che il trinket va subito nello slot, senza attesa. Da soli non cambia nulla.

## Come vedere che funziona

Nel `log.txt` del gioco (Documenti\My Games\Binding of Isaac Repentance+) all'avvio compare
`[bto-trinket-sync] loaded`. Il codice, con i dettagli tecnici e i riferimenti ai log, è in [`main.lua`](main.lua).

## Limiti

- I trinket in vendita nei negozi restano gestiti dal gioco (c'è il pagamento di mezzo).
- Con entrambi gli slot pieni (Mom's Purse e simili), cade quello del primo slot.

## Va installata su tutti i PC

Tutti i giocatori della partita devono avere le stesse mod BTO alla stessa versione. All'inizio
di ogni run online ogni mod BTO "consuma" dal generatore casuale sincronizzato un numero di
estrazioni che dipende dall'elenco delle mod BTO attive e dalle loro versioni. Se qualcuno ha un
elenco diverso, la partita si divide subito al primo frame ("Checksum mismatch") invece che a metà
run. Nel log c'è la riga `[BTO] handshake draws=… mods=…`, che deve essere uguale su tutti i PC.

Non va installata a mano: la installa e la aggiorna [BTO Sync](../../README.md).
