<div align="center">
  <img src="assets/banner-readme-plugin.png" alt="KometaThemes for Jellyfin" width="100%" />

  # KometaThemes for Jellyfin

  **English** · [Italiano](README.it.md)

  [![GitHub Release](https://img.shields.io/github/v/release/iCosiSenpai/KometaThemes?style=flat-square&color=00a4dc)](https://github.com/iCosiSenpai/KometaThemes/releases)
  [![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11.x-7c5cff?style=flat-square)](https://jellyfin.org/)
  [![.NET](https://img.shields.io/badge/.NET-9.0-512bd4?style=flat-square)](https://dotnet.microsoft.com/)
  [![CI](https://img.shields.io/github/actions/workflow/status/iCosiSenpai/KometaThemes/ci.yml?branch=main&style=flat-square&label=build%20%26%20tests)](https://github.com/iCosiSenpai/KometaThemes/actions/workflows/ci.yml)
  [![License](https://img.shields.io/github/license/iCosiSenpai/KometaThemes?style=flat-square)](LICENSE)

  [![GitHub repository](https://img.shields.io/badge/GitHub-KometaThemes-181717?style=for-the-badge&logo=github)](https://github.com/iCosiSenpai/KometaThemes)

  <a href="https://buymeacoffee.com/iCosiSenpai">
    <img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Support iCosiSenpai on Buy Me a Coffee" height="50" />
  </a>
  &nbsp;&nbsp;
  <a href="https://www.paypal.com/donate/?hosted_button_id=5A4E26XC45GLQ">
    <img src="https://www.paypalobjects.com/en_US/i/btn/btn_donateCC_LG.gif" alt="Donate with PayPal" height="47" />
  </a>
</div>

KometaThemes automatically finds and downloads anime openings and endings from [AnimeThemes](https://animethemes.moe/) for your Jellyfin library. It combines multi-provider matching, season-aware selection, a guided Theme Finder, per-item controls, resilient downloads, and an English/Italian administration interface.

> The documentation is split by language so GitHub displays only the language you choose. Use **English · Italiano** at the top of either README to switch instantly.

## At a glance

- Download OP/ED audio themes and video backdrops for series and movies.
- Resolve titles through AniDB, AniList, MyAnimeList, Kitsu, and AniSearch before a fuzzy title fallback.
- Handle multi-season anime and select a single best theme, every theme, or every theme per season.
- Search AnimeThemes manually, preview media, filter results, and create persistent item-to-anime bindings.
- Track unresolved and excluded items without repeatedly querying known misses.
- Repair Jellyfin 10.11.x theme links and build a global M3U playlist.
- Operate from an accessible, responsive bilingual frontend with no separate web build step.

## Requirements

| Requirement | Details |
|---|---|
| Jellyfin | `10.11.x`; catalog ABI `10.11.8.0` |
| Runtime | .NET 9, provided by the supported Jellyfin release |
| File Transformation | Optional; required only for the ♪ shortcut on Jellyfin item detail pages |
| Network | HTTPS access to AnimeThemes and the metadata providers you enable |
| Permissions | An administrator account is required for configuration and plugin actions |

## Installation

### Recommended: Jellyfin Plugin Catalog

1. Open **Dashboard → Plugins → Repositories**.
2. Add this repository URL:

   ```text
   https://raw.githubusercontent.com/iCosiSenpai/iCosiSenpai-Plugins/main/manifest.json
   ```

3. Open **Catalog**, select **KometaThemes**, and install the latest version.
4. Restart Jellyfin when prompted, then hard-refresh the web client (`Ctrl+Shift+R`).
5. Optional: install **File Transformation** from the Jellyfin catalog to enable the ♪ item-page shortcut.

Updates are distributed through the same catalog. The project does not require copying DLL files into the container manually.

## First setup

1. Open **Dashboard → Plugins → KometaThemes**.
2. In **General**, set the UI language and verify the library name pattern. The default `Anime` limits automatic work and UI injection to matching libraries.
3. In **Themes & Download**, choose audio/video fetch modes separately for series and movies.
4. In **Providers & Matching**, arrange metadata providers and review title fallback and cache settings.
5. Use **Sync now** for an incremental run or **Force sync** when all matching items must be re-evaluated.
6. In your Jellyfin user profile, verify **Settings → Display → Play theme songs** is enabled.

## Main workflows

### Automatic and on-demand sync

KometaThemes can run on a schedule, shortly after a new item enters a matching library, or manually. Incremental sync only processes missing or unsatisfied items. Force sync removes outdated themes and performs a full re-evaluation. Dry-run mode resolves and logs without writing media.

### Theme Finder

The guided Theme Finder supports this workflow:

1. **Search** using the Jellyfin title and year, with editable inputs.
2. **Choose an anime** from strong and broad matches using pointer or keyboard navigation.
3. **Review themes** by season, filter Audio/Video and OP/ED, optionally require creditless media, preview a source, and select individual or bulk actions.
4. **Download** the chosen themes or save only the manual binding for future automatic syncs.

The ♪ shortcut is admin-only and appears on eligible series/movie pages whose library matches `Library Pattern`. Without File Transformation, the Theme Finder remains available from the plugin dashboard.

### Item management

The item page displays downloaded themes, disk/registration status, missing files, manual bindings, and library presets. Available actions include:

- sync one item;
- delete one theme or all themes for the item;
- open the Theme Finder;
- repair detached theme links after a Jellyfin library scan;
- launch background presets across matching libraries.

### Unresolved, bindings, and exclusions

- **Unresolved** records matching and download failures, including attempts and last error.
- **Bindings** stores manual item-to-anime associations and gives them priority over automatic resolution.
- **Excluded** contains items intentionally blacklisted from future matching; each can be restored later.

## Configuration reference

| Section | Purpose |
|---|---|
| **General** | Interface language, library filter, schedule, auto-sync, cleanup, and notifications |
| **Themes & Download** | Series/movie media modes, volume, OP/ED and credit filters, season behavior, download parallelism, dry-run |
| **Providers & Matching** | Provider order, fuzzy title threshold, API rate, positive/negative cache TTLs, cache controls |
| **Excluded** | Blacklisted items and restore controls, global playlist configuration and M3U export |
| **Bindings** | Persistent manual matches, unlock/recalculate, optional downloaded-file removal |
| **Unresolved** | Retry, manual resolution, blacklist, dismiss, and clear operations |

### Fetch modes

| Mode | Behavior |
|---|---|
| `None` | Do not download this media type |
| `Single` | Download the best eligible theme |
| `All` | Download all eligible themes |
| `AllPerSeason` | Keep eligible themes grouped and named per detected season |

### Typical output

```text
Series folder/
├── theme-music/
│   ├── OP1 - Guren no Yumiya__50.mp3
│   └── ED1 - Utsukushiki Zankoku na Sekai__50.mp3
└── backdrops/
    └── OP1 - Guren no Yumiya__0.webm
```

The volume suffix is generated for Jellyfin playback. Media selection remains controlled by the per-type configuration.

## Reliability and security

- API calls use the current Jellyfin `MediaBrowser` token and same-origin credentials.
- Remote media is accepted only from same-origin HTTP(S) or HTTPS AnimeThemes domains; credentials embedded in URLs are rejected.
- Remote previews use `no-referrer`, and UI error text is sanitized and length-limited.
- Resolution results are cached in atomic JSON files with separate positive and negative TTLs.
- Requests use rate limiting, retries, and circuit-breaker behavior.
- Stale async search, item, and detail responses are discarded when navigation changes context.
- Save, sync, delete, and download actions guard against duplicate submission.

## Accessibility and frontend

The Jellyfin frontend is composed of three small HTML shells and embedded JavaScript/CSS assets. Shared modules provide:

- sequential, versioned asset loading with visible failure handling;
- keyboard tab navigation with Arrow keys, Home, and End;
- focus-trapped confirmation dialogs with Escape and focus restoration;
- live status/toast regions and `aria-busy` operation states;
- Theme Finder listbox navigation and accessible selection state;
- responsive dark/light design tokens and reduced layout overflow.

The browser suite loads the real embedded shells against a local Jellyfin API fixture. Playwright covers critical flows, while axe-core checks WCAG A/AA serious and critical violations.

## REST API

All operational endpoints require an elevated Jellyfin token unless noted otherwise.

| Endpoint | Method | Purpose |
|---|---:|---|
| `/Plugins/KometaThemes/Health` | GET | Version, health, metrics, and current sync summary |
| `/Plugins/KometaThemes/Sync/status` | GET | Live sync progress |
| `/Plugins/KometaThemes/Sync/sync` | POST | Start an incremental sync |
| `/Plugins/KometaThemes/Sync/force` | POST | Start a server-side forced sync |
| `/Plugins/KometaThemes/Sync/run` | POST | Start a library preset |
| `/Plugins/KometaThemes/Items/{id}/info` | GET | Item and theme-registration context |
| `/Plugins/KometaThemes/Items/{id}/sync` | POST | Sync one eligible item |
| `/Plugins/KometaThemes/Items/{id}/themes` | GET / DELETE | List themes or delete all themes |
| `/Plugins/KometaThemes/Items/{id}/repair` | POST | Repair Jellyfin theme links |
| `/Plugins/KometaThemes/Search` | GET | Search AnimeThemes candidates |
| `/Plugins/KometaThemes/Anime/{id}/themes` | GET | Retrieve themes and season groups |
| `/Plugins/KometaThemes/Bindings/{id}` | POST / DELETE | Save or remove a manual binding |
| `/Plugins/KometaThemes/Cache/stats` | GET | Resolution-cache statistics |
| `/Plugins/KometaThemes/Cache/clear` | POST | Clear the resolution cache |
| `/Plugins/KometaThemes/Logs?lines=200` | GET | Read current plugin log entries |
| `/Plugins/KometaThemes/Playlist/refresh` | POST | Rebuild the global playlist |
| `/Plugins/KometaThemes/Playlist/export` | GET | Download the M3U playlist |
| `/Plugins/KometaThemes/ItemButton.js` | GET | Item-button injector script; anonymous resource |

## Troubleshooting

<details>
<summary><strong>Themes download but do not play</strong></summary>

Open the item's KometaThemes page and inspect its registration banner. Run a Jellyfin library scan, then use **Repair links**. Also verify **Settings → Display → Play theme songs** in the affected user's profile; Jellyfin upgrades may reset it.
</details>

<details>
<summary><strong>The ♪ shortcut does not appear</strong></summary>

Install and enable File Transformation, restart Jellyfin, and hard-refresh the client. The shortcut is visible only to administrators, only on series/movie pages, and only when the owning library matches `Library Pattern`.
</details>

<details>
<summary><strong>An item never resolves</strong></summary>

Open **Unresolved** to retry or search and bind it manually. If the title does not exist on AnimeThemes, blacklist it to prevent repeated searches. Review provider IDs, year, title threshold, and the live activity log before lowering matching safeguards.
</details>

<details>
<summary><strong>The entire Jellyfin web UI fails after a plugin update</strong></summary>

Check the Jellyfin log for web-injection plugins throwing `ObjectDisposedException`. This can occur when another injector retains a disposed service provider during plugin reload. Perform a full Jellyfin restart from your normal administration environment and test injectors one at a time; do not replace the KometaThemes DLL manually.
</details>

## Architecture

```text
Jellyfin library
      │
      ▼
LibrarySelection ──► CompositeResolver ──► AnimeThemes API
  pattern/type          │ provider IDs        │ themes + seasons
  eligibility           └ title fallback      ▼
      │                                   Download engine
      │                                 ffmpeg + resilience
      ▼                                         │
Item sync / scheduler ──────────────────────────┤
      │                                         ▼
      ├── JsonResolutionCache            Theme files + repair
      ├── FailedItemsStore                      │
      ├── manual bindings                       ▼
      └── excluded items                 Global M3U playlist
```

```text
Jellyfin page shells
  └── kometa-loader.js
      ├── kometa-core.js       API, i18n, dialogs, sync, preview
      ├── kometa-a11y.js       tabs, busy state, announcements
      ├── config.js
      ├── search.js
      └── item.js
```

## Development and validation

Requirements: .NET 9 SDK, Node.js 20, and npm.

```bash
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
npm ci
npx playwright install chromium
npm run test:browser
```

Useful focused commands:

```bash
npm run test:e2e
npm run test:a11y
```

CI runs the .NET build/tests, Playwright end-to-end tests, axe accessibility audit, creates a DLL-only ZIP, and prints its MD5. Releases and catalog updates remain explicit publication steps. Deployment to a Jellyfin server is intentionally performed by the administrator through the Plugin Catalog.

## Release policy

KometaThemes uses `Major.Minor.Build.Revision`. UI and feature work increments Build; focused fixes increment Revision. Every published version includes:

1. synchronized assembly and frontend versions;
2. Release build and automated tests;
3. a DLL-only `KometaThemes.zip` with an MD5 checksum;
4. a GitHub Release with Features, Fixes, and Breaking changes;
5. a new top entry in the Jellyfin catalog manifest.

## Credits and license

- Theme metadata and media: [AnimeThemes](https://animethemes.moe/)
- Media server: [Jellyfin](https://jellyfin.org/)
- Author and maintainer: [iCosiSenpai](https://github.com/iCosiSenpai)
- License: [GNU GPL v3](LICENSE)

## Support the project

KometaThemes is free and open source. If it helps your library, you can support ongoing development or contribute through GitHub.

<div align="center">
  <a href="https://buymeacoffee.com/iCosiSenpai">
    <img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy me a coffee" height="50" />
  </a>
  &nbsp;&nbsp;
  <a href="https://www.paypal.com/donate/?hosted_button_id=5A4E26XC45GLQ">
    <img src="https://www.paypalobjects.com/en_US/i/btn/btn_donateCC_LG.gif" alt="Donate with PayPal" height="47" />
  </a>
  <br /><br />
  <a href="https://github.com/iCosiSenpai/KometaThemes">
    <img src="https://img.shields.io/badge/GitHub-Open%20KometaThemes-181717?style=for-the-badge&logo=github" alt="Open the KometaThemes GitHub repository" />
  </a>
</div>

For bugs and feature requests, open an [issue](https://github.com/iCosiSenpai/KometaThemes/issues) with anonymized logs, your Jellyfin version, plugin version, and reproducible steps.
