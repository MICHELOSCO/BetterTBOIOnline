# BTO Astro Sync

Astrobirth (Astro Items) senza i comandi che vedeva solo chi li premeva.

**Attiva:** Insieme a [Astrobirth](https://steamcommunity.com/sharedfiles/filedetails/?id=3260980911): BTO Sync la installa e la attiva con lei, e la spegne senza.

## Cosa succedeva

Online ogni PC simula la partita da solo e si scambiano solo gli input sincronizzati del gioco.
Astrobirth però reagisce anche a cose che vede solo il PC di chi le preme:

- **tasti della tastiera**: P per P Key, 7 per Legacy, 8 per Astrophage e My Moon My Man, il tasto di Taurus EX;
- **mouse**: Overmind, Dry Eye Syndrome;
- **Drop tenuto premuto** sul controller 0, contato sui fotogrammi disegnati (che dipendono dagli fps):
  trasforma piedistalli normali in oggetti Astro (Chaos + Succubus, Guppy + Brimstone o cuori marci o
  eterni, Black Candle + Libra, 6 set + Birthright).

Provato dal vivo il 17/10: premendo P con R Key in tasca, R Key spariva e compariva P Key, ma solo
su quel PC. Altri oggetti fanno il loro effetto mentre il gioco disegna lo schermo, cosa che
succede un numero di volte diverso su ogni PC: Copernicus, Zoltraak, SnowGrave, Decisive Strike,
Leo EX, Taurus EX e il menu dei dadi Mega.

C'era anche un errore: continuando una run salvata senza i dati di Astrobirth (per esempio
entrando a partita iniziata con un salvataggio Astro diverso) Astrobirth andava in errore a
inizio run, e il gioco saltava tutte le mod che venivano dopo, mod BTO comprese, solo su quel PC.

## Cosa fa

- Mentre gira il codice di Astrobirth, tasti, pulsanti e mouse letti direttamente rispondono "non
  premuto", e Drop tenuto premuto vale zero. Le altre mod leggono l'input come prima.
- Toglie dalle pool, uguale su tutti i PC, i 17 oggetti che funzionano solo in quei modi: Copernicus,
  Zoltraak, SnowGrave, Decisive Strike, Leo EX, Taurus EX, Dry Eye Syndrome, Mega D4, Mega D6,
  Mega D8, Hyper Quantum Dice, Solar System, P Key, Legacy, Astrophage, My Moon My Man, Overmind.
- A inizio run prepara i dati che mancavano ad Astrobirth, così non va più in errore.

## Cosa cambia per chi gioca

Quei 17 oggetti non escono più, e le trasformazioni con Drop tenuto premuto non ci sono. Tutto il
resto di Astrobirth è uguale. Vale anche giocando da soli, finché la mod è attiva.

## Come vedere che funziona

Nel `log.txt` del gioco (Documenti\My Games\Binding of Isaac Repentance+) compaiono
`[bto-astro-sync] input guard on … Astro callbacks` e, a ogni inizio run,
`[bto-astro-sync] removed 17/17 items from the pools`. Il codice, con i riferimenti ai file di
Astrobirth, è in [`main.lua`](main.lua).

## Limiti

- Gli oggetti tolti possono ancora comparire da strade che non passano dalle pool (ricette e
  trasformazioni di Astrobirth). Se escono così, possono ancora causare desync.
- Non è stata controllata per intero la libreria `isaacscript-common` inclusa in Astrobirth.

## Va installata su tutti i PC

Tutti i giocatori della partita devono avere le stesse mod BTO alla stessa versione. All'inizio
di ogni run online ogni mod BTO "consuma" dal generatore casuale sincronizzato un numero di
estrazioni che dipende dall'elenco delle mod BTO attive e dalle loro versioni. Se qualcuno ha un
elenco diverso, la partita si divide subito al primo frame ("Checksum mismatch") invece che a metà
run. Nel log c'è la riga `[BTO] handshake draws=… mods=…`, che deve essere uguale su tutti i PC.

Non va installata a mano: la installa e la aggiorna [BTO Sync](../../README.md).
