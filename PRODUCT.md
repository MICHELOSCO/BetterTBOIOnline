# Product

<!-- impeccable:product-schema 1 -->

## Platform

windows

App desktop Windows nativa (WPF, .NET Framework 4.8), non web: `sync/`. Le mod in `mods/` sono Lua per il gioco e non hanno interfaccia propria.

## Users

Giocatori di The Binding of Isaac: Repentance+ che giocano online in co-op con le mod. Per ora è il gruppo di amici dell'autore; poi l'app diventerà pubblica per chiunque giochi online con le mod.

Sono utenti nella media: installano mod dal Workshop da soli, ma non vogliono smanettare. Non devono avere a che fare con hash, cartelle o file a mano, a meno che non lo chiedano.

Il momento tipico è subito prima di una partita: l'host passa il suo codice su Discord, gli altri lo incollano e devono essere pronti in pochi clic.

## Product Purpose

BTO Sync è il mod manager di BetterTBOIOnline. Online tutti i giocatori devono avere le stesse mod dell'host, alla stessa versione; se no la partita si divide al primo frame ("Checksum mismatch"). L'app confronta le mod con quelle dell'host e le allinea: attiva, disattiva, scarica da GitHub le mod BTO e dal Workshop quelle che mancano.

Fa anche da mod manager di tutti i giorni (mod installate, Workshop, profili) e gestisce i 3 slot di salvataggio.

Funziona se un amico, dal codice incollato a "puoi entrare in partita", non deve chiedere niente a nessuno.

## Positioning

Il Lua delle mod di Isaac è isolato, quindi nessuna mod può controllare o allineare le altre: serve un programma esterno. BTO Sync conosce le mod BTO (anti-desync), che scarica dalle release GitHub verificandone l'hash, e usa il Workshop di Steam come archivio per tutte le altre. Il codice `BTO2:` dell'host descrive l'insieme esatto delle mod, con versioni e hash.

## Operating Context

- Va usato a gioco chiuso: Isaac carica le mod solo all'avvio e riscrive i salvataggi mentre gira. L'app lo rileva e blocca le modifiche.
- I codici host e profilo si scambiano su Discord.
- Le mod sono cartelle in `<gioco>/mods`; si disattivano con un file `disable.it`. Le copie Workshop si chiamano `<directory>_<id>`.
- I salvataggi stanno in Steam Cloud (`userdata/<account>/250900/remote`) o in Documenti, e i dati delle mod in `<gioco>/data/<mod>/saveN.dat`.
- Per il Workshop l'app si collega a Steam come Isaac: finché è collegata, gli amici vedono l'utente "in gioco".
- Si distribuisce come zip da GitHub (`sync-vX.Y.Z`) e si aggiorna da sola dalle release.

## Capabilities and Constraints

- Sezioni: Le mie mod (attiva/disattiva, filtri, aggiornamenti, descrizione al clic sulla riga), Workshop (cerca, installa, disinstalla), Gioca online (codice host, confronto, "Allinea ora"), Profili, Salvataggi (backup, copia tra slot, ripristino), Impostazioni (cartella mods, Steam, aggiornamenti).
- Deve girare su Windows 10/11 senza installare niente: .NET Framework 4.8, exe più due DLL.
- Gli appunti si usano tramite Win32, perché quelli WPF/OLE falliscono su alcuni PC.
- Lingua: l'interfaccia oggi è solo in italiano. L'autore vuole italiano e inglese; il modo di scegliere la lingua (automatico o da Impostazioni) è da decidere.
- Le descrizioni delle mod BTO sono in italiano, dentro il loro `metadata.xml`.

## Brand Commitments

- Nome: BTO Sync (progetto BetterTBOIOnline). Repo pubblico MICHELOSCO/BetterTBOIOnline.
- Direzione grafica scelta dall'autore: moderna e pulita, con le funzionalità ispirate a r2modman. Ha scartato esplicitamente sia il tema "alla Isaac" sia la pixel art: niente richiami grafici al gioco.
- Interazione voluta: la descrizione di una mod si apre cliccando la riga, non con un pulsante dedicato.

## Evidence on Hand

- Codice e LEGGIMI: `sync/`, `sync/LEGGIMI.txt`; release su GitHub (`v1.0.1`, `sync-v2.0.0`).
- Non ci sono testimonianze, numeri d'uso o recensioni: non vanno inventati.

## Product Principles

1. Pronti a giocare in pochi clic: allineare le mod all'host è il percorso principale e non deve richiedere conoscenze tecniche.
2. Niente perdite: prima di sovrascrivere qualcosa (mod, slot, programma) l'app ne tiene una copia o può tornare indietro, e lo dice.
3. Dire le cose come stanno: stato chiaro (gioco aperto, Steam collegato, cosa manca rispetto all'host) e avvisi in chiaro quando qualcosa va risolto a mano.
4. Dettagli tecnici solo su richiesta: hash, cartelle e versioni stanno dietro un clic, non in primo piano.
