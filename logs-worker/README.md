# BTO logs: i log delle partite, in automatico

Dopo un desync o un errore Lua, BTO Sync chiede una volta sola "Inviare i log in automatico?"
(Sempre / Solo questa volta / No; si cambia in Impostazioni). Con "Sempre", ogni volta che Isaac si
chiude dopo una partita con errori, BTO Sync manda il log dell'ultima partita a questo Worker.
Il Worker ne fa il commit nel repository privato `MICHELOSCO/bto-logs`, e lì una GitHub Action mette
a confronto i log della stessa partita.

```
BTO Sync ──POST──▶ Worker Cloudflare (token nei secret) ──Contents API──▶ bto-logs (privato) ──▶ Action: ANALISI.md
```

## Cosa parte dal PC e cosa no

- **Pulizia sul PC** (`Core.CleanLog`): gli Steam ID (`7656119…`) diventano "Giocatore 1",
  "Giocatore 2"… e i nomi dei giocatori ricevono lo stesso numero del loro ID. Sono presi dalle righe del
  log che li mettono insieme (`User <id> (<nome>)`, `from user <nome> [<id>]`, `"<nome>" <id>`,
  `Username = <nome> ID = <id>`) e dagli account Steam del PC (`loginusers.vdf`). Poi vengono sostituiti
  ovunque, come parole intere. `C:\Users\<nome>` diventa `C:\Users\<utente>`. Il Worker ripete la stessa
  pulizia, nel caso arrivi un log da un client vecchio o modificato.
- **Il log:** solo l'ultima partita, cioè dalla riga `RNG Start Seed` della run con il primo desync
  (o dell'ultima run) fino alla run dopo. È compresso con gzip.
- **Metadati:** versione di BTO Sync, codice `BTO3:` delle mod attive e lo stesso elenco in chiaro
  (cartella, id del Workshop, nome, versione, inizio dell'hash), seed della run, frame del primo desync,
  errori Lua per mod e un ID anonimo casuale per installazione (`%APPDATA%\BtoSync\log-id.txt`).
- **Ora:** il log di Isaac non ha un orologio. Al suo posto vale l'ultima scrittura di `log.txt`, cioè
  quando Isaac si è chiuso. La cartella della partita è `AAAA-MM-GG_seed-XXXXXXXX` (data UTC), quindi i
  log degli amici della stessa partita finiscono nella stessa cartella.
- La scelta è in `%APPDATA%\BtoSync\invio-log.txt` (`sempre` o `no`; se manca, BTO Sync chiede).
  `invio-log-fatto.txt` ricorda l'ultimo log mandato, così lo stesso log parte una volta sola.

Nel repository finiscono `AAAA-MM-GG_seed-XXXXXXXX/<id-anonimo>.log.gz` e `<id-anonimo>.json`. Se lo
stesso giocatore manda due log per la stessa partita, il secondo diventa `<id>-2`.

## I passi da fare (una volta)

Il Worker non è pubblicato e il repository non esiste ancora: questi passi li fai tu.

1. **Repository privato.** Su GitHub crea `MICHELOSCO/bto-logs` **privato**, con un README qualsiasi
   (la Contents API vuole almeno un commit). Copiaci il contenuto di `repo-template/`, cioè
   `.github/workflows/analisi.yml` e `scripts/analisi.mjs`, e fai il push. In *Settings → Actions →
   General → Workflow permissions* scegli **Read and write permissions**: l'Action fa il commit di
   `ANALISI.md`.
2. **Token fine-grained, limitato a quel repository.** *GitHub → Settings → Developer settings →
   Personal access tokens → Fine-grained tokens → Generate new token*:
   - Repository access: **Only select repositories** → `bto-logs`
   - Permissions → Repository permissions → **Contents: Read and write** (il resto lascialo com'è:
     Metadata in sola lettura è obbligatorio)
   - scadenza: quella che preferisci (alla scadenza rifai il passo 4 con un token nuovo)
3. **Account Cloudflare.** Registrati gratis su <https://dash.cloudflare.com/sign-up>. Poi, da questa
   cartella:
   ```bash
   npm install
   npx wrangler login
   ```
   La prima volta il dashboard ti fa scegliere un sottodominio `*.workers.dev`.
4. **Il token nei secret del Worker** (non va mai nel codice né nell'exe):
   ```bash
   npx wrangler secret put GITHUB_TOKEN
   ```
   Incolla il token quando te lo chiede.
5. **Pubblica il Worker:**
   ```bash
   npx wrangler deploy
   ```
   Alla fine stampa l'indirizzo, del tipo `https://bto-logs.<tuo-sottodominio>.workers.dev`.
6. **BTO Sync:** metti quell'indirizzo in `sync/Core.cs`, `public const string LogsUrl = "…";`, poi
   compila e rilascia come al solito. Finché `LogsUrl` è vuoto BTO Sync non chiede e non invia niente, e
   l'interruttore in Impostazioni resta nascosto.

Limiti del Worker: 2 MB per invio (il log di una partita compresso pesa qualche decina di KB), 40 MB di
log una volta decompresso, 5 invii al minuto per IP (`[[ratelimits]]` in `wrangler.toml`). Accetta solo i
campi noti e li controlla uno a uno.

## Prova in locale

```bash
npm install
npm test
```

`npm test` avvia `wrangler dev` con un finto GitHub (`test/fake-github.mjs`, scrive in `test/out/`)
e manda log con Steam ID e nomi finti, non puliti. Controlla risposte, limiti e pulizia, poi lancia lo
script dell'Action sui file "committati" e verifica `ANALISI.md`. Per provare BTO Sync contro il Worker
locale, avvialo con la variabile d'ambiente `BTO_LOGS_URL=http://127.0.0.1:8787/`.

## ANALISI.md

A ogni push l'Action (`repo-template/.github/workflows/analisi.yml`) lancia `scripts/analisi.mjs`.
Per ogni cartella con almeno 2 log scrive `ANALISI.md` con:

- **primo frame di divergenza:** confronta in ordine le righe che devono essere uguali su ogni client
  (spawn, `SpawnRNG seed`, `Level::Init`, transizioni di stanza, righe `[BTO-DIAG]`…, senza orologio,
  fps e numero del controller). Mostra il frame e le due righe dove i log si separano;
- **mod e versioni** di ciascuno (⚠ su quelle diverse tra i giocatori);
- **errori Lua per mod**, per giocatore.
