(function () {
    'use strict';

    var STATE_KEY = '__KometaThemesItemButton';
    var BTN_ID = 'kometaThemesBtn';
    var BTN_CLASS = 'button-flat btnKometaThemes detailButton';
    var PAGE_NAME = 'KometaThemesSearch';

    if (window[STATE_KEY] && typeof window[STATE_KEY].destroy === 'function') {
        window[STATE_KEY].destroy();
    }

    var state = {
        observer: null,
        injectTimer: null,
        retryTimer: null,
        fallbackInterval: null,
        currentItemId: null,
        pending: null,
        itemTypeCache: {},
        listeners: [],
        destroy: destroy
    };
    window[STATE_KEY] = state;

    // Jellyfin web routes vary across setups: hash ('#/details?id=…'),
    // legacy hashbang ('#!/details?id=…') and path-based routing.
    function getRouteCandidates() {
        var candidates = [];
        var hash = window.location.hash || '';
        if (hash) {
            candidates.push(hash.replace(/^#!?/, ''));
        }

        candidates.push((window.location.pathname || '') + (window.location.search || ''));
        return candidates;
    }

    function isDetailPage() {
        var candidates = getRouteCandidates();
        for (var i = 0; i < candidates.length; i++) {
            if (candidates[i].indexOf('/details') > -1 || candidates[i].indexOf('details?') === 0) {
                return true;
            }
        }

        return false;
    }

    function getItemId() {
        var candidates = getRouteCandidates();
        for (var i = 0; i < candidates.length; i++) {
            try {
                var qs = candidates[i].split('?')[1] || '';
                var id = new URLSearchParams(qs).get('id');
                if (id) return id;
            } catch (e) {
                // try the next candidate
            }
        }

        return null;
    }

    function getVisibleDetailButtons() {
        var containers = document.querySelectorAll('.mainDetailButtons');
        // Newest page is appended last — iterate in reverse so a stale cached
        // page earlier in the DOM never wins over the page being shown.
        for (var i = containers.length - 1; i >= 0; i--) {
            var container = containers[i];
            var page = container.closest('.page, [data-role="page"]');
            if (!page || isVisible(page)) {
                return container;
            }
        }

        return null;
    }

    function isVisible(element) {
        return !!(element.offsetWidth || element.offsetHeight || element.getClientRects().length);
    }

    function getCurrentUserId() {
        try {
            if (typeof ApiClient !== 'undefined' && typeof ApiClient.getCurrentUserId === 'function') {
                return ApiClient.getCurrentUserId();
            }
        } catch (e) {
            return null;
        }

        return null;
    }

    function fetchItem(itemId) {
        if (!itemId || typeof ApiClient === 'undefined') {
            return Promise.resolve(null);
        }

        var userId = getCurrentUserId();
        if (typeof ApiClient.getItem === 'function' && userId) {
            return ApiClient.getItem(userId, itemId);
        }

        if (typeof ApiClient.getUrl === 'function') {
            var path = userId ?
                'Users/' + encodeURIComponent(userId) + '/Items/' + encodeURIComponent(itemId) :
                'Items/' + encodeURIComponent(itemId);
            var headers = { Accept: 'application/json' };
            if (typeof ApiClient.accessToken === 'function') {
                var token = ApiClient.accessToken();
                // Modern auth scheme — X-Emby-Token is removed in Jellyfin 10.13.
                if (token) headers.Authorization = 'MediaBrowser Token="' + token + '"';
            }

            return fetch(ApiClient.getUrl(path), { credentials: 'same-origin', headers: headers })
                .then(function (response) { return response.ok ? response.json() : null; });
        }

        return Promise.resolve(null);
    }

    var isAdminPromise = null;
    function isAdmin() {
        if (!isAdminPromise) {
            isAdminPromise = (typeof ApiClient !== 'undefined' && typeof ApiClient.getCurrentUser === 'function'
                ? ApiClient.getCurrentUser()
                : Promise.reject(new Error('ApiClient unavailable'))
            ).then(function (user) {
                if (!user) {
                    // No user object — treat as transient, allow a retry later.
                    isAdminPromise = null;
                    return false;
                }

                return !!(user.Policy && user.Policy.IsAdministrator);
            }).catch(function () {
                // Transient failure (startup, network) — don't memoize false forever.
                isAdminPromise = null;
                return false;
            });
        }

        return isAdminPromise;
    }

    function isSupportedItemType(item) {
        return item && (item.Type === 'Series' || item.Type === 'Movie');
    }

    function removeButton() {
        var old = document.getElementById(BTN_ID);
        if (old) old.remove();
    }

    function createButton(itemId) {
        var btn = document.createElement('button');
        btn.id = BTN_ID;
        btn.dataset.itemId = itemId;
        btn.setAttribute('is', 'emby-button');
        btn.type = 'button';
        btn.className = BTN_CLASS;
        btn.title = 'KometaThemes';
        btn.setAttribute('aria-label', 'KometaThemes Theme Finder');

        var div = document.createElement('div');
        div.className = 'detailButton-content';

        var span = document.createElement('span');
        span.className = 'material-icons detailButton-icon music_note';
        span.setAttribute('aria-hidden', 'true');
        span.style.color = '#00a4dc';

        div.appendChild(span);
        btn.appendChild(div);

        btn.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            var currentItemId = btn.dataset.itemId || getItemId();
            if (!currentItemId) return;

            var url = buildThemeFinderUrl(currentItemId);
            try {
                Dashboard.navigate(url);
            } catch (ex) {
                window.location.hash = '#!/' + url;
            }
        });

        return btn;
    }

    function buildThemeFinderUrl(itemId) {
        return 'configurationpage?name=' + PAGE_NAME + '&itemId=' + encodeURIComponent(itemId);
    }

    function injectButton(itemId) {
        var container = getVisibleDetailButtons();
        if (!container || getItemId() !== itemId) {
            return;
        }

        var old = document.getElementById(BTN_ID);
        if (old && old.dataset.itemId === itemId && old.parentElement === container) {
            return;
        }

        removeButton();
        var moreBtn = container.querySelector('.btnMoreCommands');
        var btn = createButton(itemId);
        if (moreBtn) {
            container.insertBefore(btn, moreBtn);
        } else {
            container.appendChild(btn);
        }
    }

    // Idempotent: safe to call from the observer, the fallback interval and
    // navigation events. Never tears down in-flight work for the same item —
    // a lookup slower than the 1.5s fallback tick must still land the button.
    function ensureButton() {
        if (!isDetailPage()) {
            removeButton();
            state.currentItemId = null;
            return;
        }

        var itemId = getItemId();
        if (!itemId) {
            // Transient hash state during navigation — keep whatever is there.
            return;
        }

        if (itemId !== state.currentItemId) {
            state.currentItemId = itemId;
            removeButton();
            if (state.pending && state.pending.itemId !== itemId) {
                state.pending = null;
            }
        }

        var cached = state.itemTypeCache[itemId];
        if (cached === 'unsupported') {
            return;
        }

        if (cached === 'supported') {
            // The Theme Finder APIs require elevation — admins only. The admin
            // promise is memoized, so this resolves instantly after first check.
            isAdmin().then(function (admin) {
                if (admin && isDetailPage()) {
                    injectButton(itemId);
                }
            });
            return;
        }

        if (state.pending && state.pending.itemId === itemId) {
            return;
        }

        var pending = { itemId: itemId };
        state.pending = pending;
        pending.promise = Promise.all([fetchItem(itemId), isAdmin()]).then(function (results) {
            var item = results[0];
            var admin = results[1];
            if (state.pending === pending) {
                state.pending = null;
            }

            if (item) {
                // Only cache real lookups; network failures stay uncached so
                // the next tick retries. Admin is evaluated per attempt — a
                // transient auth failure must not poison the type cache.
                state.itemTypeCache[itemId] = isSupportedItemType(item) ? 'supported' : 'unsupported';
            }

            if (admin && state.itemTypeCache[itemId] === 'supported' && isDetailPage()) {
                injectButton(itemId);
            }
        }).catch(function () {
            if (state.pending === pending) {
                state.pending = null;
            }
        });
    }

    function scheduleEnsure(delay) {
        if (state.injectTimer) clearTimeout(state.injectTimer);
        state.injectTimer = setTimeout(function () {
            state.injectTimer = null;
            ensureButton();
        }, delay || 120);
    }

    function handleNavigation() {
        // ensureButton removes the button itself when the item really changed.
        scheduleEnsure(80);
    }

    function addWindowListener(name, handler) {
        window.addEventListener(name, handler);
        state.listeners.push({ name: name, handler: handler });
    }

    function startObserver() {
        if (!document.body) {
            state.retryTimer = setTimeout(startObserver, 100);
            return;
        }

        state.observer = new MutationObserver(function () {
            scheduleEnsure(160);
        });
        state.observer.observe(document.body, { childList: true, subtree: true });

        addWindowListener('hashchange', handleNavigation);
        addWindowListener('popstate', handleNavigation);
        addWindowListener('pageshow', function () { scheduleEnsure(60); });

        state.fallbackInterval = setInterval(function () {
            ensureButton();
        }, 1500);

        ensureButton();
    }

    function destroy() {
        if (state.injectTimer) clearTimeout(state.injectTimer);
        if (state.retryTimer) clearTimeout(state.retryTimer);
        if (state.fallbackInterval) clearInterval(state.fallbackInterval);
        if (state.observer) state.observer.disconnect();

        state.listeners.forEach(function (listener) {
            window.removeEventListener(listener.name, listener.handler);
        });
        state.listeners = [];
        state.pending = null;
        removeButton();
    }

    startObserver();
})();
