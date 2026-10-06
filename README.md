<div align="center">

<img src="Jellyfin.Plugin.KometaThemes/Web/assets/kometathemes-icon.png" alt="KometaThemes anime mascot" width="168" />

# KometaThemes

**Anime openings and endings on your Jellyfin series, season and movie pages.**

[![Latest release](https://img.shields.io/github/v/release/iCosiSenpai/KometaThemes?color=00a4dc&labelColor=171a2b)](https://github.com/iCosiSenpai/KometaThemes/releases/latest)
[![Build](https://img.shields.io/github/actions/workflow/status/iCosiSenpai/KometaThemes/ci.yml?branch=main&label=build&labelColor=171a2b)](https://github.com/iCosiSenpai/KometaThemes/actions/workflows/ci.yml)
![Jellyfin](https://img.shields.io/badge/Jellyfin-12.x-7c5cff?labelColor=171a2b)
[![License](https://img.shields.io/github/license/iCosiSenpai/KometaThemes?labelColor=171a2b)](LICENSE)

[Install](#installation) · [First run](#first-run) · [Everyday use](#everyday-use) · [Upgrading from 1.x](#upgrading-from-1x) · [Troubleshooting](docs/troubleshooting.md) · [API](docs/api.md)

</div>

KometaThemes finds each anime of your library on [animethemes.moe](https://animethemes.moe/),
downloads its openings and endings, and puts them where Jellyfin plays theme music and theme
videos. Open *Kaguya-sama* and its opening plays; open its second season and you hear the second
season's opening, not the first one's.

No account or API key is needed.

## What it does

- **Real songs, real names.** Files are named after the song, `OP1 - Love Dramatic feat. Rikka Ihara.mp3`,
  and tagged with title, artist and anime.
- **Every season its own themes.** A later season is matched to its own animethemes.moe entry by
  following the sequels on AniList, and only when the premiere year and the number of episodes
  agree. When the match is not certain the season plays the series themes: never the wrong song.
- **Matching that explains itself.** AniDB, AniList, MyAnimeList, Kitsu and aniSearch IDs first,
  then a careful title search. The anime page says how each match was made, and you can change it.
- **An episode map.** Each anime page shows which episodes every opening and ending plays in.
- **Songs, videos, or both.** Choose openings, endings, the main theme or all of them, creditless
  videos only, and the volume.
- **Per-song control.** Keep a song your settings would skip, or keep one out for good.
- **Hands off.** New anime are handled when Jellyfin has their metadata; a scheduled check picks
  up new songs. Anime that animethemes.moe does not have are retried less and less often.
- **Safe with your files.** KometaThemes only renames or deletes files it wrote itself, and keeps
  a record of them next to the media.
- **YouTube, when needed.** For a song animethemes.moe does not have, paste a YouTube link.
- **A ♪ button on item pages** shows the songs of that page and opens its KometaThemes page.

## Requirements

| | |
|---|---|
| **Jellyfin 12.0 or newer** | Built for .NET 10 and the Jellyfin 12 API. Jellyfin 10.x is not supported. |
| Administrator account | Every KometaThemes page and action is for administrators. |
| [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation) *(optional)* | Adds the ♪ button to item pages. Its 3.0.1 release needs Jellyfin 12.1 or newer. |
| yt-dlp *(optional)* | Used for YouTube imports when it is installed; otherwise the extractor inside the plugin is used. |

Jellyfin plays theme songs only when **Settings → Display → Theme songs** is on in the user's
profile, and theme videos only when **Theme videos** is on.

## Installation

1. In Jellyfin open **Dashboard → Plugins → Repositories** and add
   `https://raw.githubusercontent.com/iCosiSenpai/iCosiSenpai-Plugins/main/manifest.json`.
2. In **Catalog**, install **KometaThemes** and restart Jellyfin.
3. Optionally install **File Transformation** from its own repository for the ♪ button.

Install only through the catalog: Jellyfin keeps track of plugin versions it installed itself.

## First run

Open **Anime themes** in the dashboard sidebar. A new install asks three things:

1. **Libraries**: the libraries that hold your anime. Libraries with "anime" in the name are
   preselected; everything else is left alone.
2. **What to download**: songs (on by default), videos (off by default: 20–80 MB each), and whether
   seasons get their own themes.
3. **Start**: the first check looks up every anime. You can keep using Jellyfin meanwhile.

## Everyday use

**Library.** Every anime with its state: *Ready*, *Matched by you*, *Not checked yet*, *Excluded*,
or *Needs attention* when animethemes.moe has no entry for it. Filter and search the list.

**Anime page.** Click an anime to see:

- the entry it is matched to and how (for example *Matched by AniList ID 101921*, or *Matched as a
  sequel on AniList* for a season), with **Change match** to search animethemes.moe yourself;
- one tab for the series and one per season;
- the episode map and the list of songs, each with a preview and **Song** / **Video** buttons: a
  solid button is in the folder, a dashed one downloads at the next check, a struck one stays out;
- the files in each theme folder, including your own, which KometaThemes never touches;
- **Check again**, **Download again**, **Exclude**, and **Add a song from YouTube** when enabled.

**Activity.** What KometaThemes did recently, in plain words: downloads, renames, matches, problems.

**Settings.** Libraries, what to download, volume, automatic work, YouTube, and under *Advanced*
the order of the IDs tried, title matching, request budget, cache durations and time limits.

The full check runs on Jellyfin's schedule (**Dashboard → Scheduled tasks → Check anime themes**,
every 12 hours by default) and from **Check now**.

## Where files go

| Item | Songs | Videos |
|---|---|---|
| Series | `Series/theme-music/` | `Series/backdrops/` |
| Season with its own entry | `Series/Season 02/theme-music/` | `Series/Season 02/backdrops/` |
| Movie in its own folder | `Movie/theme-music/` | `Movie/backdrops/` |

Next to them, `_kometa_themes.json` records which files KometaThemes wrote. Movies that share a
folder with other movies get no themes: Jellyfin does not look for them there.

## Upgrading from 1.x

Install 2.0 from the catalog on Jellyfin 12. Your settings, exclusions and manual matches are kept,
and the libraries matched by the old name pattern become the selected libraries. At the next check:

- files are renamed to the song names (`OP0 - Op1__50.mp3` becomes `OP1 - Love Dramatic.mp3`),
  without downloading them again;
- the `theme.mp3` copies 1.x placed next to each series are removed, but only when they are
  byte-for-byte a song KometaThemes downloaded;
- songs 1.x put in the wrong season folder are removed, and seasons get their own when AniList
  confirms them;
- the playlist file, dry run, presets and log viewer of 1.x are gone; the Activity tab replaces the
  log viewer.

## More

- [Configuration reference](docs/configuration.md)
- [Troubleshooting](docs/troubleshooting.md)
- [HTTP API](docs/api.md)
- [Development and releases](docs/development.md)

Themes and metadata come from [animethemes.moe](https://animethemes.moe/) and
[AniList](https://anilist.co/). Please support them.

## License

GPL-3.0. See [LICENSE](LICENSE).
