# BTO Sync: cos'è, come funziona, perché è sicuro

BTO Sync è il gestore di mod di BetterTBOIOnline per *The Binding of Isaac: Repentance+* su Steam.
Serve a giocare online con gli amici con le mod attive: tutti devono avere le stesse mod, alla
stessa versione, e BTO Sync le mette in pari con un clic.

## Come funziona

- **Mod.** Legge la cartella `mods` del gioco. Per attivare o disattivare una mod crea o
  toglie il file `disable.it` nella sua cartella, come fa il menu Mods di Isaac. Non cancella nulla.
- **Workshop.** Cerca, installa e aggiorna le mod tramite il client Steam già aperto sul tuo PC,
  presentandosi come Isaac. Le "mod richieste" di una mod le legge dalla sua pagina pubblica sul
  Workshop (`steamcommunity.com`), senza account, e le installa insieme a lei. Per questo, finché è collegato, gli amici ti vedono "in gioco".
  Si scollega da solo dopo un minuto senza usare il Workshop e quando avvii il gioco: il collegamento vive in un
  processo a parte (`BTO-Sync.exe --steam-host`), perché Steam considera Isaac aperto finché quel processo non si chiude.
  Le mod che richiedono [REPENTOGON](https://repentogon.com) hanno il segno "Richiede REPENTOGON", preso da quello che
  scrive l'autore (titolo e descrizione). Se c'è il suo launcher (`REPENTOGONLauncher.exe`: scelto in Impostazioni, nella cartella del gioco o in una sua
  cartella, in `REPENTOGONLauncher` alla radice di un disco, o nelle opzioni di avvio di Isaac su Steam), "Avvia
  Isaac" lo lancia con `--isaac="<gioco>\isaac-ng.exe" --launch-mode=repentogon --stealth-mode`.
  Se non lo trova ma una mod attiva lo richiede (o c'è la copia del gioco fatta da REPENTOGON), lo dice con un avviso in alto.
- **Mod BTO.** Sono i fix per l'online di questo progetto. BTO Sync le scarica dalle release di
  questa pagina GitHub e le lega alla mod che sistemano: per esempio BTO The Future Sync si
  attiva con The Future e si spegne senza. Quelle del gioco base te le propone (non le scarica senza
  chiedere) e, una volta installate, le tiene attive. Ogni mod ha un README che spiega cosa sistema.
- **Partita.** L'host copia un codice `BTO3:` (binario, circa 170 caratteri con 15 mod) con l'elenco delle sue mod: id del Workshop
  (il nome lo chiede a Steam chi lo riceve), cartella e versione delle mod BTO e un hash corto dei file. Gli altri lo incollano e BTO Sync mostra le differenze.
  "Allinea ora" le sistema. Il codice non contiene dati personali. "Ultima partita" legge il `log.txt`
  del gioco e mostra solo quante volte la partita si è divisa e quanti errori Lua ha dato ogni mod:
  il log contiene anche i nomi e gli Steam ID dei giocatori, che BTO Sync non mostra. Al primo avvio avvisa che può
  inviare in automatico i log delle partite con desync o errori Lua ("Va bene" / "Non inviarli", si disattiva in Impostazioni): il log dell'ultima partita
  parte pulito sul PC, con "Giocatore N" al posto di Steam ID e nomi, verso il repository privato `bto-logs` (vedi [logs-worker](../logs-worker/README.md)).
- **Salvataggi.** Copia, salva e ripristina i 3 slot. Prima di sovrascrivere uno slot ne fa
  sempre un backup, in `%APPDATA%\BtoSync\saves`. Con "Backup prima di giocare" (acceso di serie)
  "Avvia Isaac" salva prima gli slot cambiati dall'ultima volta e tiene gli ultimi 5 per slot. Esporta uno slot in un file `.zip` dove scegli
  tu; un file importato diventa solo un backup, finché non lo ripristini in uno slot.
- **Profili.** Prima di cambiare le mod attive con un allineamento o un profilo, se non sono già
  salvate in un profilo le salva in una "Copia automatica".
- **Patch per l'online.** Isaac fa entrare online solo con le mod spente, a meno che il suo exe
  non sia patchato. BTO Sync **controlla** soltanto se la patch c'è: legge `isaac-ng.exe` senza
  modificarlo. Se manca, e solo se premi "Scarica la patch", scarica e apre
  [Isaac-Online-Modded](https://github.com/xADDBx/Isaac-Online-Modded) di xADDBx, un progetto
  open source separato, che applica la patch quando lo usi tu.
- **Aggiornamenti.** All'avvio guarda se in questa pagina c'è una release `sync-vX.Y.Z` più nuova.
  Si aggiorna solo se premi "Aggiorna ora".

## Perché è sicuro

- **Il codice è tutto qui**, in [`sync/`](../sync). Chiunque può leggerlo, e compilarlo con
  `dotnet build -c Release` per ottenere lo stesso programma.
- **Non chiede password né account.** Per il Workshop usa il client Steam già aperto e
  autenticato. Non legge credenziali e non vede i tuoi dati di accesso.
- **Nessuna telemetria.** Si collega solo a:
  - `api.github.com` e `github.com`: le release di questo progetto (mod BTO e aggiornamenti) e,
    solo se lo chiedi, quella di Isaac-Online-Modded;
  - `api.steampowered.com`: l'API pubblica del Workshop, senza chiave, per sapere se una mod ha
    aggiornamenti e per le immagini di anteprima;
  - `steamcommunity.com`: la pagina pubblica di una mod, per leggere quali altre mod richiede;
  - il client Steam del tuo PC, per iscriverti alle mod e scaricarle.
- **Dove scrive:**
  - la cartella `mods` del gioco;
  - i file di salvataggio di Isaac, solo quando lo chiedi tu, e sempre dopo un backup;
  - la cartella `data` del gioco, per i dati delle mod che stanno con i salvataggi;
  - la propria cartella `%APPDATA%\BtoSync`, per impostazioni, profili e backup;
  - il file `.zip` di un salvataggio esportato, dove scegli tu.
  Non tocca `isaac-ng.exe` né il registro di Windows: dal registro legge solo dove è installato
  Steam.
- **Non serve installarlo né aprirlo come amministratore.** È una cartella con un exe:
  per toglierlo basta cancellarla.
- **Quello che scarica viene da fonti fisse:** le mod BTO solo dalle release di
  `MICHELOSCO/BetterTBOIOnline`, le altre solo dal Workshop di Steam, sempre in HTTPS. Se la
  copia di una mod BTO dell'host è diversa dalla release, BTO Sync te lo dice.

### Perché Windows o l'antivirus possono avvisarti

L'exe **non ha una firma digitale**: un certificato costa e questo è un progetto tra amici.
Per questo Windows può mostrare "PC protetto": premi "Ulteriori informazioni" e poi
"Esegui comunque". Alcuni antivirus segnalano i programmi poco diffusi che scaricano file e ne
sostituiscono altri, come fa l'aggiornamento automatico. Se hai dubbi, leggi il codice di
questa cartella o compilalo tu.

## Dove trovarlo

Ultima versione: [release `sync-v…`](https://github.com/MICHELOSCO/BetterTBOIOnline/releases).
Scarica `BTO-Sync-Setup-X.Y.Z.exe` e installalo (per il tuo utente, senza amministratore), oppure
`BTO-Sync-X.Y.Z.zip` da estrarre; poi apri BTO Sync a gioco chiuso. Le istruzioni d'uso sono nel
`LEGGIMI.txt` accanto all'exe.
