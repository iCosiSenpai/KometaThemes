# Configuration reference

Everything here is edited on the plugin page (**Anime themes → Settings**). The values live in
Jellyfin's plugin configuration file; property names in brackets are the ones in that file.

## Libraries [`LibraryIds`]

The libraries KometaThemes manages. Other libraries are never read or written. After an upgrade
from 1.x the old name pattern (`LibraryPattern`) is turned into this list once.

## What to download

Separately for theme songs [`AudioSettings`] and theme videos [`VideoSettings`]:

| Setting | Default (songs / videos) | Meaning |
|---|---|---|
| Download [`FetchType`] | on / off | `None` off, `Single` only the main theme (first opening, or first ending when openings are off), `All` every theme. |
| Openings, Endings [`IgnoreOPs`, `IgnoreEDs`] | both | Which kinds. |
| Skip versions with episode scenes over the song [`IgnoreOverlapping`] | on / on | A version that only fades into the episode is used when there is no clean one. |
| Only creditless videos [`IgnoreThemesWithCredits`] | — / on | A theme with no creditless version gets no video. |
| Volume [`Volume`] | 50% | Baked into the file; changing it downloads the files again. |

| Setting | Default | Meaning |
|---|---|---|
| Each season gets its own themes [`PerSeasonThemes`] | on | Seasons are matched to the entries animethemes.moe groups with the series, by premiere date and episodes ([how](../README.md#how-seasons-are-matched)). Otherwise the season plays the series themes. |
| Most openings and most endings per folder [`MaxThemesPerSeason`] | 5 | Counted separately for openings and endings, and for songs and videos. |

When animethemes.moe lists the same song twice (an English dub `ED1-EN` next to `ED1`), only one is
kept. Among the versions of a song, a clean, creditless one without subtitles or lyrics burned into
the picture is preferred.

## Automatic work

| Setting | Default | Meaning |
|---|---|---|
| Add themes to new anime as they arrive [`AutoSyncOnItemAdded`] | on | Two minutes after Jellyfin adds an item, and again when its metadata is downloaded. |
| Delete an anime's themes when it leaves the library [`CleanupThemesOnItemRemoved`] | off | Only files KometaThemes wrote. |

The full check follows **Dashboard → Scheduled tasks → Check anime themes** (every 12 hours by default).

## YouTube

| Setting | Default | Meaning |
|---|---|---|
| Allow adding songs from YouTube links [`EnableYouTubeImport`] | off | Shows the YouTube form on anime pages. |
| yt-dlp path [`YtDlpPath`] | empty | Empty uses yt-dlp when installed, otherwise the bundled extractor. |

## Advanced

| Setting | Default | Range |
|---|---|---|
| IDs tried, in order [`ProviderPriority`] | AniDB, AniList, MyAnimeList, Kitsu | The sites animethemes.moe links anime to. |
| Match by title when no ID works [`EnableTitleFallback`] | on | |
| Title match confidence [`TitleMatchThreshold`] | 80% | 50–100% |
| Requests per minute to animethemes.moe [`RateLimitPerMinute`] | 60 | 1–90 |
| Downloads at once per anime [`DegreeOfParallelism`] | 2 | 1–4 |
| Conversion time limit [`DownloadTimeoutSeconds`] | 120 s | 15–600 s |
| Reuse matches for [`PositiveCacheTtlDays`] | 7 days | 1–365 |
| Remember misses for [`NegativeCacheTtlHours`] | 24 hours | 1–720 |

AniList is queried at most 25 times a minute, and only when animethemes.moe alone leaves a season
open. When AniList refuses requests, it is left alone for an hour.

## Lists

Exclusions [`SkippedItems`] and manual matches [`ManualBindings`] are edited from each anime's
page. A manual match can be set for a series, a single season or a movie.

## Files kept by the plugin

In Jellyfin's plugin configuration folder, `KometaThemes/`:

- `resolution-cache-v3.json`: animethemes.moe and AniList lookups (older versions are deleted).
- `failed-items.json`: anime that need attention, with when they are retried
  (1, 3, 7, then every 30 days when not found; 1, 6, 24 hours when downloads failed). After an
  upgrade that changes how anime are matched, every one of them is retried at the next check.
- `activity.json`: the Activity list.
