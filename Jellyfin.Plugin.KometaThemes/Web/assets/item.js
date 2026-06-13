/* KometaThemes — item page logic (KT.pages.item). */
(function () {
    'use strict';

    var KT = window.KT;
    var util = KT.util;

    KT.i18n.extend({
        en: {
            itemThemes: 'Item themes',
            downloadedThemes: 'Downloaded themes',
            noThemes: 'No themes downloaded yet. Use Sync or the Theme Finder to fetch some.',
            noItemTitle: 'No item selected',
            noItemBody: 'Open this page from a series or movie: use the ♪ button on the item detail page, or run a library preset below.',
            libraryPresets: 'Library presets',
            libraryPresetsNote: 'Bulk download across every matching library. Runs in the background.',
            presetAllOP: 'All OP (audio)', presetAllED: 'All ED (audio)',
            presetOPED: 'OP + ED (audio)', presetOPEDVideo: 'OP + ED + video',
            regOk: '{songs} songs / {videos} videos registered in Jellyfin',
            regMismatch: '{disk} files on disk but only {reg} registered in Jellyfin — themes may not play',
            repairLinks: 'Repair links',
            repaired: 'Links repaired: {repaired} fixed, {songs} songs / {videos} videos now registered',
            repairNeedsScan: '{count} files are not in the library yet — run a library scan, then repair again',
            syncItem: 'Sync this item',
            confirmDeleteAll: 'Delete ALL downloaded themes for this item?',
            confirmDelete: 'Delete {name}?',
            confirmPreset: 'Run "{name}" on all matching libraries now?',
            themesDeleted: 'Themes deleted',
            syncCompleted: 'Sync completed',
            season: 'Season'
        },
        it: {
            itemThemes: 'Temi elemento',
            downloadedThemes: 'Temi scaricati',
            noThemes: 'Nessun tema scaricato. Usa il Sync o il Theme Finder per scaricarne.',
            noItemTitle: 'Nessun elemento selezionato',
            noItemBody: 'Apri questa pagina da una serie o un film: usa il pulsante ♪ nella pagina dei dettagli, oppure esegui un preset libreria qui sotto.',
            libraryPresets: 'Preset libreria',
            libraryPresetsNote: 'Download massivo su tutte le librerie corrispondenti. Gira in background.',
            presetAllOP: 'Tutti gli OP (audio)', presetAllED: 'Tutti gli ED (audio)',
            presetOPED: 'OP + ED (audio)', presetOPEDVideo: 'OP + ED + video',
            regOk: '{songs} sigle / {videos} video registrati in Jellyfin',
            regMismatch: '{disk} file su disco ma solo {reg} registrati in Jellyfin — i temi potrebbero non riprodursi',
            repairLinks: 'Ripara collegamenti',
            repaired: 'Collegamenti riparati: {repaired} corretti, ora {songs} sigle / {videos} video registrati',
            repairNeedsScan: '{count} file non sono ancora in libreria — esegui una scansione, poi ripara di nuovo',
            syncItem: 'Sincronizza elemento',
            confirmDeleteAll: 'Eliminare TUTTI i temi scaricati per questo elemento?',
            confirmDelete: 'Eliminare {name}?',
            confirmPreset: 'Eseguire "{name}" su tutte le librerie corrispondenti?',
            themesDeleted: 'Temi eliminati',
            syncCompleted: 'Sincronizzazione completata',
            season: 'Stagione'
        }
    });

    var PRESETS = [
        { key: 'AllOPAudio', label: 'presetAllOP' },
        { key: 'AllEDAudio', label: 'presetAllED' },
        { key: 'AllOPEDAudio', label: 'presetOPED' },
        { key: 'AllOPEDAudioVideo', label: 'presetOPEDVideo' }
    ];

    var state = { page: null, itemId: null, poller: null };

    function q(id) { return state.page.querySelector('#' + id); }

    function setState(message, type) {
        var node = q('ktItemState');
        node.textContent = message || '';
        node.className = 'kt-state' + (type ? ' ' + type : '');
    }

    /* ---- item identity ---- */

    function loadIdentity() {
        ApiClient.getItem(ApiClient.getCurrentUserId(), state.itemId).then(function (item) {
            q('ktItemTitle').textContent = item.Name;
            var chips = q('ktItemChips');
            util.clear(chips);
            [item.Type, item.ProductionYear].forEach(function (value) {
                if (value) { chips.appendChild(util.el('span', 'kt-chip', String(value))); }
            });

            var poster = q('ktItemPoster');
            if (item.ImageTags && item.ImageTags.Primary) {
                util.clear(poster);
                var img = util.el('img');
                img.alt = item.Name;
                img.src = ApiClient.getScaledImageUrl(item.Id, { type: 'Primary', maxWidth: 200, tag: item.ImageTags.Primary });
                poster.appendChild(img);
            }
            if (item.BackdropImageTags && item.BackdropImageTags.length) {
                var backdrop = q('ktItemBackdrop');
                backdrop.style.display = '';
                backdrop.style.backgroundImage = 'url("' +
                    ApiClient.getScaledImageUrl(item.Id, { type: 'Backdrop', maxWidth: 1280, tag: item.BackdropImageTags[0] }) + '")';
            }
        }).catch(function () {
            q('ktItemTitle').textContent = state.itemId;
        });
    }

    /* ---- registration banner (Jellyfin 10.11.x link bug) ---- */

    function loadRegistration() {
        var banner = q('ktRegBanner');
        util.clear(banner);
        KT.api.get('Plugins/KometaThemes/Items/' + state.itemId + '/info').then(function (info) {
            var status = info && info.themeStatus;
            if (!status) { return; }
            var disk = status.songsOnDisk + status.videosOnDisk;
            var registered = status.registeredSongs + status.registeredVideos;
            if (disk === 0) { return; }

            if (registered >= disk) {
                banner.appendChild(util.el('span', 'kt-badge success',
                    KT.t('regOk', { songs: status.registeredSongs, videos: status.registeredVideos })));
                return;
            }

            var warn = util.el('div', 'kt-row');
            warn.appendChild(util.el('span', 'kt-badge warn',
                KT.t('regMismatch', { disk: disk, reg: registered })));
            var btn = util.el('button', 'kt-btn kt-btn-sm', KT.t('repairLinks'));
            btn.type = 'button';
            btn.addEventListener('click', repairLinks);
            warn.appendChild(btn);
            banner.appendChild(warn);
        }).catch(function () { /* banner is best-effort */ });
    }

    function repairLinks() {
        KT.ui.loading(true);
        KT.api.post('Plugins/KometaThemes/Items/' + state.itemId + '/repair').then(function (result) {
            KT.ui.loading(false);
            KT.ui.toast(KT.t('repaired', {
                repaired: result.repaired,
                songs: result.registeredSongs,
                videos: result.registeredVideos
            }), 'success', 5200);
            if (result.notScanned > 0) {
                KT.ui.toast(KT.t('repairNeedsScan', { count: result.notScanned }), 'error', 6500);
            }
            loadRegistration();
        }).catch(function (error) {
            KT.ui.loading(false);
            KT.ui.toast(error.message || KT.t('error'), 'error');
        });
    }

    /* ---- theme list ---- */

    function loadThemes() {
        var list = q('ktThemesList');
        util.clear(list);
        list.appendChild(util.el('div', 'kt-theme')).appendChild(util.el('div', 'kt-skel', '')).style.minHeight = '30px';

        KT.api.get('Plugins/KometaThemes/Items/' + state.itemId + '/themes').then(function (themes) {
            util.clear(list);
            themes = themes || [];
            q('ktThemesCount').textContent = String(themes.length);
            if (!themes.length) {
                var empty = util.el('div', 'kt-theme');
                empty.appendChild(util.el('p', 'kt-note', KT.t('noThemes')));
                list.appendChild(empty);
                return;
            }

            themes.sort(function (a, b) {
                if (a.Type !== b.Type) { return a.Type === 'OP' ? -1 : 1; }
                return (a.Sequence || 0) - (b.Sequence || 0);
            });

            themes.forEach(function (theme) {
                var row = util.el('div', 'kt-theme');
                var type = (theme.Type || 'OP').toUpperCase();
                row.appendChild(util.el('span', 'kt-type-chip ' + type.toLowerCase(), type + (theme.Sequence || '')));

                var main = util.el('div', 'kt-theme-main');
                main.appendChild(util.el('div', 'kt-theme-title', theme.Slug || theme.FileName));
                main.appendChild(util.el('div', 'kt-theme-sub', theme.FileName));
                var chips = util.el('div', 'kt-theme-chips');
                if (theme.SeasonNumber) {
                    chips.appendChild(util.el('span', 'kt-chip', KT.t('season') + ' ' + theme.SeasonNumber));
                }
                chips.appendChild(util.el('span', 'kt-badge ' + (theme.Exists ? 'success' : 'danger'),
                    theme.Exists ? KT.t('downloaded') : KT.t('missing')));
                main.appendChild(chips);
                row.appendChild(main);

                var actions = util.el('div', 'kt-theme-actions');
                var del = util.el('button', 'kt-btn kt-btn-danger kt-btn-sm', KT.t('delete'));
                del.type = 'button';
                del.addEventListener('click', function () {
                    KT.ui.confirm(KT.t('confirmDelete', { name: theme.FileName })).then(function (ok) {
                        if (!ok) { return; }
                        KT.api.del('Plugins/KometaThemes/Items/' + state.itemId + '/themes?fileName=' + encodeURIComponent(theme.FileName))
                            .then(function () {
                                KT.ui.toast(KT.t('themesDeleted'), 'success');
                                loadThemes();
                                loadRegistration();
                            })
                            .catch(function (error) { KT.ui.toast(error.message || KT.t('error'), 'error'); });
                    });
                });
                actions.appendChild(del);
                row.appendChild(actions);
                list.appendChild(row);
            });
        }).catch(function (error) {
            util.clear(list);
            var fail = util.el('div', 'kt-theme');
            fail.appendChild(util.el('p', 'kt-state error', error.message || KT.t('error')));
            list.appendChild(fail);
        });
    }

    /* ---- actions ---- */

    function syncItem() {
        setState(KT.t('syncRunning'));
        KT.ui.loading(true);
        KT.api.post('Plugins/KometaThemes/Items/' + state.itemId + '/sync').then(function (data) {
            KT.ui.loading(false);
            setState((data && data.message) || KT.t('syncCompleted'), 'success');
            loadThemes();
            loadRegistration();
        }).catch(function (error) {
            KT.ui.loading(false);
            setState(error.message || KT.t('error'), 'error');
        });
    }

    function deleteAll() {
        KT.ui.confirm(KT.t('confirmDeleteAll')).then(function (ok) {
            if (!ok) { return; }
            KT.ui.loading(true);
            KT.api.del('Plugins/KometaThemes/Items/' + state.itemId + '/themes').then(function () {
                KT.ui.loading(false);
                KT.ui.toast(KT.t('themesDeleted'), 'success');
                loadThemes();
                loadRegistration();
            }).catch(function (error) {
                KT.ui.loading(false);
                KT.ui.toast(error.message || KT.t('error'), 'error');
            });
        });
    }

    function runPreset(preset, label) {
        KT.ui.confirm(KT.t('confirmPreset', { name: label })).then(function (ok) {
            if (!ok) { return; }
            KT.api.post('Plugins/KometaThemes/Sync/run', { preset: preset }).then(function () {
                showSyncProgress();
            }).catch(function (error) {
                KT.ui.toast(error.message || KT.t('syncStartFailed'), 'error');
            });
        });
    }

    function showSyncProgress() {
        var progress = q('ktSyncProgress');
        progress.style.display = '';
        if (state.poller) { state.poller.stop(); }
        state.poller = KT.ui.syncPoller(function (status) {
            KT.ui.renderSyncStatus(progress, status);
            if (status.isFinished) {
                setTimeout(function () { progress.style.display = 'none'; }, 6000);
                loadThemes();
                loadRegistration();
            }
        });
        state.poller.start();
    }

    function renderPresets() {
        var row = q('ktPresetRow');
        util.clear(row);
        PRESETS.forEach(function (preset) {
            var btn = util.el('button', 'kt-btn kt-btn-ghost', KT.t(preset.label));
            btn.type = 'button';
            btn.addEventListener('click', function () { runPreset(preset.key, KT.t(preset.label)); });
            row.appendChild(btn);
        });
    }

    /* ---- entry point (re-entrant: dashboard re-shows cached pages) ---- */

    function show(page) {
        state.page = page;
        state.itemId = util.getItemId();

        KT.config.load().catch(function () { return null; }).then(function () {
            KT.i18n.apply(page);
            q('ktBtnSyncItem').textContent = KT.t('syncItem');
            q('ktBtnFinder').textContent = KT.t('openThemeFinder');
            q('ktBtnRefresh').textContent = KT.t('refresh');
            q('ktBtnDeleteAll').textContent = KT.t('deleteAll');
            q('ktBtnGoSettings').textContent = KT.t('settings');
            renderPresets();

            var hasItem = !!state.itemId;
            q('ktNoItem').style.display = hasItem ? 'none' : '';
            q('ktThemesCard').style.display = hasItem ? '' : 'none';
            q('ktItemActions').style.display = hasItem ? '' : 'none';

            if (!page.dataset.ktBound) {
                page.dataset.ktBound = '1';
                KT.ui.attachSyncDot(q('ktLiveDot'));
                q('ktBtnSyncItem').addEventListener('click', syncItem);
                q('ktBtnRefresh').addEventListener('click', function () { loadThemes(); loadRegistration(); });
                q('ktBtnDeleteAll').addEventListener('click', deleteAll);
                q('ktBtnFinder').addEventListener('click', function () {
                    util.navigate('configurationpage?name=KometaThemesSearch&itemId=' + encodeURIComponent(state.itemId));
                });
                q('ktBtnGoSettings').addEventListener('click', function () {
                    util.navigate('configurationpage?name=KometaThemes');
                });
            }

            if (hasItem) {
                loadIdentity();
                loadRegistration();
                loadThemes();
            } else {
                q('ktItemTitle').textContent = KT.t('noItemTitle');
            }

            KT.api.get('Plugins/KometaThemes/Sync/status').then(function (status) {
                if (status && !status.isFinished && (status.totalItems > 0 || status.processedItems > 0)) {
                    showSyncProgress();
                }
            }).catch(function () { /* optional */ });
        });
    }

    KT.pages.item = { show: show };
})();
