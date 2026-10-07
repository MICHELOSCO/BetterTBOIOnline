# Findings

## 2026-09-22 — Passo 1: ambiente e REPENTOGON

**Esito: BLOCCATO (in attesa di decisione). REPENTOGON non è stato installato né avviato.**
Non è un crash: il blocco dipende da come REPENTOGON gestisce le versioni del gioco. I dettagli sono sotto.

### Installazione del gioco

| Voce | Valore |
|---|---|
| Percorso | `F:\SteamLibrary\steamapps\common\The Binding of Isaac Rebirth` |
| Steam buildid | 22878971 |
| Versione (stringa nell'exe) | **v1.9.7.17.J460**, quindi più recente della 1.9.7.16 indicata nel brief |
| DLC installati (depot) | Afterbirth, Afterbirth+, Repentance, Repentance+ (3353470) |
| EOS | presente `EOSSDK-Win32-Shipping.dll` (crossplay) |
| Mod installate | `fair co-op items_2678027825` |
| REPENTOGON | assente (nessun `dsound.dll`, `zhl.log` o `repentogon.log`) |

### L'exe è già patchato (patch "mod online", applicata dall'utente)

`isaac-ng.exe` è diverso da `isaac-ng.exe.bak` (backup vanilla):

| File | SHA256 |
|---|---|
| `isaac-ng.exe` (patchato, 20/09/2026) | `C4118519BA5C051826FE16507715DE602B51BE4F2CD71534EA13037A0F9BAE30` |
| `isaac-ng.exe.bak` (vanilla 1.9.7.17.J460) | `3BDFC8BAE0DC7E334B76009D0AD45DFBB16EE5F00C06FFBC3A0094E34D44616B` |

I byte cambiati sono 7 in tutto (offset nel file):

```
00505080: 55 -> C3            push ebp -> ret   (una funzione ora ritorna subito)
0050DC08: 74 2A -> 90 90      je -> nop nop
0050DC0D: 74 1E -> 90 90      je -> nop nop
0050DC12: 74 12 -> 90 90      je -> nop nop
```

L'utente ha confermato che si tratta della patch per sbloccare le mod online, presa da un repo git.
Per il passo 4 è materiale utile: mostra la forma concreta della tecnica sulla 1.9.7.17
(una funzione svuotata più tre controlli condizionali saltati).

### Come REPENTOGON supporta le versioni recenti

Fonti: [install.html](https://repentogon.com/install.html), [releases](https://github.com/TeamREPENTOGON/REPENTOGON/releases), sorgente del [Launcher](https://github.com/TeamREPENTOGON/Launcher).

- L'ultima release è REPENTOGON **1.1.3** (15/09/2026). Il target dichiarato è ancora **v1.9.7.12.J273**.
- Il launcher **non** esegue REPENTOGON sulla 1.9.7.17. Procede così:
  1. calcola lo SHA256 dell'`isaac-ng.exe` principale e lo confronta con `patch/exehash.txt`,
     che contiene `3BDFC8BA…`, cioè l'hash della 1.9.7.17.J460 **vanilla**;
  2. se coincide, copia il gioco in una cartella separata `…\The Binding of Isaac Rebirth\Repentogon\`;
  3. in quella copia applica patch bsdiff che **riportano la versione a 1.9.7.12**
     (`isaac-ng.exe`, `afterbirthp.a`, `enums.lua`, `json.lua`, `main.lua`)
     e **cancella `EOSSDK-Win32-Shipping.dll`**;
  4. inietta `dsound.dll` (REPENTOGON) nella copia.
- Un'issue esterna ([#902](https://github.com/TeamREPENTOGON/REPENTOGON/issues/902)) riporta
  REPENTOGON funzionante "su 1.9.7.17". In pratica gira sulla copia riportata a 1.9.7.12.

### Cosa comporta

1. **Hash mismatch.** L'exe principale è patchato (`C411…`), quindi non combacia con `exehash.txt`.
   Il launcher lo considererà incompatibile e rifiuterà l'installazione. Per installare REPENTOGON
   bisogna rimettere temporaneamente l'exe vanilla (`.bak`) nella cartella di gioco. È una modifica
   ai file di gioco fuori dalla cartella mod, quindi serve la conferma dell'utente.
2. **Blocco concettuale, il più importante.** Il gioco con REPENTOGON è una **1.9.7.12 senza EOS**.
   Non è il codice di rete della 1.9.7.17 (crossplay/EOS) dove si presenta il bug del reconnect.
   Una diagnosi fatta lì osserva un netcode diverso, e con ogni probabilità non si collega nemmeno
   con amici che usano la 1.9.7.17 vanilla.
3. **API di rete quasi assente.** Nei docs di REPENTOGON non ci sono callback di
   join/leave/disconnect/sessione/lobby. Per l'online esiste solo `EntityPlayer:IsLocalPlayer()`.
   Un mod diagnostico potrebbe soltanto fare *polling* (numero di giocatori, controller index,
   `IsLocalPlayer`, pickup presenti nella stanza) nei callback standard come `MC_POST_UPDATE`
   e `MC_POST_PLAYER_INIT`.

### Opzioni (decisione dell'utente)

- **A.** Installare comunque REPENTOGON: ripristino temporaneo dell'exe vanilla, installazione, poi
  rimessa della patch mod-online. Si ottiene una sandbox 1.9.7.12 per imparare l'API, ma **non** serve
  a diagnosticare il bug sulla 1.9.7.17.
- **B.** Saltare REPENTOGON e scrivere il mod diagnostico con la **Lua vanilla** sulla 1.9.7.17,
  facendo polling dei giocatori e dei pickup. Funziona sul netcode reale, e le mod restano attive
  online grazie alla patch già applicata. Le informazioni sono limitate ma rilevanti.
- **C.** Aspettare che REPENTOGON faccia il porting alla 1.9.7.17 (nessuna data annunciata).

**Decisione (22/09/2026): opzione B.** REPENTOGON viene scartato perché non espone eventi di rete e
gira su un netcode diverso. La patch Isaac-Online-Modded già applicata (pattern `Coop` + `Analytics`,
vedi research-notes) basta a tenere attive le mod online.

## 2026-09-22 — Analisi dei log online esistenti (sessioni 19–20/09/2026)

Il gioco scrive da solo log di rete molto dettagliati in
`Documents/My Games/Binding of Isaac Repentance+/online_logs/` (13 sessioni, 3 desync).
Coop tra MICHELOSCO (<SteamID omesso>) e Megumi (<SteamID omesso>).

### 1. Lo "sdoppiamento" nasce da un desync, non da una disconnessione di rete

In tutte e tre le sessioni con split la sequenza è la stessa:

```
Desync detected! desyncFrame = N
MSG_DESYNC_NOTIFY / CONFIRM / RECOVERY_START / RECOVERY_COMPLETE   (scambiati regolarmente)
Finalizing resync!
NetManager::RemoveRemotePlayer  "Megumi"
NetDesyncHandler::RemoveUser    "Megumi"
Removing collectible ... from player 0      (in 13_07_15: Key Piece 1 e 2)
Desync Handler creating new lobby            <- ognuno si ritrova da solo nella propria lobby
```

La rete funziona: i messaggi di recovery arrivano tutti. Ma dopo la "recovery" il gioco
**rimuove comunque il peer e crea una nuova lobby**. Cause dei desync (`desync_diff.txt`):

| Sessione | Frame | Entità divergente | Mod attive |
|---|---|---|---|
| 19/09 23:40 | 51823 | giocatore `1.0.9` (Eden): velocità Y diversa (-5.257 vs -5.158) | **nessuna** |
| 20/09 12:52 | 0 | nessuna: checksum **identici** su entrambi i peer | 9 mod (StageAPI, EID, …) |
| 20/09 13:07 | 63715 | giocatore `1.0.18` (Bethany): velocità diversa | 6 mod |

- Un desync si è verificato anche **senza mod**: le mod non sono l'unica causa.
- Il desync al frame 0 con checksum identici sembra un **falso positivo** all'avvio della run.
  L'ha segnalato per primo il peer di Megumi. Va indagato: potrebbe dipendere dalla lista mod
  diversa tra i due client.

### 2. Il rientro resta in "waiting": ipotesi sul pending join

Sessione 20/09 12:52, dopo lo split (frame a 30 fps):

```
[Frame 4]    Processing pending joins          (subito dopo il caricamento della stanza)
[Frame 2376] Megumi joined lobby               (accetta l'invito)
[Frame 2493] Received Save Data / Player Info  (il suo client manda i dati)
[Frame 2540] Adding pending join for player    (l'host mette il join in coda)
             ... nessuna transizione di stanza tra 2540 e 2813 ...
[Frame 2813] Megumi left lobby                 (circa 9 s dopo, il suo client rinuncia)
```

**Ipotesi:** i join "in corso" (join-in-progress) vengono elaborati **solo al caricamento di
una stanza**. Se l'host resta fermo nella stessa stanza, il join resta in coda e il client
che entra va in timeout.
**Test (serve l'amico):** dopo "Adding pending join", l'host cambia subito stanza e si controlla
se nel log compare `Processing pending joins` seguito dall'ingresso del giocatore.

Da notare anche che il joiner rientra con i **suoi** dati di salvataggio e non con il personaggio
e gli oggetti che aveva nella run. Anche se il join riesce, gli oggetti del personaggio non tornano
automaticamente.

## 2026-09-22 — Mod diagnostico verificato in singleplayer

Il mod si carica senza errori sulla v1.9.7.17.J460 (patchata) e scrive 26 righe `[BTO-DIAG]` in
una run di prova. Ha funzionato: caricamento, player init, avvio della run, nuovo piano, nuova
stanza, conteggio giocatori, collectible spawn (id 195, 217, 564) e raccolta oggetti (`items 0 -> 1 -> 2`).
Non ancora osservati: uscita dalla run (`MC_PRE_GAME_EXIT`) e rimozione di un giocatore.
Per la rimozione serve una sessione online.
Primo tentativo fallito: `os` è `nil` nella sandbox Lua vanilla. Ora si usa `Isaac.GetTime()`.

## 2026-09-22 — Il gioco sceglie sempre DROP dopo un desync

Nei log, dopo `process_desync_confirm_message` compare `resolutionPlan = SYNC_PLAN_DROP | SYNC_PLAN_SYNC`.
In **tutti e 3** i desync il piano scelto è stato `SYNC_PLAN_DROP`, cioè il gioco rimuove il peer:

| Sessione | Stato del frame | Seed globale | Checksum salvataggio | Piano |
|---|---|---|---|---|
| 19/09 23:40 | diverso | uguale | uguale | DROP |
| 20/09 12:52 | uguale | uguale | **diverso** (442259532 vs 3984795189) | DROP |
| 20/09 13:07 | diverso | uguale | uguale | DROP |

Ipotesi: con solo 2 peer non esiste una maggioranza che stabilisca quale stato è corretto,
quindi il gioco scarta sempre l'altro. Il desync al frame 0 dipende dal **salvataggio condiviso
diverso** tra i due client, non dal gameplay.
**Obiettivo per un fix futuro:** capire in quali condizioni il gioco sceglie SYNC. Forzare SYNC eviterebbe lo split.

## 2026-09-22 — Opzioni di avvio nascoste

Stringhe trovate nell'exe: `--localhost_match`, `--networktest`, `--luadebug`, `--luaheapsize=`,
`--set-stage=`, `--set-stage-type=`, `--load-room=`.
Esiste anche un backend `ServiceLocalhost`/`LobbyLocalhost` ("Localhost lobby values currently not supported").

Prova con `--localhost_match`:
- Steam rilancia il gioco e mantiene l'argomento.
- All'avvio il gioco crea **da solo** una friend lobby (`Creating friend lobby… Successfully joined lobby`).
  L'ID è di tipo Steam, quindi la lobby passa comunque da Steam e non da un backend locale.
- Una seconda istanza con la stessa opzione si chiude subito: niente log, niente processo.
  Probabilmente il gioco non ammette due istanze aperte con lo stesso account.
- **Esito: non è una via praticabile per testare con un solo PC e un solo account.** Superare il limite
  delle due istanze richiederebbe di modificare il binario o di aggiungere file nella cartella di gioco,
  cose fuori perimetro per ora. `--networktest` non è ancora stato provato.

## 2026-09-22 — Catalogo dei casi reali (confermato dall'utente)

L'utente conferma che le sessioni del 19–20/09 riproducono esattamente i casi d'uso del progetto.

**Split (problema 1):** 3 casi, tutti con lo stesso meccanismo: desync → `SYNC_PLAN_DROP` →
`RemoveUser` → `Desync Handler creating new lobby`. Nessuno è una vera disconnessione di rete.

**Rientro fallito:** i log mostrano due varianti.

| Variante | Sessioni | Cosa si vede sul lato host (MICHELOSCO) |
|---|---|---|
| **A. Invito che non arriva** | 19/09 23:40, 20/09 13:07 | Dopo lo split: `Successfully created/joined lobby`, poi **nessuna traccia di Megumi** fino alla fine della run. L'invito resta in "waiting" senza raggiungere la lobby dell'host. |
| **B. Join in coda mai elaborato** | 20/09 12:52 (duplicata in 12:54) | Megumi entra nella lobby, invia Save Data e Player Info, l'host registra `Adding pending join`. Nessun cambio stanza segue. Circa 270 frame dopo lei esce. |

Altri tentativi dopo lo split, con nuove lobby e run da zero: 20/09 13:03 (lei esce al frame 346)
e 20/09 13:06 (al frame 747 lei chiede di nuovo i dati di salvataggio, `Save Data Request`, ed esce
subito). Sono uscite rapide, compatibili con un errore sul suo lato che il nostro log non mostra.

Dettaglio sulla sessione 13:07: dopo lo split il numero di frame **torna indietro**
(da 63717 a 57809). La run solitaria dell'host sembra ripartire da uno stato precedente
(checkpoint o snapshot), non dal frame del desync.

**Per chiudere la diagnosi manca il lato di Megumi.** Servono i suoi `online_logs/sessions/`
delle stesse date (19/09 sera, 20/09 dalle 12:50 alle 13:10). Soprattutto per la variante A:
se il suo client ha provato a entrare in una lobby, quale ID ha usato (vecchia o nuova?) e con quale errore.

## 2026-09-22 — Analisi dal lato di Megumi (i suoi `online_logs/sessions`)

Log in `logs-megumi/`, escluso da git perché contiene dati personali. Megumi usa Steam, con il gioco
in `C:\Program Files (x86)\Steam\...`. Le cartelle hanno gli stessi nomi delle nostre, quindi le
sessioni si confrontano una a una.

### Il desync è simmetrico: entrambi scelgono DROP

In 23:40 e 13:07 il client di Megumi vede lo stesso desync con gli stessi checksum, sceglie
`SYNC_PLAN_DROP` per "Michele" e crea **anche lui** una lobby nuova. Nessuno dei due resta nella
lobby originale.

### La run in solo riparte dall'inizio del piano

Dopo lo split il frame di gioco di Megumi torna a **48547** ("Room 1.2 Start Room"), mentre il frame
di rete prosegue (52718, 57854…). Stessa cosa sul nostro lato in 13:07 (63717 → 57809).
**Lo split ricarica lo stato salvato a inizio piano.** Chi continua in solo perde tutto quello che
ha fatto nel piano corrente.

### ❌ Ipotesi smentita: il cambio stanza NON sblocca il pending join

In 23:40 Michele entra nella nuova lobby di Megumi (frame di rete 52718). Il client di lei registra
`Adding pending join for player …` **più di 12 volte in circa 2,5 minuti** (52999 → 57854) e nel
frattempo **cambia stanza oltre 20 volte**. Il join non viene mai elaborato.
- `Processing pending joins` compare **una sola volta** in tutti i log di entrambi, al frame 4
  subito dopo la creazione della lobby.
- `Joining existing game on frame %d` (stringa presente nell'exe) **non compare mai**.
- Il sistema di "revival" per i giocatori caduti (`MSG_DROP_ON/OFF/EXECUTE`, `RevivePlayer`,
  `Revival Frame … has passed without being revived`) **non si attiva mai**: lo split da desync
  chiama `RemoveUser` direttamente e salta questo meccanismo.

**Nuova ipotesi:** dopo uno split per desync la partita non è più in uno stato che accetta join
in corso. La coda si riempie ma nessuno la elabora. Per capire quale condizione blocca
l'elaborazione servirà guardare il codice attorno a `Adding pending join` / `Processing pending joins`
(fase futura, reverse engineering).

### Riprendere la run salvata: bloccato dallo stato delle mod

Nei tentativi successivi (12:56, 12:58, 13:03, 13:06) il client di Megumi scrive:

```
Cannot continue a game that doesn't have the same modding state (rep+rerunstate1.dat)!   (x20)
Cannot continue a game that doesn't have the same modding state (rep+rerunstate2.dat)!   (x8)
Cannot continue a game that doesn't have the same modding state (rep+rerunstate3.dat)!   (x8)
```

Il gioco **rifiuta di continuare la run salvata** perché il set di mod attive è cambiato rispetto
al momento del salvataggio. Per questo in quei tentativi il messaggio è `Join New Game`, non
"existing": si riparte da zero. In più `thefuture_2993558907` va in errore Lua sul client di Megumi.

### Mod diverse tra i due client: probabile causa dei desync

| Sessione | Solo Michele | Solo Megumi |
|---|---|---|
| 19/09 23:40 | nessuna mod (patch online non ancora applicata) | ~55 mod caricate come **content** (XML/risorse, nessuno script Lua), tra cui stackable items, better doors, epiphany, repentogon… |
| 20/09 12:56 | stageapi15 | external item descriptions, **start_with_spoonbender** |
| 20/09 13:07 | – | **start_with_spoonbender** |

Una mod "content" (solo XML, senza Lua) può comunque cambiare il gameplay: statistiche,
oggetti iniziali, entità. `start_with_spoonbender` presente su un solo client è un candidato forte
per il desync delle 13:07. Nel caso 23:40 le mod content di Megumi, caricate contro il gioco
vanilla di Michele, possono spiegare lo scarto di velocità del giocatore.

### Conclusioni operative

1. **Per ridurre gli split subito, senza codice:** stessa identica lista di mod (anche solo content)
   su entrambi i client, e nessuna mod che cambia il gameplay presente da un solo lato.
2. **Per rientrare nella stessa run dopo uno split:** con il gioco com'è oggi non funziona. Il
   pending join non viene mai elaborato, e il salvataggio non si può riprendere se le mod
   sono cambiate. È il cuore del problema 1 e richiede la fase di reverse engineering.

### Precisazione dell'utente (22/09/2026)

Dopo ogni split il secondo giocatore provava a rientrare e restava in **"waiting"** senza mai
entrare in partita, **finché non abbandonava a mano**. Le uscite rapide (13:03 al frame 346,
13:06 al frame 747, 12:52 al frame 2813) sono quindi **abbandoni manuali**, non errori del client.
Una sola volta ciascuno ha provato a finire la run in solo (per Megumi è la sessione 23:40:
continua a giocare mentre il join di Michele resta in coda).

Il "waiting" dal lato del joiner corrisponde esattamente a `Adding pending join` mai seguito da
`Processing pending joins` / `Joining existing game` dal lato host. **Caso d'uso e sintomo nei log
coincidono.** Il punto da indagare è perché la coda dei pending join non viene elaborata dopo uno split.

### Correzione: le mod NON sono la causa principale dei desync

L'utente riferisce che le ultime run erano senza mod, o al massimo con una, e lo split è avvenuto lo stesso.
Conteggio dai log (`LOADED MOD` = contenuto caricato, `Lua` = script eseguiti):

| Sessione | Michele content/Lua | Megumi content/Lua | Desync |
|---|---|---|---|
| 19/09 (tutte) | 0 / 0 | ~54 mod / **0** | 23:40 |
| 20/09 12:52 | 15 / 11 | – | frame 0 |
| 20/09 12:56–13:06 | 6–14 / 4–10 | 8–18 / 4–9 | nessuno |
| 20/09 13:07 | 6 / 4 | 8 / 4 | 63715 |

- Nel desync 19/09 23:40 **nessuno script di mod girava** su nessuno dei due client. Megumi caricava
  solo il contenuto (risorse) delle mod, e nel log non compare nessun XML di gameplay. È una partita
  praticamente vanilla.
- In 2 casi su 3 la divergenza è la velocità del giocatore (`PostVelocity`): è compatibile con un
  errore di calcolo del gioco base, non con una mod.

**Conclusione rivista:** il gioco va in desync anche senza mod. Le mod diverse sono un fattore
aggravante, non la causa. Il difetto da correggere è la **gestione** del desync (sempre DROP) e il
**rientro** (pending join mai elaborato), che avvengono allo stesso modo con o senza mod.

## 2026-09-22 — Rete di sicurezza: mod snapshot personaggio (consegnata)

`diagnostics-mod/snapshot.lua`, inclusa da `main.lua`. Sola scrittura su file mod, nessun effetto
sul gameplay. Verificata in singleplayer: si carica e fotografa senza errori (`snapshot(game-start)`,
`snapshot(new-room)`).

- A ogni stanza fotografa ogni giocatore: oggetti (ciclo 1..732 con `GetCollectibleNum`), trinket,
  oggetti attivi, carte/pillole nei 4 slot, tutti i tipi di cuore, monete/bombe/chiavi, tipo personaggio.
  Salva su `mod:SaveData` (JSON), quindi sopravvive alla chiusura/riavvio.
- Alla riduzione del numero di giocatori (lo split) congela l'ultima foto valida in `state.frozen`.
- Recupero da console: `bto_dump` elenca le foto, `bto_restore <indice>` riapplica una foto al
  giocatore locale 0.

**Limiti noti (`ponytail:` accettati per ora):**
- Enumerazione oggetti O(732) a ogni stanza; se pesa, aumentare STRIDE o filtrare per pool.
- Non salva la carica esatta degli attivi, la posizione, gli effetti temporanei, i famigli speciali.
- La console vanilla richiede `EnableDebugConsole=1` in `options.ini`. Il recupero automatico al
  rientro dipende dal fix del rientro (fase patch), non ancora fatto.

## Stato / prossimo bivio (decisione utente)

L'analisi statica indica due fix possibili, entrambi richiedono di **modificare il comportamento del
gioco** (patch su file con backup, oppure hook in memoria). Il brief li mette fuori scope e le mie
regole richiedono conferma esplicita prima di procedere. Non è stato scritto né applicato alcun patcher.

- **Fix A — sbloccare il rientro:** far elaborare la coda dei pending join anche quando l'host è
  rimasto solo (gate a VA 0x0090DF6F / chiamanti). Piccolo, ma da validare con debugger + 2 giocatori.
- **Fix B — evitare lo split:** al posto di DROP, far rientrare il peer divergente col percorso
  join-in-progress. Più invasivo.

Serve un test dal vivo con Megumi per validare qualunque patch: patchare il netcode solo da analisi
statica può causare crash o desync peggiori.

## 2026-09-22 — Strada "Continua" (idea utente) NON percorribile

Verificato sui log: per le run online il gioco **non salva uno stato riprendibile**. Al menu controlla
`rep+gamestate1/2/3.dat` e trova sempre `SteamCloud could not find or open ... No Game State detected`,
poi `Cannot continue a game that doesn't have the same modding state (rep+rerunstate<N>.dat)`.
Nessun file `gamestate` esiste nella cartella save. Conclusione: non c'è una sessione online da
riprendere col "Continua". Abilitarlo richiederebbe di far salvare e ricaricare l'intera run online
al gioco — più invasivo del rientro dal vivo. **Scelta: strada A (rientro).**

## Piano fix rientro (autorizzato dall'utente il 22/09/2026)

Obiettivo: dopo uno split, il giocatore in "waiting" rientra nella run **in corso** dell'host e
recupera il personaggio esatto di prima del desync.

Due pezzi:
- **Pezzo 1 — sbloccare l'elaborazione della coda (patch al gioco).** `NetManager::ProcessPendingJoins`
  (VA 0x0090DF40) non viene chiamata quando l'host è rimasto solo. Approccio candidato: al sito
  "Adding pending join" (VA ~0x916A4D) iniettare, via code-cave, una chiamata a
  `ProcessPendingJoins(force=1)` subito dopo l'inserimento in coda, così il join viene elaborato
  anche senza altri giocatori remoti. Da fare come **patch su file con backup** (stessa tecnica della
  patch mod-online già presente, coesistente con essa), NON come iniezione in memoria del processo.
- **Pezzo 2 — recupero personaggio (mod, già pronta).** Al rientro il gioco darebbe un personaggio
  nuovo; `snapshot.lua` riapplica la foto congelata allo split (oggetti, cuori, consumabili).

Il punto esatto della patch va **confermato in una sessione di test dal vivo con Megumi**: patchare il
netcode solo da analisi statica può causare crash o desync. Serve provocare uno split reale e osservare,
con la mod diagnostica attiva su entrambi, se la chiamata forzata fa completare il rientro senza desync.

### Protocollo di test (sessione con Megumi)
1. Entrambi con mod diagnostica+snapshot identiche e stessa lista mod. `EnableDebugConsole=1`.
2. Avviate una run online, giocate fino a provocare uno split (mod diverse, o attesa di un desync).
3. Chi è fuori prova a rientrare; l'host **cambia stanza** subito dopo.
4. Raccogliete i log di entrambi (`online_logs/sessions/…`) + le righe `[BTO-DIAG]`/`[BTO-SNAP]`.
5. Con quei dati si finalizza e prova il Pezzo 1; a rientro riuscito si aggancia il Pezzo 2.

## 2026-09-22 — Correzione modello (chiarimento utente)

Precisazione importante dell'utente: **allo split nessuno perde il personaggio**. Ogni giocatore
continua da solo con tutto quello che aveva. (Il `Removing collectible ... from player` nei log
dell'host riguarda la sua copia del giocatore rimosso, non il personaggio dell'altro nella sua
sessione.) L'unica cosa rotta è il **rientro**: per tornare insieme uno chiude il gioco e prova a
entrare nella sessione ancora aperta dell'altro, ma resta in **"waiting"** e non entra mai.

Conseguenze:
- Il fix necessario è **solo il Pezzo 1** (sbloccare l'elaborazione della coda di pending join lato
  host). Bersaglio invariato: `Adding pending join` non seguito da `Processing pending joins`.
- **Non è ancora noto** se, a rientro sbloccato, il gioco restituisca il personaggio pieno o uno
  vuoto: chi entra è sempre rimasto in waiting. Da verificare nel test.
- La mod snapshot passa da "pezzo centrale" a **riserva**: serve solo se il rientro desse un
  personaggio vuoto. La foto è salvata su file, quindi sopravvive alla chiusura del gioco di chi
  rientra. La riga `REJOIN dry-run` mostrerà cosa recupererebbe, da confrontare con ciò che il gioco
  dà davvero.

Flusso reale del rientro: B **chiude del tutto il gioco**, poi usa invito/join per entrare nella
sessione di A ancora in corso (client fresco che fa join-in-progress). È il percorso già analizzato.

## 2026-09-22 — Diagnosi desync: RNG in sync, diverge solo la velocità (chiave)

Dai `desync_diff.txt` dei due desync mid-run (23:40 Eden, 13:07 Bethany):

| | RNG globale (P0 / P1) | Velocità personaggio (P0 vs P1) |
|---|---|---|
| 23:40 | `1c46977a` / `1c46977a` — **UGUALE** | Y: -5.2567 vs -5.1579 |
| 13:07 | `fa8e37ba` / `fa8e37ba` — **UGUALE** | (4.1287, 1.9684) vs (4.0851, 1.9054) |

**L'RNG globale è identico su entrambi i PC; diverge solo la velocità del personaggio (~0.1).**
Implicazioni:
- Il desync **non** è causato da RNG né da mod (quelle divergerebbero l'RNG). È una micro-divergenza
  di **fisica/movimento** del gioco base. Uguagliare le mod aiuta poco per questo tipo.
- Possibile causa: previsione del movimento con input delay, o risoluzione collisione/knockback non
  perfettamente deterministica. Config attuale: `OnlineInputDelay=2`, `EosCrossplay=0` (backend Steam).
- **Leva a costo zero:** alzare `OnlineInputDelay` (2 -> 4) su entrambi i PC e misurare se i desync
  calano. Reversibile, prezzo = un po' di input lag. Da validare nel test.
- **Rafforza il "non spezzare la sessione":** la divergenza è minima e isolata (RNG e save allineati),
  quindi una risincronizzazione mirata della sola velocità sarebbe semplice. Il DROP del gioco è
  sproporzionato.

### Obiettivo ultimo dell'utente: "che il desync non avvenga proprio"
Prevenire OGNI desync = rendere deterministico il netcode del gioco base (gli sviluppatori stessi non
ci riescono, per questo esiste il rilevatore). Non realistico per il progetto. Decomposizione
raggiungibile, in ordine:
1. Ridurre la frequenza: stessa versione, stesse mod, `OnlineInputDelay` più alto.
2. Far sì che il desync NON spezzi la sessione: su desync, risincronizzare il peer invece di scartarlo
   (riusa il trasferimento di stato del rientro). È la versione ottenibile del traguardo.
3. Se comunque spezza, rientro (Pezzo 1).
Il Pezzo 1 è il mattone comune di 2 e 3.

## 2026-09-22 19:45 — CRASH di entrambi i client causato dalla mod snapshot (corretto)

Durante la prima sessione di test (Isaac + Azazel, ~50k frame, nessun desync) il gioco è crashato
**su entrambi i PC nello stesso momento**. Report: `crash_dumps/isaacv1.9.7.17.J460-20260922-194550-*.txt`.

```
[C](-1): GetCollectibleNum                       <- crash nativo (access violation)
snapshot.lua(43): snapshotPlayer                 p:GetCollectibleNum(id, true) nel ciclo 1..732
snapshot.lua(78): snapshotAll
snapshot.lua(137): MC_POST_NEW_ROOM
```

- Contesto: `Starting room transition (type 6)` verso la Starting Room, subito dopo la morte di un boss.
- Una transizione dello stesso tipo 6 al frame 40670 era passata senza crash: la causa dipende da uno
  stato preciso del gioco in quel momento (probabilmente un giocatore in stato transitorio), non dal
  solo tipo di transizione. Root cause non confermata.
- Crash simultaneo = in lockstep entrambi eseguono la mod nello stesso frame con lo stesso stato.
- Un crash nativo non è intercettabile con `pcall` da Lua.

**Azione:** snapshot **disattivato** (`include("snapshot")` commentato in `main.lua`). Il logger
`main.lua` resta attivo: ha retto l'intera sessione. Visto il chiarimento dell'utente (allo split
nessuno perde il personaggio), lo snapshot è solo una riserva e non vale il rischio. Da riattivare
solo se il test del rientro mostra un personaggio vuoto, e solo dopo averlo corretto (gate con
`HasCollectible`, `ItemConfig` non nil, snapshot differito a stanza stabile, salto giocatori morti).

**Lezione:** qualunque mod che gira online va provata a lungo in singleplayer, attraversando
morti, revive e boss, prima di darla a entrambi i client: un crash deterministico colpisce tutti.

### Correzione (utente): non è morto il boss, sono morti i giocatori durante la bossfight

Il report mostra il frame che torna indietro: `[Frame 52192] TriggerBossDeath` poi
`[Frame 50210] Starting room transition (type 6)` verso la Starting Room. Alla morte il gioco ha
**ricaricato uno stato salvato in precedenza** (respawn/revive), ricostruendo i personaggi.
Ipotesi di causa raffinata: `MC_POST_NEW_ROOM` è scattato durante quella ricostruzione e
`GetCollectibleNum` è stato chiamato su un personaggio non ancora valido. Le morti in bossfight sono
frequenti: lo snapshot com'era avrebbe crashato spesso. Disattivarlo era corretto.

Indizio utile per l'obiettivo "non spezzare la sessione": il gioco ha già un meccanismo per
**ricaricare uno stato salvato** (usato per il revive). Da studiare come possibile base per una
risincronizzazione al posto del DROP.

## 2026-09-23 — Due desync a metà run in The Future (StageAPI 2.42, The Future 1.8)

**17:18 (3 giocatori, Megumi + Xandred_Kaze):** desync al frame 37271, cioè al primo cambio stanza
dentro il piano The Future. Megumi e Xandred hanno lo stesso checksum; Michele è l'unico diverso: sul
suo client la transizione verso la stanza a sinistra (idx 83) è partita, sugli altri il giocatore è
fermo contro il bordo (x=38, velocità azzerata). Stesso copione dopo lo split: entrambi entrano nella
nuova lobby, `Adding pending join` e poi più niente (bug del rientro confermato, anche con 3 giocatori).

**21:08 (2 giocatori, SuperPanciLollo):** desync al frame 34799. RNG globale **uguale**, diverge
**una sola entità**: 391.151.0 = *Selfish Knight* di The Future.
Velocità dopo l'update: (-4.0, 0) su un PC e (-2.8, -1.2) sull'altro. È esattamente
`Lerp((-4,0), (0,-4), 0.3)`: su un solo PC il cavaliere ha girato da LEFT a UP.

Causa (`tf_scripts/monsters/selfishknight.lua:127`): la svolta dipende da `mod:RandomInt(1, 50-…)`
senza RNG proprio, quindi usa `TheFuture.rng`. Lo stesso RNG viene consumato anche in
`MC_POST_RENDER` dal timer dell'effetto fulmine (`tf_scripts/thefuture.lua:226`). Il render gira un
numero di volte diverso su ciascun PC (fps, recupero dopo lag), quindi `TheFuture.rng` va fuori fase
e l'IA dei mostri che lo usano diverge. È l'unico punto di render che consuma quell'RNG (verificati
tutti i callback `*_RENDER` della mod).

**Fix:** mod `thefuture-onlinefix/` (va installata su tutti i client). Durante `MC_POST_RENDER`, le
chiamate a `TheFuture:RandomInt` senza RNG proprio usano un RNG cosmetico separato. Il gameplay non
cambia. Non tocca i file di The Future, quindi sopravvive agli aggiornamenti Steam.

Il fumetto di Steven senza testo è cosmetico: il font viene caricato da un percorso fisso
`mods/thefuture_2993558907/…` (`tf_scripts/stevennpc.lua:10`), nel log non c'è nessun errore. Da
verificare se il problema c'è anche in singleplayer.

## 2026-09-24 — ✅ Il rientro FUNZIONA: la coda viene elaborata al cambio PIANO

Sessione `09_24_2026__20_27_11` (Michele host; Megumi, Xandred_Kaze, SuperPanciLollo). Fix The Future
attivo (`[BTO-FUTUREFIX] RandomInt wrapped`).

**Desync 1 (frame 54644, Depths, stanza del boss):** divergono solo 3 lacrime (2.0.0) di circa 0.09 px,
e l'RNG globale è uguale. È la solita micro-divergenza di fisica del gioco base. The Future non c'entra.
Split e DROP come sempre.

**Rientro:** SuperPanciLollo e Megumi entrano nella nuova lobby, `Adding pending join` (55800, 56142).
Poi l'host prende la botola: `Starting stage transition` → **`Processing pending joins` → `Adding 2 new
players` → `Joining existing game on frame 56503`**.
Corregge la diagnosi precedente: la coda non è bloccata per sempre, viene elaborata **solo alla
transizione di piano**. Il cambio stanza non basta, e nei casi precedenti si abbandonava prima di
cambiare piano.
- Chi rientra riceve un **personaggio nuovo** (Isaac, Azazel) più **5 oggetti casuali dal pool
  treasure**: è la compensazione vanilla per chi entra a partita in corso. Il personaggio di prima
  non torna.
- L'host serializza e ricarica lo stato per tutti (anche per sé: `player removed` ×3, poi respawn, `RNG
  Start Seed … [Continue]`, stanza "The Gate").

**Desync 2 (frame 56554, 51 frame dopo il join, `Level::Init` del piano nuovo):** i due che sono
rientrati concordano tra loro, l'host è diverso, **RNG globale diverso**. L'host ha nella Start Room
6.19.0 (spawn del layout a (240,160), presente a ogni piano) e 1000.12548 (StageAPIStageShadow), gli
altri no.
Ipotesi (senza i log dei joiner): tutti ripartono dallo stesso stato serializzato, ma la generazione del
piano diverge. Candidati: (a) lo stato Lua delle mod (StageAPI, The Future, Fair Co-op Items) **non
viene trasferito** al join, quindi i joiner hanno lo stato Lua "fresco" o i loro file locali, l'host
quello di tutta la run; (b) il join elaborato *durante* una transizione di piano.
**Servono i log di chi è rientrato** (`online_logs/sessions` e `desyncs` delle 20:27 del 24/09).

## 2026-09-24 — Desync all'avvio quando SuperPanciLollo NON è l'host

Sessioni 23/09 20:49, 20:55, 21:03 (host Michele, solo Panci) e 24/09 21:02–21:10 (host Michele, 4
giocatori). Il desync arriva sempre al frame 0–1 e finisce con DROP. Secondo l'utente non succede
quando l'host è Panci.

Nei casi a 2 giocatori (gli unici con i dati remoti completi):
- **Save checksum UGUALE** (394715111 = 394715111; 4218897375 = 4218897375): il salvataggio condiviso
  non c'entra.
- Stesso seed della run, stesso `Level::Init`, stesse entità nelle stesse posizioni (2 giocatori, 6.19,
  1000.21).
- **RNG globale già diverso al frame 0** (0f36f3bb vs 0ac4f55b).

Quindi all'avvio della run il client di Panci consuma l'RNG in modo diverso dagli altri: esegue
qualcosa in più o in meno. Cause probabili: una mod (anche solo "content"/XML) o una versione di mod
presente solo da lui, oppure file residui nella sua cartella mods. L'asimmetria host/non host fa
pensare a codice che si comporta diversamente in base a chi è il giocatore 0 o l'host.
**Serve il `log.txt` di Panci** subito dopo un avvio fallito, per confrontare le righe
`LOADED MOD` / `Running Lua Script` con le nostre.

## 2026-09-26 — ✅ CAUSA dei desync all'avvio e dopo il rientro: preload di StageAPI alla prima run

Log di Megumi e SuperPanciLollo in `logs-others/` (esclusi da git). Stesse 7 mod Lua su tutti i client.

**Schema (tabella per seed, memoria Lua al primo "Lua mem usage" della run):** la memoria Lua è circa
37,9 MB alla **prima run dopo l'avvio del gioco** e circa 56,6 MB dalle run successive.
- Desync all'avvio: 9C48, DGLK, RKXG (Panci alla prima run, gli altri no), ZKS3 (Michele alla prima
  run, gli altri no). 48ND e 9KM4: Panci senza mod Lua (817 KB, mod non attive).
- Nessun desync: tutti alla prima run (NJ91) oppure tutti già oltre la prima (DCN2, SST2, YKC2).

**Meccanismo** (`stageapi15_1348031964/scripts/stageapi/stage/extrarooms.lua:205-279`, StageAPI 2.42):
in `MC_POST_GAME_STARTED`, se `StageAPI.PreloadedGotoData` non è ancora caricato (cioè alla prima run
del processo), StageAPI esegue circa 100 comandi console `goto` per leggere i dati delle stanze. Nel
log si vedono circa 100 `Starting room transition` al frame 0, solo sul client alla prima run. Poi,
se si è nella prima stanza del piano 1, imposta `DataLoadNeedsRestart` ed esegue `Isaac.ExecuteCommand("restart")`
al `MC_POST_NEW_ROOM` successivo. Online lo fa **solo quel client**: RNG globale diverso al frame 0,
entità identiche, save identico → desync.
- Spiega anche il **desync subito dopo il rientro** (24/09 frame 56554): chi rientra ha appena
  riavviato il gioco, quindi è alla prima run. Megumi fa 103 `Starting room transition` a frame fermo
  prima del `Level::Init`.
- Spiega "Panci da ospite desincronizza": lui avvia il gioco ed entra subito, mentre gli altri hanno
  già giocato.

**Workaround (senza codice):** dopo aver avviato il gioco, **ognuno fa partire una run in solo,
aspetta che carichi (può ricominciare da sola una volta), poi esce** e solo dopo va online. Vale anche
per chi rientra dopo uno split.

## 2026-09-30 — Log di Xandred: desync del 25/09 tra Xan e Megumi

Log di Xan in `logs-others/xan/`. Entrambi avevano `thefuture-onlinefix` attivo (`[BTO-FUTUREFIX] RandomInt
wrapped`) e il logger v0.1 (senza traccia): la causa NON è il fulmine e non c'è ancora la traccia.

- **23:02, frame 176** (run CFLR): entrambi con memoria Lua 56,6 MB, cioè **nessuno alla prima run** (la
  run 0S6G subito prima, con entrambi a 37,9 MB, non ha avuto desync, coerente con la diagnosi del
  preload). Quindi è un desync di gioco base a inizio run: posizione del giocatore diversa di ~16 px e
  lacrime di ~1,5 px al frame 176.
- **23:22, frame 48294** (run TGEE, The Future): divergenza precoce, visibile prima del dump. Nella stanza
  "Sin - Monster Slots" la stessa moneta (5.20.1, stesso seed 22d2058a) compare al frame di rete **48217 su
  Xan e 48249 su Megumi**, 32 frame dopo. I frame locali di transizione differiscono di 2 (45575 vs 45573).
  Quindi il movimento/interazione dei giocatori era già diverso nella stanza delle slot, molto prima del
  frame segnalato. Al dump: posizioni giocatore diverse di ~50 px, 4 proiettili (9.12.0) presenti solo su
  Megumi. Ipotesi: stesso tipo di micro-divergenza di fisica del gioco base, amplificata in una stanza
  con molte interazioni. Non riconducibile a una mod dal solo log.
- Il codice `CURRENT ROOM INDEX 84 <hash>` è costante per client e diverso tra PC anche nelle run senza
  desync: NON è un indicatore di divergenza (pista scartata).

**Prossimo passo:** logger v0.2 (traccia di stanza, entità, posizione p0 ogni secondo) su tutti i client,
poi confronto degli `f=` tra i due log per il primo secondo divergente.

## 2026-10-02 — Le sessioni dell'1/10 sono inquinate da un bug mio; il rientro non scatta ai cambi piano di StageAPI

**Bug mio (corretto in `thefuture-onlinefix` v0.3, commit 42e0e70):** nella v0.2 usavo `game` senza
definirlo → `Error in "PostGameStart" call: …thefuture-onlinefix/main.lua:44: attempt to index a nil value
(global 'game')` a ogni avvio online (1/10: 1+3+1 errori; 30/09 prima dell'aggiornamento: 0). La mod ha
priorità IMPORTANT, quindi gira per prima e l'errore salta le altre callback `PostGameStart` (il logger
non scrive `game started` in 2 sessioni su 3, StageAPI salta il suo preload). **I desync d'avvio e quello in
The Future dell'1/10 non sono valutabili.**

**Rientro di Megumi (sessione 10_01_2026__21_08_56, host Michele):** desync al frame 42397, lei entra nella
lobby, `Adding pending join` al 43065. Michele esce da The Future: compare `Level::Init m_Stage 6` **senza**
`Starting stage transition` e **senza** `Processing pending joins`. Lei lascia la lobby al 55845; la discesa
vanilla successiva (`Starting stage transition` + `Level::Init m_Stage 7`) è al 56500, solo ~650 frame dopo.

Causa (codice): StageAPI cambia piano da/verso gli stage custom con la console,
`Isaac.ExecuteCommand("stage …")` (`stage/transitions.lua:367, 797-814`), non con la transizione di piano
del gioco. Il join in coda viene elaborato solo nella transizione di piano vanilla (così il 24/09,
con una botola normale). Quindi: **i cambi piano via StageAPI (entrare/uscire da The Future) non fanno
entrare chi aspetta; serve poi una discesa vanilla (botola).**
Da verificare: che anche l'ingresso in The Future passi per la console (ipotesi, stesso codice).

## 2026-10-05 — Serata con v0.3: i desync d'avvio vengono dal client di Megumi, non dal preload

Log di Michele (host), processi 20:23 e 20:50. `thefuture-onlinefix` v0.3: 0 errori `PostGameStart`,
`[BTO-FUTUREFIX] preload forced` + `restart suppressed` su ogni run online.

- **Michele e Panci hanno sempre lo stesso checksum al frame 1** (es. 20:45: 10051334 = 10051334; 20:52:
  1e3af7fa = 1e3af7fa). Il preload forzato funziona tra due client con v0.3.
- Il terzo peer, **Megumi, risulta con checksum 00000000** (nessun framestate: il suo client non è arrivato
  al frame 1) in tutti i desync d'avvio della serata (9 run di fila, incluse run con memoria Lua già a
  56,9 MB). Nel run 20:50 lei esce dalla lobby al frame 8; nel 20:52 al frame 14, e la run di Michele+Panci
  prosegue senza desync (J3QJ).
- Ipotesi principale: Megumi ha ancora la v0.2 con il bug `game` nil (errore in `PostGameStart` che
  interrompe l'avvio) o non ha la mod. Da confermare con il suo `log.txt`: cercare
  `Error in "PostGameStart"`.
- Rumore non correlato: `fair co-op items` dà `main.lua:255: attempt to get length of a nil value (global
  'pedestals')` in `MC_POST_RENDER` (40 errori per processo, il log si ferma lì). Solo render.

## 2026-10-07 — Sessione del 06/10 (v0.4): nessun desync in 95.000 frame; il "lag" è il caricamento dei piani

Log di Michele, sessione `10_06_2026__21_32_21`: host con Panci (2 giocatori), una sola run (HDB4 0GJ4), 0 errori Lua,
`preload forced` + `restart suppressed` + `sync guard active` presenti. Memoria Lua 38 MB al primo avvio = prima
run del processo, quella che prima desincronizzava: **nessun desync**. Fino al piano 10 (stage 10), fine run
`NotifyGameEnd Result = 2` al frame di rete 95387. The Future non visitato (0 `Generating custom level`): le
correzioni sul fulmine e su `math.random` restano non verificate.

**Disconnessione:** al frame di rete 69827 `User MICHELOSCO (locale) disconnected from lobby` → il gioco
rimuove Panci (`RemoveUser`, `Creating public lobby`, nota: lobby *pubblica*, non friend) e Michele prosegue da
solo per ~25.000 frame. Non è un desync: è la perdita della lobby Steam sul client di Michele. Manca il log di
Panci per vedere il suo lato.

**Il "lag" è il caricamento del piano (non la rete):** dalle tracce (tempo reale vs frame) 8 blocchi da 7–12 s
corrispondono a 8 `Starting stage transition`/`Level::Init`. Due di questi (12,4 s) sono avvenuti **dopo** la
disconnessione, quindi in solo: dipendono dal PC/mod, non dal netcode. Ogni piano esegue `[ModManager] load stage`
×7 (una volta per mod). Blocchi minori da 1,7–2,6 s a ogni ~6000 frame (non classificati).

## 2026-10-07 — Revisione completa delle correzioni (v0.5)

Riletto tutto il codice della mod contro `resources/scripts/main.lua`, StageAPI 2.42, The Future 1.8 e i log.

### ❌ Bug grave nella v0.4 (corretto)
`_RunCallback` (main.lua:66) chiama le callback **senza pcall** e si ferma alla prima che genera un errore o
ritorna non-nil (confermato dai log dell'1/10: 3 errori → solo 1 `game started` del logger, quello della run
`[Continue]` in cui il mio handler non errava). Fair Co-op Items va in errore in `MC_POST_RENDER`
(`main.lua:255 … 'pedestals'`, ≥40 volte per processo): il mio decremento `LATE+1` di `renderDepth` veniva
saltato e il contatore restava bloccato >0 → **tutte** le estrazioni (`Random()`, `RandomVector()`,
`TheFuture:RandomInt`) anche in update sarebbero andate sull'RNG locale → desync certo in The Future. Il 06/10
non si è visto solo perché The Future non è stata visitata. **v0.5:** niente contatore; un flag messo a
`true` dalla prima handler di ogni callback di render e a `false` dalla prima handler di ogni callback di
update (tutte tranne `INPUT_ACTION`, `EXECUTE_CMD`, `EVALUATE_CACHE`). Si auto-ripara a ogni tick.

### ✅ Nuova causa trovata: `StageAPI.RandomRNG` non sincronizzato → porta d'ingresso casuale
`library/random.lua:4`: `StageAPI.RandomRNG:SetSeed(Random(), 0)` **una volta al caricamento**, mai per run.
`StageAPI.Random(a, b)` senza rng lo usa per scelte di gioco: `core/callbacks.lua:1350` quando il layout
caricato non ha la porta da cui si è entrati sceglie una porta valida a caso e **sposta tutti i giocatori lì**;
`:1557` prezzo dei patti col diavolo; `grid/customdoors.lua:671-707` particelle delle porte custom.
Combacia con due desync a "prima stanza di The Future":
- 23/09 17:18: su Michele i 3 giocatori a x≈558 (porta destra), su Megumi/Xan a x≈38-43 (porta sinistra),
  fermi, tutti nello stesso punto per client.
- 01/10 42395: su Michele entrambi a (320,160) (porta alta), su Megumi uno a (317,430) (porta bassa).
**v0.5:** riseminato in modo deterministico prima di ogni handler StageAPI (`MC_POST_NEW_LEVEL` con lo
stage seed, `MC_POST_NEW_ROOM` con il decoration seed, priorità `IMPORTANT-1`); in render instradato
sull'RNG cosmetico.

### ✅ Nuova causa: uscita da The Future guidata dal render
La botola custom chiama `StageAPI.GotoCustomStage(data.GoesTo, true)` (`stage/transitions.lua:959`):
l'animazione avanza in `MC_GET_SHADER_PARAMS` (render) e lancia `stage N` quando il contatore supera 150
(`transitions.lua:8-38`) → il piano cambia a un frame di gioco diverso per fps. L'ingresso (Steven:
`GotoCustomStage(mod.Stage, false)`; botola vanilla: sostituzione in `MC_POST_NEW_ROOM`) è in update, ok.
**v0.5:** con ≥2 giocatori `playTransition` forzato a `false` (comando immediato, niente animazione online).

### ❌ Il rientro NON era coperto (v0.3/0.4) — mirror sperimentale in v0.5
Al join il gioco ricarica lo stato su tutti i client ma `MC_POST_GAME_STARTED` scatta **solo sul joiner**
(log host 24/09: nessun `game started` al join; EID lo documenta in `main.lua:4859`). Quindi solo il joiner
rifaceva preload + riseed di `TheFuture.rng` → desync al `Level::Init` 51 frame dopo (24/09), identico con la
v0.3. **v0.5 (sperimentale):** i client già in partita riconoscono il reload (tutti i loro player rimossi,
conteggio cresciuto, run in corso) e in `MC_POST_NEW_ROOM`(LATE+1) eseguono `Isaac.RunCallback(
MC_POST_GAME_STARTED, false)`, cioè lo stesso dispatch del joiner nello stesso punto. Un eventuale dispatch
reale duplicato nello stesso frame viene inghiottito (la mia handler IMPORTANT ritorna `true`). Verifica nel
log dell'host: `join mirror: running POST_GAME_STARTED` seguito da `[BTO-DIAG] game started`.
Nota: `mod:SaveData` non viene trasferito al join; ciò che resta divergente è lo stato Lua persistente delle
mod (es. `HasSpokenToSteven`): il mirror non lo copre.

### Correzioni alle note precedenti
- 05/10: "checksum 00000000 = il client di Megumi non è arrivato al frame 1" era una lettura azzardata; il
  dato certo è che Michele e Panci concordavano e Megumi (mod vecchie) no.
- `CURRENT ROOM INDEX <hash>` non è un indicatore (già scartato).
- Fair Co-op Items usa variabili **globali** (`players`, `pedestals`, `pos`, `it`, …): oltre all'errore in render,
  può collidere con altre mod. Non toccato: il suo oggetto mod è locale, non raggiungibile.

### Cosa resta non verificabile da qui
Nessuna delle modifiche è eseguibile senza due PC. In solo, il log deve mostrare `v0.5 active` e nessun
`Error in`; online, `preload forced`, `restart suppressed`, e in The Future `stage transition animation
skipped`. Il mirror del join si verifica solo provocando uno split e un rientro.

## 2026-10-07 — Achievement con le mod: non servono mod né patch

Richiesta: una mod che riattivi gli achievement con le mod attive. Verificato nell'exe (dettagli e indirizzi
in `research-notes.md`, sezione "Achievement con le mod attive") e nei log di tutti: nella 1.9.7.17 le mod
**non** bloccano gli sblocchi. Regola reale: offline, con almeno una mod attiva, gli sblocchi sono vietati
**solo finché su quel salvataggio non si è battuta Mom** (achievement 4, The Womb); online contano solo i
quattro "Shared save user ID" della partita, le mod sono ignorate. Prova: log di Michele 06/10 (7 mod, online)
con sblocchi nuovi 376, 35, 143, 15, 8, 159; Panci ha sbloccato 28 e 87 con le mod prima ancora di battere Mom
(online), poi The Womb e altri 10 al kill di Mom; Megumi 545. Il gate è il byte ReadOnly del salvataggio,
loggato come `Setting PersistentGameData ReadOnly to True/False`: nei log è False in tutte le run, True solo
nel deathmatch online post-run. Una mod Lua non può toccare nulla di questo. Patch da 1 byte individuata
(0x959626) per togliere anche la condizione "Mom battuta": non applicata, non necessaria.
Aggiornamento 07/10 sera: la "dicitura" e l'icona rossa viste online sono l'avviso di inizio run di **EID
Online+** (testo "Gli achievement sono disabilitati!", 10 s, icona lucchetto), non un messaggio del gioco. La
sua euristica (Book of Revelations/Cube of Meat "disponibili") fallisce online perché la disponibilità degli
oggetti viene calcolata sui salvataggi di tutti i giocatori della lobby (`IsAvailable` 0x730AA0). In solo
con le stesse mod non compare (verificato aprendo il gioco). Dettagli in research-notes.md.
