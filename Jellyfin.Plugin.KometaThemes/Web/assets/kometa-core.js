/* KometaThemes core — window.KT namespace.
   Shared by all plugin pages: API client (modern MediaBrowser auth),
   plugin config access, i18n, UI helpers (toast, confirm, sync poller,
   media preview player) and small DOM utilities. No toolchain, ES5-ish. */
(function () {
    'use strict';

    if (window.KT && window.KT.VERSION === '1.0.0.1') { return; }

    var KT = {
        VERSION: '1.0.0.1',
        GUID: '48c98707-45d1-43ac-94b8-f74d875ad29c',
        pages: (window.KT && window.KT.pages) || {}
    };

    /* ---------------- util ---------------- */

    var util = {
        esc: function (value) {
            return String(value == null ? '' : value)
                .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
        },
        el: function (tag, className, text) {
            var node = document.createElement(tag);
            if (className) { node.className = className; }
            if (text != null) { node.textContent = text; }
            return node;
        },
        clear: function (node) {
            while (node && node.firstChild) { node.removeChild(node.firstChild); }
        },
        /* Reads a query parameter from the dashboard hash (#/configurationpage?name=X&itemId=Y)
           with a fallback to the regular search string. */
        param: function (name) {
            try {
                var hash = window.location.hash || '';
                var qs = hash.indexOf('?') > -1 ? hash.slice(hash.indexOf('?') + 1) : '';
                var fromHash = new URLSearchParams(qs).get(name);
                if (fromHash) { return fromHash; }
                return new URLSearchParams(window.location.search).get(name);
            } catch (e) {
                return null;
            }
        },
        getItemId: function () { return util.param('itemId') || util.param('id'); },
        debounce: function (fn, ms) {
            var timer = null;
            return function () {
                var args = arguments, self = this;
                if (timer) { clearTimeout(timer); }
                timer = setTimeout(function () { timer = null; fn.apply(self, args); }, ms);
            };
        },
        formatDate: function (iso) {
            if (!iso) { return '-'; }
            try { return new Date(iso).toLocaleString(); } catch (e) { return String(iso); }
        },
        navigate: function (url) {
            try { Dashboard.navigate(url); } catch (e) { window.location.hash = '#/' + url; }
        }
    };

    /* ---------------- api ---------------- */

    function authHeader() {
        try {
            if (typeof ApiClient !== 'undefined' && typeof ApiClient.accessToken === 'function') {
                var token = ApiClient.accessToken();
                if (token) { return 'MediaBrowser Token="' + token + '"'; }
            }
        } catch (e) { /* fall through */ }
        return null;
    }

    function apiUrl(path) {
        try {
            if (typeof ApiClient !== 'undefined' && typeof ApiClient.getUrl === 'function') {
                return ApiClient.getUrl(path);
            }
        } catch (e) { /* fall through */ }
        return path;
    }

    function request(method, path, body) {
        var headers = { Accept: 'application/json' };
        var auth = authHeader();
        if (auth) { headers.Authorization = auth; }
        var options = { method: method, credentials: 'same-origin', headers: headers };
        if (body !== undefined) {
            headers['Content-Type'] = 'application/json';
            options.body = JSON.stringify(body);
        }
        return fetch(apiUrl(path), options).then(function (response) {
            var isJson = (response.headers.get('Content-Type') || '').indexOf('json') > -1;
            if (!response.ok) {
                return (isJson ? response.json() : response.text()).catch(function () { return null; })
                    .then(function (payload) {
                        var message = (payload && (payload.error || payload.message)) ||
                            (typeof payload === 'string' && payload) ||
                            ('HTTP ' + response.status);
                        var error = new Error(message);
                        error.status = response.status;
                        error.payload = payload;
                        throw error;
                    });
            }
            if (response.status === 204 || !isJson) { return null; }
            return response.json();
        });
    }

    KT.api = {
        get: function (path) { return request('GET', path); },
        post: function (path, body) { return request('POST', path, body); },
        del: function (path) { return request('DELETE', path); },
        /* GET a binary endpoint and trigger a browser download. */
        download: function (path, filename) {
            var headers = {};
            var auth = authHeader();
            if (auth) { headers.Authorization = auth; }
            return fetch(apiUrl(path), { credentials: 'same-origin', headers: headers })
                .then(function (response) {
                    if (!response.ok) { throw new Error('HTTP ' + response.status); }
                    return response.blob();
                })
                .then(function (blob) {
                    var url = URL.createObjectURL(blob);
                    var link = util.el('a');
                    link.href = url;
                    link.download = filename;
                    document.body.appendChild(link);
                    link.click();
                    link.remove();
                    setTimeout(function () { URL.revokeObjectURL(url); }, 5000);
                });
        }
    };

    /* ---------------- plugin configuration ---------------- */

    KT.config = {
        load: function () {
            return ApiClient.getPluginConfiguration(KT.GUID).then(function (config) {
                KT.i18n.setLang(config.UiLanguage || 'en');
                return config;
            });
        },
        save: function (config) {
            return ApiClient.updatePluginConfiguration(KT.GUID, config);
        }
    };

    /* ---------------- i18n ---------------- */

    var dict = {
        en: {
            save: 'Save', discard: 'Discard', cancel: 'Cancel', confirm: 'Confirm',
            close: 'Close', refresh: 'Refresh', loading: 'Loading…', retry: 'Retry',
            saved: 'Settings saved', saveFailed: 'Save failed', error: 'Something went wrong',
            audio: 'Audio', video: 'Video', downloaded: 'Downloaded', missing: 'Missing',
            preview: 'Preview', delete: 'Delete', deleteAll: 'Delete all',
            syncNow: 'Sync now', dryRun: 'Dry run', syncRunning: 'Sync in progress…',
            syncDone: 'Sync finished', syncStartFailed: 'Could not start sync',
            phase: 'Phase', processed: 'Processed', resolved: 'Resolved',
            downloadedCount: 'Downloaded', failed: 'Failed', skipped: 'Skipped',
            idle: 'Idle', never: 'Never', unsavedChanges: 'Unsaved changes',
            confirmTitle: 'Are you sure?', themeFinder: 'Theme Finder',
            openThemeFinder: 'Open Theme Finder', settings: 'Settings',
            dotIdle: 'No sync running', dotRunning: 'Sync in progress', dotError: 'Last sync failed'
        },
        it: {
            save: 'Salva', discard: 'Annulla modifiche', cancel: 'Annulla', confirm: 'Conferma',
            close: 'Chiudi', refresh: 'Aggiorna', loading: 'Caricamento…', retry: 'Riprova',
            saved: 'Impostazioni salvate', saveFailed: 'Salvataggio fallito', error: 'Qualcosa è andato storto',
            audio: 'Audio', video: 'Video', downloaded: 'Scaricato', missing: 'Mancante',
            preview: 'Anteprima', delete: 'Elimina', deleteAll: 'Elimina tutti',
            syncNow: 'Sync ora', dryRun: 'Dry run', syncRunning: 'Sync in corso…',
            syncDone: 'Sync completato', syncStartFailed: 'Impossibile avviare il sync',
            phase: 'Fase', processed: 'Processati', resolved: 'Risolti',
            downloadedCount: 'Scaricati', failed: 'Falliti', skipped: 'Saltati',
            idle: 'Inattivo', never: 'Mai', unsavedChanges: 'Modifiche non salvate',
            confirmTitle: 'Sei sicuro?', themeFinder: 'Theme Finder',
            openThemeFinder: 'Apri Theme Finder', settings: 'Impostazioni',
            dotIdle: 'Nessun sync in corso', dotRunning: 'Sync in corso', dotError: 'Ultimo sync fallito'
        }
    };

    KT.i18n = {
        lang: 'en',
        setLang: function (lang) { KT.i18n.lang = dict[lang] ? lang : 'en'; },
        extend: function (extra) {
            Object.keys(extra).forEach(function (lang) {
                dict[lang] = dict[lang] || {};
                Object.keys(extra[lang]).forEach(function (key) { dict[lang][key] = extra[lang][key]; });
            });
        },
        apply: function (root) {
            (root || document).querySelectorAll('[data-kt]').forEach(function (node) {
                node.textContent = KT.t(node.getAttribute('data-kt'));
            });
            (root || document).querySelectorAll('[data-kt-placeholder]').forEach(function (node) {
                node.placeholder = KT.t(node.getAttribute('data-kt-placeholder'));
            });
        }
    };

    KT.t = function (key, params) {
        var lang = dict[KT.i18n.lang] || dict.en;
        var text = lang[key] != null ? lang[key] : (dict.en[key] != null ? dict.en[key] : key);
        if (params) {
            Object.keys(params).forEach(function (name) {
                text = text.replace(new RegExp('\\{' + name + '\\}', 'g'), params[name]);
            });
        }
        return text;
    };

    /* ---------------- ui ---------------- */

    var ui = {};

    ui.loading = function (show) {
        try {
            if (show) { Dashboard.showLoadingMsg(); } else { Dashboard.hideLoadingMsg(); }
        } catch (e) { /* dashboard global missing — pages render their own skeletons */ }
    };

    ui.toast = function (message, type, timeout) {
        var host = document.querySelector('.kt-toast-host');
        if (!host) {
            host = util.el('div', 'kt-toast-host');
            document.body.appendChild(host);
        }
        var toast = util.el('div', 'kt-toast' + (type ? ' ' + type : ''), message);
        host.appendChild(toast);
        setTimeout(function () {
            toast.classList.add('leaving');
            setTimeout(function () { toast.remove(); }, 220);
        }, timeout || 3200);
    };

    ui.confirm = function (message, title) {
        return new Promise(function (resolve) {
            var veil = util.el('div', 'kt-modal-veil');
            var modal = util.el('div', 'kt-modal kt-page');
            modal.appendChild(util.el('h3', null, title || KT.t('confirmTitle')));
            modal.appendChild(util.el('p', null, message));
            var row = util.el('div', 'kt-row');
            var btnCancel = util.el('button', 'kt-btn kt-btn-ghost', KT.t('cancel'));
            var btnOk = util.el('button', 'kt-btn kt-btn-primary', KT.t('confirm'));
            btnCancel.type = 'button';
            btnOk.type = 'button';
            row.appendChild(btnCancel);
            row.appendChild(btnOk);
            modal.appendChild(row);
            veil.appendChild(modal);
            function done(value) { veil.remove(); resolve(value); }
            btnCancel.addEventListener('click', function () { done(false); });
            btnOk.addEventListener('click', function () { done(true); });
            veil.addEventListener('click', function (e) { if (e.target === veil) { done(false); } });
            document.body.appendChild(veil);
            btnOk.focus();
        });
    };

    /* Shared sync status poller. onUpdate(status) is called on every tick;
       polling stops automatically once status.isFinished is true. */
    ui.syncPoller = function (onUpdate, intervalMs) {
        var timer = null;
        var poller = {
            running: false,
            start: function () {
                if (poller.running) { return; }
                poller.running = true;
                tick();
                timer = setInterval(tick, intervalMs || 2000);
            },
            stop: function () {
                poller.running = false;
                if (timer) { clearInterval(timer); timer = null; }
            }
        };
        function tick() {
            KT.api.get('Plugins/KometaThemes/Sync/status').then(function (status) {
                onUpdate(status);
                if (status && status.isFinished) { poller.stop(); }
            }).catch(function () { /* keep polling */ });
        }
        return poller;
    };

    /* Live sync indicator dot for the page heroes. Polls slowly while idle and
       tightens up while a sync is running; pauses when the tab is hidden or the
       element left the DOM. Returns { stop }. */
    ui.attachSyncDot = function (el) {
        if (!el) { return { stop: function () {} }; }
        var IDLE_MS = 15000;
        var ACTIVE_MS = 2500;
        var timer = null;
        var stopped = false;

        function apply(status) {
            var cls = 'kt-live-dot ';
            var titleKey;
            if (status && status.phase === 'failed') {
                cls += 'is-error';
                titleKey = 'dotError';
            } else if (status && !status.isFinished && status.phase && status.phase !== 'idle') {
                cls += 'is-running';
                titleKey = 'dotRunning';
            } else {
                cls += 'is-idle';
                titleKey = 'dotIdle';
            }
            el.className = cls;
            el.title = KT.t(titleKey);
            return cls.indexOf('is-running') > -1;
        }

        function schedule(ms) {
            if (stopped) { return; }
            timer = setTimeout(tick, ms);
        }

        function tick() {
            if (stopped) { return; }
            if (document.hidden || !el.isConnected) {
                schedule(IDLE_MS);
                return;
            }
            KT.api.get('Plugins/KometaThemes/Sync/status').then(function (status) {
                schedule(apply(status) ? ACTIVE_MS : IDLE_MS);
            }).catch(function () {
                schedule(IDLE_MS);
            });
        }

        tick();
        return {
            stop: function () {
                stopped = true;
                if (timer) { clearTimeout(timer); timer = null; }
            }
        };
    };

    /* Renders a sync status object into a .kt-progress block (fill, phase, counters). */
    ui.renderSyncStatus = function (root, status) {
        if (!root || !status) { return; }
        var fill = root.querySelector('.kt-progress-fill');
        if (fill) {
            fill.classList.toggle('indeterminate', !status.isFinished && status.totalItems === 0);
            fill.style.width = (status.isFinished ? 100 : (status.progressPercent || 0)) + '%';
        }
        var text = root.querySelector('[data-kt-sync-text]');
        if (text) {
            var label = status.isFinished ? KT.t('syncDone') : (status.message || status.phase || KT.t('idle'));
            if (!status.isFinished && status.totalItems > 0) {
                label += ' (' + status.processedItems + '/' + status.totalItems + ')';
            }
            text.textContent = label;
        }
        var counters = root.querySelector('[data-kt-sync-counters]');
        if (counters) {
            util.clear(counters);
            [[KT.t('phase'), status.phase || '-'],
             [KT.t('processed'), status.processedItems],
             [KT.t('resolved'), status.resolvedItems],
             [KT.t('downloadedCount'), status.downloadedItems],
             [KT.t('skipped'), status.skippedItems]].forEach(function (pair) {
                var span = util.el('span', null, pair[0] + ': ');
                span.appendChild(util.el('strong', null, String(pair[1] != null ? pair[1] : '-')));
                counters.appendChild(span);
            });
        }
    };

    /* Shared click-to-load media preview (one floating player at a time). */
    ui.player = (function () {
        var current = null;
        function close() {
            if (current) { current.remove(); current = null; }
        }
        function open(url, mediaType, title) {
            close();
            var panel = util.el('div', 'kt-player kt-page');
            var head = util.el('div', 'kt-player-head');
            var badge = util.el('span', 'kt-badge ' + (mediaType === 'video' ? 'ed' : 'op'), mediaType === 'video' ? KT.t('video') : KT.t('audio'));
            badge.classList.add('kt-player-badge');
            var label = util.el('div', 'kt-player-title', title || KT.t('preview'));
            var btnClose = util.el('button', 'kt-player-close', '✕');
            btnClose.type = 'button';
            btnClose.setAttribute('aria-label', KT.t('close'));
            btnClose.addEventListener('click', close);
            head.appendChild(badge);
            head.appendChild(label);
            head.appendChild(btnClose);
            panel.appendChild(head);
            var media = document.createElement(mediaType === 'video' ? 'video' : 'audio');
            media.controls = true;
            media.autoplay = true;
            media.preload = 'none';
            media.src = url;
            media.volume = 0.65;
            media.addEventListener('error', function () {
                ui.toast(KT.t('error'), 'error');
            });
            panel.appendChild(media);
            document.body.appendChild(panel);
            current = panel;
        }
        return { open: open, close: close, toggle: function (url, mediaType, title) {
            if (current && current.dataset.url === url) { close(); return; }
            open(url, mediaType, title);
            if (current) { current.dataset.url = url; }
        } };
    })();

    KT.util = util;
    KT.ui = ui;
    window.KT = KT;
})();
