# BetterTBOIOnline

Giocare online con gli amici a *The Binding of Isaac: Repentance+* (Steam, Windows) **con le mod
attive**, senza che la partita si divida.

Online Isaac funziona in lockstep: ogni PC simula la stessa partita e basta una differenza minima
(una mod in più, una versione diversa, un numero casuale pescato in un momento diverso) perché la
partita si "sdoppi" (desync). Questo progetto ha due parti:

- **BTO Sync**, un programma per Windows che mette le mod di tutti in pari con quelle dell'host,
  con un clic;
- **le mod BTO**, piccoli fix che tolgono le cause di desync e crash trovate nei log delle nostre
  partite, nel gioco base e in mod del Workshop come The Future, StageAPI e Fair Co-op Items.

Le mod BTO non si installano a mano: le installa e le aggiorna BTO Sync. Ognuna ha il suo README
che spiega cosa sistema e perché (vedi [la tabella](#le-mod-bto)).

## Scaricalo

1. Vai nelle [release](https://github.com/MICHELOSCO/BetterTBOIOnline/releases) e scarica
   `BTO-Sync-X.Y.Z.zip` dall'ultima release `sync-v…`.
2. Estrailo dove vuoi e apri `BTO-Sync.exe`, **a gioco chiuso** e con Steam aperto. Non serve
   installare nulla.
3. Se Windows dice "PC protetto": "Ulteriori informazioni" › "Esegui comunque". Succede perché
   l'exe non ha una firma digitale: [perché è sicuro](docs/BTO-Sync.md#perché-è-sicuro).

Si aggiorna da solo: all'avvio guarda se qui c'è una versione nuova e la installa se premi
"Aggiorna ora". Quando vuoi, "Cerca aggiornamenti" controlla anche le mod del Workshop e ti fa
scegliere cosa aggiornare, o aggiornare tutto.

## Cosa fa

In alto ci sono le sezioni (Partita, Mod, Workshop, Profili, Salvataggi), il profilo attivo e
"Avvia Isaac"; Impostazioni e Info sono le due icone accanto.

### Mod
Tutte le mod installate, con l'interruttore per attivarle o disattivarle (disattivare non cancella
nulla), la ricerca, i filtri e "Aggiorna" per le mod del Workshop vecchie. Cliccando una mod se ne
legge la descrizione e quali altre mod richiede. Se una mod attiva ne richiede un'altra spenta o
mancante, BTO Sync lo dice in alto e la sistema con un clic; accendendo una mod accende anche quelle
che le servono. I fix BTO, che BTO Sync gestisce da solo, stanno in fondo in un gruppo chiuso
"Fix BTO automatici": un clic per vederli. Le mod BTO hanno l'interruttore bloccato: le gestisce BTO Sync insieme alla
mod che sistemano. A destra, "Stato delle mod" (aggiornamenti, mod richieste che mancano) e lo spazio
che occupano, le più pesanti per prime.

![Mod](docs/img/mods.png)

### Workshop
Il Workshop di Steam in una griglia di copertine. Cerchi per nome (la ricerca parte da sola), ordini
come su Steam (più popolari nel periodo che scegli, più votate, più iscritti, più recenti, aggiornate
di recente) e filtri per tag (Oggetti, Nemici, Boss, Musica…). Ogni scheda dice se la mod è "solo
grafica e suoni" o può cambiare il gameplay, e se **richiede altre mod**. Cliccando una mod si apre
una finestra al centro con descrizione e numeri. "Installa" ti iscrive, scarica la mod e la mette
già attiva, **insieme alle mod che richiede** (per esempio The Future con Custom Stage API).

![Workshop](docs/img/workshop.png)

### Partita
Il cuore del programma, e la pagina che si apre all'avvio. L'host preme "Copia il mio codice" e lo
incolla su Discord: è un codice `BTO3:` con l'elenco delle sue mod (i codici `BTO1:` e `BTO2:` delle
versioni vecchie si leggono ancora). Gli altri seguono tre passi: incollano il codice, "Allinea ora"
attiva, disattiva o scarica quello che serve, poi "Avvia Isaac". "Ultima partita" dice cosa ha
scritto il gioco nel suo log: se la partita si è divisa (desync) e quali mod hanno dato errori Lua. Se
lo permetti, il log della partita parte da solo per l'analisi dei desync, prima ripulito sul PC da Steam ID e nomi.

![Partita](docs/img/online.png)

### Profili
Elenchi di mod attive da salvare e richiamare, per esempio "Run con The Future" e "Vanilla+".
Un profilo si può mandare agli amici come codice, e dal codice di un amico (anche quello dell'host,
con "Salva come profilo" in Partita) se ne crea uno. Prima che "Allinea ora" o "Applica"
cambino le tue mod, se non sono già in un profilo BTO Sync le salva in una "Copia automatica":
niente va perso per sbaglio.

![Profili](docs/img/profiles.png)

### Salvataggi
I 3 slot di Isaac con obiettivi, morti e oggetti scoperti. Puoi fare un backup, copiare uno slot in
un altro e ripristinare, anche dalle copie che il gioco fa da solo ogni giorno. Prima di
sovrascrivere uno slot BTO Sync ne fa sempre un backup. Uno slot si può **esportare** in un file
.zip, da tenere o da mandare a un amico, e un file si può **importare**: diventa un backup, che
rimetti in uno slot quando vuoi. Cliccando uno slot si vede il suo percorso: i boss finali battuti
e con quali personaggi.

![Salvataggi](docs/img/saves.png)

![Percorso di uno slot](docs/img/progress.png)

### Impostazioni e Info
La cartella del gioco, Steam, gli aggiornamenti e il controllo della **patch per l'online**: Isaac
fa entrare online solo con le mod spente, a meno che il suo exe non sia patchato. BTO Sync controlla
se la patch c'è e, se manca, propone di scaricare
[Isaac-Online-Modded](https://github.com/xADDBx/Isaac-Online-Modded), che la applica. Subito dopo
propone i fix BTO del gioco base che ti mancano.
La pagina Info riassume come funziona il programma e perché è sicuro.

![Impostazioni](docs/img/settings.png)

## Come funziona e perché è sicuro

In breve:
- il codice è tutto in [`sync/`](sync);
- non chiede password o account e non manda dati a nessuno;
- parla solo con GitHub, con l'API pubblica di Steam e con il tuo client Steam;
- non modifica l'exe del gioco.

Tutti i dettagli sono in [docs/BTO-Sync.md](docs/BTO-Sync.md).

## Le mod BTO

| Mod | Cosa sistema | Attiva con |
|---|---|---|
| [BTO Trinket Sync](mods/bto-trinket-sync/README.md) | Raccogliere o scambiare un trinket divideva la partita | gioco base, proposta |
| [BTO Treasure Jingle Sync](mods/bto-treasure-jingle-sync/README.md) | Una mod che riavvia la musica nella stanza del tesoro causava un desync | gioco base, proposta |
| [BTO Render RNG Guard](mods/bto-render-rng-guard/README.md) | I numeri casuali usati mentre si disegna lo schermo (dipendono dagli fps) toccavano quelli sincronizzati | gioco base, proposta |
| [BTO Join Sync](mods/bto-join-sync/README.md) | Rientro in una run già iniziata dopo uno split (sperimentale) | gioco base, proposta |
| [BTO StageAPI Sync](mods/bto-stageapi-sync/README.md) | Piani personalizzati di StageAPI senza desync | StageAPI |
| [BTO The Future Sync](mods/bto-thefuture-sync/README.md) | Nemici di The Future che sparano anelli di proiettili | The Future |
| [BTO The Future Revive Fix](mods/bto-thefuture-revive-fix/README.md) | La Revive Machine dentro The Future bloccava tutti | The Future |
| [BTO Fair Co-op Crash Fix](mods/bto-faircoop-crash-fix/README.md) | Crash di tutti insieme subito dopo uno split | Fair Co-op Items |
| [BTO Golden Items Sync](mods/bto-goldenitems-sync/README.md) | Oggetti dorati diversi da un PC all'altro | Golden Items |
| [BTO Astro Sync](mods/bto-astro-sync/README.md) | Comandi da tastiera e mouse che vedeva solo chi li premeva, oggetti che agivano mentre si disegna lo schermo | Astrobirth |
| [BTO Sirens Fix](mods/bto-sirens-fix/README.md) | Errore Lua a ogni avvio (innocuo, ma faceva inviare il log ogni volta) | [Fish] Siren's Items Over Mom's |
| [BetterTBOIOnline Diagnostics](diagnostics-mod/README.md) | Solo log, per analizzare i desync (non cambia il gioco) | se installata |

Tutti devono avere le stesse mod BTO alla stessa versione: le mod se ne accorgono da sole all'inizio
della run, e BTO Sync le tiene in pari.

## Sviluppo

Struttura del repo, mod diagnostica e protocollo dei test: [docs/sviluppo.md](docs/sviluppo.md).
Analisi dei desync: [docs/findings.md](docs/findings.md).
