# HTTP API

Every route lives under `/KometaThemes/` and needs an **administrator** session (the usual
`Authorization: MediaBrowser … Token="…"` header). Answers are camelCase JSON. Errors are
`{ "error": "…" }` with a sentence meant to be shown as it is.

The one exception is `GET /KometaThemes/ItemButton.js`: the static script of the ♪ button, anonymous
because the web client loads it before anyone signs in.

## State and settings

| Method | Route | Purpose |
|---|---|---|
| GET | `State` | Version, setup state, libraries, counts per status, check status, YouTube status. |
| GET | `Settings` | Every setting the page edits. |
| POST | `Settings` | Save settings. Never touches exclusions or manual matches. |
| POST | `Setup/Complete` | Mark the first-run setup as done. |
| POST | `WhatsNew/Dismiss` | Hide the 2.0 note. |
| GET | `Activity?limit=&itemId=` | Latest activity, newest first. |
| POST | `Cache/Clear` | Forget every lookup and make waiting anime due again. |

## The full check

| Method | Route | Purpose |
|---|---|---|
| GET | `Check` | Status: running, phase, done/total, downloads, current anime, last message. |
| POST | `Check` | Start it. Body `{ "retryProblems": true }` also retries anime that are waiting after a problem. `409` if already running. |
| DELETE | `Check` | Stop after the anime in progress. |

## Library and anime

`{itemId}` can be a series, a season, an episode or a movie: seasons and episodes resolve to their
series. Actions that act on one folder (match, songs, files, YouTube) take the series, season or
movie whose folder it is.

| Method | Route | Purpose |
|---|---|---|
| GET | `Library` | One summary per managed series or movie. |
| GET | `Items/{itemId}` | The anime page: summary, problem, and per folder the match, the songs with their state, and the files. |
| GET | `Items/{itemId}/Summary` | Whether the item is managed, without any network lookup (used by the ♪ button). |
| POST | `Items/{itemId}/Check?redownload=` | Bring this anime up to date now. |
| PUT | `Items/{itemId}/Match` | Body `{ "animeId": 1386 }`: match a series, season or movie to an animethemes.moe entry, then download. |
| DELETE | `Items/{itemId}/Match` | Back to the automatic match. |
| PUT | `Items/{itemId}/Excluded?deleteFiles=` | Exclude; optionally delete the files KometaThemes wrote. |
| DELETE | `Items/{itemId}/Excluded` | Manage it again. |
| PUT | `Items/{itemId}/Themes/{themeId}` | Body `{ "change": "audio", "audio": false }`: keep (`true`), keep out (`false`) or follow the settings (`null`). |
| DELETE | `Items/{itemId}/Files?directory=&name=` | Delete one file KometaThemes wrote; `404` for any other file. |
| GET | `Items/{itemId}/Search?q=` | animethemes.moe search, ranked against the item. |
| GET | `Anime/{animeId}` | An entry with its songs, for previews. |
| POST | `Items/{itemId}/YouTube` | Body `{ "url", "type": "OP"|"ED", "sequence", "title", "format": "audio"|"video"|"both" }`. |

Item actions return the updated anime page, so a client never needs a second request.
