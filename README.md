# BetterTBOIOnline

Strumenti per migliorare il co-op online di *The Binding of Isaac: Repentance+* (Steam, Windows).

Obiettivi:
1. Rientrare nella run dell'amico, con il proprio personaggio e i propri oggetti, dopo uno "sdoppiamento" della partita.
2. Mantenere attive online le mod compatibili con il multiplayer.

| Cartella | Contenuto |
|---|---|
| `diagnostics-mod/` | Mod Lua in sola lettura. Scrive righe `[BTO-DIAG]` in `log.txt`. |
| `mod-online-patcher/` | Reimplementazione futura della patch "mod online" (vuota per ora). |
| `docs/findings.md` | Risultati sperimentali: ambiente, log, ipotesi. |
| `docs/research-notes.md` | Studio delle tecniche: Isaac-Online-Modded, REPENTOGON, log di rete del gioco. |

## Installare il mod diagnostico

Crea un collegamento (junction) dalla cartella mod del gioco a questo repo:

```bat
mklink /J "F:\SteamLibrary\steamapps\common\The Binding of Isaac Rebirth\mods\bettertboionline-diagnostics" "%CD%\diagnostics-mod"
```

Per leggere l'output, cerca `BTO-DIAG` in `Documents\My Games\Binding of Isaac Repentance+\log.txt`
oppure in `online_logs\sessions\<data>\log.txt`.
