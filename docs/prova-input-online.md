# Prova: le azioni sono sincronizzate in online?

Serve a decidere se gli oggetti di Astrobirth tolti da [BTO Astro Sync](../mods/bto-astro-sync/README.md)
si possono recuperare. La domanda è una sola: in online, quando una mod chiede a Lua "questo giocatore
sta premendo Drop?" (`Input.IsActionPressed(azione, player.ControllerIndex)`), la risposta è la stessa,
allo stesso frame, su tutti i PC? Oppure ogni PC vede solo i tasti di chi ci gioca sopra?

## Cosa serve

- Due PC con **BTO Sync 2.9.0 o più nuovo**, con le stesse mod (BTO Sync le allinea come sempre).
- Su **entrambi** la mod **BetterTBOIOnline Diagnostics 0.5 attiva**. È lei che scrive le righe
  `[BTO-DIAG] … pN input …` nel log. Chi non l'ha la trova proposta in BTO Sync.
- Python sul PC di chi confronta i log.

## Come si fa (5 minuti)

1. Avviate una partita online insieme, come al solito.
2. Nella prima stanza, **a turno**, ognuno per una decina di secondi:
   - cammina a sinistra e a destra, poi spara nelle 4 direzioni;
   - preme Drop (Ctrl o il tasto del controller) due o tre volte, e una volta lo tiene premuto per 2 secondi;
   - usa l'oggetto attivo e la carta/pillola, se le ha, e preme Mappa (Tab).
   Poi fate le stesse cose **tutti e due insieme** per altri dieci secondi.
3. Uscite dalla partita e **chiudete Isaac** (tutti e due).
4. **Prima di riaprire il gioco**, ognuno copia il proprio `log.txt` da
   `Documenti\My Games\Binding of Isaac Repentance+\` (a ogni avvio viene sovrascritto).
   L'amico manda il suo in privato (Discord): contiene i nomi Steam, quindi non va pubblicato.
5. Confronto, dalla cartella del progetto:

   ```bash
   python tools/input-sync-check.py log-mio.txt log-amico.txt
   ```

## Come leggere il risultato

Per ogni giocatore lo script mostra quanti frame con azioni premute ha visto ciascun PC, quanto
coincidono e con quale sfasamento, e quanto coincidono movimento e sparo del personaggio, che
servono da riferimento perché fanno parte della simulazione. In fondo c'è il verdetto:

| Verdetto | Cosa vuol dire | Cosa si può fare |
|---|---|---|
| **SINCRONIZZATE** | Lua legge le stesse azioni, allo stesso frame, su tutti i PC | recuperare gli oggetti a comando (P Key, Legacy, Astrophage, My Moon My Man, Overmind) e gli aggiornamenti con Drop, legandoli ad azioni del gioco e contando i frame di gioco |
| **SFASATE** | le azioni coincidono ma con qualche frame di differenza tra i PC (il PC locale le legge prima del ritardo di rete) | non usabili direttamente: per decidere si usano solo movimento e sparo del personaggio, con comandi più scomodi (es. doppio scatto) |
| **NON SINCRONIZZATE** | un PC non vede le azioni dell'altro giocatore | come sopra: solo movimento e sparo del personaggio |
| **INCERTO** | nessuno ha premuto niente, la partita si era già divisa, o i log non sono della stessa run | ripetere la prova |

Nel log ci deve essere anche `[bto-astro-sync] removed 17/17 items from the pools`: è la conferma che
Astro Sync riconosce la partita a 2 giocatori. Da soli scrive `alone: Astrobirth unchanged`.
