// A stateful stand-in for the KometaThemes 2.0 API, shaped like the C# DTOs (camelCase).
// Theme data comes from real animethemes.moe responses saved as test fixtures.
const fs = require('node:fs');
const path = require('node:path');

const fixtures = path.resolve(__dirname, '..', '..', 'Jellyfin.Plugin.KometaThemes.Tests', 'Fixtures');
const load = (name) => JSON.parse(fs.readFileSync(path.join(fixtures, name + '.json'), 'utf8')).anime;

const KAGUYA = 'a0000000-0000-0000-0000-000000000001';
const KAGUYA_S1 = 'a0000000-0000-0000-0000-000000000011';
const KAGUYA_S2 = 'a0000000-0000-0000-0000-000000000012';
const FRIEREN = 'a0000000-0000-0000-0000-000000000002';
const UNRESOLVED = 'a0000000-0000-0000-0000-000000000003';

const TITLES = [
  'Akira', 'Bocchi the Rock!', 'Chainsaw Man', 'Cowboy Bebop', 'Death Note', 'Dungeon Meshi', 'Frieren: Beyond Journey’s End',
  'Fullmetal Alchemist: Brotherhood', 'Haikyu!!', 'Jujutsu Kaisen', 'Kaguya-sama: Love Is War', 'K-On!', 'Made in Abyss',
  'Mob Psycho 100', 'Mushishi', 'Neon Genesis Evangelion', 'Oshi no Ko', 'Ping Pong the Animation', 'Spy x Family',
  'Steins;Gate', 'The Apothecary Diaries', 'Violet Evergarden', 'Your Lie in April', 'Yuru Camp'
];

function id(n) {
  return 'b0000000-0000-0000-0000-' + String(n).padStart(12, '0');
}

function baseLibrary() {
  return TITLES.map((name, index) => {
    if (name.startsWith('Kaguya')) {
      return { id: KAGUYA, name, year: 2019, type: 'Series', seasons: 4, seasonsWithOwnThemes: 3, openings: 4, endings: 6, status: 'ready', detail: null, manual: false, checkedUtc: hoursAgo(2) };
    }
    if (name.startsWith('Frieren')) {
      return { id: FRIEREN, name, year: 2023, type: 'Series', seasons: 2, seasonsWithOwnThemes: 1, openings: 3, endings: 4, status: 'ready', detail: null, manual: true, checkedUtc: hoursAgo(2) };
    }
    if (name === 'Ping Pong the Animation') {
      return { id: UNRESOLVED, name: 'Il prisma dell’amore', year: 2026, type: 'Series', seasons: 1, seasonsWithOwnThemes: 0, openings: 0, endings: 0, status: 'attention', detail: 'no entry on animethemes.moe for its IDs or title', manual: false, checkedUtc: null };
    }
    if (name === 'Akira') {
      return { id: id(index), name, year: 1988, type: 'Movie', seasons: 0, seasonsWithOwnThemes: 0, openings: 1, endings: 1, status: 'ready', detail: null, manual: false, checkedUtc: hoursAgo(5) };
    }
    if (name === 'Mushishi') {
      return { id: id(index), name, year: 2005, type: 'Series', seasons: 3, seasonsWithOwnThemes: 0, openings: 0, endings: 0, status: 'pending', detail: null, manual: false, checkedUtc: null };
    }
    if (name === 'Yuru Camp') {
      return { id: id(index), name, year: 2018, type: 'Series', seasons: 3, seasonsWithOwnThemes: 2, openings: 3, endings: 3, status: 'excluded', detail: null, manual: false, checkedUtc: hoursAgo(30) };
    }
    return { id: id(index), name, year: 2000 + (index % 24), type: 'Series', seasons: 1 + (index % 3), seasonsWithOwnThemes: index % 3, openings: 1 + (index % 4), endings: 1 + (index % 3), status: 'ready', detail: null, manual: false, checkedUtc: hoursAgo(2) };
  });
}

function hoursAgo(hours) {
  return new Date(Date.now() - hours * 3600 * 1000).toISOString();
}

function ranges(episodes) {
  if (!episodes) return [];
  const parsed = episodes.split(',').map((part) => part.trim()).filter(Boolean).map((part) => {
    const [start, end] = part.split('-').map(Number);
    return [start, end || start];
  }).sort((a, b) => a[0] - b[0]);
  const merged = [];
  for (const range of parsed) {
    const last = merged[merged.length - 1];
    if (last && range[0] <= last[1] + 1) last[1] = Math.max(last[1], range[1]);
    else merged.push(range.slice());
  }
  return merged;
}

// Best version per theme: no spoiler, creditless, no overlap, first version.
function themesOf(anime, saved) {
  return anime.animethemes.filter((t) => t.type === 'OP' || t.type === 'ED').map((theme) => {
    let best = null;
    let bestScore = Infinity;
    for (const entry of theme.animethemeentries) {
      for (const video of entry.videos) {
        const score = (entry.spoiler ? 50 : 0) + (video.nc ? 0 : 10) + (video.overlap === 'None' ? 0 : 15) + ((entry.version || 1) - 1);
        if (score < bestScore) {
          bestScore = score;
          best = { entry, video };
        }
      }
    }
    const episodes = theme.animethemeentries.map((e) => e.episodes).filter(Boolean).join(', ') || null;
    const code = /^(OP|ED)\d/.test(theme.slug) ? theme.slug : theme.type + (theme.sequence || 1);
    const title = theme.song && theme.song.title;
    const artists = theme.song ? theme.song.artists.map((a) => a.name).join(', ') : '';
    const has = saved(theme);
    return {
      themeId: theme.id,
      animeId: anime.id,
      code,
      type: theme.type,
      title,
      artists,
      episodes,
      ranges: ranges(episodes),
      audio: { wanted: has.audio !== false, bySettings: true, choice: null, file: has.audio === false ? null : `${code} - ${title}.mp3`, preview: best.video.audio.link },
      video: { wanted: has.video !== false, bySettings: true, choice: null, file: has.video ? `${code} - ${title}.webm` : null, preview: best.video.link },
      creditless: best.video.nc,
      overlap: best.video.overlap,
      source: best.video.source || 'Unknown',
      resolution: best.video.resolution,
      spoiler: best.entry.spoiler,
      nsfw: best.entry.nsfw
    };
  });
}

function animeDto(anime, score) {
  return {
    id: anime.id, name: anime.name, slug: anime.slug, year: anime.year, season: anime.season, format: anime.media_format,
    cover: null, link: 'https://animethemes.moe/anime/' + anime.slug,
    themes: anime.animethemes.filter((t) => t.type === 'OP' || t.type === 'ED').length, score: score === undefined ? null : score
  };
}

function filesOf(themes, extra) {
  const files = [];
  for (const theme of themes) {
    if (theme.audio.file) files.push({ directory: 'theme-music', fileName: theme.audio.file, source: 'animethemes', title: theme.title, artists: theme.artists, code: theme.code, size: 3400000 });
    if (theme.video.file) files.push({ directory: 'backdrops', fileName: theme.video.file, source: 'animethemes', title: theme.title, artists: theme.artists, code: theme.code, size: 42000000 });
  }
  return files.concat(extra || []);
}

class MockApi {
  constructor() {
    this.reset({});
  }

  reset(options) {
    this.kaguya1 = load('kaguya-s1');
    this.kaguya2 = load('kaguya-s2');
    this.frieren = load('frieren');
    this.library = baseLibrary();
    this.setupRequired = Boolean(options.setup);
    this.whatsNew = !this.setupRequired && options.whatsNew !== false;
    this.check = { running: false, phase: 'idle', trigger: null, total: 0, done: 0, downloaded: 0, failed: 0, current: null, startedUtc: null, finishedUtc: hoursAgo(2), message: 'Checked 24 anime: 9 file(s) downloaded, 1 need attention' };
    this.overrides = {};
    this.matches = {};
    this.excluded = new Set(['b0000000-0000-0000-0000-000000000023']);
    this.settings = {
      libraryIds: this.setupRequired ? [] : ['c0000000-0000-0000-0000-000000000001'],
      audio: { enabled: true, amount: 'all', openings: true, endings: true, skipOverlaps: true, creditlessOnly: false, volume: 50 },
      video: { enabled: !this.setupRequired, amount: 'all', openings: true, endings: true, skipOverlaps: true, creditlessOnly: true, volume: 50 },
      maxPerFolder: 5, perSeason: true, autoOnAdd: true, cleanupOnRemove: false, titleFallback: true, titleThreshold: 80,
      providerPriority: ['AniDB', 'AniList', 'MyAnimeList', 'Kitsu', 'AniSearch'], rateLimit: 60, matchCacheDays: 7, missCacheHours: 24,
      convertTimeout: 120, parallel: 2, youTube: true, ytDlpPath: null
    };
    this.activity = [
      { timeUtc: hoursAgo(2), kind: 'Checked', itemId: null, itemName: null, message: 'Checked 24 anime: 9 file(s) downloaded, 1 need attention' },
      { timeUtc: hoursAgo(2.1), kind: 'Downloaded', itemId: KAGUYA, itemName: 'Kaguya-sama: Love Is War', message: 'Downloaded 5 file(s): DADDY! DADDY! DO! feat. Airi Suzuki, Kaze ni Fukarete, GIRI GIRI, Heart wa Oteage' },
      { timeUtc: hoursAgo(2.2), kind: 'Renamed', itemId: KAGUYA, itemName: 'Kaguya-sama: Love Is War', message: 'Renamed 3 file(s) after their songs' },
      { timeUtc: hoursAgo(2.3), kind: 'Removed', itemId: KAGUYA, itemName: 'Kaguya-sama: Love Is War', message: 'Removed 3 file(s) that no longer belong here' },
      { timeUtc: hoursAgo(26), kind: 'NotFound', itemId: UNRESOLVED, itemName: 'Il prisma dell’amore', message: 'No match on animethemes.moe. Find it by hand from its page.' }
    ];
  }

  counts() {
    const rows = this.libraryRows();
    return {
      total: rows.length,
      ready: rows.filter((r) => r.status === 'ready').length,
      attention: rows.filter((r) => r.status === 'attention' || r.status === 'failed').length,
      pending: rows.filter((r) => r.status === 'pending').length,
      empty: rows.filter((r) => r.status === 'empty').length,
      excluded: rows.filter((r) => r.status === 'excluded').length,
      manual: rows.filter((r) => r.manual).length
    };
  }

  libraryRows() {
    return this.library.map((row) => {
      const copy = Object.assign({}, row);
      if (this.excluded.has(row.id)) copy.status = 'excluded';
      if (this.matches[row.id]) {
        copy.manual = true;
        if (copy.status === 'attention') {
          copy.status = 'ready';
          copy.openings = 1;
          copy.endings = 2;
        }
      }
      return copy;
    });
  }

  state() {
    return {
      version: '2.0.0.0',
      setupRequired: this.setupRequired,
      whatsNew: this.whatsNew,
      libraries: [
        { id: 'c0000000-0000-0000-0000-000000000001', name: 'Anime', collectionType: 'tvshows', selected: this.settings.libraryIds.includes('c0000000-0000-0000-0000-000000000001') },
        { id: 'c0000000-0000-0000-0000-000000000002', name: 'Anime Movies', collectionType: 'movies', selected: this.settings.libraryIds.includes('c0000000-0000-0000-0000-000000000002') },
        { id: 'c0000000-0000-0000-0000-000000000003', name: 'TV Shows', collectionType: 'tvshows', selected: this.settings.libraryIds.includes('c0000000-0000-0000-0000-000000000003') },
        { id: 'c0000000-0000-0000-0000-000000000004', name: 'Music', collectionType: 'music', selected: false }
      ],
      counts: this.counts(),
      sync: this.tick(),
      lastCheck: { utc: this.check.finishedUtc, summary: this.check.message },
      youTube: { enabled: this.settings.youTube, backend: 'bundled', available: true, error: '' }
    };
  }

  tick() {
    if (this.check.running) {
      this.check.done = Math.min(this.check.total, this.check.done + 3);
      this.check.phase = 'downloading';
      this.check.current = TITLES[this.check.done % TITLES.length];
      if (this.check.done >= this.check.total) {
        Object.assign(this.check, { running: false, phase: 'idle', current: null, finishedUtc: new Date().toISOString(), message: 'Checked 24 anime: 2 file(s) downloaded, 1 need attention' });
      }
    }
    return Object.assign({}, this.check);
  }

  folder(itemId, label, seasonNumber, episodes, year, anime, match, savedFn) {
    const themes = anime ? themesOf(anime, savedFn || (() => ({ audio: true, video: true }))) : [];
    for (const theme of themes) {
      const key = itemId + ':' + theme.themeId;
      for (const media of ['audio', 'video']) {
        const choice = (this.overrides[key] || {})[media];
        if (choice !== undefined && choice !== null) {
          theme[media].choice = choice;
          theme[media].wanted = choice;
          theme[media].file = choice ? `${theme.code} - ${theme.title}.${media === 'audio' ? 'mp3' : 'webm'}` : null;
        }
      }
    }
    return { itemId, label, seasonNumber, episodes, year, match, themes, files: filesOf(themes, seasonNumber === 0 && itemId === KAGUYA ? [{ directory: 'theme-music', fileName: 'My favourite remix.mp3', source: 'yours', title: null, artists: null, code: null, size: 5100000 }] : []), checkedUtc: hoursAgo(2) };
  }

  detail(itemId) {
    const row = this.libraryRows().find((r) => r.id === itemId) || this.libraryRows().find((r) => itemId.startsWith(r.id.slice(0, 30)));
    if (!row) return null;
    const summary = row;
    let folders;
    let problem = null;
    if (row.id === KAGUYA) {
      folders = [
        this.folder(KAGUYA, 'Series', 0, 0, 2019, this.kaguya1, { state: 'matched', method: 'AniList ID 101921', manual: false, anime: [animeDto(this.kaguya1)] }),
        this.folder(KAGUYA_S1, 'Season 1', 1, 12, 2019, null, { state: 'inherit', method: 'plays the series themes', manual: false, anime: [] }),
        this.folder(KAGUYA_S2, 'Season 2', 2, 12, 2020, this.kaguya2, { state: 'matched', method: 'sequel on AniList, aired 2020, 12 episodes', manual: false, anime: [animeDto(this.kaguya2)] }, () => ({ audio: true, video: false }))
      ];
    } else if (row.id === FRIEREN) {
      folders = [
        this.folder(FRIEREN, 'Series', 0, 0, 2023, this.frieren, { state: 'matched', method: 'matched by you', manual: true, anime: [animeDto(this.frieren)] }, (t) => ({ audio: true, video: t.type === 'OP' }))
      ];
    } else if (row.id === UNRESOLVED) {
      problem = { reason: 'Unresolved', error: 'no entry on animethemes.moe for its IDs or title', attempts: 3, nextAttemptUtc: new Date(Date.now() + 6 * 86400000).toISOString() };
      folders = [this.folder(row.id, 'Series', 0, 0, row.year, this.matches[row.id] ? this.kaguya1 : null,
        this.matches[row.id]
          ? { state: 'matched', method: 'matched by you', manual: true, anime: [animeDto(this.kaguya1)] }
          : { state: 'notFound', method: 'no entry on animethemes.moe for its IDs or title', manual: false, anime: [] })];
      if (this.matches[row.id]) problem = null;
    } else {
      folders = [this.folder(row.id, row.type === 'Movie' ? 'Movie' : 'Series', 0, 0, row.year, this.frieren, { state: 'matched', method: 'AniDB ID 17617', manual: false, anime: [animeDto(this.frieren)] })];
    }
    return { id: row.id, name: row.name, year: row.year, type: row.type, summary, problem, folders, note: null };
  }

  handle(method, route, query, body) {
    const parts = route.split('/').filter(Boolean);
    const json = (data, status) => ({ status: status || 200, data });
    const noContent = () => ({ status: 204, data: null });

    if (method === 'GET' && route === 'State') return json(this.state());
    if (method === 'GET' && route === 'Settings') return json(this.settings);
    if (method === 'POST' && route === 'Settings') {
      this.settings = Object.assign({}, this.settings, body);
      return json(this.settings);
    }
    if (method === 'POST' && route === 'Setup/Complete') {
      this.setupRequired = false;
      return noContent();
    }
    if (method === 'POST' && route === 'WhatsNew/Dismiss') {
      this.whatsNew = false;
      return noContent();
    }
    if (route === 'Check') {
      if (method === 'GET') return json(this.tick());
      if (method === 'POST') {
        if (this.check.running) return json({ error: 'A check is already running.' }, 409);
        Object.assign(this.check, { running: true, phase: 'matching', trigger: 'manual', total: 24, done: 0, downloaded: 0, failed: 0, startedUtc: new Date().toISOString() });
        return json(this.check);
      }
      if (method === 'DELETE') {
        Object.assign(this.check, { running: false, phase: 'idle', message: `Stopped after ${this.check.done} anime`, finishedUtc: new Date().toISOString() });
        return json(this.check);
      }
    }
    if (method === 'GET' && route === 'Activity') {
      const entries = query.itemId ? this.activity.filter((e) => e.itemId === query.itemId) : this.activity;
      return json(entries.slice(0, Number(query.limit) || 50));
    }
    if (method === 'POST' && route === 'Cache/Clear') return noContent();
    if (method === 'GET' && route === 'Library') return json(this.libraryRows());

    if (parts[0] === 'Anime' && method === 'GET') {
      const anime = [this.kaguya1, this.kaguya2, this.frieren].find((a) => String(a.id) === parts[1]);
      if (!anime) return json({ error: 'That entry is not on animethemes.moe.' }, 404);
      return json({ anime: animeDto(anime), themes: themesOf(anime, () => ({ audio: false, video: false })) });
    }

    if (parts[0] === 'Items') {
      const itemId = parts[1];
      const owner = [KAGUYA_S1, KAGUYA_S2].includes(itemId) ? KAGUYA : itemId;
      const action = parts.slice(2).join('/');
      if (method === 'GET' && action === '') {
        const detail = this.detail(owner);
        return detail ? json(detail) : json({ error: 'This item no longer exists, or it is not a series, season or movie.' }, 404);
      }
      if (method === 'GET' && action === 'Summary') {
        const row = this.libraryRows().find((r) => r.id === owner);
        return json(row ? { managed: true, ownerId: owner, summary: row } : { managed: false });
      }
      if (method === 'POST' && action === 'Check') return json(this.detail(owner));
      if (action === 'Match') {
        if (method === 'PUT') this.matches[owner] = body.animeId;
        else delete this.matches[owner];
        this.activity.unshift({ timeUtc: new Date().toISOString(), kind: 'Matched', itemId: owner, itemName: (this.detail(owner) || {}).name, message: 'Matched to an entry' });
        return json(this.detail(owner));
      }
      if (action === 'Excluded') {
        if (method === 'PUT') this.excluded.add(owner);
        else this.excluded.delete(owner);
        return json(this.detail(owner));
      }
      if (parts[2] === 'Themes' && method === 'PUT') {
        const key = itemId + ':' + parts[3];
        this.overrides[key] = Object.assign({}, this.overrides[key]);
        if (body.change === 'audio' || body.change === 'both') this.overrides[key].audio = body.audio;
        if (body.change === 'video' || body.change === 'both') this.overrides[key].video = body.video;
        return json(this.detail(owner));
      }
      if (action === 'Files' && method === 'DELETE') {
        const detail = this.detail(owner);
        const folder = detail.folders.find((f) => f.itemId === itemId);
        const file = folder && folder.files.find((f) => f.fileName === query.name && f.directory === query.directory);
        if (!file || file.source === 'yours') return json({ error: 'KometaThemes did not write that file, so it does not delete it.' }, 404);
        const theme = folder.themes.find((t) => t.audio.file === file.fileName || t.video.file === file.fileName);
        if (theme) {
          const key = itemId + ':' + theme.themeId;
          this.overrides[key] = Object.assign({}, this.overrides[key], file.directory === 'backdrops' ? { video: false } : { audio: false });
        }
        return json(this.detail(owner));
      }
      if (action === 'Search' && method === 'GET') {
        const all = [this.kaguya1, this.kaguya2, this.frieren];
        const q = String(query.q || '').toLowerCase();
        const found = all.filter((a) => a.name.toLowerCase().includes(q.split(' ')[0]) || q.includes('kaguya') === a.name.toLowerCase().includes('kaguya'));
        return json(found.map((a, i) => animeDto(a, 100 - i * 20)));
      }
      if (action === 'YouTube' && method === 'POST') {
        if (!/youtu/.test(body.url || '')) return json({ error: 'That is not a YouTube video link. Paste a youtube.com or youtu.be link.' }, 400);
        return json(this.detail(owner));
      }
    }
    return json({ error: 'Unknown request ' + method + ' ' + route }, 404);
  }
}

module.exports = { MockApi, KAGUYA, KAGUYA_S2, FRIEREN, UNRESOLVED };
