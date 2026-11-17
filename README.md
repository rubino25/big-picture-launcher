# Big Picture Launcher

> **Progetto realizzato interamente con l'intelligenza artificiale, in *vibe coding*.** Codice, script e documentazione sono stati scritti da un'AI seguendo le indicazioni dell'autore, senza una revisione manuale riga per riga. Usalo sapendolo, a tuo rischio.

Launcher a schermo intero per Windows, pensato per usare il PC da un'altra stanza con il solo controller: libreria di giochi e applicazioni con copertine, gestione dei programmi aperti, mouse e tastiera dal controller, audio, schermo, spegnimento.

## Avvio

- `dist\BigPicture.exe` — schermo intero.
- `dist\BigPicture.exe "D:\Giochi"` — aggiunge la cartella alla libreria e parte.
- `dist\BigPicture.exe --windowed` — in finestra.

Non richiede installazioni (.NET Framework 4.8 è già in Windows 10/11).

## Comandi nel launcher

| Controller | Tastiera | Azione |
|---|---|---|
| Croce / levetta sinistra | Frecce | Muovi |
| A | Invio / Spazio | Avvia / Seleziona |
| B | Esc / Backspace | Indietro; sulla griglia chiede se chiudere il launcher |
| X | O | Opzioni del gioco |
| Y / Start | Tab / F1 | Menu |
| LB / RB | Q / E | Cambia sezione: Giochi ↔ Applicazioni (nei menu: pagina su / giù) |
| LT / RT | — | Volume giù / su |
| **Back + Start tenuti ½ s**, oppure Guide | — | Torna al launcher da qualsiasi situazione |

**Back** è il tastino a sinistra del logo Xbox (⧉), **Start** quello a destra (☰).

**Tasto Guide su Windows 11:** di norma apre la Game Bar. Per usarlo col launcher disattiva «Apri Game Bar con questo pulsante» in Impostazioni → Giochi → Game Bar, oppure usa Back+Start.

### Mouse

Clic seleziona, doppio clic avvia, clic destro apre le opzioni (o il menu, su uno spazio vuoto). I comandi in basso, le sezioni in alto e la ✕ sono cliccabili. Nei menu: clic su una voce la esegue; clic destro o clic fuori dal riquadro torna indietro. Usando il controller il puntatore sparisce; riappare muovendo il mouse.

### Chiudere il programma

Controller: B sulla griglia → «Chiudi». Mouse: la ✕ in alto a destra. Oppure Menu → Esci dal launcher, o Alt+F4.

## Quando avvii un gioco

- Il launcher va in **standby**: il controller comanda solo il gioco, quindi i tasti non fanno partire nient'altro. Lo schermo del launcher mostra «Avvio in corso…» finché il gioco non compare.
- Il launcher riconosce il gioco dai suoi processi: quello avviato, i processi figli e qualsiasi programma che parte dalla cartella del gioco (serve per Steam, Epic e gli avviatori).
- **Quando chiudi il gioco** il launcher torna in primo piano da solo.
- **Back + Start** (o Guide) mentre giochi riporta il launcher e apre:
  - Torna al gioco
  - Chiudi il gioco (come cliccare la X)
  - Termina forzatamente (se è bloccato; chiede conferma)
  - Modalità desktop
  - Resta nel launcher (il gioco resta aperto; in alto compare «▶ … in esecuzione», cliccabile)
- Se con un gioco aperto provi ad avviarne un altro, il launcher chiede cosa fare invece di partire.

Limite: per i siti web e alcune app di Windows non si può seguire il processo; lo standby finisce quando torni al launcher.

## Menu (Y)

- **Programmi aperti**: elenco delle finestre aperte. Per ognuna: vai alla finestra, vai con mouse dal controller, chiudi, termina forzatamente (non disponibile per i componenti di Windows come Esplora risorse).
- **Modalità desktop**: il controller diventa mouse e tastiera, per tutto ciò che il launcher non gestisce (login di Steam, avvisi, installer, browser). Vedi sotto.
- **Audio**: volume, muto, scelta dell'uscita (TV/HDMI, cuffie, casse).
- **Schermo**: solo schermo principale, duplica, estendi, solo secondo schermo, risoluzione. **Ogni modifica si annulla da sola dopo 15 secondi** se non premi «Mantieni»: se l'immagine sparisce dalla TV basta aspettare.
- **Driver video** (solo con scheda Nvidia): versione installata, ultima versione disponibile, «Aggiorna con NVIDIA app» oppure «Apri pagina del driver». Entrambe aprono il programma e passano in modalità desktop per comandarlo dal controller. Il launcher controlla da solo 15 secondi dopo l'avvio e poi ogni 6 ore; se c'è una nuova versione lo segnala in alto e con un avviso (una volta per versione). A Nvidia vengono inviati solo l'ID del modello di scheda e la versione di Windows. Il controllo automatico si disattiva dallo stesso menu.
- **Aggiungi alla libreria**: cartella di giochi, singolo gioco, applicazione dal menu Start, sito web (Netflix, YouTube…), applicazione da file.
- Aggiorna libreria, Ordina per, Impostazioni, Spegnimento (sospendi, riavvia, spegni), Esci.

## Modalità desktop

Una striscia in alto ricorda i comandi. **Back + Start** per tornare al launcher.

| Controller | Azione |
|---|---|
| Levetta destra | Puntatore (più la spingi, più è veloce) |
| RT oppure A | Clic sinistro (tieni premuto per trascinare) |
| LT | Clic destro |
| Levetta sinistra | Scorri |
| Croce | Frecce |
| X / B | Invio / Esc |
| RB / LB | Tab / Alt+Tab |
| Start | Tasto Windows |
| Y | Tastiera a schermo |

Tastiera a schermo: croce per muoversi, A scrive, X cancella, Y spazio, LB maiuscola, Start Invio, B la chiude. I tasti si possono anche cliccare col puntatore. Scrive nel programma in primo piano.

Limite: Windows non permette di comandare le finestre «Esegui come amministratore» e le richieste di conferma UAC.

## Libreria

- Due sezioni: **Giochi** e **Applicazioni** (LB/RB o clic sul titolo). X → «Sposta in Applicazioni/Giochi» per correggere.
- **Steam, Epic Games e GOG** vengono letti in automatico (si disattivano da Impostazioni).
- **Cartella di giochi**: ogni sottocartella diventa un gioco; valgono anche `.exe`, `.lnk` e `.url` messi direttamente nella cartella.
- L'eseguibile principale è scelto in automatico; se è sbagliato: X → «Cambia eseguibile».
- X → «Rinomina», «Cerca copertina…», «Nascondi dalla libreria» (si ripristinano da Impostazioni → Giochi nascosti).

## Copertine

Ordine di ricerca:
1. `cover.jpg|png` (o `poster`, `folder`, `boxart`, `box`) nella cartella del gioco.
2. `dist\data\covers\<Nome>.png|jpg`.
3. Per i giochi Steam: la copertina già scaricata dal client Steam.
4. Download dallo store di Steam (per nome, o per ID se il gioco è di Steam).
5. SteamGridDB, se è impostata la chiave API (gratuita su steamgriddb.com → Preferences → API).
6. Se non si trova nulla: icona dell'exe + nome. Le applicazioni usano sempre la propria icona, salvo «Cerca copertina…».

Ai servizi online viene inviato solo il nome (o l'ID Steam) del gioco. Si disattiva da Impostazioni → Copertine online.

## Stato in alto

Batteria del controller (se senza fili), rete, volume, gioco in esecuzione, promemoria Back+Start, ora.

## Uso dall'altra stanza: cose da sistemare in Windows

- **Accesso automatico** a Windows, altrimenti all'accensione serve la tastiera per la password.
- **Impostazioni → Avvio con Windows** nel launcher.
- Per accendere il PC dalla sala: Wake-on-LAN, oppure accensione da tastiera/USB nel BIOS.

## Dati

Tutto in `dist\data\`: `config.json` (cartelle, scelte, nascosti, chiave SteamGridDB), `covers\`, `apps\` (collegamenti ai siti web), `error.log`.

## Compilare

```
powershell -ExecutionPolicy Bypass -File build.ps1
```

## Limiti noti

- Solo controller XInput (Xbox e compatibili). I pad PlayStation funzionano tramite Steam Input o DS4Windows.
- Finestre di amministratore e UAC non comandabili dal controller.

## Licenza

[PolyForm Noncommercial 1.0.0](LICENSE.md): uso, modifica e ridistribuzione consentiti solo per scopi non commerciali.
