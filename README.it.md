<div align="center">
  <img src="assets/banner-readme-plugin.png" alt="KometaThemes per Jellyfin" width="100%" />

  # KometaThemes per Jellyfin

  [English](README.md) · **Italiano**

  [![Release GitHub](https://img.shields.io/github/v/release/iCosiSenpai/KometaThemes?style=flat-square&color=00a4dc)](https://github.com/iCosiSenpai/KometaThemes/releases)
  [![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11.x-7c5cff?style=flat-square)](https://jellyfin.org/)
  [![.NET](https://img.shields.io/badge/.NET-9.0-512bd4?style=flat-square)](https://dotnet.microsoft.com/)
  [![CI](https://img.shields.io/github/actions/workflow/status/iCosiSenpai/KometaThemes/ci.yml?branch=main&style=flat-square&label=build%20%26%20test)](https://github.com/iCosiSenpai/KometaThemes/actions/workflows/ci.yml)
  [![Licenza](https://img.shields.io/github/license/iCosiSenpai/KometaThemes?style=flat-square)](LICENSE)

  [![Repository GitHub](https://img.shields.io/badge/GitHub-KometaThemes-181717?style=for-the-badge&logo=github)](https://github.com/iCosiSenpai/KometaThemes)

  <a href="https://buymeacoffee.com/iCosiSenpai">
    <img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Offrimi un caffè su Buy Me a Coffee" height="50" />
  </a>
  &nbsp;&nbsp;
  <a href="https://www.paypal.com/donate/?hosted_button_id=5A4E26XC45GLQ">
    <img src="https://www.paypalobjects.com/en_US/i/btn/btn_donateCC_LG.gif" alt="Dona con PayPal" height="47" />
  </a>
</div>

KometaThemes trova e scarica automaticamente opening ed ending anime da [AnimeThemes](https://animethemes.moe/) per la tua libreria Jellyfin. Combina matching multi-provider, selezione consapevole delle stagioni, Theme Finder guidato, gestione per singolo elemento, download resilienti e interfaccia amministrativa in italiano e inglese.

> La documentazione è separata per lingua: GitHub mostra soltanto la lingua scelta. Usa **English · Italiano** in cima ai due README per cambiare immediatamente versione.

## In breve

- Scarica sigle OP/ED audio e video backdrop per serie e film.
- Risolve i titoli tramite AniDB, AniList, MyAnimeList, Kitsu e AniSearch, poi usa un fallback fuzzy per titolo.
- Gestisce anime multi-stagione e permette di scegliere il tema migliore, tutti i temi o tutti i temi per stagione.
- Cerca manualmente su AnimeThemes, riproduce anteprime, filtra i risultati e crea binding persistenti elemento-anime.
- Tiene traccia degli elementi non risolti ed esclusi senza ripetere richieste inutili.
- Ripara i collegamenti dei temi su Jellyfin 10.11.x e genera una playlist globale M3U.
- Offre un frontend responsive e accessibile in due lingue, senza una pipeline di build web separata.

## Requisiti

| Requisito | Dettagli |
|---|---|
| Jellyfin | `10.11.x`; ABI catalogo `10.11.8.0` |
| Runtime | .NET 9, incluso nella versione Jellyfin supportata |
| File Transformation | Opzionale; serve solo per il collegamento ♪ nelle pagine degli elementi |
| Rete | Accesso HTTPS ad AnimeThemes e ai provider di metadati abilitati |
| Permessi | Per configurazione e azioni del plugin serve un account amministratore |

## Installazione

### Metodo consigliato: Catalogo plugin Jellyfin

1. Apri **Dashboard → Plugin → Repository**.
2. Aggiungi questo URL:

   ```text
   https://raw.githubusercontent.com/iCosiSenpai/iCosiSenpai-Plugins/main/manifest.json
   ```

3. Apri **Catalogo**, seleziona **KometaThemes** e installa la versione più recente.
4. Riavvia Jellyfin quando richiesto, poi esegui un refresh completo del client (`Ctrl+Shift+R`).
5. Facoltativo: installa **File Transformation** dal catalogo Jellyfin per abilitare il collegamento ♪ nelle pagine degli elementi.

Gli aggiornamenti vengono distribuiti dallo stesso catalogo. Non serve copiare manualmente DLL nel container.

## Prima configurazione

1. Apri **Dashboard → Plugin → KometaThemes**.
2. In **Generale**, scegli la lingua UI e verifica il pattern del nome libreria. Il valore predefinito `Anime` limita attività automatiche e iniezione UI alle librerie corrispondenti.
3. In **Temi & Download**, scegli separatamente le modalità audio/video per serie e film.
4. In **Provider & Matching**, ordina i provider e controlla fallback per titolo e cache.
5. Usa **Sync ora** per un passaggio incrementale oppure **Forza sync** per rivalutare tutti gli elementi corrispondenti.
6. Nel profilo utente Jellyfin verifica **Impostazioni → Schermo → Riproduci sigle**.

## Flussi principali

### Sync automatico e manuale

KometaThemes può essere eseguito a intervalli pianificati, poco dopo l'aggiunta di un elemento a una libreria corrispondente oppure su richiesta. Il sync incrementale elabora soltanto elementi mancanti o non soddisfatti. Il sync forzato elimina i temi obsoleti e rivaluta tutto. La modalità dry-run risolve e registra nel log senza scrivere media.

### Theme Finder

Il Theme Finder segue un percorso guidato:

1. **Cerca** con titolo e anno Jellyfin, entrambi modificabili.
2. **Scegli un anime** tra match forti e risultati estesi, usando mouse o tastiera.
3. **Esamina i temi** per stagione, filtra Audio/Video e OP/ED, richiedi eventualmente versioni creditless, riproduci un'anteprima e usa selezioni singole o massive.
4. **Scarica** i temi scelti oppure salva soltanto il binding manuale per i sync automatici futuri.

Il collegamento ♪ è riservato agli amministratori e compare su serie/film idonei appartenenti a librerie compatibili con `Library Pattern`. Senza File Transformation, il Theme Finder resta disponibile dalla dashboard del plugin.

### Gestione per elemento

La pagina dell'elemento mostra temi scaricati, stato su disco/registrazione, file mancanti, binding manuali e preset libreria. Permette di:

- sincronizzare un singolo elemento;
- eliminare un tema o tutti i temi dell'elemento;
- aprire il Theme Finder;
- riparare i collegamenti staccati dopo una scansione Jellyfin;
- avviare preset in background su tutte le librerie corrispondenti.

### Non risolti, binding ed esclusioni

- **Non risolti** registra errori di matching e download, tentativi ed ultimo errore.
- **Binding** conserva associazioni manuali elemento-anime con priorità sul resolver automatico.
- **Esclusi** contiene elementi in blacklist, ignorati nelle ricerche future e ripristinabili.

## Riferimento configurazione

| Sezione | Contenuto |
|---|---|
| **Generale** | Lingua, filtro librerie, pianificazione, auto-sync, pulizia e notifiche |
| **Temi & Download** | Modalità media serie/film, volume, filtri OP/ED e crediti, stagioni, parallelismo, dry-run |
| **Provider & Matching** | Ordine provider, soglia fuzzy, rate API, TTL cache positiva/negativa e controlli cache |
| **Esclusi** | Blacklist e ripristino, configurazione playlist globale ed export M3U |
| **Binding** | Match manuali persistenti, sblocco/ricalcolo, rimozione opzionale dei file |
| **Non risolti** | Riprova, risoluzione manuale, blacklist, ignora e svuota lista |

### Modalità download

| Modalità | Comportamento |
|---|---|
| `None` | Non scarica questo tipo di media |
| `Single` | Scarica il miglior tema idoneo |
| `All` | Scarica tutti i temi idonei |
| `AllPerSeason` | Mantiene temi idonei raggruppati e nominati per stagione rilevata |

### Output tipico

```text
Cartella serie/
├── theme-music/
│   ├── OP1 - Guren no Yumiya__50.mp3
│   └── ED1 - Utsukushiki Zankoku na Sekai__50.mp3
└── backdrops/
    └── OP1 - Guren no Yumiya__0.webm
```

Il suffisso del volume viene generato per la riproduzione Jellyfin. La selezione dei media resta controllata dalla configurazione di ciascun tipo.

## Affidabilità e sicurezza

- Le API usano il token Jellyfin `MediaBrowser` corrente e credenziali same-origin.
- I media remoti sono accettati solo da HTTP(S) same-origin o domini AnimeThemes in HTTPS; URL con credenziali incorporate vengono bloccati.
- Le anteprime remote usano `no-referrer`; gli errori UI sono sanitizzati e limitati in lunghezza.
- I risultati di risoluzione sono salvati in cache JSON atomica con TTL positivi e negativi separati.
- Le richieste applicano rate limit, retry e circuit breaker.
- Le risposte asincrone obsolete vengono scartate quando la navigazione cambia contesto.
- Salvataggi, sync, eliminazioni e download impediscono invii duplicati.

## Accessibilità e frontend

Il frontend Jellyfin usa tre shell HTML leggere e asset JavaScript/CSS embedded. I moduli condivisi forniscono:

- caricamento sequenziale e versionato con errore visibile;
- navigazione delle tab con frecce, Home ed End;
- dialog di conferma con focus trap, Escape e ripristino del focus;
- regioni live, toast e stati operativi `aria-busy`;
- navigazione listbox e stato selezione accessibile nel Theme Finder;
- token responsive per temi scuri/chiari e contenimento dell'overflow.

La suite browser carica le vere shell embedded contro un fixture locale delle API Jellyfin. Playwright verifica i flussi critici; axe-core controlla violazioni WCAG A/AA serie e critiche.

## API REST

Tutti gli endpoint operativi richiedono un token Jellyfin elevato, salvo indicazione contraria.

| Endpoint | Metodo | Funzione |
|---|---:|---|
| `/Plugins/KometaThemes/Health` | GET | Versione, salute, metriche e riepilogo sync |
| `/Plugins/KometaThemes/Sync/status` | GET | Avanzamento sync live |
| `/Plugins/KometaThemes/Sync/sync` | POST | Avvia sync incrementale |
| `/Plugins/KometaThemes/Sync/force` | POST | Avvia sync forzato lato server |
| `/Plugins/KometaThemes/Sync/run` | POST | Avvia preset libreria |
| `/Plugins/KometaThemes/Items/{id}/info` | GET | Contesto elemento e registrazione temi |
| `/Plugins/KometaThemes/Items/{id}/sync` | POST | Sincronizza un elemento idoneo |
| `/Plugins/KometaThemes/Items/{id}/themes` | GET / DELETE | Elenca o elimina tutti i temi |
| `/Plugins/KometaThemes/Items/{id}/repair` | POST | Ripara i link tema Jellyfin |
| `/Plugins/KometaThemes/Search` | GET | Cerca candidati AnimeThemes |
| `/Plugins/KometaThemes/Anime/{id}/themes` | GET | Recupera temi e gruppi stagione |
| `/Plugins/KometaThemes/Bindings/{id}` | POST / DELETE | Salva o rimuove un binding manuale |
| `/Plugins/KometaThemes/Cache/stats` | GET | Statistiche cache di risoluzione |
| `/Plugins/KometaThemes/Cache/clear` | POST | Svuota la cache di risoluzione |
| `/Plugins/KometaThemes/Logs?lines=200` | GET | Legge le righe log del plugin |
| `/Plugins/KometaThemes/Playlist/refresh` | POST | Rigenera la playlist globale |
| `/Plugins/KometaThemes/Playlist/export` | GET | Scarica la playlist M3U |
| `/Plugins/KometaThemes/ItemButton.js` | GET | Script iniezione pulsante; risorsa anonima |

## Risoluzione problemi

<details>
<summary><strong>I temi vengono scaricati ma non riprodotti</strong></summary>

Apri la pagina KometaThemes dell'elemento e controlla il banner di registrazione. Avvia una scansione della libreria Jellyfin e poi usa **Ripara collegamenti**. Verifica inoltre **Impostazioni → Schermo → Riproduci sigle** nel profilo interessato: un aggiornamento Jellyfin può aver resettato questa opzione.
</details>

<details>
<summary><strong>Il collegamento ♪ non compare</strong></summary>

Installa e abilita File Transformation, riavvia Jellyfin e ricarica completamente il client. Il collegamento è visibile solo agli amministratori, sulle pagine di serie/film e quando la libreria proprietaria corrisponde a `Library Pattern`.
</details>

<details>
<summary><strong>Un elemento non viene mai risolto</strong></summary>

Apri **Non risolti** per riprovare oppure cercare e creare un binding manuale. Se il titolo non esiste su AnimeThemes, inseriscilo in blacklist. Prima di ridurre le protezioni del matching controlla ID provider, anno, soglia titolo e log attività live.
</details>

<details>
<summary><strong>L'intera interfaccia Jellyfin smette di funzionare dopo un aggiornamento plugin</strong></summary>

Controlla nel log Jellyfin se un plugin di iniezione web genera `ObjectDisposedException`. Può succedere quando un injector conserva un service provider dismesso durante il reload. Esegui un riavvio completo di Jellyfin dal tuo normale ambiente amministrativo e prova gli injector uno alla volta; non sostituire manualmente la DLL KometaThemes.
</details>

## Architettura

```text
Libreria Jellyfin
      │
      ▼
LibrarySelection ──► CompositeResolver ──► API AnimeThemes
 pattern/tipo           │ ID provider         │ temi + stagioni
 idoneità               └ fallback titolo     ▼
      │                                   Motore download
      │                                 ffmpeg + resilienza
      ▼                                         │
Sync item / scheduler ──────────────────────────┤
      │                                         ▼
      ├── JsonResolutionCache            File tema + repair
      ├── FailedItemsStore                      │
      ├── binding manuali                       ▼
      └── elementi esclusi              Playlist globale M3U
```

```text
Shell pagina Jellyfin
  └── kometa-loader.js
      ├── kometa-core.js       API, i18n, dialog, sync, anteprima
      ├── kometa-a11y.js       tab, stato busy, annunci
      ├── config.js
      ├── search.js
      └── item.js
```

## Sviluppo e validazione

Requisiti: SDK .NET 9, Node.js 20 e npm.

```bash
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
npm ci
npx playwright install chromium
npm run test:browser
```

Comandi mirati:

```bash
npm run test:e2e
npm run test:a11y
```

La CI esegue build/test .NET, test end-to-end Playwright, audit accessibilità axe, crea uno ZIP contenente soltanto la DLL e ne stampa l'MD5. Release e catalogo restano passaggi espliciti. Il deploy sul server Jellyfin viene eseguito intenzionalmente dall'amministratore tramite Catalogo plugin.

## Politica di rilascio

KometaThemes usa `Major.Minor.Build.Revision`. Modifiche UI e funzionalità incrementano Build; correzioni circoscritte incrementano Revision. Ogni versione pubblicata include:

1. versioni assembly e frontend sincronizzate;
2. build Release e test automatici;
3. `KometaThemes.zip` con sola DLL e checksum MD5;
4. GitHub Release con Funzionalità, Correzioni e Breaking change;
5. nuova voce in testa al manifest del catalogo Jellyfin.

## Crediti e licenza

- Metadati e media dei temi: [AnimeThemes](https://animethemes.moe/)
- Media server: [Jellyfin](https://jellyfin.org/)
- Autore e maintainer: [iCosiSenpai](https://github.com/iCosiSenpai)
- Licenza: [GNU GPL v3](LICENSE)

## Sostieni il progetto

KometaThemes è gratuito e open source. Se migliora la tua libreria, puoi sostenere lo sviluppo o contribuire su GitHub.

<div align="center">
  <a href="https://buymeacoffee.com/iCosiSenpai">
    <img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Offrimi un caffè" height="50" />
  </a>
  &nbsp;&nbsp;
  <a href="https://www.paypal.com/donate/?hosted_button_id=5A4E26XC45GLQ">
    <img src="https://www.paypalobjects.com/en_US/i/btn/btn_donateCC_LG.gif" alt="Dona con PayPal" height="47" />
  </a>
  <br /><br />
  <a href="https://github.com/iCosiSenpai/KometaThemes">
    <img src="https://img.shields.io/badge/GitHub-Apri%20KometaThemes-181717?style=for-the-badge&logo=github" alt="Apri la repository GitHub di KometaThemes" />
  </a>
</div>

Per bug e proposte apri una [issue](https://github.com/iCosiSenpai/KometaThemes/issues) includendo log anonimizzati, versione Jellyfin, versione plugin e passaggi riproducibili.
