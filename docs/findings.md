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
