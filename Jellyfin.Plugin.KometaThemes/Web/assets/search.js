/* KometaThemes — Theme Finder logic (KT.pages.search).
   Guided flow: search → pick anime → pick themes (per season) → download. */
(function () {
    'use strict';

    var KT = window.KT;
    var util = KT.util;

    KT.i18n.extend({
        en: {
            finderKicker: 'Theme Finder',
            finderTarget: 'Target item',
            finderNoItemBody: 'Open the Theme Finder from a series or movie detail page (♪ button).',
            step1: 'Search', step2: 'Pick anime', step3: 'Pick themes',
            searchTitle: 'Search animethemes.moe', searchLabel: 'Title', searchYear: 'Year', searchBtn: 'Search',
            searching: 'Searching…', searchEmpty: 'No confident matches. Check the broad results or adjust the title.',
            searchFailed: 'Search failed',
            resultsTitle: 'Results', broadResults: 'Broad results (lower confidence)',
            detailEmpty: 'Pick a result on the left to inspect its themes.',
            loadingThemes: 'Loading themes…',
            confidenceExact: 'Exact', confidenceStrong: 'Strong', confidenceWeak: 'Weak',
            seasonGroup: 'Season {n}', seasonEpisodes: 'ep. {range}', otherThemes: 'Other themes',
            creditless: 'creditless', overlap: 'overlap',
            selCount: '{n} selected',
            selReviewTitle: 'Selected themes', selReviewEmpty: 'Nothing selected yet.', selRemove: 'Remove',
            bulkOpAudio: 'All OP audio', bulkEdAudio: 'All ED audio', bulkVideo: 'All video', bulkClear: 'Clear',
            download: 'Download', downloading: 'Downloading…',
            downloadDone: 'Download finished: {ok} ok, {fail} failed',
            alreadyDownloaded: 'Already downloaded',
            previewFailedHint: 'Preview failed — open on animethemes.moe',
            noItemTitle: 'No item selected',
            bindingSaved: 'Manual binding saved',
            saveBinding: 'Save binding only',
            bindingExists: 'Bound to: {anime}',
            removeBinding: 'Remove binding',
            bindingRemoved: 'Binding removed',
            saveBindingFailed: 'Failed to save binding',
            filterAudio: 'Audio', filterVideo: 'Video', filterOP: 'OP', filterED: 'ED', filterCreditless: 'Creditless only', filterClear: 'Clear filters', filterAllVisible: 'Select visible',
            noVisibleThemes: 'No themes match the active filters.'
        },
        it: {
            finderKicker: 'Theme Finder',
            finderTarget: 'Elemento di destinazione',
            finderNoItemBody: 'Apri il Theme Finder dalla pagina dei dettagli di una serie o un film (pulsante ♪).',
            step1: 'Cerca', step2: 'Scegli anime', step3: 'Scegli temi',
            searchTitle: 'Cerca su animethemes.moe', searchLabel: 'Titolo', searchYear: 'Anno', searchBtn: 'Cerca',
            searching: 'Ricerca in corso…', searchEmpty: 'Nessun match affidabile. Controlla i risultati estesi o modifica il titolo.',
            searchFailed: 'Ricerca fallita',
            resultsTitle: 'Risultati', broadResults: 'Risultati estesi (bassa confidenza)',
            detailEmpty: 'Scegli un risultato a sinistra per esaminarne i temi.',
            loadingThemes: 'Caricamento temi…',
            confidenceExact: 'Esatto', confidenceStrong: 'Forte', confidenceWeak: 'Debole',
            seasonGroup: 'Stagione {n}', seasonEpisodes: 'ep. {range}', otherThemes: 'Altri temi',
            creditless: 'creditless', overlap: 'overlap',
            selCount: '{n} selezionati',
            selReviewTitle: 'Temi selezionati', selReviewEmpty: 'Niente selezionato.', selRemove: 'Rimuovi',
            bulkOpAudio: 'Tutti OP audio', bulkEdAudio: 'Tutte ED audio', bulkVideo: 'Tutti video', bulkClear: 'Svuota',
            download: 'Scarica', downloading: 'Download in corso…',
            downloadDone: 'Download completato: {ok} ok, {fail} falliti',
            alreadyDownloaded: 'Già scaricato',
            previewFailedHint: 'Anteprima non riuscita — apri su animethemes.moe',
            noItemTitle: 'Nessun elemento selezionato',
            bindingSaved: 'Binding manuale salvato',
            saveBinding: 'Salva solo binding',
            bindingExists: 'Bindato a: {anime}',
            removeBinding: 'Rimuovi binding',
            bindingRemoved: 'Binding rimosso',
            saveBindingFailed: 'Salvataggio binding fallito',
            filterAudio: 'Audio', filterVideo: 'Video', filterOP: 'OP', filterED: 'ED', filterCreditless: 'Solo creditless', filterClear: 'Pulisci filtri', filterAllVisible: 'Seleziona visibili',
            noVisibleThemes: 'Nessun tema corrisponde ai filtri attivi.'
        }
    });

    var state = {
        page: null,
        itemId: null,
        itemInfo: null,
        results: [],
        broadResults: [],
        selectedAnimeId: null,
        selectedAnimeName: null,
        selectedAnimeSlug: null,
        themes: [],
        seasonGroups: [],
        /* selection keyed by media URL (stable + globally unique across anime/seasons),
           value = {url, mediaType, themeName, animeId, animeName}. Persists across results. */
        selected: {},
        currentBinding: null,
        downloading: false,
        // Active filters for step 3 (type + creditless)
        filters: { audio: true, video: true, op: true, ed: true, creditless: false },
        // For keyboard nav in results
        resultsActiveIndex: -1,
        broadActiveIndex: -1,
        contextRequestId: 0,
        searchRequestId: 0,
        detailRequestId: 0
    };

    function q(id) { return state.page.querySelector('#' + id); }

    /* ---- stepper ---- */

    function setStep(step) {
        state.page.querySelectorAll('.kt-step').forEach(function (node) {
            var index = parseInt(node.dataset.step, 10);
            node.classList.toggle('active', index === step);
            node.classList.toggle('done', index < step);
        });
    }

    /* ---- hero / item context ---- */

    function loadItemContext() {
        var itemId = state.itemId;
        var contextId = ++state.contextRequestId;
        KT.api.get('Plugins/KometaThemes/Items/' + encodeURIComponent(itemId) + '/info').then(function (info) {
            if (contextId !== state.contextRequestId || itemId !== state.itemId) { return; }
            state.itemInfo = info;
            q('ktFinderTitle').textContent = info.name;
            var chips = q('ktFinderChips');
            util.clear(chips);
            [info.type, info.productionYear, info.originalTitle && info.originalTitle !== info.name ? info.originalTitle : null]
                .forEach(function (value) {
                    if (value) { chips.appendChild(util.el('span', 'kt-chip', String(value))); }
                });

            var paths = q('ktFinderPaths');
            util.clear(paths);
            [['Dir', info.directoryPath], ['File', info.filePath]].forEach(function (pair) {
                if (!pair[1]) { return; }
                var row = util.el('div', 'kt-path');
                row.appendChild(util.el('span', 'kt-path-label', pair[0]));
                row.appendChild(util.el('span', 'kt-path-value', pair[1]));
                var copy = util.el('button', 'kt-btn kt-btn-sm kt-btn-ghost', KT.t('copy'));
                copy.type = 'button';
                copy.addEventListener('click', function () {
                    if (!navigator.clipboard) { return; }
                    navigator.clipboard.writeText(pair[1]).then(function () {
                        copy.textContent = KT.t('copied');
                        setTimeout(function () { copy.textContent = KT.t('copy'); }, 1200);
                    }).catch(function () { KT.ui.toast(KT.t('error'), 'error'); });
                });
                row.appendChild(copy);
                paths.appendChild(row);
            });

            q('ktSearchInput').value = info.name || '';
            q('ktSearchYear').value = info.productionYear || '';
            loadBinding(contextId, itemId);
            runSearch();

            ApiClient.getItem(ApiClient.getCurrentUserId(), itemId).then(function (item) {
                if (contextId !== state.contextRequestId || itemId !== state.itemId) { return; }
                if (item.ImageTags && item.ImageTags.Primary) {
                    var poster = q('ktFinderPoster');
                    util.clear(poster);
                    var img = util.el('img');
                    img.alt = item.Name;
                    var imageUrl = util.safeUrl(ApiClient.getScaledImageUrl(item.Id, { type: 'Primary', maxWidth: 200, tag: item.ImageTags.Primary }));
                    if (imageUrl) { img.src = imageUrl; poster.appendChild(img); }
                }
                if (item.BackdropImageTags && item.BackdropImageTags.length) {
                    var backdrop = q('ktFinderBackdrop');
                    var backdropUrl = ApiClient.getScaledImageUrl(item.Id, { type: 'Backdrop', maxWidth: 1280, tag: item.BackdropImageTags[0] });
                    backdrop.style.display = util.setBackgroundImage(backdrop, backdropUrl) ? '' : 'none';
                }
            }).catch(function () { /* poster is optional */ });
        }).catch(function (error) {
            if (contextId !== state.contextRequestId || itemId !== state.itemId) { return; }
            q('ktBtnSearch').disabled = false;
            q('ktFinderTitle').textContent = itemId;
            setSearchState(error.message || KT.t('error'), 'error');
        });
    }

    function loadBinding(contextId, itemId) {
        var expectedContext = contextId == null ? state.contextRequestId : contextId;
        var expectedItemId = itemId || state.itemId;
        KT.api.get('Plugins/KometaThemes/Items/' + encodeURIComponent(expectedItemId) + '/binding').then(function (data) {
            if (expectedContext !== state.contextRequestId || expectedItemId !== state.itemId) { return; }
            state.currentBinding = data && data.hasBinding ? data : null;
            renderBinding();
        }).catch(function () {
            if (expectedContext !== state.contextRequestId || expectedItemId !== state.itemId) { return; }
            state.currentBinding = null;
            renderBinding();
        });
    }

    function renderBinding() {
        var existing = state.page.querySelector('.kt-binding-banner');
        if (existing) { existing.remove(); }
        if (!state.currentBinding) { return; }

        var banner = util.el('div', 'kt-binding-banner');
        banner.appendChild(util.el('span', 'kt-badge success', KT.t('bindingExists', { anime: state.currentBinding.animeName })));
        var remove = util.el('button', 'kt-btn kt-btn-sm kt-btn-ghost', KT.t('removeBinding'));
        remove.type = 'button';
        remove.addEventListener('click', function () {
            KT.api.del('Plugins/KometaThemes/Bindings/' + encodeURIComponent(state.itemId)).then(function () {
                state.currentBinding = null;
                renderBinding();
                KT.ui.toast(KT.t('bindingRemoved'), 'success');
            }).catch(function (error) { KT.ui.toast(error.message || KT.t('error'), 'error'); });
        });
        banner.appendChild(remove);

        var title = q('ktFinderTitle');
        if (title && title.parentElement) {
            title.parentElement.insertBefore(banner, title.nextSibling);
        }
    }

    function saveBindingOnly() {
        if (!state.selectedAnimeId) {
            KT.ui.toast(KT.t('noItemTitle'), 'error');
            return;
        }

        KT.api.post('Plugins/KometaThemes/Bindings/' + encodeURIComponent(state.itemId), {
            animeId: state.selectedAnimeId,
            animeName: state.selectedAnimeName || '',
            slug: state.selectedAnimeSlug || '',
            source: 'ThemeFinder'
        }).then(function () {
            KT.ui.toast(KT.t('bindingSaved'), 'success');
            loadBinding();
        }).catch(function (error) {
            KT.ui.toast(error.message || KT.t('saveBindingFailed'), 'error');
        });
    }

    /* ---- search ---- */

    function setSearchState(message, type) {
        var node = q('ktSearchState');
        node.className = 'kt-state' + (type ? ' ' + type : '');
        util.clear(node);
        if (type === 'loading') { node.appendChild(util.el('span', 'kt-spinner')); }
        node.appendChild(document.createTextNode(message || ''));
    }

    function runSearch() {
        var title = q('ktSearchInput').value.trim();
        if (!title) { return; }
        var requestId = ++state.searchRequestId;
        var itemId = state.itemId;
        var year = q('ktSearchYear').value;
        q('ktBtnSearch').disabled = true;
        setSearchState(KT.t('searching'), 'loading');
        setStep(1);
        clearDetail();

        var url = 'Plugins/KometaThemes/Search?title=' + encodeURIComponent(title) +
            (year ? '&year=' + encodeURIComponent(year) : '') +
            '&itemId=' + encodeURIComponent(itemId);

        KT.api.get(url).then(function (data) {
            if (requestId !== state.searchRequestId || itemId !== state.itemId) { return; }
            state.results = data.results || [];
            state.broadResults = data.broadResults || [];
            renderResults();
            if (!state.results.length && !state.broadResults.length) {
                setSearchState(KT.t('searchEmpty'), 'error');
            } else {
                setSearchState(data.retriedTitle ? '"' + data.retriedTitle + '"' : '', '');
                setStep(2);
            }
        }).catch(function (error) {
            if (requestId === state.searchRequestId && itemId === state.itemId) {
                setSearchState(KT.t('searchFailed') + ': ' + (error.message || ''), 'error');
            }
        }).finally(function () {
            if (requestId === state.searchRequestId && itemId === state.itemId) { q('ktBtnSearch').disabled = false; }
        });
    }

    function confidenceBadge(result) {
        var level = String(result.confidence || 'Weak').toLowerCase();
        var cls = level === 'exact' ? 'success' : (level === 'strong' ? 'op' : 'warn');
        return util.el('span', 'kt-badge ' + cls, KT.t('confidence' + level.charAt(0).toUpperCase() + level.slice(1)));
    }

    function resultButton(result) {
        var btn = util.el('button', 'kt-result' + (result.broad ? ' broad' : ''));
        btn.type = 'button';
        btn.id = 'kt-r-' + result.id;
        btn.dataset.animeId = String(result.id);
        btn.setAttribute('role', 'option');
        btn.setAttribute('aria-selected', 'false');
        var poster = util.el('div', 'kt-poster');
        var imageUrl = util.safeUrl(result.imageUrl);
        if (imageUrl) {
            var img = util.el('img');
            img.loading = 'lazy';
            img.alt = result.name;
            img.referrerPolicy = 'no-referrer';
            img.src = imageUrl;
            poster.appendChild(img);
        } else {
            poster.appendChild(util.el('div', 'kt-poster-ph', '?'));
        }
        btn.appendChild(poster);
        var body = util.el('div', 'kt-result-body');
        body.appendChild(util.el('div', 'kt-result-title', result.name));
        var sub = [result.year, result.season, result.mediaFormat].filter(Boolean).join(' · ');
        if (sub) { body.appendChild(util.el('div', 'kt-result-sub', sub)); }
        body.appendChild(confidenceBadge(result));
        btn.appendChild(body);
        var badge = util.el('span', 'kt-result-badge');
        badge.style.display = 'none';
        btn.appendChild(badge);
        btn.addEventListener('click', function () { selectAnime(result); });
        return btn;
    }

    function renderResults() {
        var card = q('ktResultsCard');
        card.style.display = '';
        var list = q('ktResults');
        util.clear(list);
        state.results.forEach(function (result) { list.appendChild(resultButton(result)); });
        q('ktResultsCount').textContent = String(state.results.length + state.broadResults.length);

        var broadWrap = q('ktBroadWrap');
        var broadList = q('ktBroadResults');
        util.clear(broadList);
        if (state.broadResults.length) {
            broadWrap.style.display = '';
            broadWrap.open = state.results.length === 0;
            state.broadResults.forEach(function (result) { broadList.appendChild(resultButton(result)); });
        } else {
            broadWrap.style.display = 'none';
        }
        updateResultBadges();
        // reset keyboard state (setup is attached once in show)
        state.resultsActiveIndex = -1;
        state.broadActiveIndex = -1;
        var listEl = q('ktResults');
        if (listEl) {
            listEl.removeAttribute('aria-activedescendant');
            listEl.querySelectorAll('.kt-result').forEach(function (el) {
                el.classList.remove('active');
                el.setAttribute('aria-selected', 'false');
            });
        }
        // also ensure broad list has basic a11y attrs (buttons inside already have role from resultButton)
        var broadList = q('ktBroadResults');
        if (broadList) {
            broadList.setAttribute('role', 'listbox');
            broadList.setAttribute('aria-label', KT.t('broadResults') || 'Broad results');
        }
    }

    /* ---- keyboard nav for results (arrow/Home/End/Enter/Esc + ARIA) ---- */

    function highlightResult(index, isBroad) {
        isBroad = !!isBroad;
        var list = isBroad ? q('ktBroadResults') : q('ktResults');
        if (!list) return;
        var items = list.querySelectorAll('.kt-result');
        if (isBroad) {
            state.broadActiveIndex = Math.max(-1, Math.min(index, items.length - 1));
        } else {
            state.resultsActiveIndex = Math.max(-1, Math.min(index, items.length - 1));
        }
        var activeIdx = isBroad ? state.broadActiveIndex : state.resultsActiveIndex;
        items.forEach(function (el, i) {
            var isActive = (i === activeIdx);
            el.classList.toggle('active', isActive);
            el.setAttribute('aria-selected', isActive ? 'true' : 'false');
            if (isActive) {
                list.setAttribute('aria-activedescendant', el.id || '');
                el.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
                try { el.focus({ preventScroll: true }); } catch (e) { el.focus(); }
            }
        });
        if (activeIdx < 0) {
            list.removeAttribute('aria-activedescendant');
        }
    }

    function setupResultsKeyboard() {
        var list = q('ktResults');
        if (!list || list.dataset.ktKeys === '1') return;
        list.dataset.ktKeys = '1';

        list.setAttribute('role', 'listbox');
        list.setAttribute('aria-label', KT.t('resultsTitle') || 'Results');
        list.setAttribute('aria-multiselectable', 'false');
        list.setAttribute('tabindex', '0');

        list.addEventListener('keydown', function (ev) {
            var items = list.querySelectorAll('.kt-result');
            if (!items.length) return;
            var idx = state.resultsActiveIndex;
            if (ev.key === 'ArrowDown') {
                ev.preventDefault();
                highlightResult(idx < 0 ? 0 : idx + 1, false);
            } else if (ev.key === 'ArrowUp') {
                ev.preventDefault();
                highlightResult(idx <= 0 ? items.length - 1 : idx - 1, false);
            } else if (ev.key === 'Home') {
                ev.preventDefault();
                highlightResult(0, false);
            } else if (ev.key === 'End') {
                ev.preventDefault();
                highlightResult(items.length - 1, false);
            } else if (ev.key === 'Enter' || ev.key === ' ') {
                ev.preventDefault();
                if (idx >= 0 && items[idx]) {
                    var id = items[idx].dataset.animeId;
                    var all = (state.results || []).concat(state.broadResults || []);
                    var res = all.find(function (r) { return String(r.id) === id; });
                    if (res) selectAnime(res);
                }
            } else if (ev.key === 'Escape') {
                ev.preventDefault();
                highlightResult(-1, false);
            }
        });

        // focus list → auto-activate first for immediate arrow UX
        list.addEventListener('focus', function () {
            if (state.resultsActiveIndex < 0) {
                var items = list.querySelectorAll('.kt-result');
                if (items.length) highlightResult(0, false);
            }
        });

        // click also sets active for consistency (mouse + kbd mix)
        list.addEventListener('click', function (ev) {
            var btn = ev.target.closest('.kt-result');
            if (btn) {
                var items = list.querySelectorAll('.kt-result');
                for (var i = 0; i < items.length; i++) {
                    if (items[i] === btn) {
                        state.resultsActiveIndex = i;
                        highlightResult(i, false);
                        break;
                    }
                }
            }
        }, true);
    }

    function setupBroadKeyboard() {
        var list = q('ktBroadResults');
        if (!list || list.dataset.ktKeys === '1') return;
        list.dataset.ktKeys = '1';

        list.setAttribute('role', 'listbox');
        list.setAttribute('aria-label', KT.t('broadResults') || 'Broad results');
        list.setAttribute('aria-multiselectable', 'false');
        list.setAttribute('tabindex', '0');

        list.addEventListener('keydown', function (ev) {
            var items = list.querySelectorAll('.kt-result');
            if (!items.length) return;
            var idx = state.broadActiveIndex;
            if (ev.key === 'ArrowDown') {
                ev.preventDefault();
                highlightResult(idx < 0 ? 0 : idx + 1, true);
            } else if (ev.key === 'ArrowUp') {
                ev.preventDefault();
                highlightResult(idx <= 0 ? items.length - 1 : idx - 1, true);
            } else if (ev.key === 'Home') {
                ev.preventDefault();
                highlightResult(0, true);
            } else if (ev.key === 'End') {
                ev.preventDefault();
                highlightResult(items.length - 1, true);
            } else if (ev.key === 'Enter' || ev.key === ' ') {
                ev.preventDefault();
                if (idx >= 0 && items[idx]) {
                    var id = items[idx].dataset.animeId;
                    var all = (state.results || []).concat(state.broadResults || []);
                    var res = all.find(function (r) { return String(r.id) === id; });
                    if (res) selectAnime(res);
                }
            } else if (ev.key === 'Escape') {
                ev.preventDefault();
                highlightResult(-1, true);
            }
        });

        list.addEventListener('focus', function () {
            if (state.broadActiveIndex < 0) {
                var items = list.querySelectorAll('.kt-result');
                if (items.length) highlightResult(0, true);
            }
        });

        list.addEventListener('click', function (ev) {
            var btn = ev.target.closest('.kt-result');
            if (btn) {
                var items = list.querySelectorAll('.kt-result');
                for (var i = 0; i < items.length; i++) {
                    if (items[i] === btn) {
                        state.broadActiveIndex = i;
                        highlightResult(i, true);
                        break;
                    }
                }
            }
        }, true);
    }

    /* ---- anime detail + themes ---- */

    function clearDetail() {
        /* clears the currently-shown anime detail but NOT state.selected —
           selections persist across results so the user can pick from multiple seasons. */
        state.detailRequestId++;
        state.selectedAnimeId = null;
        state.selectedAnimeName = null;
        state.themes = [];
        state.seasonGroups = [];
        q('ktAnimeCard').style.display = 'none';
        q('ktDetailEmpty').style.display = '';
        util.clear(q('ktSeasonGroups'));
        // reset filters for new selection or clear
        state.filters = { audio: true, video: true, op: true, ed: true, creditless: false };
        updateSelBar();
    }

    function selectAnime(result) {
        state.page.querySelectorAll('.kt-result').forEach(function (node) {
            node.classList.toggle('selected', node.dataset.animeId === String(result.id));
        });
        clearDetail();
        var requestId = ++state.detailRequestId;
        state.selectedAnimeId = result.id;
        q('ktDetailEmpty').style.display = 'none';
        var groupsHost = q('ktSeasonGroups');
        var loading = util.el('div', 'kt-card');
        loading.appendChild(util.el('span', 'kt-spinner'));
        loading.appendChild(document.createTextNode(KT.t('loadingThemes')));
        groupsHost.appendChild(loading);

        var url = 'Plugins/KometaThemes/Anime/' + encodeURIComponent(result.id) + '/themes?itemId=' + encodeURIComponent(state.itemId);
        if (result.slug) { url += '&slug=' + encodeURIComponent(result.slug); }

        KT.api.get(url).then(function (data) {
            if (requestId !== state.detailRequestId || state.selectedAnimeId !== result.id) { return; }
            var anime = data.anime || result;
            state.selectedAnimeName = anime.name || result.name || '';
            state.selectedAnimeSlug = anime.slug || result.slug || '';
            renderAnime(anime);
            state.themes = data.themes || [];
            state.seasonGroups = data.seasonGroups || [];
            renderFiltersBar();
            renderSeasonGroups();
            updateSelBar();
            setStep(3);
        }).catch(function (error) {
            if (requestId !== state.detailRequestId) { return; }
            util.clear(groupsHost);
            var fail = util.el('div', 'kt-card');
            fail.appendChild(util.el('p', 'kt-state error', error.message || KT.t('error')));
            groupsHost.appendChild(fail);
        });
    }

    function renderAnime(anime) {
        var card = q('ktAnimeCard');
        card.style.display = '';
        q('ktAnimeTitle').textContent = anime.name;
        var chips = q('ktAnimeChips');
        util.clear(chips);
        [anime.year, anime.season, anime.mediaFormat].forEach(function (value) {
            if (value) { chips.appendChild(util.el('span', 'kt-chip', String(value))); }
        });
        q('ktAnimeSynopsis').textContent = (anime.synopsis || '').replace(/<[^>]*>/g, '');
        var poster = q('ktAnimePoster');
        util.clear(poster);
        var image = util.safeUrl(anime.largeImageUrl || anime.imageUrl);
        if (image) {
            var img = util.el('img');
            img.alt = anime.name;
            img.referrerPolicy = 'no-referrer';
            img.src = image;
            poster.appendChild(img);
        } else {
            poster.appendChild(util.el('div', 'kt-poster-ph', '?'));
        }
    }

    /* Selection model: key = media URL → {url, mediaType, themeName, animeId, animeName}.
       URLs are globally unique, so selections survive switching between results/seasons. */

    function mediaUrl(theme, mediaType) {
        return util.safeUrl(mediaType === 'video' ? theme.videoUrl : theme.audioUrl);
    }

    function themeName(theme) {
        var name = theme.label || ((theme.type || 'Theme') + (theme.sequence || ''));
        if (theme.title && theme.title.toLowerCase() !== name.toLowerCase()) {
            name += ' - ' + theme.title;
        }
        return name;
    }

    function isAvailable(theme, mediaType) {
        if (mediaType === 'audio') { return !!mediaUrl(theme, mediaType) && !theme.audioDownloaded; }
        return !!mediaUrl(theme, mediaType) && !theme.videoDownloaded;
    }

    function setSelected(theme, mediaType, selected) {
        if (!theme || !isAvailable(theme, mediaType)) { return false; }
        var url = mediaUrl(theme, mediaType);
        if (!url) { return false; }
        if (selected) {
            state.selected[url] = {
                url: url,
                mediaType: mediaType,
                themeName: themeName(theme),
                animeId: state.selectedAnimeId,
                animeName: state.selectedAnimeName || ''
            };
        } else {
            delete state.selected[url];
        }
        return true;
    }

    function themeRow(theme, index) {
        var row = util.el('div', 'kt-theme');
        row.setAttribute('tabindex', '0');
        row.setAttribute('role', 'group');
        row.setAttribute('aria-label', themeName(theme) || 'theme');
        var type = String(theme.type || 'OP').toUpperCase();
        row.appendChild(util.el('span', 'kt-type-chip ' + type.toLowerCase(), theme.label || (type + (theme.sequence || ''))));

        var main = util.el('div', 'kt-theme-main');
        main.appendChild(util.el('div', 'kt-theme-title', theme.title || theme.slug));
        var subParts = [theme.episodes && 'ep ' + theme.episodes, theme.resolution && theme.resolution + 'p', theme.source]
            .filter(Boolean).join(' · ');
        if (subParts) { main.appendChild(util.el('div', 'kt-theme-sub', subParts)); }
        var chips = util.el('div', 'kt-theme-chips');
        if (theme.creditless) { chips.appendChild(util.el('span', 'kt-badge neutral', KT.t('creditless'))); }
        if (theme.overlap && theme.overlap !== 'None') { chips.appendChild(util.el('span', 'kt-badge warn', KT.t('overlap'))); }
        if (theme.version) { chips.appendChild(util.el('span', 'kt-badge neutral', 'v' + theme.version)); }
        if (chips.childNodes.length) { main.appendChild(chips); }
        row.appendChild(main);

        var actions = util.el('div', 'kt-theme-actions');
        ['audio', 'video'].forEach(function (mediaType) {
            if (!state.filters[mediaType]) { return; }
            var url = mediaUrl(theme, mediaType);
            if (!url) { return; }
            var downloaded = mediaType === 'audio' ? theme.audioDownloaded : theme.videoDownloaded;

            var previewBtn = util.el('button', 'kt-preview-btn', '▶');
            previewBtn.type = 'button';
            previewBtn.title = KT.t('preview') + ' ' + KT.t(mediaType);
            previewBtn.addEventListener('click', function () {
                var vol = (mediaType === 'audio' ? (state.audioVolume || 50) : (state.videoVolume || 50)) / 100;
                KT.ui.player.toggle(url, mediaType, themeName(theme), vol);
            });
            actions.appendChild(previewBtn);

            var toggle = util.el('button', 'kt-media-toggle ' + mediaType, KT.t(mediaType));
            toggle.type = 'button';
            toggle.dataset.url = url;
            toggle.setAttribute('aria-pressed', state.selected[url] ? 'true' : 'false');
            if (downloaded) {
                toggle.classList.add('downloaded');
                toggle.textContent = KT.t(mediaType) + ' ✓';
                toggle.title = KT.t('alreadyDownloaded');
                toggle.disabled = true;
            } else {
                if (state.selected[url]) { toggle.classList.add('on'); }
                toggle.addEventListener('click', function () {
                    var nowSelected = !state.selected[url];
                    if (setSelected(theme, mediaType, nowSelected)) {
                        toggle.classList.toggle('on', nowSelected);
                        toggle.setAttribute('aria-pressed', nowSelected ? 'true' : 'false');
                        updateSelBar();
                    }
                });
            }
            actions.appendChild(toggle);
        });
        row.appendChild(actions);

        // Basic keyboard support for row: Enter/Space toggles first available media
        row.addEventListener('keydown', function (ev) {
            if (ev.key === 'Enter' || ev.key === ' ') {
                ev.preventDefault();
                var firstToggle = actions.querySelector('.kt-media-toggle:not([disabled])');
                if (firstToggle) {
                    firstToggle.click();
                } else {
                    var firstPreview = actions.querySelector('.kt-preview-btn');
                    if (firstPreview) firstPreview.click();
                }
            }
        });

        return row;
    }

    function matchesFilters(theme) {
        var f = state.filters || { audio: true, video: true, op: true, ed: true, creditless: false };
        var type = String(theme.type || '').toUpperCase();
        if (f.creditless && !theme.creditless) { return false; }
        if (!f.op && type === 'OP') { return false; }
        if (!f.ed && type === 'ED') { return false; }
        if (!f.audio && !f.video) { return false; }
        var hasAudio = !!theme.audioUrl || !!theme.audioDownloaded;
        var hasVideo = !!theme.videoUrl || !!theme.videoDownloaded;
        return (f.audio && hasAudio) || (f.video && hasVideo);
    }

    function getFilteredThemesForGroup(groupThemes) {
        if (!groupThemes) return [];
        return groupThemes.filter(function (gt) {
            // gt is the raw from season group; find full theme for filter check
            var full = state.themes.find(function (t) {
                return t.entryId === gt.entryId && t.videoId === gt.videoId && t.slug === gt.slug;
            });
            return full ? matchesFilters(full) : true;
        });
    }

    function renderFiltersBar() {
        var host = q('ktSeasonGroups');
        // Remove old bar if present
        var old = host.querySelector('#ktFiltersBar');
        if (old) old.remove();

        var bar = util.el('div', 'kt-filters-bar');
        bar.id = 'ktFiltersBar';

        // Create toggles
        var types = [
            { key: 'audio', label: KT.t('filterAudio') },
            { key: 'video', label: KT.t('filterVideo') },
            { key: 'op', label: KT.t('filterOP') },
            { key: 'ed', label: KT.t('filterED') }
        ];

        types.forEach(function (item) {
            var btn = util.el('button', 'kt-btn kt-btn-sm kt-filter-btn', item.label);
            btn.type = 'button';
            btn.dataset.filterKey = item.key;
            btn.setAttribute('aria-pressed', state.filters[item.key] ? 'true' : 'false');
            if (state.filters[item.key]) btn.classList.add('on');
            btn.addEventListener('click', function () {
                state.filters[item.key] = !state.filters[item.key];
                btn.classList.toggle('on', state.filters[item.key]);
                btn.setAttribute('aria-pressed', state.filters[item.key] ? 'true' : 'false');
                renderSeasonGroups();
            });
            bar.appendChild(btn);
        });

        var credit = util.el('button', 'kt-btn kt-btn-sm kt-filter-btn', KT.t('filterCreditless'));
        credit.type = 'button';
        credit.dataset.filterKey = 'creditless';
        credit.setAttribute('aria-pressed', state.filters.creditless ? 'true' : 'false');
        if (state.filters.creditless) credit.classList.add('on');
        credit.addEventListener('click', function () {
            state.filters.creditless = !state.filters.creditless;
            credit.classList.toggle('on', state.filters.creditless);
            credit.setAttribute('aria-pressed', state.filters.creditless ? 'true' : 'false');
            renderSeasonGroups();
        });
        bar.appendChild(credit);

        var clear = util.el('button', 'kt-btn kt-btn-sm kt-btn-ghost', KT.t('filterClear'));
        clear.type = 'button';
        clear.addEventListener('click', function () {
            state.filters = { audio: true, video: true, op: true, ed: true, creditless: false };
            renderFiltersBar();
            renderSeasonGroups();
        });
        bar.appendChild(clear);

        var selVis = util.el('button', 'kt-btn kt-btn-sm', KT.t('filterAllVisible'));
        selVis.type = 'button';
        selVis.addEventListener('click', function () {
            // Select all currently visible media, respecting Audio/Video filters.
            var visible = [];
            state.seasonGroups.forEach(function (g) {
                (g.themes || []).forEach(function (gt) {
                    var full = state.themes.find(function (t) { return t.entryId === gt.entryId && t.videoId === gt.videoId && t.slug === gt.slug; });
                    if (full && matchesFilters(full)) visible.push(full);
                });
            });
            visible.forEach(function (theme) {
                if (state.filters.audio) { setSelected(theme, 'audio', true); }
                if (state.filters.video) { setSelected(theme, 'video', true); }
            });
            syncToggleVisuals();
            updateSelBar();
        });
        bar.appendChild(selVis);

        // Insert at top of host
        if (host.firstChild) {
            host.insertBefore(bar, host.firstChild);
        } else {
            host.appendChild(bar);
        }
    }

    function renderSeasonGroups() {
        var host = q('ktSeasonGroups');
        // keep filters bar
        var bar = host.querySelector('#ktFiltersBar');
        util.clear(host);
        if (bar) host.appendChild(bar);

        var rendered = new Set();
        var groups = state.seasonGroups.length
            ? state.seasonGroups
            : [{ seasonNumber: null, themes: state.themes }];

        groups.forEach(function (group, groupIndex) {
            var details = util.el('details', 'kt-season kt-reveal');
            details.open = groupIndex === 0 || groups.length === 1;
            var summary = util.el('summary');
            var label = group.seasonNumber != null && group.startEpisode != null
                ? KT.t('seasonGroup', { n: group.seasonNumber })
                : (group.seasonNumber != null && groups.length > 1 ? KT.t('otherThemes') : KT.t('seasonGroup', { n: group.seasonNumber || 1 }));
            summary.appendChild(document.createTextNode(label));
            if (group.startEpisode != null) {
                summary.appendChild(util.el('span', 'kt-chip', KT.t('seasonEpisodes', { range: group.startEpisode + '–' + (group.endEpisode || '?') })));
            }
            var counts = util.el('span', 'kt-chip kt-season-count',
                'OP ' + (group.opCount != null ? group.opCount : '·') + ' / ED ' + (group.edCount != null ? group.edCount : '·'));
            summary.appendChild(counts);

            // Per-group bulk actions, aligned with the active media filters.
            var bulk = util.el('span', 'kt-group-bulk');
            if (state.filters.audio) {
                var ba = util.el('button', 'kt-btn kt-btn-xs', KT.t('audio'));
                ba.type = 'button';
                ba.addEventListener('click', function (event) { event.preventDefault(); event.stopPropagation(); selectGroup(group, 'audio'); });
                bulk.appendChild(ba);
            }
            if (state.filters.video) {
                var bv = util.el('button', 'kt-btn kt-btn-xs', KT.t('video'));
                bv.type = 'button';
                bv.addEventListener('click', function (event) { event.preventDefault(); event.stopPropagation(); selectGroup(group, 'video'); });
                bulk.appendChild(bv);
            }
            summary.appendChild(bulk);

            details.appendChild(summary);

            var list = util.el('div', 'kt-themes');
            var filteredGroupThemes = getFilteredThemesForGroup(group.themes || []);
            filteredGroupThemes.forEach(function (groupTheme) {
                var index = state.themes.findIndex(function (candidate, candidateIndex) {
                    return !rendered.has(candidateIndex) &&
                        candidate.entryId === groupTheme.entryId &&
                        candidate.videoId === groupTheme.videoId &&
                        candidate.slug === groupTheme.slug;
                });
                if (index === -1) { return; }
                rendered.add(index);
                list.appendChild(themeRow(state.themes[index], index));
            });
            details.appendChild(list);
            host.appendChild(details);
        });

        /* themes that didn't land in any group */
        var leftovers = state.themes.map(function (_, index) { return index; })
            .filter(function (index) { return !rendered.has(index) && matchesFilters(state.themes[index]); });
        if (leftovers.length && state.seasonGroups.length) {
            var extra = util.el('details', 'kt-season');
            extra.open = false;
            var summary = util.el('summary', null, KT.t('otherThemes'));
            extra.appendChild(summary);
            var list = util.el('div', 'kt-themes');
            leftovers.forEach(function (index) { list.appendChild(themeRow(state.themes[index], index)); });
            extra.appendChild(list);
            host.appendChild(extra);
        }

        if (!host.querySelector('.kt-theme')) {
            var empty = util.el('div', 'kt-empty');
            empty.appendChild(util.el('p', 'kt-note', KT.t('noVisibleThemes')));
            host.appendChild(empty);
        }
    }

    /* ---- selection bar + download ---- */

    function selectedValues() {
        return Object.keys(state.selected).map(function (key) { return state.selected[key]; });
    }

    function updateSelBar() {
        var count = Object.keys(state.selected).length;
        var bar = q('ktSelBar');
        bar.style.display = (count > 0 || state.themes.length) ? '' : 'none';
        q('ktSelCount').textContent = KT.t('selCount', { n: count });
        q('ktBtnDownload').disabled = count === 0 || state.downloading;
        q('ktBtnSaveBinding').disabled = !state.selectedAnimeId;
        renderSelReview();
        updateResultBadges();
    }

    /* Re-sync the on/off look of theme toggles from state.selected, keyed by data-url. */
    function syncToggleVisuals() {
        state.page.querySelectorAll('#ktSeasonGroups .kt-media-toggle[data-url]').forEach(function (toggle) {
            if (toggle.disabled) { return; }
            var selected = !!state.selected[toggle.dataset.url];
            toggle.classList.toggle('on', selected);
            toggle.setAttribute('aria-pressed', selected ? 'true' : 'false');
        });
    }

    /* Expandable list of every selected theme, grouped by anime, each removable. */
    function renderSelReview() {
        var review = q('ktSelReview');
        var body = q('ktSelReviewBody');
        var entries = selectedValues();
        q('ktSelReviewTitle').textContent = KT.t('selReviewTitle') + ' · ' + entries.length;
        review.style.display = entries.length ? '' : 'none';
        util.clear(body);
        if (!entries.length) { return; }

        var groups = {};
        var order = [];
        entries.forEach(function (entry) {
            var key = entry.animeName || String(entry.animeId || '—');
            if (!groups[key]) { groups[key] = []; order.push(key); }
            groups[key].push(entry);
        });
        order.forEach(function (key) {
            var group = util.el('div', 'kt-sel-group');
            group.appendChild(util.el('div', 'kt-sel-group-title', key));
            groups[key].forEach(function (entry) {
                var row = util.el('div', 'kt-sel-item');
                row.appendChild(util.el('span', 'kt-badge neutral', KT.t(entry.mediaType)));
                row.appendChild(util.el('span', 'kt-sel-item-name', entry.themeName));
                var remove = util.el('button', 'kt-sel-remove', '✕');
                remove.type = 'button';
                remove.title = KT.t('selRemove');
                remove.setAttribute('aria-label', KT.t('selRemove'));
                remove.addEventListener('click', function () {
                    delete state.selected[entry.url];
                    syncToggleVisuals();
                    updateSelBar();
                });
                row.appendChild(remove);
                group.appendChild(row);
            });
            body.appendChild(group);
        });
    }

    /* Badge on each search result showing how many themes were picked from that anime. */
    function updateResultBadges() {
        var counts = {};
        Object.keys(state.selected).forEach(function (url) {
            var id = String(state.selected[url].animeId || '');
            if (id) { counts[id] = (counts[id] || 0) + 1; }
        });
        state.page.querySelectorAll('.kt-result').forEach(function (node) {
            var badge = node.querySelector('.kt-result-badge');
            if (!badge) { return; }
            var n = counts[node.dataset.animeId] || 0;
            badge.textContent = n ? String(n) : '';
            badge.style.display = n ? '' : 'none';
        });
    }

    function bulkSelect(kind) {
        if (kind === 'clear') {
            state.selected = {};
        } else {
            state.themes.forEach(function (theme) {
                var type = String(theme.type || '').toUpperCase();
                if (kind === 'op-audio' && type === 'OP') { setSelected(theme, 'audio', true); }
                if (kind === 'ed-audio' && type === 'ED') { setSelected(theme, 'audio', true); }
                if (kind === 'video') { setSelected(theme, 'video', true); }
            });
        }
        syncToggleVisuals();
        updateSelBar();
    }

    function selectGroup(group, mediaType) {
        if (!group || !group.themes) return;
        (group.themes || []).forEach(function (gt) {
            var th = state.themes.find(function (t) {
                return t.entryId === gt.entryId && t.videoId === gt.videoId && t.slug === gt.slug;
            });
            if (th && matchesFilters(th)) {
                setSelected(th, mediaType, true);
            }
        });
        syncToggleVisuals();
        updateSelBar();
    }

    function logLine(message, level) {
        var box = q('ktDownloadLog');
        box.style.display = '';
        box.appendChild(util.el('div', level || 'info', message));
        box.scrollTop = box.scrollHeight;
    }

    function download() {
        var items = selectedValues();
        if (!items.length || state.downloading) { return; }
        state.downloading = true;
        var btn = q('ktBtnDownload');
        btn.disabled = true;
        btn.textContent = KT.t('downloading');
        logLine(KT.t('downloading'), 'info');

        KT.api.post('Plugins/KometaThemes/Items/' + encodeURIComponent(state.itemId) + '/download', {
            urls: items,
            animeId: state.selectedAnimeId,
            animeName: state.selectedAnimeName || '',
            animeSlug: state.selectedAnimeSlug || ''
        }).then(function (data) {
            var results = (data && data.results) || [];
            var ok = 0;
            var fail = 0;
            results.forEach(function (result) {
                if (result.success) {
                    ok++;
                    logLine('✓ ' + result.name, 'success');
                    /* drop only successful items; failures stay selected for an easy retry */
                    if (result.url) { delete state.selected[result.url]; }
                } else {
                    fail++;
                    logLine('✗ ' + result.name + (result.error ? ' — ' + result.error : ''), 'error');
                }
            });
            KT.ui.toast(KT.t('downloadDone', { ok: ok, fail: fail }), fail ? 'error' : 'success', 5000);
            /* refresh theme list so downloaded badges flip */
            var current = state.results.concat(state.broadResults).filter(function (result) {
                return result.id === state.selectedAnimeId;
            })[0];
            if (current) { selectAnime(current); }
        }).catch(function (error) {
            logLine(error.message || KT.t('error'), 'error');
            KT.ui.toast(error.message || KT.t('error'), 'error');
        }).finally(function () {
            state.downloading = false;
            btn.textContent = KT.t('download');
            updateSelBar();
        });
    }

    /* ---- entry point ---- */

    function show(page) {
        state.page = page;
        var itemId = util.getItemId();
        var itemChanged = itemId !== state.itemId;
        state.itemId = itemId;
        if (itemChanged) {
            state.contextRequestId++;
            state.searchRequestId++;
            state.detailRequestId++;
            state.itemInfo = null;
        }

        KT.config.load().catch(function () { return null; }).then(function () {
            KT.i18n.apply(page);
            q('ktBtnSearch').textContent = KT.t('searchBtn');
            q('ktBtnDownload').textContent = KT.t('download');
            q('ktBtnSaveBinding').textContent = KT.t('saveBinding');
            var bulkLabels = { 'op-audio': 'bulkOpAudio', 'ed-audio': 'bulkEdAudio', video: 'bulkVideo', clear: 'bulkClear' };
            page.querySelectorAll('[data-bulk]').forEach(function (btn) {
                btn.textContent = KT.t(bulkLabels[btn.dataset.bulk]);
            });

            var hasItem = !!state.itemId;
            q('ktFinderNoItem').style.display = hasItem ? 'none' : '';
            q('ktFinderWorkspace').style.display = hasItem ? '' : 'none';

            if (!page.dataset.ktBound) {
                page.dataset.ktBound = '1';
                KT.ui.attachSyncDot(q('ktLiveDot'));
                q('ktBtnSearch').addEventListener('click', runSearch);
                q('ktSearchInput').addEventListener('keydown', function (event) {
                    if (event.key === 'Enter') { event.preventDefault(); runSearch(); }
                    if (event.key === 'Escape') {
                        q('ktSearchInput').value = '';
                        var clr = q('ktSearchClear');
                        if (clr) clr.style.display = 'none';
                    }
                });
                // clear (x) button for search input (polished UX + a11y)
                var si = q('ktSearchInput');
                var inputWrap = si ? si.parentNode : null;
                if (inputWrap && !q('ktSearchClear')) {
                    var clr = util.el('button', 'kt-search-clear', '✕');
                    clr.id = 'ktSearchClear';
                    clr.type = 'button';
                    clr.setAttribute('aria-label', KT.t('filterClear'));
                    clr.addEventListener('click', function () {
                        si.value = '';
                        si.focus();
                        clr.style.display = 'none';
                    });
                    if (!inputWrap.style.position || inputWrap.style.position === 'static') {
                        inputWrap.style.position = 'relative';
                    }
                    inputWrap.appendChild(clr);
                    si.addEventListener('input', function () {
                        clr.style.display = si.value ? '' : 'none';
                    });
                    clr.style.display = 'none';
                }
                // keyboard nav for results (attach once; containers persist across re-renders)
                setupResultsKeyboard();
                setupBroadKeyboard();
                q('ktBtnDownload').addEventListener('click', download);
                q('ktBtnSaveBinding').addEventListener('click', saveBindingOnly);
                page.querySelectorAll('[data-bulk]').forEach(function (btn) {
                    btn.addEventListener('click', function () { bulkSelect(btn.dataset.bulk); });
                });
            }

            if (hasItem && itemChanged) {
                state.currentBinding = null;
                state.results = [];
                state.broadResults = [];
                state.selected = {};
                q('ktBtnSearch').disabled = false;
                q('ktResultsCard').style.display = 'none';
                q('ktSearchYear').value = '';
                var poster = q('ktFinderPoster');
                util.clear(poster);
                poster.appendChild(util.el('div', 'kt-poster-ph', '♪'));
                var backdrop = q('ktFinderBackdrop');
                backdrop.style.display = 'none';
                backdrop.style.backgroundImage = '';
                renderBinding();
                clearDetail();
                setStep(1);
                loadItemContext();
            } else if (hasItem && !state.itemInfo) {
                loadItemContext();
            }
        });
    }

    KT.pages.search = { show: show };
})();
