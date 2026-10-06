/*
 * KometaThemes 2.0 — page controller.
 *
 * Jellyfin loads this file as an ES module through data-controller="__plugin/KometaThemesJs" and
 * constructs the default export with the page element and the URL parameters. Everything the page
 * shows is built with DOM calls and textContent: names and titles come from animethemes.moe and
 * from the library, and are never parsed as HTML.
 */

const VERSION = '2.0.0.0';
const PAGE = 'KometaThemes';
const LIBRARY_PAGE_SIZE = 100;

/* ------------------------------------------------------------------ icons */

const ICON_PATHS = {
    play: 'M8 5v14l11-7z',
    pause: 'M6 19h4V5H6v14zm8-14v14h4V5h-4z',
    check: 'M9 16.17 4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z',
    close: 'M19 6.41 17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z',
    search: 'M15.5 14h-.79l-.28-.27A6.47 6.47 0 0 0 16 9.5 6.5 6.5 0 1 0 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z',
    refresh: 'M17.65 6.35A7.96 7.96 0 0 0 12 4a8 8 0 1 0 7.73 10h-2.08A6 6 0 1 1 12 6c1.66 0 3.14.69 4.22 1.78L13 11h7V4l-2.35 2.35z',
    music: 'M12 3v10.55A4 4 0 1 0 14 17V7h4V3h-6z',
    video: 'M17 10.5V7a1 1 0 0 0-1-1H4a1 1 0 0 0-1 1v10a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-3.5l4 4v-11l-4 4z',
    warning: 'M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z',
    info: 'M11 7h2v2h-2zm0 4h2v6h-2zm1-9a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm0 18a8 8 0 1 1 0-16 8 8 0 0 1 0 16z',
    trash: 'M6 19a2 2 0 0 0 2 2h8a2 2 0 0 0 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z',
    back: 'M20 11H7.83l5.59-5.59L12 4l-8 8 8 8 1.41-1.41L7.83 13H20v-2z',
    up: 'M7.41 15.41 12 10.83l4.59 4.58L18 14l-6-6-6 6z',
    down: 'M7.41 8.59 12 13.17l4.59-4.58L18 10l-6 6-6-6z',
    link: 'M19 19H5V5h7V3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7h-2v7zM14 3v2h3.59l-9.83 9.83 1.41 1.41L19 6.41V10h2V3h-7z',
    block: 'M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zM4 12a8 8 0 0 1 12.9-6.31L5.69 16.9A7.9 7.9 0 0 1 4 12zm8 8a7.9 7.9 0 0 1-4.9-1.69L18.31 7.1A7.9 7.9 0 0 1 20 12a8 8 0 0 1-8 8z',
    stop: 'M6 6h12v12H6z',
    add: 'M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6v2z',
    history: 'M13 3a9 9 0 0 0-9 9H1l3.89 3.89.07.14L9 12H6a7 7 0 1 1 2.05 4.94l-1.42 1.42A9 9 0 1 0 13 3zm-1 5v5l4.28 2.54.72-1.21-3.5-2.08V8H12z',
    download: 'M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z'
};

function icon(name, label) {
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('class', 'kt-ico');
    if (label) {
        svg.setAttribute('role', 'img');
        svg.setAttribute('aria-label', label);
    } else {
        svg.setAttribute('aria-hidden', 'true');
        svg.setAttribute('focusable', 'false');
    }
    const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    path.setAttribute('d', ICON_PATHS[name] || ICON_PATHS.info);
    svg.appendChild(path);
    return svg;
}

/* ------------------------------------------------------------------ DOM helpers */

function h(tag, attrs, ...children) {
    const node = document.createElement(tag);
    for (const [key, value] of Object.entries(attrs || {})) {
        if (value === undefined || value === null || value === false) continue;
        if (key === 'text') node.textContent = value;
        else if (key === 'class') node.className = value;
        else if (key === 'on') for (const [event, handler] of Object.entries(value)) node.addEventListener(event, handler);
        else if (key === 'dataset') Object.assign(node.dataset, value);
        else if (key === 'style') node.setAttribute('style', value);
        else if (value === true) node.setAttribute(key, '');
        else node.setAttribute(key, String(value));
    }
    for (const child of children.flat()) {
        if (child === undefined || child === null || child === false) continue;
        node.appendChild(typeof child === 'string' || typeof child === 'number' ? document.createTextNode(String(child)) : child);
    }
    return node;
}

function clear(node) {
    while (node.firstChild) node.removeChild(node.firstChild);
    return node;
}

function plural(count, one, many) {
    return `${count} ${count === 1 ? one : many}`;
}

const relative = new Intl.RelativeTimeFormat('en', { numeric: 'auto' });

function ago(iso) {
    if (!iso) return 'never';
    const seconds = (new Date(iso).getTime() - Date.now()) / 1000;
    const abs = Math.abs(seconds);
    if (abs < 60) return 'just now';
    if (abs < 3600) return relative.format(Math.round(seconds / 60), 'minute');
    if (abs < 86400) return relative.format(Math.round(seconds / 3600), 'hour');
    return relative.format(Math.round(seconds / 86400), 'day');
}

function posterUrl(itemId, height) {
    return window.ApiClient.getUrl(`Items/${itemId}/Images/Primary`, { fillHeight: height, fillWidth: Math.round(height * 2 / 3), quality: 90 });
}

function poster(itemId, name, height, className) {
    const fallback = h('span', { class: className || 'kt-poster', 'aria-hidden': 'true', text: (name || '?').trim().charAt(0).toUpperCase() });
    const img = h('img', { class: className || 'kt-poster', src: posterUrl(itemId, height), alt: '', loading: 'lazy', decoding: 'async', width: String(Math.round(height * 2 / 3)), height: String(height) });
    img.addEventListener('error', () => img.replaceWith(fallback), { once: true });
    return img;
}

/* ------------------------------------------------------------------ API */

class ApiError extends Error {
    constructor(message, status) {
        super(message);
        this.status = status;
    }
}

async function api(method, path, options) {
    const { query, body } = options || {};
    const client = window.ApiClient;
    const headers = { Accept: 'application/json' };
    client.setRequestHeaders(headers);
    if (body !== undefined) headers['Content-Type'] = 'application/json';

    let response;
    try {
        response = await fetch(client.getUrl(`KometaThemes/${path}`, query), {
            method,
            headers,
            body: body === undefined ? undefined : JSON.stringify(body)
        });
    } catch {
        throw new ApiError('Jellyfin could not be reached. Check your connection and try again.', 0);
    }

    const text = await response.text();
    let data = null;
    if (text) {
        try {
            data = JSON.parse(text);
        } catch {
            data = null;
        }
    }

    if (!response.ok) {
        let message = data && data.error;
        if (!message) {
            if (response.status === 401 || response.status === 403) message = 'Only administrators can manage KometaThemes. Sign in again as one.';
            else if (response.status === 404) message = 'This KometaThemes version does not know that request. Reload the page.';
            else message = `The server could not finish this (error ${response.status}). Details are in the Jellyfin log.`;
        }
        throw new ApiError(message, response.status);
    }
    return data;
}

/* ------------------------------------------------------------------ audio preview */

class Preview {
    constructor(onChange) {
        this.audio = new Audio();
        this.audio.preload = 'none';
        this.key = null;
        this.onChange = onChange;
        this.audio.addEventListener('ended', () => this.stop());
        this.audio.addEventListener('error', () => {
            if (this.key) {
                const key = this.key;
                this.stop();
                this.onChange(null, key, 'This browser cannot play the preview. Open it on animethemes.moe instead.');
            }
        });
    }

    toggle(key, url) {
        if (this.key === key) {
            this.stop();
            return;
        }
        this.stop();
        if (!url) return;
        this.key = key;
        this.audio.src = url;
        this.audio.volume = 0.6;
        this.audio.play().catch(() => {
            /* reported through the error event */
        });
        this.onChange(key);
    }

    stop() {
        const was = this.key;
        this.key = null;
        this.audio.pause();
        this.audio.removeAttribute('src');
        this.audio.load();
        if (was) this.onChange(null, was);
    }
}

/* ------------------------------------------------------------------ the page */

class KometaPage {
    constructor(view, params) {
        this.view = view;
        this.root = view.querySelector('#ktRoot');
        this.state = {
            tab: 'library',
            itemId: params && params.item ? params.item : null,
            filter: 'all',
            query: '',
            shown: LIBRARY_PAGE_SIZE,
            overview: null,
            library: null,
            detail: null,
            folderIndex: 0,
            settings: null,
            savedSettings: null,
            activity: null,
            setupStep: 0,
            setupDraft: null
        };
        this.preview = new Preview((key, stopped, error) => this.onPreview(key, stopped, error));
        this.timer = null;
        this.alive = true;

        this.status = h('p', { class: 'kt-sr', role: 'status', 'aria-live': 'polite', 'aria-atomic': 'true' });

        view.addEventListener('viewshow', (event) => {
            const item = event.detail && event.detail.params && event.detail.params.item;
            if (item) {
                this.state.itemId = item;
                this.state.tab = 'library';
            }
            this.start();
        });
        view.addEventListener('viewbeforehide', () => this.pause());
        view.addEventListener('viewdestroy', () => this.destroy());
    }

    /* ---------- lifecycle ---------- */

    async start() {
        this.alive = true;
        try {
            await this.loadOverview();
            this.root.removeAttribute('aria-busy');
            this.render();
        } catch (error) {
            this.renderFatal(error);
        }
    }

    pause() {
        this.preview.stop();
        clearTimeout(this.timer);
        this.timer = null;
    }

    destroy() {
        this.pause();
        this.alive = false;
    }

    async loadOverview() {
        this.state.overview = await api('GET', 'State');
        this.schedulePoll();
    }

    schedulePoll() {
        clearTimeout(this.timer);
        if (!this.alive) return;
        const running = this.state.overview && this.state.overview.sync && this.state.overview.sync.running;
        this.timer = setTimeout(() => this.poll(running), running ? 2000 : 30000);
    }

    async poll(wasRunning) {
        if (!this.alive || document.hidden) {
            this.schedulePoll();
            return;
        }
        try {
            const sync = await api('GET', 'Check');
            const finished = wasRunning && !sync.running;
            this.state.overview.sync = sync;
            if (finished) {
                this.say(sync.message || 'The check finished.');
                await this.loadOverview();
                this.state.library = null;
                if (this.state.tab === 'library' && !this.state.itemId) await this.ensureLibrary();
                this.render();
                return;
            }
            this.renderHeader();
        } catch {
            /* keep the last state; the next poll tries again */
        }
        this.schedulePoll();
    }

    /* ---------- feedback ---------- */

    say(message) {
        this.status.textContent = '';
        setTimeout(() => {
            this.status.textContent = message;
        }, 50);
        this.toast(message);
    }

    toast(message) {
        const page = this.view;
        const old = page.querySelector('.kt-toast');
        if (old) old.remove();
        const toast = h('div', { class: 'kt-toast', 'aria-hidden': 'true', text: message });
        page.appendChild(toast);
        setTimeout(() => toast.remove(), 4500);
    }

    fail(error) {
        this.say(error && error.message ? error.message : 'Something went wrong. Details are in the Jellyfin log.');
    }

    async busy(button, work) {
        if (button) {
            button.disabled = true;
            button.setAttribute('aria-busy', 'true');
        }
        try {
            return await work();
        } catch (error) {
            this.fail(error);
            return undefined;
        } finally {
            if (button && button.isConnected) {
                button.disabled = false;
                button.removeAttribute('aria-busy');
            }
        }
    }

    /* ---------- skeleton ---------- */

    render() {
        clear(this.root);
        const overview = this.state.overview;
        this.header = h('div', { class: 'kt-top' });
        this.root.append(this.header, this.status);
        this.renderHeader();

        if (overview.setupRequired) {
            this.renderSetup();
            return;
        }


        this.tabs = h('div', { class: 'kt-tabs', role: 'tablist', 'aria-label': 'KometaThemes sections' });
        const sections = [['library', 'Library'], ['activity', 'Activity'], ['settings', 'Settings']];
        for (const [id, label] of sections) {
            const tab = h('button', {
                class: 'kt-tab',
                role: 'tab',
                id: `kt-tab-${id}`,
                'aria-selected': String(this.state.tab === id),
                'aria-controls': 'kt-panel',
                tabindex: this.state.tab === id ? '0' : '-1',
                on: { click: () => this.go(id) }
            }, label);
            if (id === 'library' && overview.counts.attention > 0) {
                tab.appendChild(h('span', { class: 'kt-count', text: String(overview.counts.attention), 'aria-label': `${overview.counts.attention} need attention` }));
            }
            this.tabs.appendChild(tab);
        }
        this.tabs.addEventListener('keydown', (event) => this.tabKeys(event, this.tabs));
        this.panel = h('div', { id: 'kt-panel', role: 'tabpanel', 'aria-labelledby': `kt-tab-${this.state.tab}` });
        this.root.append(this.tabs, this.panel);
        this.renderPanel();
    }

    tabKeys(event, list) {
        if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
        const tabs = [...list.querySelectorAll('[role="tab"]')];
        const index = tabs.indexOf(document.activeElement);
        if (index < 0) return;
        event.preventDefault();
        let next = index;
        if (event.key === 'ArrowLeft') next = (index - 1 + tabs.length) % tabs.length;
        if (event.key === 'ArrowRight') next = (index + 1) % tabs.length;
        if (event.key === 'Home') next = 0;
        if (event.key === 'End') next = tabs.length - 1;
        tabs[next].focus();
        tabs[next].click();
    }

    go(tab) {
        if (this.state.tab === 'settings' && tab !== 'settings' && this.settingsDirty() && !window.confirm('Leave without saving your changes?')) {
            return;
        }
        this.preview.stop();
        this.state.tab = tab;
        if (tab !== 'library') this.state.itemId = null;
        if (tab === 'settings') this.state.settings = null;
        this.render();
        const active = this.tabs && this.tabs.querySelector('[aria-selected="true"]');
        if (active) active.focus();
    }

    renderPanel() {
        clear(this.panel);
        if (this.state.tab === 'library') {
            return this.state.itemId ? this.renderItem() : this.renderLibrary();
        }
        return this.state.tab === 'activity' ? this.renderActivity() : this.renderSettings();
    }

    renderFatal(error) {
        this.root.removeAttribute('aria-busy');
        clear(this.root).appendChild(this.note('error', 'KometaThemes could not load', error && error.message ? error.message : String(error), [
            h('button', { class: 'kt-btn', type: 'button', on: { click: () => this.start() } }, 'Try again')
        ]));
    }

    note(kind, title, text, actions) {
        const iconName = kind === 'error' || kind === 'warn' ? 'warning' : 'info';
        return h('div', { class: `kt-note kt-note--${kind}`, role: kind === 'error' ? 'alert' : null },
            icon(iconName),
            h('div', {}, h('h3', { text: title }), typeof text === 'string' ? h('p', { class: 'kt-muted', text }) : text),
            actions && actions.length ? h('div', { class: 'kt-row-actions' }, actions) : null);
    }

    /* ---------- header: brand and the full check ---------- */

    renderHeader() {
        if (!this.header) return;
        const { sync, lastCheck } = this.state.overview;
        clear(this.header);
        this.header.appendChild(h('div', { class: 'kt-brand' },
            h('img', { src: 'configurationpage?name=KometaThemesIcon', alt: '', width: '40', height: '40' }),
            h('h2', { text: 'Anime themes' })));

        const check = h('div', { class: 'kt-check' });
        if (sync.running) {
            const percent = sync.total ? Math.round((100 * sync.done) / sync.total) : 0;
            check.append(
                h('span', { class: 'kt-dot kt-dot--run', 'aria-hidden': 'true' }),
                h('span', { class: 'kt-num', text: sync.phase === 'matching' && !sync.done ? 'Matching your anime…' : `Checking ${sync.done} of ${sync.total}` }),
                h('span', { class: 'kt-progress', role: 'progressbar', 'aria-valuemin': '0', 'aria-valuemax': '100', 'aria-valuenow': String(percent), 'aria-label': 'Check progress' }, h('i', { style: `transform:scaleX(${percent / 100})` })),
                h('button', { class: 'kt-btn kt-btn--ghost', type: 'button', on: { click: (e) => this.stopCheck(e.currentTarget) } }, icon('stop'), 'Stop'));
            if (sync.current) check.insertBefore(h('span', { class: 'kt-muted', text: sync.current }), check.children[2]);
        } else {
            const problems = this.state.overview.counts.attention;
            check.append(
                h('span', { class: `kt-dot ${problems ? 'kt-dot--warn' : 'kt-dot--ok'}`, 'aria-hidden': 'true' }),
                h('span', { title: lastCheck.summary || '', text: lastCheck.utc ? `Last check ${ago(lastCheck.utc)}` : 'Not checked yet' }),
                h('button', { class: 'kt-btn', type: 'button', on: { click: (e) => this.startCheck(e.currentTarget, false) } }, icon('refresh'), 'Check now'));
        }
        this.header.appendChild(check);
    }

    async startCheck(button, retryProblems) {
        await this.busy(button, async () => {
            this.state.overview.sync = await api('POST', 'Check', { body: { retryProblems } });
            this.say('Checking your anime. You can keep using the page.');
            this.renderHeader();
            this.schedulePoll();
        });
    }

    async stopCheck(button) {
        await this.busy(button, async () => {
            await api('DELETE', 'Check');
            this.say('The check stops after the anime in progress.');
        });
    }

    whatsNew() {
        const list = h('ul', {},
            h('li', { text: 'Files are named after the song, like “OP1 - Love Dramatic.mp3”. Yours are renamed, not downloaded again.' }),
            h('li', { text: 'Each season gets its own openings and endings when AniList confirms which entry it is.' }),
            h('li', { text: 'Duplicate theme.mp3 copies and themes 1.x put in the wrong season are removed at the next check.' }));
        return this.note('info', 'What’s new in KometaThemes 2.0', list, [
            h('button', { class: 'kt-btn kt-btn--ghost', type: 'button', on: { click: async (e) => {
                await this.busy(e.currentTarget, () => api('POST', 'WhatsNew/Dismiss'));
                this.state.overview.whatsNew = false;
                e.currentTarget.closest('.kt-note').remove();
            } } }, 'Got it')
        ]);
    }

    /* ---------- library ---------- */

    async ensureLibrary() {
        if (!this.state.library) this.state.library = await api('GET', 'Library');
        return this.state.library;
    }

    async renderLibrary() {
        const panel = this.panel;
        const overview = this.state.overview;
        if (overview.whatsNew) panel.appendChild(this.whatsNew());
        if (!overview.libraries.some((library) => library.selected)) {
            panel.appendChild(this.emptyState('Choose the libraries that hold your anime', 'KometaThemes only looks at the libraries you pick.', h('button', { class: 'kt-btn', type: 'button', on: { click: () => this.go('settings') } }, 'Open Settings')));
            return;
        }

        const filters = h('div', { class: 'kt-filters', role: 'group', 'aria-label': 'Show' });
        const counts = overview.counts;
        const chips = [
            ['all', 'All', counts.total],
            ['attention', 'Needs attention', counts.attention, 'kt-chip--warn'],
            ['pending', 'Not checked yet', counts.pending],
            ['manual', 'Matched by you', counts.manual],
            ['excluded', 'Excluded', counts.excluded]
        ];
        for (const [id, label, count, extra] of chips) {
            if (id !== 'all' && !count && this.state.filter !== id) continue;
            filters.appendChild(h('button', {
                class: `kt-chip ${extra || ''}`,
                type: 'button',
                'aria-pressed': String(this.state.filter === id),
                on: { click: () => {
                    this.state.filter = id;
                    this.state.shown = LIBRARY_PAGE_SIZE;
                    this.renderPanel();
                } }
            }, label, h('b', { text: String(count) })));
        }
        const search = h('input', { type: 'search', name: 'kt-library-search', autocomplete: 'off', spellcheck: 'false', placeholder: 'Search your anime…', 'aria-label': 'Search your anime', value: this.state.query });
        search.addEventListener('input', () => {
            this.state.query = search.value;
            this.state.shown = LIBRARY_PAGE_SIZE;
            this.renderRows(list, more);
        });
        filters.appendChild(h('div', { class: 'kt-search' }, search));
        panel.appendChild(filters);

        const list = h('ul', { class: 'kt-list', 'aria-busy': 'true' });
        for (let i = 0; i < 6; i++) list.appendChild(h('li', { class: 'kt-skel', 'aria-hidden': 'true' }));
        const more = h('div', { class: 'kt-more' });
        panel.append(list, more);

        try {
            await this.ensureLibrary();
        } catch (error) {
            clear(list);
            list.removeAttribute('aria-busy');
            panel.replaceChild(this.note('error', 'The library could not be loaded', error.message, [h('button', { class: 'kt-btn', type: 'button', on: { click: () => this.renderPanel() } }, 'Try again')]), list);
            return;
        }
        list.removeAttribute('aria-busy');
        this.renderRows(list, more);
    }

    filteredLibrary() {
        const query = this.state.query.trim().toLowerCase();
        return this.state.library.filter((row) => {
            if (query && !row.name.toLowerCase().includes(query)) return false;
            switch (this.state.filter) {
                case 'attention': return row.status === 'attention' || row.status === 'failed';
                case 'pending': return row.status === 'pending';
                case 'manual': return row.manual;
                case 'excluded': return row.status === 'excluded';
                default: return true;
            }
        });
    }

    renderRows(list, more) {
        clear(list);
        clear(more);
        const rows = this.filteredLibrary();
        if (!rows.length) {
            list.appendChild(h('li', { class: 'kt-empty' }, this.state.query
                ? h('p', { text: `No anime matches “${this.state.query}”.` })
                : h('p', { text: this.state.filter === 'excluded' ? 'Nothing is excluded. Exclude an anime from its page when it should never get themes.' : 'Nothing here.' })));
            return;
        }
        for (const row of rows.slice(0, this.state.shown)) list.appendChild(h('li', {}, this.libraryRow(row)));
        if (rows.length > this.state.shown) {
            more.appendChild(h('button', { class: 'kt-btn kt-btn--ghost', type: 'button', on: { click: () => {
                this.state.shown += LIBRARY_PAGE_SIZE;
                this.renderRows(list, more);
            } } }, `Show ${Math.min(LIBRARY_PAGE_SIZE, rows.length - this.state.shown)} more of ${rows.length}`));
        }
    }

    libraryRow(row) {
        const meta = [row.year, row.type === 'Movie' ? 'movie' : plural(row.seasons || 1, 'season', 'seasons')].filter(Boolean).join(', ');
        const songs = h('span', { class: 'kt-songs' });
        if (row.openings || row.endings) {
            songs.appendChild(h('strong', { text: [row.openings ? plural(row.openings, 'opening', 'openings') : null, row.endings ? plural(row.endings, 'ending', 'endings') : null].filter(Boolean).join(', ') }));
            if (row.seasonsWithOwnThemes) songs.appendChild(document.createTextNode(`, own themes in ${plural(row.seasonsWithOwnThemes, 'season', 'seasons')}`));
        } else {
            songs.textContent = {
                attention: 'Not found on animethemes.moe',
                failed: row.detail || 'Downloads failed',
                pending: 'Waiting for the next check',
                empty: 'No theme fits your settings',
                excluded: 'Excluded'
            }[row.status] || '';
        }

        let status;
        if (row.status === 'attention') {
            status = h('span', { class: 'kt-btn kt-btn--warn', 'aria-hidden': 'true', text: 'Find a match' });
        } else {
            const label = row.manual && row.status === 'ready' ? 'Matched by you' : {
                ready: 'Ready', failed: 'Download failed', pending: 'Not checked', empty: 'No themes', excluded: 'Excluded'
            }[row.status];
            const kind = row.manual && row.status === 'ready' ? 'manual' : row.status;
            status = h('span', { class: `kt-status kt-status--${kind}`, text: label });
        }

        return h('button', { class: 'kt-row', type: 'button', dataset: { id: row.id }, on: { click: () => this.openItem(row.id) }, 'aria-label': `${row.name}, ${meta}. ${songs.textContent}` },
            poster(row.id, row.name, 120),
            h('span', { class: 'kt-name' }, h('b', { text: row.name }), h('span', { text: meta })),
            songs,
            status);
    }

    emptyState(title, text, action) {
        return h('div', { class: 'kt-empty' },
            h('img', { src: 'configurationpage?name=KometaThemesIcon', alt: '' }),
            h('h3', { text: title }),
            h('p', { text }),
            action);
    }

    /* ---------- anime page ---------- */

    openItem(itemId) {
        this.preview.stop();
        this.state.itemId = itemId;
        this.state.detail = null;
        this.state.folderIndex = 0;
        this.renderPanel();
        window.scrollTo({ top: 0 });
    }

    async closeItem() {
        this.preview.stop();
        const id = this.state.itemId;
        this.state.itemId = null;
        this.state.detail = null;
        await this.renderPanel();
        const row = id && this.panel.querySelector(`.kt-row[data-id="${id}"]`);
        if (row) row.focus();
    }

    async renderItem() {
        const panel = this.panel;
        panel.appendChild(h('button', { class: 'kt-btn kt-btn--quiet kt-back', type: 'button', on: { click: () => this.closeItem() } }, icon('back'), 'Library'));
        const holder = h('div', { 'aria-busy': 'true' }, h('div', { class: 'kt-skel' }), h('div', { class: 'kt-skel' }), h('div', { class: 'kt-skel' }));
        panel.appendChild(holder);
        try {
            this.state.detail = await api('GET', `Items/${this.state.itemId}`);
        } catch (error) {
            holder.replaceWith(this.note('error', 'This anime could not be opened', error.message, [
                h('button', { class: 'kt-btn', type: 'button', on: { click: () => this.renderPanel() } }, 'Try again')
            ]));
            return;
        }
        holder.replaceWith(this.itemView());
    }

    refreshItem(detail, message) {
        this.state.detail = detail;
        this.state.library = null;
        const current = this.panel.querySelector('.kt-detail');
        if (current) current.replaceWith(this.itemView());
        if (message) this.say(message);
        api('GET', 'State').then((overview) => {
            this.state.overview = overview;
            this.renderHeader();
        }).catch(() => {});
    }

    itemView() {
        const detail = this.state.detail;
        const summary = detail.summary;
        const excluded = summary.status === 'excluded';

        const facts = h('div', { class: 'kt-facts' },
            h('span', { text: [detail.year, detail.type === 'Movie' ? 'Movie' : plural(summary.seasons || 1, 'season', 'seasons')].filter(Boolean).join(', ') }),
            summary.openings || summary.endings
                ? h('span', { text: [plural(summary.openings, 'opening', 'openings'), plural(summary.endings, 'ending', 'endings')].join(', ') + ' in the library' })
                : null,
            summary.checkedUtc ? h('span', { text: `Checked ${ago(summary.checkedUtc)}` }) : null);

        const actions = h('div', { class: 'kt-row-actions' });
        if (excluded) {
            actions.appendChild(h('button', { class: 'kt-btn', type: 'button', on: { click: (e) => this.include(e.currentTarget) } }, 'Manage it again'));
        } else {
            actions.append(
                h('button', { class: 'kt-btn kt-btn--ghost', type: 'button', on: { click: (e) => this.checkItem(e.currentTarget, false) } }, icon('refresh'), 'Check again'),
                h('button', { class: 'kt-btn kt-btn--quiet', type: 'button', on: { click: (e) => this.confirmButton(e.currentTarget, 'Download all again?', () => this.checkItem(e.currentTarget, true)) } }, icon('download'), 'Download again'),
                h('button', { class: 'kt-btn kt-btn--quiet', type: 'button', on: { click: () => this.openExclude() } }, icon('block'), 'Exclude'));
        }

        const side = h('aside', { class: 'kt-side' },
            poster(detail.id, detail.name, 420, 'kt-cover'),
            h('h2', { text: detail.name }),
            facts,
            actions);

        const main = h('div', { class: 'kt-main' });
        if (excluded) {
            main.appendChild(this.note('info', 'KometaThemes leaves this anime alone', 'It is excluded: no themes are downloaded, renamed or removed for it.'));
        }
        if (detail.problem && !excluded) {
            const unresolved = detail.problem.reason === 'Unresolved';
            main.appendChild(this.note('warn',
                unresolved ? 'No match on animethemes.moe yet' : 'Some files could not be downloaded',
                `${capitalize(detail.problem.error || '')}. Next automatic try ${ago(detail.problem.nextAttemptUtc)}.`.replace(/^\. /, ''),
                unresolved
                    ? [h('button', { class: 'kt-btn', type: 'button', on: { click: () => this.openMatch(detail.folders[0] || null) } }, icon('search'), 'Find a match')]
                    : [h('button', { class: 'kt-btn', type: 'button', on: { click: (e) => this.checkItem(e.currentTarget, false) } }, 'Try again')]));
        }
        if (detail.note) main.appendChild(this.note('warn', 'Themes cannot be added here', detail.note));

        if (detail.folders.length > 1) {
            const tabs = h('div', { class: 'kt-seasons', role: 'tablist', 'aria-label': 'Series and seasons' });
            detail.folders.forEach((folder, index) => {
                tabs.appendChild(h('button', {
                    class: 'kt-tab',
                    role: 'tab',
                    'aria-selected': String(index === this.state.folderIndex),
                    tabindex: index === this.state.folderIndex ? '0' : '-1',
                    on: { click: () => {
                        this.preview.stop();
                        this.state.folderIndex = index;
                        const view = this.itemView();
                        this.panel.querySelector('.kt-detail').replaceWith(view);
                        view.querySelector('.kt-seasons [aria-selected="true"]').focus();
                    } }
                }, folder.label));
            });
            tabs.addEventListener('keydown', (event) => this.tabKeys(event, tabs));
            main.appendChild(tabs);
        }

        const folder = detail.folders[Math.min(this.state.folderIndex, detail.folders.length - 1)];
        if (folder) main.appendChild(this.folderView(folder, excluded));
        main.appendChild(this.itemActivity());

        return h('div', { class: 'kt-detail' }, side, main);
    }

    folderView(folder, excluded) {
        const box = h('section', { class: 'kt-main', 'aria-label': folder.label });
        box.appendChild(this.matchLine(folder, excluded));

        if (folder.themes.length) {
            const map = this.episodeMap(folder);
            if (map) box.appendChild(map);
            box.appendChild(this.trackList(folder, excluded));
            box.appendChild(h('p', { class: 'kt-legend', text: 'Song and Video show what is in this folder. A dashed button downloads at the next check; a struck one stays out even if your settings would include it. Settings decide the rest.' }));
        } else if (folder.match.state === 'inherit') {
            box.appendChild(h('p', { class: 'kt-muted', text: 'Jellyfin plays the series themes on this season’s page.' }));
        }

        if (folder.files.length) box.appendChild(this.fileList(folder, excluded));
        if (this.state.overview.youTube.enabled && !excluded) box.appendChild(this.youTubeForm(folder));
        return box;
    }

    matchLine(folder, excluded) {
        const match = folder.match;
        let how;
        let why = null;
        if (match.state === 'matched') {
            if (match.manual) how = 'Matched by you';
            else if (match.method.startsWith('sequel on AniList')) {
                how = 'Matched as a sequel on AniList';
                const detail = match.method.replace(/^sequel on AniList,?\s*/, '');
                why = detail ? `Same ${detail.includes('episodes') ? 'year and episode count' : 'year'} as this season: ${detail}.` : null;
            } else how = `Matched by ${match.method}`;
        } else if (match.state === 'inherit') {
            how = 'Plays the series themes';
            const detail = match.method.replace(/^plays the series themes:?\s*/, '').replace(/^\(|\)$/g, '');
            why = detail ? detail.charAt(0).toUpperCase() + detail.slice(1) + '.' : null;
        } else if (match.state === 'notFound') {
            how = 'Not found on animethemes.moe';
        } else {
            how = 'animethemes.moe could not be reached';
            why = 'Nothing was changed. The next check tries again.';
        }

        const text = h('div', { class: 'kt-match-text' },
            h('span', { class: `kt-match-how kt-match-how--${match.state}` }, icon(match.state === 'matched' ? 'check' : match.state === 'notFound' ? 'warning' : 'info'), how));
        for (const anime of match.anime) {
            text.appendChild(h('span', { class: 'kt-match-entry' },
                'animethemes.moe: ',
                h('a', { href: anime.link, target: '_blank', rel: 'noopener', text: anime.name }),
                ` (${[anime.season, anime.year].filter(Boolean).join(' ')}${anime.format ? `, ${anime.format}` : ''})`));
        }
        if (why) text.appendChild(h('span', { class: 'kt-match-entry', text: why }));

        const buttons = h('div', { class: 'kt-row-actions' });
        if (!excluded) {
            buttons.appendChild(h('button', { class: 'kt-btn kt-btn--ghost', type: 'button', on: { click: () => this.openMatch(folder) } }, icon('search'), match.state === 'matched' ? 'Change match' : 'Find a match'));
            if (match.manual) {
                buttons.appendChild(h('button', { class: 'kt-btn kt-btn--quiet', type: 'button', on: { click: (e) => this.clearMatch(e.currentTarget, folder) } }, 'Use the automatic match'));
            }
        }
        return h('div', { class: 'kt-match' }, text, buttons);
    }

    episodeMap(folder) {
        let last = folder.episodes || 0;
        for (const theme of folder.themes) for (const range of theme.ranges) last = Math.max(last, range[1]);
        if (!last || last > 400) return null;

        const grid = h('div', { class: 'kt-map-grid', style: `grid-template-columns: 2.4rem repeat(${last}, minmax(${last > 60 ? 6 : 18}px, 1fr))`, 'aria-hidden': 'true' });
        grid.appendChild(h('span'));
        const step = last > 60 ? 10 : last > 26 ? 5 : 1;
        for (let episode = 1; episode <= last; episode++) {
            grid.appendChild(h('span', { class: 'kt-map-ep', text: episode === 1 || episode % step === 0 ? String(episode) : '' }));
        }

        let row = 2;
        for (const lane of ['OP', 'ED']) {
            const themes = folder.themes.filter((theme) => theme.type === lane);
            if (!themes.length) continue;
            grid.appendChild(h('span', { class: 'kt-map-lane', style: `grid-row:${row};grid-column:1`, text: lane }));
            themes.forEach((theme) => {
                const ranges = theme.ranges.length ? theme.ranges : [[1, last]];
                const color = `kt-c${folder.themes.indexOf(theme) % 4}`;
                for (const [start, end] of ranges) {
                    if (start < 1 || start > last) continue;
                    grid.appendChild(h('span', {
                        class: `kt-map-bar ${color} ${theme.audio.wanted || theme.video.wanted ? '' : 'kt-off'}`,
                        style: `grid-row:${row};grid-column:${start + 1} / ${Math.min(end, last) + 2}`,
                        dataset: { theme: String(theme.themeId) },
                        title: `${theme.code} ${theme.title || ''}`
                    }, h('span', { text: theme.title || theme.code })));
                }
            });
            row++;
        }

        const caption = folder.episodes
            ? `${plural(folder.episodes, 'episode', 'episodes')} in your library. Bars show the episodes each song plays in.`
            : 'Bars show the episodes each song plays in, as animethemes.moe lists them.';
        return h('figure', { class: 'kt-map', style: 'margin:0' }, grid, h('figcaption', { class: 'kt-map-caption', text: caption }));
    }

    trackList(folder, excluded) {
        const list = h('ol', { class: 'kt-tracks', 'aria-label': 'Openings and endings' });
        folder.themes.forEach((theme, index) => {
            const key = `${folder.itemId}:${theme.themeId}`;
            const play = h('button', {
                class: 'kt-icon-btn',
                type: 'button',
                'aria-pressed': String(this.preview.key === key),
                'aria-label': `Preview ${theme.code} ${theme.title || ''}`.trim(),
                dataset: { preview: key },
                on: { click: () => this.preview.toggle(key, theme.audio.preview) }
            }, icon(this.preview.key === key ? 'pause' : 'play'));
            if (!theme.audio.preview) play.disabled = true;

            const flags = [];
            if (!theme.creditless) flags.push('with credits');
            if (theme.overlap !== 'None') flags.push(theme.overlap === 'Over' ? 'scenes over the song' : 'fades into the episode');
            if (theme.spoiler) flags.push('spoiler');
            if (theme.nsfw) flags.push('NSFW');

            const item = h('li', { class: 'kt-track', dataset: { theme: String(theme.themeId) } },
                play,
                h('span', { class: 'kt-code' }, h('i', { class: `kt-c${index % 4}` }), theme.code),
                h('span', { class: 'kt-song' },
                    h('b', { text: theme.title || 'Untitled song' }),
                    h('span', { text: theme.artists || 'Artist not listed' }),
                    flags.length ? h('span', { class: 'kt-flags kt-muted', text: flags.join(', ') }) : null),
                h('span', { class: 'kt-eps', text: theme.ranges.length ? episodesText(theme.ranges) : 'All episodes' }),
                h('span', { class: 'kt-media' },
                    this.mediaToggle(folder, theme, 'audio', 'Song', excluded),
                    this.mediaToggle(folder, theme, 'video', 'Video', excluded)));

            item.addEventListener('mouseenter', () => this.highlight(theme.themeId, true));
            item.addEventListener('mouseleave', () => this.highlight(theme.themeId, false));
            list.appendChild(item);
        });
        return list;
    }

    highlight(themeId, on) {
        for (const bar of this.panel.querySelectorAll(`.kt-map-bar[data-theme="${themeId}"]`)) bar.classList.toggle('kt-hl', on);
    }

    mediaToggle(folder, theme, media, label, excluded) {
        const state = theme[media];
        const saved = Boolean(state.file);
        let title;
        if (saved) title = `${label} is in the folder (${state.file}). Click to remove it and keep it out.`;
        else if (state.wanted) title = `${label} downloads at the next check. Click to keep it out.`;
        else title = state.choice === false ? `${label} is kept out by you. Click to download it.` : `${label} is not included by your settings. Click to download it anyway.`;

        return h('button', {
            class: 'kt-toggle',
            type: 'button',
            'aria-pressed': String(state.wanted),
            'data-state': saved ? 'saved' : state.wanted ? 'pending' : 'off',
            title,
            'aria-label': `${label} of ${theme.code}: ${title}`,
            disabled: excluded,
            on: { click: (e) => this.toggleMedia(e.currentTarget, folder, theme, media) }
        }, icon(saved ? 'check' : media === 'audio' ? 'music' : 'video'), label);
    }

    async toggleMedia(button, folder, theme, media) {
        const state = theme[media];
        const wanted = !state.wanted;
        const choice = wanted === state.bySettings ? null : wanted;
        const body = { change: media, [media]: choice };
        const detail = await this.busy(button, () => api('PUT', `Items/${folder.itemId}/Themes/${theme.themeId}`, { body }));
        if (detail) {
            const what = `${media === 'audio' ? 'song' : 'video'} of ${theme.code} ${theme.title || ''}`.trim();
            this.refreshItem(detail, wanted ? `Added the ${what}.` : `Removed the ${what}. It stays out.`);
        }
    }

    fileList(folder, excluded) {
        const list = h('ul', { class: 'kt-files' });
        for (const file of folder.files) {
            const source = { animethemes: 'animethemes.moe', youtube: 'YouTube', yours: 'not from KometaThemes' }[file.source];
            const remove = file.source === 'yours' || excluded ? null : h('button', {
                class: 'kt-icon-btn',
                type: 'button',
                'aria-label': `Delete ${file.fileName}`,
                title: 'Delete this file',
                on: { click: (e) => this.confirmButton(e.currentTarget, 'Delete?', () => this.deleteFile(e.currentTarget, folder, file)) }
            }, icon('trash'));
            list.appendChild(h('li', { class: 'kt-file' },
                h('span', {}, h('code', { text: `${file.directory}/${file.fileName}` }), h('span', { class: 'kt-muted', text: ` ${formatSize(file.size)}` })),
                h('span', { class: 'kt-tag', text: source }),
                remove || h('span')));
        }
        const yours = folder.files.filter((file) => file.source === 'yours').length;
        return h('details', { class: 'kt-fold' },
            h('summary', { text: `Files in this folder (${folder.files.length})` }),
            yours ? h('p', { class: 'kt-help', text: 'Files not from KometaThemes are yours: they are never renamed or deleted, and Jellyfin plays them too.' }) : null,
            list);
    }

    confirmButton(button, question, action) {
        if (button.dataset.confirm === 'yes') {
            delete button.dataset.confirm;
            action();
            return;
        }
        const original = [...button.childNodes];
        const wasLabel = button.getAttribute('aria-label');
        button.dataset.confirm = 'yes';
        clear(button).append(question);
        button.setAttribute('aria-label', `${question} Press again to confirm.`);
        button.classList.add('kt-btn', 'kt-btn--danger');
        setTimeout(() => {
            if (!button.isConnected || button.dataset.confirm !== 'yes') return;
            delete button.dataset.confirm;
            clear(button).append(...original);
            if (wasLabel) button.setAttribute('aria-label', wasLabel);
            else button.removeAttribute('aria-label');
            button.classList.remove('kt-btn--danger');
            if (button.classList.contains('kt-icon-btn')) button.classList.remove('kt-btn');
        }, 4000);
    }

    async deleteFile(button, folder, file) {
        const detail = await this.busy(button, () => api('DELETE', `Items/${folder.itemId}/Files`, { query: { directory: file.directory, name: file.fileName } }));
        if (detail) this.refreshItem(detail, `Deleted ${file.fileName}.`);
    }

    youTubeForm(folder) {
        const url = h('input', { class: 'kt-input', id: `kt-yt-url-${folder.itemId}`, name: 'url', type: 'url', inputmode: 'url', required: true, placeholder: 'https://www.youtube.com/watch?v=…', autocomplete: 'off', spellcheck: 'false' });
        const type = h('select', { id: `kt-yt-type-${folder.itemId}` }, h('option', { value: 'OP', text: 'Opening' }), h('option', { value: 'ED', text: 'Ending' }));
        const sequence = h('input', { class: 'kt-input kt-number', id: `kt-yt-seq-${folder.itemId}`, name: 'sequence', type: 'number', inputmode: 'numeric', min: '1', max: '99', value: '1' });
        const title = h('input', { class: 'kt-input', id: `kt-yt-title-${folder.itemId}`, name: 'title', type: 'text', autocomplete: 'off', placeholder: 'Gurenge…' });
        const format = h('select', { id: `kt-yt-format-${folder.itemId}` }, h('option', { value: 'audio', text: 'Song' }), h('option', { value: 'video', text: 'Video' }), h('option', { value: 'both', text: 'Song and video' }));
        const submit = h('button', { class: 'kt-btn', type: 'submit' }, icon('add'), 'Add from YouTube');
        const form = h('form', { class: 'kt-form', style: 'gap:12px' },
            h('p', { class: 'kt-help', text: 'For songs animethemes.moe does not have. Only add videos you are allowed to download.' }),
            this.field('Video link', url),
            h('div', { class: 'kt-inline' }, this.field('Kind', type), this.field('Number', sequence), this.field('Save as', format)),
            this.field('Song title', title, 'Leave it empty to use the video title.'),
            h('div', {}, submit));
        form.addEventListener('submit', async (event) => {
            event.preventDefault();
            const body = { url: url.value.trim(), type: type.value, sequence: Number(sequence.value) || 1, title: title.value.trim(), format: format.value };
            const detail = await this.busy(submit, () => api('POST', `Items/${folder.itemId}/YouTube`, { body }));
            if (detail) this.refreshItem(detail, 'Added from YouTube.');
        });
        return h('details', { class: 'kt-fold' }, h('summary', { text: 'Add a song from YouTube' }), form);
    }

    field(label, control, help) {
        const id = control.id || `kt-f-${Math.random().toString(36).slice(2)}`;
        control.id = id;
        return h('div', { class: 'kt-field' }, h('label', { for: id, text: label }), control, help ? h('p', { class: 'kt-help', text: help }) : null);
    }

    itemActivity() {
        const fold = h('details', { class: 'kt-fold' }, h('summary', { text: 'Recent activity for this anime' }));
        fold.addEventListener('toggle', async () => {
            if (!fold.open || fold.dataset.loaded) return;
            fold.dataset.loaded = 'true';
            try {
                const entries = await api('GET', 'Activity', { query: { limit: 20, itemId: this.state.detail.id } });
                fold.appendChild(entries.length ? this.activityList(entries, false) : h('p', { class: 'kt-muted', text: 'Nothing yet.' }));
            } catch (error) {
                fold.appendChild(h('p', { class: 'kt-muted', text: error.message }));
            }
        });
        return fold;
    }

    async checkItem(button, redownload) {
        const detail = await this.busy(button, () => api('POST', `Items/${this.state.detail.id}/Check`, { query: redownload ? { redownload: true } : undefined }));
        if (!detail) return;
        const before = this.state.detail.summary;
        const after = detail.summary;
        let message = 'Up to date.';
        if (after.status === 'attention') message = 'Still no match on animethemes.moe. Find it by hand.';
        else if (after.status === 'failed') message = 'Some files could not be downloaded. See the note at the top.';
        else if (redownload) message = 'Downloaded again.';
        else if (after.openings + after.endings !== before.openings + before.endings) message = 'Themes updated.';
        this.refreshItem(detail, message);
    }

    async clearMatch(button, folder) {
        const detail = await this.busy(button, () => api('DELETE', `Items/${folder.itemId}/Match`));
        if (detail) this.refreshItem(detail, 'Back to the automatic match.');
    }

    async include(button) {
        const detail = await this.busy(button, () => api('DELETE', `Items/${this.state.detail.id}/Excluded`));
        if (detail) this.refreshItem(detail, 'KometaThemes manages this anime again.');
    }

    openExclude() {
        const detail = this.state.detail;
        const dialog = this.dialog('Exclude this anime?', (body, foot, close) => {
            body.appendChild(h('p', { text: `KometaThemes will stop downloading, renaming and removing themes for ${detail.name}. You can undo this from its page.` }));
            const keep = h('button', { class: 'kt-btn kt-btn--ghost', type: 'button' }, 'Exclude, keep its files');
            const remove = h('button', { class: 'kt-btn kt-btn--danger', type: 'button' }, 'Exclude and delete its files');
            keep.addEventListener('click', () => this.exclude(keep, false, close));
            remove.addEventListener('click', () => this.exclude(remove, true, close));
            foot.append(keep, remove);
        });
        return dialog;
    }

    async exclude(button, deleteFiles, close) {
        const detail = await this.busy(button, () => api('PUT', `Items/${this.state.detail.id}/Excluded`, { query: deleteFiles ? { deleteFiles: true } : undefined }));
        if (!detail) return;
        close();
        this.refreshItem(detail, deleteFiles ? 'Excluded, and its files are deleted.' : 'Excluded. Its files stay where they are.');
    }

    /* ---------- dialogs ---------- */

    dialog(title, build) {
        const dialog = h('dialog', { class: 'kt-dialog', 'aria-labelledby': 'kt-dialog-title' });
        const close = () => {
            this.preview.stop();
            dialog.close();
        };
        const head = h('div', { class: 'kt-dialog-head' },
            h('h3', { id: 'kt-dialog-title', text: title }),
            h('button', { class: 'kt-icon-btn', type: 'button', 'aria-label': 'Close', on: { click: close } }, icon('close')));
        const body = h('div', { class: 'kt-dialog-body' });
        const foot = h('div', { class: 'kt-dialog-foot' });
        const inner = h('div', { class: 'kt-dialog-in' }, head);
        dialog.appendChild(inner);
        const extra = build(body, foot, close, inner);
        inner.append(...(extra ? [extra] : []), body, foot);
        dialog.addEventListener('close', () => dialog.remove());
        dialog.addEventListener('click', (event) => {
            if (event.target === dialog) close();
        });
        this.root.appendChild(dialog);
        dialog.showModal();
        const focus = dialog.querySelector('input, .kt-dialog-foot button');
        if (focus) focus.focus();
        return dialog;
    }

    openMatch(folder) {
        const detail = this.state.detail;
        const target = folder || { itemId: detail.id, label: detail.name, seasonNumber: 0 };
        const isSeason = target.seasonNumber > 0;
        const initial = isSeason ? `${detail.name} season ${target.seasonNumber}` : detail.name;

        this.dialog(isSeason ? `Find the entry for ${target.label}` : `Find the entry for ${detail.name}`, (body, foot, close) => {
            const input = h('input', { type: 'search', class: 'kt-input', name: 'kt-match-search', autocomplete: 'off', spellcheck: 'false', value: initial, 'aria-label': 'Search animethemes.moe' });
            const go = h('button', { class: 'kt-btn', type: 'submit' }, icon('search'), 'Search');
            const bar = h('form', { class: 'kt-searchbar', role: 'search' }, input, go);
            const results = h('div', { role: 'list', 'aria-label': 'Results', 'aria-live': 'polite' });
            body.appendChild(results);
            foot.appendChild(h('p', { class: 'kt-help', style: 'margin-right:auto', text: 'Results come from animethemes.moe. Pick the entry whose year and format match.' }));
            foot.appendChild(h('button', { class: 'kt-btn kt-btn--ghost', type: 'button', on: { click: close } }, 'Cancel'));

            const search = async () => {
                const query = input.value.trim();
                if (!query) return;
                clear(results).appendChild(h('p', { class: 'kt-muted', text: 'Searching…' }));
                try {
                    const found = await api('GET', `Items/${target.itemId}/Search`, { query: { q: query } });
                    clear(results);
                    if (!found.length) {
                        results.appendChild(h('p', { class: 'kt-muted', text: 'Nothing found. Try the Japanese or English title, or fewer words.' }));
                        return;
                    }
                    found.forEach((anime, index) => results.appendChild(this.resultRow(anime, index === 0, target, close)));
                } catch (error) {
                    clear(results).appendChild(h('p', { class: 'kt-muted', text: error.message }));
                }
            };
            bar.addEventListener('submit', (event) => {
                event.preventDefault();
                search();
            });
            setTimeout(search, 0);
            return bar;
        });
    }

    resultRow(anime, best, target, close) {
        const current = (this.state.detail.folders.find((f) => f.itemId === target.itemId) || { match: { anime: [] } }).match.anime.some((a) => a.id === anime.id);
        const cover = anime.cover
            ? h('img', { class: 'kt-poster', src: anime.cover, alt: '', loading: 'lazy', referrerpolicy: 'no-referrer', width: '48', height: '72' })
            : h('span', { class: 'kt-poster', 'aria-hidden': 'true', text: anime.name.charAt(0) });
        const meta = [anime.season && anime.year ? `${anime.season} ${anime.year}` : anime.year, anime.format, plural(anime.themes, 'theme', 'themes')].filter(Boolean).join(', ');
        const toggle = h('button', { class: 'kt-btn kt-btn--ghost', type: 'button', 'aria-expanded': 'false' }, 'Preview');
        const row = h('div', { class: 'kt-result', role: 'listitem', 'aria-expanded': 'false' },
            cover,
            h('span', { class: 'kt-name' }, h('b', { text: anime.name }), h('span', { text: meta })),
            h('span', { class: 'kt-row-actions' },
                current ? h('span', { class: 'kt-score', text: 'Current' }) : best && anime.score >= 62 ? h('span', { class: 'kt-score', text: 'Best match' }) : null,
                toggle));

        let pick = null;
        toggle.addEventListener('click', async () => {
            const open = toggle.getAttribute('aria-expanded') !== 'true';
            toggle.setAttribute('aria-expanded', String(open));
            row.setAttribute('aria-expanded', String(open));
            if (!open) {
                if (pick) pick.hidden = true;
                return;
            }
            if (pick) {
                pick.hidden = false;
                return;
            }
            pick = h('div', { class: 'kt-result-pick' }, h('p', { class: 'kt-muted', text: 'Loading its themes…' }));
            row.appendChild(pick);
            try {
                const data = await api('GET', `Anime/${anime.id}`);
                clear(pick);
                const list = h('ol');
                for (const theme of data.themes) {
                    const key = `pick:${theme.themeId}`;
                    const play = h('button', { class: 'kt-icon-btn', type: 'button', 'aria-label': `Preview ${theme.code} ${theme.title || ''}`, dataset: { preview: key }, 'aria-pressed': 'false', on: { click: () => this.preview.toggle(key, theme.audio.preview) } }, icon('play'));
                    list.appendChild(h('li', { style: 'display:flex;gap:10px;align-items:center;margin:4px 0' }, play, h('span', {}, h('b', { text: `${theme.code} ` }), theme.title || 'Untitled', theme.artists ? h('span', { class: 'kt-muted', text: ` — ${theme.artists}` }) : null)));
                }
                const use = h('button', { class: 'kt-btn', type: 'button' }, icon('check'), current ? 'Download it again' : 'Use this entry');
                use.addEventListener('click', async () => {
                    const detail = await this.busy(use, () => api('PUT', `Items/${target.itemId}/Match`, { body: { animeId: anime.id } }));
                    if (!detail) return;
                    close();
                    this.refreshItem(detail, `Matched to ${anime.name}.`);
                });
                pick.append(data.themes.length ? list : h('p', { class: 'kt-muted', text: 'This entry lists no themes yet.' }), h('div', { class: 'kt-row-actions' }, use, h('a', { class: 'kt-btn kt-btn--quiet', href: anime.link, target: '_blank', rel: 'noopener' }, icon('link'), 'Open on animethemes.moe')));
            } catch (error) {
                clear(pick).appendChild(h('p', { class: 'kt-muted', text: error.message }));
            }
        });
        return row;
    }

    /* ---------- preview state ---------- */

    onPreview(key, stopped, error) {
        for (const button of this.root.querySelectorAll('[data-preview]')) {
            const playing = button.dataset.preview === key;
            button.setAttribute('aria-pressed', String(playing));
            const svg = button.querySelector('svg');
            if (svg) svg.replaceWith(icon(playing ? 'pause' : 'play'));
        }
        if (error) this.say(error);
    }

    /* ---------- activity ---------- */

    async renderActivity() {
        const panel = this.panel;
        const holder = h('div', { 'aria-busy': 'true' }, h('div', { class: 'kt-skel' }), h('div', { class: 'kt-skel' }));
        panel.appendChild(holder);
        try {
            const entries = await api('GET', 'Activity', { query: { limit: 150 } });
            holder.replaceWith(entries.length
                ? this.activityList(entries, true)
                : this.emptyState('Nothing has happened yet', 'Downloads, matches and problems show up here as KometaThemes works.', null));
        } catch (error) {
            holder.replaceWith(this.note('error', 'The activity could not be loaded', error.message));
        }
    }

    activityList(entries, linkItems) {
        const icons = { Downloaded: 'download', Removed: 'trash', Renamed: 'music', Matched: 'check', NotFound: 'warning', Failed: 'warning', Imported: 'add', Checked: 'history' };
        const list = h('ol', { class: 'kt-activity' });
        for (const entry of entries) {
            const who = entry.itemName
                ? linkItems && entry.itemId
                    ? h('button', { class: 'kt-link', type: 'button', text: entry.itemName, on: { click: () => {
                        this.state.tab = 'library';
                        this.state.itemId = entry.itemId;
                        this.render();
                    } } })
                    : h('b', { text: entry.itemName })
                : null;
            list.appendChild(h('li', { class: `kt-act kt-act--${entry.kind}` },
                icon(icons[entry.kind] || 'info'),
                h('div', {}, who, who ? h('br') : null, h('span', { class: who ? 'kt-muted' : '', text: entry.message })),
                h('time', { datetime: entry.timeUtc, title: new Date(entry.timeUtc).toLocaleString(), text: ago(entry.timeUtc) })));
        }
        return list;
    }

    /* ---------- settings ---------- */

    settingsDirty() {
        return Boolean(this.state.settings && this.state.savedSettings && JSON.stringify(this.state.settings) !== JSON.stringify(this.state.savedSettings));
    }

    async renderSettings() {
        const panel = this.panel;
        if (!this.state.settings) {
            panel.appendChild(h('div', { class: 'kt-skel' }));
            try {
                const settings = await api('GET', 'Settings');
                this.state.settings = settings;
                this.state.savedSettings = JSON.parse(JSON.stringify(settings));
            } catch (error) {
                clear(panel).appendChild(this.note('error', 'Settings could not be loaded', error.message, [h('button', { class: 'kt-btn', type: 'button', on: { click: () => this.renderPanel() } }, 'Try again')]));
                return;
            }
            clear(panel);
        }

        const s = this.state.settings;
        const form = h('form', { class: 'kt-form', novalidate: true });
        const savebar = h('div', { class: 'kt-savebar', hidden: true });
        const changed = () => {
            savebar.hidden = !this.settingsDirty();
        };
        form.addEventListener('input', changed);
        form.addEventListener('change', changed);

        form.append(
            this.librariesSection(s, changed),
            this.downloadSection(s),
            this.automationSection(s),
            this.youTubeSection(s),
            this.advancedSection(s, changed));

        const save = h('button', { class: 'kt-btn', type: 'submit' }, 'Save');
        const discard = h('button', { class: 'kt-btn kt-btn--ghost', type: 'button', on: { click: () => {
            this.state.settings = JSON.parse(JSON.stringify(this.state.savedSettings));
            this.renderPanel();
        } } }, 'Discard changes');
        savebar.append(h('p', { text: 'You have unsaved changes.' }), discard, save);
        form.appendChild(savebar);
        form.addEventListener('submit', async (event) => {
            event.preventDefault();
            if (!s.libraryIds.length && !window.confirm('No library is selected, so KometaThemes will do nothing. Save anyway?')) return;
            const saved = await this.busy(save, () => api('POST', 'Settings', { body: s }));
            if (!saved) return;
            this.state.settings = saved;
            this.state.savedSettings = JSON.parse(JSON.stringify(saved));
            this.state.library = null;
            this.state.overview = await api('GET', 'State').catch(() => this.state.overview);
            savebar.hidden = true;
            this.say('Settings saved. They apply from the next check.');
        });
        panel.appendChild(form);
    }

    librariesSection(s, changed) {
        const libraries = this.state.overview.libraries;
        const box = h('div', { class: 'kt-libraries' });
        if (!libraries.length) box.appendChild(h('p', { class: 'kt-muted', text: 'Jellyfin has no libraries yet.' }));
        for (const library of libraries) {
            const id = library.id;
            const row = this.checkbox(library.name, s.libraryIds.includes(id), (checked) => {
                s.libraryIds = checked ? [...new Set([...s.libraryIds, id])] : s.libraryIds.filter((x) => x !== id);
                changed();
            }, library.collectionType ? describeLibrary(library.collectionType) : null);
            box.appendChild(row);
        }
        return h('fieldset', { class: 'kt-section' },
            h('legend', { text: 'Libraries' }),
            h('p', { text: 'KometaThemes adds themes to the series and movies in these libraries, and leaves every other library alone.' }),
            box);
    }

    downloadSection(s) {
        const perSeason = this.checkbox('Each season gets its own themes', s.perSeason, (v) => { s.perSeason = v; },
            'A later season is matched to its own animethemes.moe entry through AniList, by year and number of episodes. When the match is not certain, the season plays the series themes.');
        const max = h('input', { class: 'kt-input kt-number', type: 'number', min: '1', max: '50', value: String(s.maxPerFolder) });
        max.addEventListener('input', () => { s.maxPerFolder = clamp(Number(max.value), 1, 50); });
        return h('fieldset', { class: 'kt-section' },
            h('legend', { text: 'What to download' }),
            h('p', { text: 'Jellyfin plays these on the series, season and movie pages, when theme songs or theme videos are turned on in the user’s Display settings.' }),
            this.mediaGroup(s.audio, 'Theme songs', 'MP3 files in theme-music/. Light and fast.', false),
            this.mediaGroup(s.video, 'Theme videos', 'The opening or ending video in backdrops/. Larger: one video is 20 to 80 MB.', true),
            perSeason,
            this.field('Most themes per series, season or movie', max, 'Applies to each kind separately, songs and videos.'));
    }

    mediaGroup(m, title, help, isVideo) {
        const group = h('div', { class: 'kt-group', dataset: { off: String(!m.enabled) } });
        const enabled = this.checkbox(title, m.enabled, (v) => {
            m.enabled = v;
            group.dataset.off = String(!v);
        }, help);
        const name = `kt-amount-${isVideo ? 'video' : 'audio'}`;
        const one = h('input', { type: 'radio', name, value: 'one', checked: m.amount === 'one' });
        const all = h('input', { type: 'radio', name, value: 'all', checked: m.amount !== 'one' });
        one.addEventListener('change', () => { if (one.checked) m.amount = 'one'; });
        all.addEventListener('change', () => { if (all.checked) m.amount = 'all'; });
        const volume = h('input', { type: 'range', min: '0', max: '100', step: '5', value: String(m.volume), 'aria-label': `${title} volume` });
        const out = h('output', { text: `${m.volume}%` });
        volume.addEventListener('input', () => {
            m.volume = Number(volume.value);
            out.textContent = `${m.volume}%`;
        });

        group.append(...[
            enabled,
            h('div', { class: 'kt-inline', role: 'group', 'aria-label': `${title}: which themes` },
                this.inlineCheck('Openings', m.openings, (v) => { m.openings = v; }),
                this.inlineCheck('Endings', m.endings, (v) => { m.endings = v; })),
            h('div', { class: 'kt-inline', role: 'radiogroup', 'aria-label': `${title}: how many` },
                h('label', { class: 'kt-radio' }, one, 'Only the main theme'),
                h('label', { class: 'kt-radio' }, all, 'Every theme')),
            this.checkbox('Skip versions with episode scenes over the song', m.skipOverlaps, (v) => { m.skipOverlaps = v; }),
            isVideo ? this.checkbox('Only creditless videos', m.creditlessOnly, (v) => { m.creditlessOnly = v; }, 'Videos without the staff credits on screen. Some themes have no such version.') : null,
            h('div', { class: 'kt-field' }, h('span', { class: 'kt-field-label', text: 'Volume' }), h('div', { class: 'kt-range' }, volume, out), h('p', { class: 'kt-help', text: 'Baked into the file. Changing it downloads the files again at the next check.' }))
        ].filter(Boolean));
        return group;
    }

    automationSection(s) {
        return h('fieldset', { class: 'kt-section' },
            h('legend', { text: 'Automatic work' }),
            this.checkbox('Add themes to new anime as they arrive', s.autoOnAdd, (v) => { s.autoOnAdd = v; }, 'A new series or movie is handled a couple of minutes after Jellyfin has downloaded its metadata.'),
            this.checkbox('Delete an anime’s themes when it leaves the library', s.cleanupOnRemove, (v) => { s.cleanupOnRemove = v; }, 'Only the files KometaThemes downloaded are deleted.'),
            h('p', { class: 'kt-help' }, 'The full check runs on Jellyfin’s schedule: Dashboard, Scheduled tasks, “Check anime themes”.'));
    }

    youTubeSection(s) {
        const yt = this.state.overview.youTube;
        const status = yt.available
            ? yt.backend === 'yt-dlp' ? 'Uses yt-dlp, found on this server.' : 'Uses the extractor built into KometaThemes.'
            : yt.error || 'Not available on this server.';
        return h('fieldset', { class: 'kt-section' },
            h('legend', { text: 'YouTube' }),
            this.checkbox('Allow adding songs from YouTube links', s.youTube, (v) => { s.youTube = v; }, `For themes animethemes.moe does not have. ${status} Downloading from YouTube may not be allowed for every video or in every country.`));
    }

    advancedSection(s, changed) {
        const order = h('ol', { class: 'kt-order', 'aria-label': 'Order of the IDs tried' });
        const renderOrder = () => {
            clear(order);
            s.providerPriority.forEach((provider, index) => {
                const move = (delta) => {
                    const next = index + delta;
                    if (next < 0 || next >= s.providerPriority.length) return;
                    const list = [...s.providerPriority];
                    [list[index], list[next]] = [list[next], list[index]];
                    s.providerPriority = list;
                    renderOrder();
                    changed();
                    order.querySelectorAll('button')[Math.max(0, next * 2 + (delta > 0 ? 1 : 0))]?.focus();
                };
                order.appendChild(h('li', {},
                    h('span', { text: provider }),
                    h('button', { class: 'kt-icon-btn', type: 'button', 'aria-label': `Move ${provider} up`, disabled: index === 0, on: { click: () => move(-1) } }, icon('up')),
                    h('button', { class: 'kt-icon-btn', type: 'button', 'aria-label': `Move ${provider} down`, disabled: index === s.providerPriority.length - 1, on: { click: () => move(1) } }, icon('down'))));
            });
        };
        renderOrder();

        const threshold = h('input', { type: 'range', min: '50', max: '100', step: '1', value: String(s.titleThreshold), 'aria-label': 'Title match confidence' });
        const thresholdOut = h('output', { text: `${s.titleThreshold}%` });
        threshold.addEventListener('input', () => {
            s.titleThreshold = Number(threshold.value);
            thresholdOut.textContent = `${s.titleThreshold}%`;
        });

        const number = (key, min, max) => {
            const input = h('input', { class: 'kt-input kt-number', type: 'number', min: String(min), max: String(max), value: String(s[key]) });
            input.addEventListener('input', () => { s[key] = clamp(Number(input.value), min, max); });
            return input;
        };

        const clearCache = h('button', { class: 'kt-btn kt-btn--ghost', type: 'button' }, 'Forget lookups and retry everything');
        clearCache.addEventListener('click', () => this.confirmButton(clearCache, 'Forget all lookups?', async () => {
            await this.busy(clearCache, () => api('POST', 'Cache/Clear'));
            this.say('Lookups forgotten. The next check asks animethemes.moe and AniList again for every anime.');
        }));

        return h('details', { class: 'kt-fold' },
            h('summary', { text: 'Advanced' }),
            h('div', { class: 'kt-form', style: 'padding-top:12px' },
                h('fieldset', { class: 'kt-section' },
                    h('legend', { text: 'Matching' }),
                    h('div', { class: 'kt-field' }, h('span', { class: 'kt-field-label', text: 'IDs tried, in order' }), order, h('p', { class: 'kt-help', text: 'Each anime is looked up by the first of these IDs it has in Jellyfin.' })),
                    this.checkbox('Match by title when no ID works', s.titleFallback, (v) => { s.titleFallback = v; }),
                    h('div', { class: 'kt-field' }, h('span', { class: 'kt-field-label', text: 'Title match confidence' }), h('div', { class: 'kt-range' }, threshold, thresholdOut), h('p', { class: 'kt-help', text: 'Higher means fewer automatic matches and fewer wrong ones.' }))),
                h('fieldset', { class: 'kt-section' },
                    h('legend', { text: 'Network and conversion' }),
                    h('div', { class: 'kt-inline' },
                        this.field('Requests per minute to animethemes.moe', number('rateLimit', 1, 90)),
                        this.field('Downloads at once per anime', number('parallel', 1, 4)),
                        this.field('Conversion time limit (seconds)', number('convertTimeout', 15, 600))),
                    h('div', { class: 'kt-inline' },
                        this.field('Reuse matches for (days)', number('matchCacheDays', 1, 365)),
                        this.field('Remember misses for (hours)', number('missCacheHours', 1, 720))),
                    h('div', {}, clearCache))));
    }

    checkbox(label, checked, set, help) {
        const id = `kt-c-${Math.random().toString(36).slice(2)}`;
        const input = h('input', { type: 'checkbox', checked, id, 'aria-labelledby': `${id}-l`, 'aria-describedby': help ? `${id}-d` : null });
        input.addEventListener('change', () => set(input.checked));
        return h('label', { class: 'kt-check-row', for: id }, input, h('span', { id: `${id}-l`, text: label }), help ? h('small', { id: `${id}-d`, text: help }) : null);
    }

    inlineCheck(label, checked, set) {
        const input = h('input', { type: 'checkbox', checked });
        input.addEventListener('change', () => set(input.checked));
        return h('label', { class: 'kt-radio' }, input, label);
    }

    /* ---------- first run ---------- */

    async renderSetup() {
        if (!this.state.setupDraft) {
            try {
                const settings = await api('GET', 'Settings');
                if (!settings.libraryIds.length) {
                    settings.libraryIds = this.state.overview.libraries.filter((library) => /anime|アニメ/i.test(library.name)).map((library) => library.id);
                }
                this.state.setupDraft = settings;
            } catch (error) {
                this.root.appendChild(this.note('error', 'Setup could not start', error.message, [h('button', { class: 'kt-btn', type: 'button', on: { click: () => this.start() } }, 'Try again')]));
                return;
            }
        }

        const s = this.state.setupDraft;
        const step = this.state.setupStep;
        const names = ['Libraries', 'What to download', 'Start'];
        const box = h('section', { class: 'kt-setup', 'aria-labelledby': 'kt-setup-title' });
        box.appendChild(h('h3', { id: 'kt-setup-title', text: 'Set up KometaThemes', style: 'font-size:1.3em' }));
        box.appendChild(h('ol', { class: 'kt-steps', 'aria-label': 'Setup steps' }, names.map((name, index) => h('li', { 'aria-current': index === step ? 'step' : null, text: name }))));

        const next = h('button', { class: 'kt-btn', type: 'button' }, step === 2 ? 'Save and start the first check' : 'Continue');
        const back = step > 0 ? h('button', { class: 'kt-btn kt-btn--ghost', type: 'button', on: { click: () => { this.state.setupStep--; this.render(); } } }, 'Back') : null;

        if (step === 0) {
            box.appendChild(this.librariesSection(s, () => { next.disabled = !s.libraryIds.length; }));
            next.disabled = !s.libraryIds.length;
            next.addEventListener('click', () => { this.state.setupStep = 1; this.render(); });
        } else if (step === 1) {
            box.appendChild(h('div', { class: 'kt-form' },
                this.mediaGroup(s.audio, 'Theme songs', 'Openings and endings as MP3, played on the anime’s page.', false),
                this.mediaGroup(s.video, 'Theme videos', 'The video version, for clients that show theme videos. Larger files.', true),
                this.checkbox('Each season gets its own themes', s.perSeason, (v) => { s.perSeason = v; }, 'Later seasons are matched to their own entries through AniList.')));
            next.addEventListener('click', () => { this.state.setupStep = 2; this.render(); });
        } else {
            const libs = this.state.overview.libraries.filter((library) => s.libraryIds.includes(library.id)).map((library) => library.name);
            const what = [s.audio.enabled ? `songs (${s.audio.amount === 'one' ? 'main theme' : 'every theme'})` : null, s.video.enabled ? `videos (${s.video.amount === 'one' ? 'main theme' : 'every theme'})` : null].filter(Boolean);
            box.appendChild(h('div', { class: 'kt-group' },
                h('p', {}, h('b', { text: 'Libraries: ' }), libs.join(', ') || 'none'),
                h('p', {}, h('b', { text: 'Downloads: ' }), what.join(' and ') || 'nothing yet'),
                h('p', {}, h('b', { text: 'Seasons: ' }), s.perSeason ? 'their own themes when the match is certain' : 'the series themes')));
            box.appendChild(h('p', { class: 'kt-muted', text: 'The first check looks up every anime, so it can take a while on a large library. You can use Jellyfin meanwhile, and change all of this later in Settings.' }));
            next.addEventListener('click', () => this.finishSetup(next));
        }
        box.appendChild(h('div', { class: 'kt-row-actions' }, back, next));
        this.root.appendChild(box);
    }

    async finishSetup(button) {
        const ok = await this.busy(button, async () => {
            await api('POST', 'Settings', { body: this.state.setupDraft });
            await api('POST', 'Setup/Complete');
            await api('POST', 'Check', { body: { retryProblems: false } });
            return true;
        });
        if (!ok) return;
        this.state.setupDraft = null;
        this.state.setupStep = 0;
        await this.loadOverview();
        this.state.tab = 'library';
        this.render();
        this.say('All set. The first check is running.');
    }
}

/* ------------------------------------------------------------------ small helpers */

function capitalize(text) {
    return text ? text.charAt(0).toUpperCase() + text.slice(1) : text;
}

function clamp(value, min, max) {
    return Number.isFinite(value) ? Math.min(max, Math.max(min, Math.round(value))) : min;
}

function episodesText(ranges) {
    const text = ranges.map(([start, end]) => (start === end ? String(start) : `${start}–${end}`)).join(', ');
    return (ranges.length === 1 && ranges[0][0] === ranges[0][1] ? 'Episode ' : 'Episodes ') + text;
}

function formatSize(bytes) {
    if (!bytes) return '';
    if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function describeLibrary(type) {
    return { tvshows: 'Shows', movies: 'Movies', mixed: 'Mixed content', homevideos: 'Home videos', music: 'Music' }[String(type).toLowerCase()] || String(type);
}

export default function KometaThemesController(view, params) {
    view.dataset.ktVersion = VERSION;
    view.dataset.ktPage = PAGE;
    this.page = new KometaPage(view, params || {});
}
