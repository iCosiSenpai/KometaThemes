# Development and releases

## Layout

| Folder | What |
|---|---|
| `Jellyfin.Plugin.KometaThemes/AnimeThemes`, `AniList`, `Http` | Remote APIs: client, retries without Polly, shared request budgets. |
| `Resolving` | Matching: manual matches, external IDs, title search, seasons through AniList sequels (`SeasonMatcher` is pure). |
| `Themes` | Which songs a folder should hold (`ThemeCatalog`, `ThemePlanner`, both pure), file names, the per-folder state file, the installer (download, ffmpeg, rename, prune). |
| `Library` | Managed libraries, folders of an item, the library summary, library events, Jellyfin refresh. |
| `Sync` | One-anime processing, the full check, problems with backoff, activity. |
| `Api` | The plugin's HTTP API and its DTOs. |
| `Web` | The page (`kometa.js`, `kometa.css`), the ♪ button (`item-button.js`), the File Transformation hook. |
| `Configuration` | Settings, migration from 1.x, the page shell. |

The frontend is plain JavaScript without a build step. Jellyfin loads `kometa.js` as an ES module
through `data-controller="__plugin/KometaThemesJs"`. It reads Jellyfin 12's `--jf-palette-*` CSS
variables, so it follows the user's theme.

## Build and test

Requires the .NET 10 SDK, Node 20 and ffmpeg (for the installer tests).

```bash
dotnet build -c Release
dotnet test -c Release
npm ci && npx playwright install chromium
npm run test:browser          # page, ♪ button, axe WCAG 2.1 AA in Dark and Light
```

Build against a newer server API with `-p:JellyfinVersion=12.2.0`. Releases are always built
against 12.0.0, the oldest supported server.

`tests/browser/server.js` serves the page in a shell that imitates Jellyfin's dashboard, with a
mock API fed by real animethemes.moe data from `Jellyfin.Plugin.KometaThemes.Tests/Fixtures`.
Run it with `node tests/browser/server.js` and open `http://127.0.0.1:4173/web/host.html`.

## Releases

See `AGENTS.md`: version in `Directory.Build.props`, `configPage.html`, `kometa.js` and
`package.json`; the zip holds `Jellyfin.Plugin.KometaThemes.dll`, `YoutubeExplode.dll` and
`meta.json`; every release goes to GitHub and to the catalog.
