# Audit delle fonti di desync (06/10/2026)

## Come funziona il netcode (dai log e dall'analisi statica)

Lockstep deterministico: ogni client simula tutto da solo e si scambiano solo gli input. A ogni frame
confrontano un checksum dello stato (entità, RNG globale). Se differisce → `Desync detected`, poi
`NetDesyncHandler` decide per ogni peer SYNC o DROP (`0x8FDC7C`, vedi research-notes): **SYNC = "questo peer
ha il mio stato, tienilo"**, DROP altrimenti. Non esiste una correzione dello stato: con 2 giocatori un
desync porta sempre a DROP e a una lobby nuova a testa (`0x8FE2F0..0x8FE350`: se resta solo il locale →
`creating new lobby`). Con 3+ giocatori vince la maggioranza e solo il peer divergente viene scartato.

L'unico meccanismo di trasferimento di stato è il **join in corsa**: l'host serializza la partita, la
carica anche lui (`player removed` ×N, `RNG Start Seed … [Continue]`) e la manda al joiner. Il 24/09
ha funzionato e portava con sé anche i dati salvati delle mod (`[StageAPI] Successfully loaded save data`).

## Fonti di desync trovate, in ordine di evidenza

| # | Fonte | Evidenza | Stato |
|---|---|---|---|
| 1 | Preload dei dati stanze di StageAPI alla prima run del processo: solo quel client esegue ~100 `goto` e un `restart` | desync al frame 0–1 nei log di 4 giocatori; Michele+Panci con v0.3 concordano al frame 1 | mitigata (thefuture-onlinefix ≥0.2) |
| 2 | Draw dell'RNG sincronizzato dentro callback di render (il render gira un numero di volte diverso su ogni PC) | The Future: timer fulmine → Selfish Knight (24/09, RNG globale uguale, una sola entità diversa) | corretta per TheFuture.rng; v0.4 la isola per `Random()`/`RandomVector()` di **qualsiasi** mod |
| 3 | `math.random` Lua (stream locale, non sincronizzato) usato in gameplay | The Future: effetti con posizioni casuali in update (`moretug.lua:173`, `sisyphus.lua:332`) | v0.4: ri-seed per frame da (seed run, frame) |
| 4 | Liste di mod diverse tra i client | 19–20/09 | si evita con lo zip uguale per tutti |
| 5 | Piccola divergenza di fisica del gioco base (velocità ~0.1, lacrime ~1.5 px) | desync senza mod il 19/09, 20/09, 24/09, 25/09 | **non risolvibile con una mod** |
| 6 | `StageAPI.RandomRNG` seminato una volta da `Random()` al caricamento, mai per run; usato per la porta d'ingresso casuale (sposta tutti i giocatori), prezzo diavolo, particelle porte | 23/09 17:18 e 01/10 42395: giocatori tutti a una porta diversa per client | v0.5: riseed deterministico per piano/stanza |
| 7 | Uscita da The Future con animazione StageAPI: `stage N` lanciato da `MC_GET_SHADER_PARAMS` (render) | codice `transitions.lua:8-38, 959` | v0.5: niente animazione con ≥2 giocatori |
| 8 | Join in corsa: `MC_POST_GAME_STARTED` solo sul joiner → preload/riseed asimmetrici | 24/09 desync 51 frame dopo il join | v0.5 sperimentale: mirror del dispatch sui client già in partita |

La 5 è il motivo per cui "evitare tutti i desync" non è raggiungibile: non dipende dalle mod e il
gioco, con 2 giocatori, non ha un arbitro. Serve una **risincronizzazione guidata dall'host**.

## Audit delle mod installate (uso di API locali al client)

Conteggio di righe (`grep`, escluse righe commentate):

| Mod | math.random | Random() | RandomVector() | GetRandomPosition | GetTime | Input. |
|---|---|---|---|---|---|---|
| StageAPI 2.42 | – | 4 | – | – | 4 | 12 |
| The Future 1.8 | 12 | – | 24 | 10 | – | – |
| Fair Co-op Items | – | – | – | – | – | 4 |
| EID Online Plus | 1 | – | – | – | 10 | 93 |
| Antibirth Music+++ | – | 2 | – | – | – | – |

Valutazione: StageAPI usa `Random()` solo alla creazione delle stanze (update, ordine uguale su tutti).
`Input.` e `Isaac.GetTime` sono per UI/EID, non toccano la simulazione (da verificare caso per caso se
una mod li usa per decidere gameplay). The Future è l'unica con molto uso di randomico, quasi tutto con
`npc:GetDropRNG()` (sicuro); restano `math.random` in effetti e `RandomVector()` negli update (sicuro
finché l'ordine di chiamata è uguale su tutti).

## Cosa significa "i client dettati dall'host"

Due livelli, di costo e rischio molto diversi:

1. **Prevenzione (mod Lua, fatto in parte):** togliere alle mod la possibilità di sfasare l'RNG
   (punti 1–3). Vale per tutte le mod, non solo The Future. Non copre il punto 5.
2. **Risincronizzazione (patch all'exe):** al posto di DROP + lobby nuova, far entrare il client
   divergente nel percorso di join in corsa dell'host, così riceve lo stato (e i save data delle mod)
   dell'host. Pezzi necessari, tutti individuati ma non provati:
   - host: non creare la lobby nuova quando scarta un peer, ma restare nella lobby e continuare
     (`0x8FE348`: salto condizionale da evitare);
   - client scartato: invece di creare una lobby, rientrare nella lobby dell'host (funzione di join
     lobby del backend Steam/EOS, da individuare);
   - host: elaborare subito i join in coda (`ProcessPendingJoins(force=1)`, `0x90DF40`) senza aspettare
     il cambio piano.
   Rischio alto (netcode nativo, due backend, nessun debugger né secondo PC per provare). Richiede una
   sessione di test con un secondo giocatore e backup dell'exe; la patch mod-online esistente va
   conservata (vedi research-notes).

Per le mod: il join in corsa trasferisce i dati salvati (`SaveData`), non lo stato Lua in memoria
(`TheFuture.rng`, tabelle locali). Una mod che tiene stato rilevante solo in memoria resterebbe
divergente dopo la risincronizzazione, quindi la prevenzione (punti 1–3) resta necessaria.
