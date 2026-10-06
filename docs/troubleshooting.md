# Troubleshooting

## Themes are downloaded but do not play

1. In the user's profile, **Settings → Display**: turn on **Theme songs** (and **Theme videos**).
2. Open the anime page in KometaThemes and check that the songs show a solid **Song** button and
   appear under *Files in this folder*.
3. Run **Check again** on that anime: it also asks Jellyfin to register the files.

## An anime says "Needs attention"

animethemes.moe has no entry for its IDs or title. Open it and use **Find a match**: search by the
Japanese or English title and pick the entry whose year and format match. Not every anime is on
animethemes.moe; for those, enable YouTube import and add the song from a link.

KometaThemes retries such anime by itself after 1, 3, 7 and then every 30 days, and at once if
the anime's titles or IDs change in Jellyfin.

## A season plays the series themes

That season could not be matched with confidence: AniList has no sequel whose premiere year and
episode count agree with the season in your library. The season tab says why. Use **Find a match**
on that tab to choose its entry yourself.

## The wrong anime was matched

Use **Change match** on the series, season or movie tab. The files of the old match are replaced at
once. **Use the automatic match** removes your choice again.

## The ♪ button is missing on item pages

- It needs the **File Transformation** plugin. Its 3.0.1 release only runs on Jellyfin 12.1 or newer.
- It shows only to administrators, and only on anime in the libraries KometaThemes manages.
- After installing File Transformation, restart Jellyfin and reload the web client (`Ctrl+Shift+R`).
- Without File Transformation, the same button can be added with the JavaScript Injector plugin by
  loading `../KometaThemes/ItemButton.js`.

## A YouTube import fails

YouTube changes often. If yt-dlp is installed it is used and usually keeps up; otherwise the bundled
extractor is updated with each KometaThemes release. Only videos up to 30 minutes and 512 MB are accepted.

## Where to look

- **Anime themes → Activity**: what happened, in plain words.
- Jellyfin's log (**Dashboard → Logs**): lines from `Jellyfin.Plugin.KometaThemes`.
