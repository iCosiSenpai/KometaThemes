# AGENTS.md — KometaThemes Rules

## ⚠️ READ THIS FIRST — MANDATORY

### AI Agent — Absolute Constraints

**1. Every change MUST bump the version. No exceptions.**
- `Major.Minor.Build.Revision` (e.g., `2.0.0.0`)
- Bump the **Revision** (4th digit) for hotfixes, typos, small fixes
- Bump the **Build** (3rd digit) for new features, UI changes
- Bump the **Minor** (2nd digit) for significant new functionality
- Bump the **Major** (1st digit) for breaking changes
- The version is written in: `Directory.Build.props` (`Version`, `AssemblyVersion`, `FileVersion`),
  `Configuration/configPage.html` (`.release-badge` and the stylesheet `?v=`), `Web/kometa.js`
  (`VERSION`), `package.json` and `build.yaml`. `python3 tools/check_versions.py` fails when they
  disagree, and CI runs it.

**2. Every change MUST be committed and pushed to GitHub.**
- `git add -A && git commit -m "..." && git push origin main`
- Meaningful commit messages describing WHY, not just WHAT

**3. Every change MUST have a GitHub Release with changelog.**
- `gh release create vX.Y.Z.W KometaThemes.zip --repo iCosiSenpai/KometaThemes --title "..." --notes "..."`
- Changelog must list: Features, Fixes, Breaking changes
- Checksum: `md5sum` of the zip **downloaded back from the public release**, lowercase.

**4. Every change MUST update the manifest.**
- Edit `../iCosiSenpai-Plugins/manifest.json` — add the new version entry at the top of the
  KometaThemes `versions` array.
- Fields: `version`, `changelog` (Italian, like the other entries), `targetAbi` (`12.0.0.0`),
  `sourceUrl`, `checksum`, `timestamp`.
- The catalog lists only versions for Jellyfin 12 (2.x). Never put 1.x versions back: they target
  Jellyfin 10.11 and crash on 12.
- Commit + push the manifest repo separately.

**5. NEVER auto-deploy to the NAS / Jellyfin.**
- **DO NOT** run `docker cp`, `docker restart`, `curl` to the production Jellyfin, or any NAS-side install.
- The release flow ends at: push code → push release → push manifest → **STOP**.
- The user deploys from Dashboard → Plugins → Catalog when ready.
- Allowed: reading Jellyfin logs for debugging, and one disposable test Jellyfin as described below.

---

## Release flow

1. Bump the version everywhere (see 1.) and run `python3 tools/check_versions.py`.
2. `dotnet test -c Release` and `npm run test:browser` must pass.
3. Build against the oldest supported server and zip **exactly three files**:
   ```bash
   dotnet build Jellyfin.Plugin.KometaThemes/Jellyfin.Plugin.KometaThemes.csproj -c Release -p:JellyfinVersion=12.0.0
   out=Jellyfin.Plugin.KometaThemes/bin/Release/net10.0
   python3 tools/release_meta.py meta.json
   zip -j KometaThemes.zip "$out/Jellyfin.Plugin.KometaThemes.dll" "$out/YoutubeExplode.dll" meta.json
   ```
   - The Jellyfin packages are referenced with `ExcludeAssets=runtime`, so no server assembly lands
     in the output; still, never zip `"$out"/*.dll`.
   - `meta.json` carries the plugin's own name (`KometaThemes`): Jellyfin groups installed versions
     by that name, and a mismatch leaves two versions loaded after an update.
   - CI fails if the archive gains or loses a file.
4. `gh release create vX.Y.Z.W KometaThemes.zip --repo iCosiSenpai/KometaThemes ...`
5. Download the public asset without authentication and take its md5.
6. Add the version at the top of the catalog manifest; commit and push both repositories.
7. **STOP. Do not deploy. The user installs from the catalog.**

## Jellyfin

- Jellyfin **12.0 or newer only** (.NET 10, `Jellyfin.Controller`/`Jellyfin.Model` `$(JellyfinVersion)`,
  default 12.0.0, `targetAbi` 12.0.0.0). CI builds and tests against 12.0, 12.1 and 12.2.
- Production runs in container `jellyfin`; its config is `/volume1/docker/media/jellyfin/config`.
  Logs: `/volume1/docker/media/jellyfin/config/log/log_YYYYMMDD.log` (read only).
- Disposable test server: at most **one** container at a time from an image already on disk
  (no pull), loopback port (never 8096), `--cpus=1 --memory=1536m`, non-root user, data in the
  session's temporary folder (never under `/volume1`), removed right after, and recorded in
  `/volume1/docker/AGENT.md`. Never point a test at the production server.
- `POST /ScheduledTasks/Running/{key}` needs the task's GUID, not its `Key`: fetch
  `GET /ScheduledTasks`, filter by `Key`, then POST to `/ScheduledTasks/Running/{id}`.

## Code rules

- The frontend is vanilla JavaScript with no build step. Dynamic text goes through `textContent`
  only (names and titles come from animethemes.moe and the library). Colors come from Jellyfin's
  `--jf-palette-*` variables; text colors must pass WCAG AA in Dark and Light (the axe tests check).
- New pages or assets must be registered in `Plugin.GetPages()` and embedded by the `.csproj`.
- Only files recorded in a folder's `_kometa_themes.json` may be renamed or deleted. Never touch
  files the plugin did not write.
- `SeasonMatcher`, `ThemeCatalog` and `ThemePlanner` are pure: keep network and disk out of them,
  and cover changes with tests (real animethemes.moe fixtures live in the test project).
- Settings are saved through `POST /KometaThemes/Settings`, which never rewrites the exclusion
  and match lists; every server-side configuration write goes through `Plugin.MutateConfiguration`.

## Don't Touch
- Do not modify `komga` or `kometamanga` services.
- Do not modify other plugins without asking.
