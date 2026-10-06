/*
 * KometaThemes 2.0 — the ♪ button on Jellyfin's item pages.
 *
 * Added to index.html by the File Transformation plugin. It shows only to administrators, only on
 * series, seasons and movies that KometaThemes manages, and opens a small panel in place with the
 * songs of that page and a link to the full anime page.
 */
(function () {
    'use strict';

    if (window.__kometaThemesItemButton) return;
    window.__kometaThemesItemButton = true;

    var BUTTON_CLASS = 'kometathemes-item-button';
    var PANEL_ID = 'kometathemes-item-panel';
    var adminCheck = null;

    function client() {
        return window.ApiClient;
    }

    function call(method, path) {
        var api = client();
        var headers = { Accept: 'application/json' };
        api.setRequestHeaders(headers);
        return fetch(api.getUrl('KometaThemes/' + path), { method: method, headers: headers }).then(function (response) {
            return response.text().then(function (text) {
                var data = null;
                try {
                    data = text ? JSON.parse(text) : null;
                } catch (e) {
                    data = null;
                }
                if (!response.ok) throw new Error((data && data.error) || 'KometaThemes could not answer (' + response.status + ').');
                return data;
            });
        });
    }

    function isAdmin() {
        if (!adminCheck) {
            adminCheck = client().getCurrentUser().then(function (user) {
                return Boolean(user && user.Policy && user.Policy.IsAdministrator);
            }).catch(function () {
                adminCheck = null;
                return false;
            });
        }
        return adminCheck;
    }

    function el(tag, className, text) {
        var node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined && text !== null) node.textContent = text;
        return node;
    }

    function closePanel() {
        var panel = document.getElementById(PANEL_ID);
        if (panel) panel.remove();
        document.removeEventListener('keydown', onKey, true);
        document.removeEventListener('click', onOutside, true);
        var open = document.querySelector('.' + BUTTON_CLASS + '[aria-expanded="true"]');
        if (open) open.setAttribute('aria-expanded', 'false');
    }

    function onKey(event) {
        if (event.key === 'Escape') {
            var button = document.querySelector('.' + BUTTON_CLASS + '[aria-expanded="true"]');
            closePanel();
            if (button) button.focus();
        }
    }

    function onOutside(event) {
        var panel = document.getElementById(PANEL_ID);
        if (panel && !panel.contains(event.target) && !event.target.closest('.' + BUTTON_CLASS)) closePanel();
    }

    function manageUrl(ownerId) {
        return '#/configurationpage?name=KometaThemes&item=' + encodeURIComponent(ownerId);
    }

    function style(panel) {
        panel.style.cssText = [
            'position:absolute', 'z-index:1000', 'width:min(380px,calc(100vw - 24px))', 'padding:14px 16px',
            'border-radius:10px', 'border:1px solid var(--jf-palette-divider, rgba(255,255,255,.12))',
            'background:var(--jf-palette-background-paper, #202020)', 'color:var(--jf-palette-text-primary, #fff)',
            'box-shadow:0 16px 40px rgba(0,0,0,.45)', 'font-size:.95rem', 'line-height:1.45', 'text-align:left', 'box-sizing:border-box'
        ].join(';');
    }

    function render(panel, detail, itemId) {
        while (panel.firstChild) panel.removeChild(panel.firstChild);
        var folder = detail.folders.find(function (f) { return f.itemId === itemId; }) || detail.folders[0];
        var title = el('h3', null, 'Theme songs on this page');
        title.style.cssText = 'margin:0 0 8px;font-size:1rem;font-weight:600';
        panel.appendChild(title);

        var muted = 'color:var(--jf-palette-text-secondary, rgba(255,255,255,.7))';
        if (!folder) {
            var none = el('p', null, detail.note || 'KometaThemes cannot add themes to this item.');
            none.style.cssText = 'margin:0 0 12px;' + muted;
            panel.appendChild(none);
        } else {
            var present = folder.themes.filter(function (t) { return t.audio.file || t.video.file; });
            if (folder.match.state === 'inherit' && !present.length) {
                var inherit = el('p', null, 'This season plays the series themes.');
                inherit.style.cssText = 'margin:0 0 12px;' + muted;
                panel.appendChild(inherit);
            } else if (!present.length) {
                var empty = el('p', null, folder.match.state === 'notFound'
                    ? 'Not found on animethemes.moe yet. Open the anime page to pick the entry.'
                    : 'No theme files yet. They are added at the next check.');
                empty.style.cssText = 'margin:0 0 12px;' + muted;
                panel.appendChild(empty);
            } else {
                var list = el('ul');
                list.style.cssText = 'list-style:none;margin:0 0 12px;padding:0;display:grid;gap:6px';
                present.forEach(function (theme) {
                    var li = el('li');
                    li.style.cssText = 'display:flex;gap:8px;align-items:baseline';
                    var code = el('b', null, theme.code);
                    code.style.cssText = 'min-width:3.2em;font-variant-numeric:tabular-nums';
                    var song = el('span', null, theme.title || 'Untitled song');
                    var artist = el('span', null, theme.artists ? ' ' + theme.artists : '');
                    artist.style.cssText = muted;
                    var wrap = el('span');
                    wrap.appendChild(song);
                    wrap.appendChild(artist);
                    li.appendChild(code);
                    li.appendChild(wrap);
                    list.appendChild(li);
                });
                panel.appendChild(list);
            }
        }

        var actions = el('div');
        actions.style.cssText = 'display:flex;gap:8px;flex-wrap:wrap';
        var manage = el('a', 'raised button-submit emby-button', 'Manage themes');
        manage.href = manageUrl(detail.id);
        manage.style.cssText = 'margin:0;padding:.5em 1em;text-decoration:none';
        manage.addEventListener('click', closePanel);
        var check = el('button', 'raised emby-button', 'Check again');
        check.type = 'button';
        check.style.cssText = 'margin:0;padding:.5em 1em';
        check.addEventListener('click', function () {
            check.disabled = true;
            check.textContent = 'Checking…';
            call('POST', 'Items/' + encodeURIComponent(detail.id) + '/Check').then(function (updated) {
                render(panel, updated, itemId);
            }).catch(function (error) {
                check.disabled = false;
                check.textContent = 'Check again';
                showError(panel, error.message);
            });
        });
        actions.appendChild(manage);
        actions.appendChild(check);
        panel.appendChild(actions);
    }

    function showError(panel, message) {
        var p = el('p', null, message);
        p.setAttribute('role', 'alert');
        p.style.cssText = 'margin:8px 0 0;color:var(--jf-palette-error-light, #d15353)';
        panel.appendChild(p);
    }

    function openPanel(button, itemId) {
        if (button.getAttribute('aria-expanded') === 'true') {
            closePanel();
            return;
        }
        closePanel();
        var panel = el('div');
        panel.id = PANEL_ID;
        panel.setAttribute('role', 'dialog');
        panel.setAttribute('aria-label', 'Theme songs');
        style(panel);
        var loading = el('p', null, 'Loading…');
        loading.style.margin = '0';
        panel.appendChild(loading);

        var rect = button.getBoundingClientRect();
        panel.style.top = (rect.bottom + window.scrollY + 8) + 'px';
        document.body.appendChild(panel);
        // Right-aligned to the button, kept inside the window.
        var width = panel.offsetWidth;
        var viewport = document.documentElement.clientWidth;
        var left = Math.min(rect.right - width, viewport - width - 12);
        panel.style.left = (Math.max(12, left) + window.scrollX) + 'px';
        button.setAttribute('aria-expanded', 'true');
        document.addEventListener('keydown', onKey, true);
        setTimeout(function () { document.addEventListener('click', onOutside, true); }, 0);

        panel.tabIndex = -1;
        panel.focus();
        call('GET', 'Items/' + encodeURIComponent(itemId)).then(function (detail) {
            render(panel, detail, itemId);
        }).catch(function (error) {
            panel.removeChild(loading);
            showError(panel, error.message);
        });
    }

    function makeButton() {
        var button = document.createElement('button');
        button.setAttribute('is', 'emby-button');
        button.type = 'button';
        button.className = 'button-flat detailButton emby-button ' + BUTTON_CLASS;
        button.title = 'Theme songs';
        button.setAttribute('aria-label', 'Theme songs');
        button.setAttribute('aria-haspopup', 'dialog');
        button.setAttribute('aria-expanded', 'false');
        var content = el('div', 'detailButton-content');
        var icon = el('span', 'material-icons detailButton-icon music_note');
        icon.setAttribute('aria-hidden', 'true');
        content.appendChild(icon);
        button.appendChild(content);
        button.addEventListener('click', function () {
            openPanel(button, button.dataset.itemId);
        });
        return button;
    }

    function update(view, itemId) {
        var container = view && view.querySelector('.mainDetailButtons');
        if (!container) return;
        var existing = container.querySelector('.' + BUTTON_CLASS);
        if (existing) existing.hidden = true;
        if (!itemId) return;

        isAdmin().then(function (admin) {
            if (!admin) return null;
            return call('GET', 'Items/' + encodeURIComponent(itemId) + '/Summary');
        }).then(function (answer) {
            if (!answer || !answer.managed) return;
            var button = container.querySelector('.' + BUTTON_CLASS) || makeButton();
            button.dataset.itemId = itemId;
            button.hidden = false;
            if (!button.isConnected) {
                var more = container.querySelector('.btnMoreCommands');
                container.insertBefore(button, more || null);
            }
        }).catch(function () {
            /* KometaThemes not reachable or the item is not managed: no button. */
        });
    }

    document.addEventListener('viewshow', function (event) {
        closePanel();
        var params = (event.detail && event.detail.params) || {};
        var view = event.target;
        if (view && view.querySelector && view.querySelector('.mainDetailButtons')) {
            update(view, params.id);
        }
    });
})();
