# BTO Treasure Jingle Sync

Online, una mod che riavvia la musica nella stanza del tesoro non può più dividere la partita.

**Attiva:** Sempre (gioco base). BTO Sync te la propone se manca.

## Cosa succedeva

Desync dell'08/10 (frame 49365): in una stanza del tesoro solo un PC ha chiamato `Room:PlayMusic()`
da Lua. Era "The Specialist for Good Items" (Workshop 2575911103), installata solo su quel PC, che
rimette la musica della stanza. Nella stanza del tesoro, alla prima visita, quella funzione del
gioco sceglie una delle quattro musichette d'ingresso usando il generatore casuale **sincronizzato**:
un'estrazione in più su un solo PC, ed ecco il desync.

## Cosa fa

Online, solo nel caso che consuma il generatore (stanza del tesoro, prima visita), la chiamata
`Room:PlayMusic()` fatta da una mod viene scritta nel log e saltata. In tutti gli altri casi non
cambia nulla, e la musica che il gioco mette entrando nella stanza non passa da qui.

## Cosa cambia per chi gioca

Niente di visibile: al massimo, con una mod come quella del "balletto", la musichetta non riparte.
La regola resta: stesse mod per tutti, niente mod in più su un solo PC.

## Come vedere che funziona

Nel `log.txt` del gioco (Documenti\My Games\Binding of Isaac Repentance+) all'avvio compare
`[bto-treasure-jingle-sync] loaded: Room:PlayMusic wrapped`. Il codice, con i dettagli tecnici e i riferimenti ai log, è in [`main.lua`](main.lua).

## Limiti

- Ripara solo questa chiamata. Una mod presente su un solo PC può dividere la partita in mille altri modi.

## Va installata su tutti i PC

Tutti i giocatori della partita devono avere le stesse mod BTO alla stessa versione. All'inizio
di ogni run online ogni mod BTO "consuma" dal generatore casuale sincronizzato un numero di
estrazioni che dipende dall'elenco delle mod BTO attive e dalle loro versioni. Se qualcuno ha un
elenco diverso, la partita si divide subito al primo frame ("Checksum mismatch") invece che a metà
run. Nel log c'è la riga `[BTO] handshake draws=… mods=…`, che deve essere uguale su tutti i PC.

Non va installata a mano: la installa e la aggiorna [BTO Sync](../../README.md).
